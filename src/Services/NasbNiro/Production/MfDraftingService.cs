using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Production
{
    public interface IMfDraftingService
    {
        /// <summary>Hook: a make process became ready to go. Never throws; returns the number of orders drafted.</summary>
        Task<int> DraftForProcessAsync(int processId, CancellationToken ct);

        /// <summary>Hook: the WBS of a work order was generated. Never throws; returns the number of orders drafted.</summary>
        Task<int> DraftForWorkOrderAsync(int workOrderId, CancellationToken ct);

        Task<ResponseDto<DraftOrdersResultDto>> DraftAsync(DraftOrdersDto dto, CancellationToken ct);
    }

    /// <summary>
    /// The system drafts the production orders (and their operations from the approved routing); people release them.
    /// One order per make process (type Service, Assembly, FinalAssembly or Construction); drafting the same process twice does nothing.
    /// </summary>
    public class MfDraftingService : IMfDraftingService
    {
        private static readonly ProcessType[] MakeTypes = { ProcessType.Service, ProcessType.Assembly, ProcessType.FinalAssembly, ProcessType.Construction };

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;
        private readonly IItpService _itp;
        private readonly ILogger<MfDraftingService> _logger;

        public MfDraftingService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null, IItpService itp = null, ILogger<MfDraftingService> logger = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
            _itp = itp;
            _logger = logger;
        }

        private int Me
        {
            get
            {
                try { return _user.UserId; } catch (Exception) { return 0; }
            }
        }

        public async Task<int> DraftForProcessAsync(int processId, CancellationToken ct)
        {
            var added = new List<object>();
            try
            {
                var result = new DraftOrdersResultDto();
                ProcessEntity process = await _uow.Set<ProcessEntity>().AsNoTracking().Include(p => p.Bom).ThenInclude(b => b.Assembly).FirstOrDefaultAsync(p => p.Id == processId && !p.Deleted, ct);
                if (process == null || !MakeTypes.Contains(process.Type)) return 0;
                await DraftOneAsync(process, null, result, added, ct);
                if (added.Count > 0) await _uow.SaveChangesAsync(ct);
                FillIds(result, added);
                await EnsureItpsAsync(result, ct);
                await SyncGatesAsync(result.OrderIds, ct);
                return result.Drafted;
            }
            catch (Exception ex)
            {
                // a hook never throws into the process that called it, but a failed drafting must leave a trace (R1p review 19)
                _logger?.LogWarning(ex, "Production order drafting for process {ProcessId} failed", processId);
                Detach(added);
                return 0;
            }
        }

        public async Task<int> DraftForWorkOrderAsync(int workOrderId, CancellationToken ct)
        {
            var added = new List<object>();
            try
            {
                var result = new DraftOrdersResultDto();
                await DraftWorkOrderAsync(workOrderId, result, added, ct);
                if (added.Count > 0) await _uow.SaveChangesAsync(ct);
                FillIds(result, added);
                await EnsureItpsAsync(result, ct);
                await SyncGatesAsync(result.OrderIds, ct);
                return result.Drafted;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Production order drafting for work order {WorkOrderId} failed", workOrderId);
                Detach(added);
                return 0;
            }
        }

        public async Task<ResponseDto<DraftOrdersResultDto>> DraftAsync(DraftOrdersDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Planners)) return MfText.Bad<DraftOrdersResultDto>(MfText.NotAllowed);
            if (dto == null || dto.WorkOrderId.HasValue == dto.ProcessId.HasValue) return MfText.Bad<DraftOrdersResultDto>("دستور کار یا فرایند را (فقط یکی) مشخص کنید.");
            if (dto.ItemId.HasValue && !dto.ProcessId.HasValue) return MfText.Bad<DraftOrdersResultDto>("کالای جایگزین فقط همراه با یک فرایند معتبر است.");
            var result = new DraftOrdersResultDto();
            var added = new List<object>();
            try
            {
                if (dto.WorkOrderId.HasValue)
                {
                    if (!await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.WorkOrderId.Value && !w.Deleted, ct)) return MfText.NotFound<DraftOrdersResultDto>("دستور کار پیدا نشد.");
                    await DraftWorkOrderAsync(dto.WorkOrderId.Value, result, added, ct);
                }
                else
                {
                    ProcessEntity process = await _uow.Set<ProcessEntity>().AsNoTracking().Include(p => p.Bom).ThenInclude(b => b.Assembly).FirstOrDefaultAsync(p => p.Id == dto.ProcessId.Value && !p.Deleted, ct);
                    if (process == null) return MfText.NotFound<DraftOrdersResultDto>("فرایند پیدا نشد.");
                    if (!MakeTypes.Contains(process.Type)) return MfText.Bad<DraftOrdersResultDto>("فقط برای فرایندهای ساخت، مونتاژ و خدمات دستور تولید ساخته می‌شود (خرید دستور تولید ندارد).");
                    await DraftOneAsync(process, dto.ItemId, result, added, ct);
                }
                if (added.Count > 0) await _uow.SaveChangesAsync(ct);
                FillIds(result, added);
            }
            catch (DbUpdateException)
            {
                Detach(added);
                return MfText.Bad<DraftOrdersResultDto>("ثبت دستور تولید ممکن نشد؛ دوباره تلاش کنید.");
            }
            await EnsureItpsAsync(result, ct);
            await SyncGatesAsync(result.OrderIds, ct);
            return ResponseDtoHelper.Ok(result);
        }

        private void Detach(List<object> added)
        {
            foreach (object e in added)
            {
                try { _uow.Entry(e).State = EntityState.Detached; } catch (Exception) { /* already detached */ }
            }
        }

        /// <summary>
        /// R1p D19: a production order cannot be released while its work order has no ITP, so the ITP is drafted from the quality template when the orders are drafted (a draft made by the system; the people
        /// of quality submit it and the client approves it). When quality has no template the note says so, and the release gate refuses the order with the missing ITP named. Never fails the drafting.
        /// </summary>
        private async Task EnsureItpsAsync(DraftOrdersResultDto result, CancellationToken ct)
        {
            if (_itp == null || result.OrderIds.Count == 0) return;
            try
            {
                var workOrders = await (from o in _uow.Set<MfProductionOrder>().AsNoTracking()
                                        join w in _uow.Set<WorkOrder>().AsNoTracking() on o.WorkOrderId equals w.Id
                                        where result.OrderIds.Contains(o.Id)
                                        select new { w.Id, w.Code }).Distinct().ToListAsync(ct);
                foreach (var wo in workOrders)
                {
                    if (await _uow.Set<Itp>().AnyAsync(i => i.WorkOrderId == wo.Id && !i.Deleted, ct)) continue;
                    int? created = await _itp.EnsureItpForWorkOrderAsync(wo.Id, ct);
                    result.Notes.Add(created.HasValue
                        ? $"برنامه بازرسی و آزمون (ITP) پیش‌نویس دستور کار {wo.Code} از قالب کیفیت ساخته شد؛ پیش از صدور دستور تولید باید به تأیید کارفرما برسد."
                        : $"برای دستور کار {wo.Code} برنامه بازرسی و آزمون (ITP) ساخته نشد (قالب ITP در ماژول کیفیت پیدا نشد)؛ بدون ITP دستور تولید صادر نمی‌شود.");
                }
            }
            catch (Exception ex)
            {
                // the drafting of the orders stands; the release gate names the missing ITP (R1p review 19: the failure is logged and told to the planner instead of being swallowed)
                _logger?.LogWarning(ex, "Drafting the ITP of the work orders of production orders {OrderIds} failed", string.Join(",", result.OrderIds));
                result.Notes.Add("ساخت خودکار برنامه بازرسی و آزمون (ITP) با خطا روبه‌رو شد؛ آن را در ماژول کیفیت بسازید، بدون ITP دستور تولید صادر نمی‌شود.");
            }
        }

        private async Task SyncGatesAsync(List<int> orderIds, CancellationToken ct)
        {
            if (orderIds.Count == 0) return;
            List<MfProductionOrder> orders = await _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToListAsync(ct);
            foreach (MfProductionOrder o in orders) await MfGates.OrderAsync(_notify, o, ct);
        }

        private async Task DraftWorkOrderAsync(int workOrderId, DraftOrdersResultDto result, List<object> added, CancellationToken ct)
        {
            List<ProcessEntity> processes = await _uow.Set<ProcessEntity>().AsNoTracking().Include(p => p.Bom).ThenInclude(b => b.Assembly)
                .Where(p => !p.Deleted && !p.Bom.Deleted && p.Bom.Assembly.WorkOrderId == workOrderId && MakeTypes.Contains(p.Type))
                .OrderBy(p => p.Bom.Assembly.Id).ThenBy(p => p.Sort).ThenBy(p => p.Id).ToListAsync(ct);
            foreach (ProcessEntity p in processes) await DraftOneAsync(p, null, result, added, ct);
        }

        private async Task DraftOneAsync(ProcessEntity process, int? itemOverride, DraftOrdersResultDto result, List<object> added, CancellationToken ct)
        {
            string title = string.IsNullOrWhiteSpace(process.Title) ? "فرایند " + process.Id : process.Title;
            if (await _uow.Set<MfProductionOrder>().AnyAsync(o => o.ProcessId == process.Id && !o.Deleted && o.Status != MfOrderStatus.Cancelled, ct)
                || added.OfType<MfProductionOrder>().Any(o => o.ProcessId == process.Id))
            {
                result.Skipped++;
                result.Notes.Add($"{title}: برای این فرایند قبلاً دستور تولید صادر شده است.");
                return;
            }
            string exitCode = MfText.Clean(process.ExitCommodityCode);
            InventoryItem item = itemOverride.HasValue
                ? await _uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemOverride.Value && !i.Deleted, ct)
                : exitCode == null ? null : await _uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Code == exitCode && !i.Deleted, ct);
            if (item == null)
            {
                result.Skipped++;
                result.Notes.Add(exitCode == null ? $"{title}: کد کالای خروجی فرایند مشخص نیست." : $"{title}: کالایی با کد {exitCode} در انبار تعریف نشده است.");
                return;
            }
            Bom bom = process.Bom;
            int workOrderId = bom.Assembly.WorkOrderId;
            int contractId = await _uow.Set<WorkOrder>().AsNoTracking().Where(w => w.Id == workOrderId).Select(w => w.ContractId).FirstAsync(ct);
            decimal quantity = bom.Count > 0 ? bom.Count : 1;
            DateTimeOffset now = DateTimeOffset.Now;
            int? activityId = await _uow.Set<PcActivity>().AsNoTracking()
                .Where(a => a.SourceType == PcSourceType.Process && a.SourceId == process.Id && !a.Deleted).Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);

            var order = new MfProductionOrder
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, MfNumbers.ProductionOrder, "PO-MF", DateTime.Today, ct),
                WorkOrderId = workOrderId, ContractId = contractId, ProcessId = process.Id, BomId = bom.Id, ActivityId = activityId, ItemId = item.Id, Quantity = quantity,
                Unit = MfText.Clean(bom.Unit) ?? item.Unit, Status = MfOrderStatus.Draft, AutoDrafted = true,
                CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
            };
            _uow.Set<MfProductionOrder>().Add(order);   // the components and operations come with the graph
            added.Add(order);

            // the input commodity of the process is the component the order consumes
            string enterCode = MfText.Clean(process.EnterCommodityCode);
            if (enterCode != null)
            {
                InventoryItem component = await _uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Code == enterCode && !i.Deleted, ct);
                if (component != null && component.Id != item.Id)
                {
                    var line = new MfOrderComponent { ItemId = component.Id, Quantity = quantity, Unit = component.Unit };
                    order.Components.Add(line);
                    added.Add(line);
                }
            }

            MfResolvedRouting routing = await MfRouting.ResolveAsync(_uow, item, ct);
            if (routing.Revision != null)
            {
                order.RoutingRevisionId = routing.Revision.Id;
                foreach (MfOperation op in OperationsFromRouting(routing, quantity, now, Me))
                {
                    order.Operations.Add(op);
                    added.Add(op);
                }
            }
            else result.Notes.Add($"{title}: {routing.Message} دستور بدون عملیات ساخته شد.");

            result.Drafted++;
        }

        private static void FillIds(DraftOrdersResultDto result, List<object> added) => result.OrderIds = added.OfType<MfProductionOrder>().Select(o => o.Id).ToList();

        /// <summary>The operations of an order, copied from the approved routing (the same code serves the refresh of a draft order).</summary>
        internal static List<MfOperation> OperationsFromRouting(MfResolvedRouting routing, decimal quantity, DateTimeOffset now, int userId)
        {
            var list = new List<MfOperation>();
            foreach (OperationTemplate t in routing.Steps)
            {
                routing.Standards.TryGetValue(t.Id, out MfOperationStandard s);
                MfInspectionRequirement inspection = s?.InspectionRequirement ?? MfInspectionRequirement.None;
                if (inspection == MfInspectionRequirement.None && t.RequiresQc) inspection = MfInspectionRequirement.Qc;
                list.Add(new MfOperation
                {
                    Sequence = t.Sequence, OperationTemplateId = t.Id, Title = t.Title, WorkCenterId = s?.WorkCenterId,
                    SetupHoursPlanned = s?.SetupHours ?? 0, RunHoursPlanned = Math.Round((s?.RunHoursPerUnit ?? 0) * quantity, 4),
                    Status = MfOperationStatus.Waiting, InspectionRequirement = inspection, ItpTestType = s?.ItpTestType,
                    IsOutside = s?.IsOutside ?? false, OutsideSupplierId = s?.DefaultSupplierId, OutsideLeadDays = s?.OutsideLeadDays ?? 0,
                    IsWelding = s?.IsWelding ?? false, WpsId = s != null && s.IsWelding ? s.WpsId : null,
                    CreateDate = now, ModifyDate = now, CreatorUserId = userId, ModifierUserId = userId,
                });
            }
            return list;
        }
    }
}
