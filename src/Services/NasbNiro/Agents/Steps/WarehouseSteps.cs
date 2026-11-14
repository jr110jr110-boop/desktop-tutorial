using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectBalance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProjectBalance;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.ProjectBalanceDtos;

namespace Services.NasbNiro.Agents.Steps
{
    // Station 7 (warehouse) and the feeders of planning: the balance of the project materials, the point-of-use kit (a draft material request per activity), its submission, the gate of the material
    // request, and the physical work of the warehouse (the issue, the confirmation of the issue or receipt, the incoming inspection).

    public static class WarehouseSteps
    {
        public static IServiceCollection AddWarehouseSteps(this IServiceCollection services)
        {
            services.AddScoped<IAgentStep, BalanceRunStep>();
            services.AddScoped<IAgentStep, KitDraftStep>();
            services.AddScoped<IAgentStep, MaterialRequestSubmitStep>();
            services.AddScoped<IAgentStep, MaterialRequestApproveStep>();
            services.AddScoped<IAgentStep, MaterialIssueStep>();
            services.AddScoped<IAgentStep, DocumentConfirmStep>();
            services.AddScoped<IAgentStep, InspectionStep>();
            return services;
        }
    }

    /// <summary>The base of the warehouse steps: station 7.</summary>
    internal abstract class WhStep : ProcStep
    {
        public override int Station => 7;

        protected static string MrLink(int id) => "/inventory/material-requests/" + id.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>PLAN.BALANCE.RUN: a current project baseline that has no material balance (or whose balance is older than the baseline) gets one from the planning agent.</summary>
    internal sealed class BalanceRunStep : ProcStep
    {
        public override string Code => "PLAN.BALANCE.RUN";
        public override int Station => 4;
        public override string AgentCode => AgentCodes.Plan;
        public override string Title => "ایجنت برنامه‌ریزی: اجرای بالانس مواد پروژه";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "PLANNING,PMO";

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<ProjectMaterialBalance> runs = uow.Set<ProjectMaterialBalance>().AsNoTracking();
            var rows = await uow.Set<PbProjectBaseline>().AsNoTracking()
                .Where(b => !b.Deleted && b.IsCurrent && (!runs.Any(r => r.BaselineId == b.Id && !r.Deleted) || runs.Where(r => r.BaselineId == b.Id && !r.Deleted).Max(r => r.RunAt) < b.CopiedAt))
                .OrderBy(b => b.Id).Take(ProcStepCommon.Cap).Select(b => new { b.Id, b.WorkOrderId, b.ContractId }).ToListAsync(ct);
            List<int> workOrderIds = rows.Select(r => r.WorkOrderId).Distinct().ToList();
            Dictionary<int, string> codes = await uow.Set<Entities.Model.NasbNiro.Contracts.WorkOrder>().AsNoTracking().Where(w => workOrderIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Code, ct);
            return rows.Select(r => Item("PbProjectBaseline", r.Id, codes.TryGetValue(r.WorkOrderId, out string c) ? c : null, r.ContractId, r.WorkOrderId, "پایه‌ی پروژه بالانس مواد ندارد یا بالانس آن از پایه قدیمی‌تر است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            if (!item.WorkOrderId.HasValue) return AgentStepResult.Refused("دستور ساخت این پروژه مشخص نیست.");
            ResponseDto<BalanceRunBatchDto> batch = await services.GetRequiredService<IProjectBalanceService>().RunAsync(new BalanceRunRequestDto { WorkOrderId = item.WorkOrderId, CreateDrafts = false }, ct);
            if (!batch.Success) return ProcStepCommon.Refusal(batch);
            if (batch.Data.Runs.Count == 0) return AgentStepResult.Waiting(string.Join(" | ", batch.Data.Notes).Length == 0 ? "بالانسی ساخته نشد." : string.Join(" | ", batch.Data.Notes));
            return AgentStepResult.Done("ProjectMaterialBalance", batch.Data.Runs[0].Id, null, $"ایجنت برنامه‌ریزی بالانس مواد پروژه {item.TargetNumber} را اجرا کرد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = "/project-balance", Title = "اجرای بالانس مواد پروژه " + item.TargetNumber };
    }

    /// <summary>WH.KIT.DRAFT: the activities that start within the kit days and have reserved stock get one draft material request each (the kit service; it never submits).</summary>
    internal sealed class KitDraftStep : WhStep
    {
        public override string Code => "WH.KIT.DRAFT";
        public override string AgentCode => AgentCodes.Plan;
        public override string Title => "ایجنت برنامه‌ریزی: پیش‌نویس درخواست کیت پای‌کار برای فعالیت‌های هفته‌ی آینده";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "PLANNING,PMO";

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            BalanceSettings s = await BalanceSettingKeys.LoadAsync(uow, ct);
            DateTime last = today.AddDays(s.KitDaysAhead);
            IQueryable<ProjectMaterialBalance> runs = uow.Set<ProjectMaterialBalance>().AsNoTracking().Where(r => !r.Deleted);
            IQueryable<MaterialRequest> requests = uow.Set<MaterialRequest>().AsNoTracking().Where(m => !m.Deleted && m.Status != MaterialRequestStatus.Rejected && m.ActivityId != null);
            IQueryable<PbProjectBaseline> baselines = uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => !b.Deleted && b.IsCurrent);
            var rows = await runs.Where(r => baselines.Any(b => b.Id == r.BaselineId) && r.RunNumber == runs.Where(x => x.BaselineId == r.BaselineId).Max(x => x.RunNumber)
                                              && uow.Set<ProjectMaterialBalanceLine>().Any(l => l.BalanceId == r.Id && !l.Deleted && l.ItemId != null && l.ActivityId != null && l.UseByDate != null && l.UseByDate <= last
                                                                                          && (l.ReservedQty > 0 || l.SubstituteQty > 0) && !requests.Any(m => m.ActivityId == l.ActivityId)))
                .OrderBy(r => r.Id).Take(ProcStepCommon.Cap).Select(r => new { r.BaselineId, r.WorkOrderId, r.ContractId }).ToListAsync(ct);
            List<int> workOrderIds = rows.Select(r => r.WorkOrderId).Distinct().ToList();
            Dictionary<int, string> codes = await uow.Set<Entities.Model.NasbNiro.Contracts.WorkOrder>().AsNoTracking().Where(w => workOrderIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Code, ct);
            return rows.Select(r => Item("PbProjectBaseline", r.BaselineId, codes.TryGetValue(r.WorkOrderId, out string c) ? c : null, r.ContractId, r.WorkOrderId,
                $"فعالیتی تا {s.KitDaysAhead.ToString(CultureInfo.InvariantCulture)} روز دیگر شروع می‌شود، موجودی برایش رزرو شده و درخواست کیت ندارد.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<KitRunResultDto> kits = await services.GetRequiredService<IKitService>().DraftKitsAsync(false, ct);
            if (!kits.Success) return ProcStepCommon.Refusal(kits);
            return AgentStepResult.Done("MaterialRequest", null, kits.Data.Requests.FirstOrDefault(),
                $"ایجنت برنامه‌ریزی {kits.Data.Drafted} پیش‌نویس کیت ساخت ({kits.Data.Late} دیرکرد، {kits.Data.Skipped} ردشده).");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = "/inventory/material-requests", Title = "پیش‌نویس کیت پای‌کار پروژه " + item.TargetNumber };
    }

    /// <summary>WH.MR.SUBMIT: the planning agent submits the kit drafts it made (a submitted request is approved by planning and by production; nothing is decided here).</summary>
    internal sealed class MaterialRequestSubmitStep : WhStep
    {
        public override string Code => "WH.MR.SUBMIT";
        public override string AgentCode => AgentCodes.Plan;
        public override string Title => "ایجنت برنامه‌ریزی: ارسال پیش‌نویس درخواست کیت برای تایید";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "PLANNING,PMO";

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            int seat = await ProcStepCommon.SeatUserIdAsync(uow, AgentCode, ct);
            if (seat == 0) return new List<AgentWorkItem>();
            var rows = await uow.Set<MaterialRequest>().AsNoTracking().Where(m => !m.Deleted && m.Status == MaterialRequestStatus.Draft && m.CreatorUserId == seat)
                .OrderBy(m => m.Id).Take(ProcStepCommon.Cap).Select(m => new { m.Id, m.Number, m.WorkOrderId, ContractId = m.WorkOrderId == null ? (int?)null : m.WorkOrder.ContractId }).ToListAsync(ct);
            return rows.Select(m => Item("MaterialRequest", m.Id, m.Number, m.ContractId, m.WorkOrderId, "پیش‌نویس کیت ساخته‌شده توسط ایجنت منتظر ارسال است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<MaterialRequestDto> sent = await services.GetRequiredService<IInventoryWorkflowService>().RequestActionAsync(new MaterialRequestActionDto { Id = item.TargetId, Action = "submit" }, ct);
            if (!sent.Success) return ProcStepCommon.Refusal(sent);
            return AgentStepResult.Done("MaterialRequest", sent.Data.Id, sent.Data.Number, $"ایجنت برنامه‌ریزی درخواست کیت {sent.Data.Number} را برای تایید ارسال کرد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = MrLink(item.TargetId), Title = "ارسال درخواست کیت " + item.TargetNumber };
    }

    /// <summary>WH.MR.APPROVE: planning and production approve a submitted material request (two different people); the send-back is the existing reject action with its reason.</summary>
    internal sealed class MaterialRequestApproveStep : WhStep, IAgentReturnable
    {
        public override string Code => "WH.MR.APPROVE";
        public override string AgentCode => AgentCodes.Wh;
        public override string Title => "تایید درخواست کالا (برنامه‌ریزی و مدیر تولید)";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<MaterialRequest>().AsNoTracking().Where(m => !m.Deleted && m.Status == MaterialRequestStatus.Submitted)
                .OrderBy(m => m.Id).Take(ProcStepCommon.Cap)
                .Select(m => new { m.Id, m.Number, m.WorkOrderId, ContractId = m.WorkOrderId == null ? (int?)null : m.WorkOrder.ContractId, Planning = m.PlanningApprovedAt != null, Production = m.ManufacturingApprovedAt != null }).ToListAsync(ct);
            // the same two approvals as the action of the service: planning and the production manager, what is approved is not asked again
            return rows.Select(m => Item("MaterialRequest", m.Id, m.Number, m.ContractId, m.WorkOrderId, "درخواست کالا منتظر تایید است.",
                ProcStepCommon.Roles(m.Planning ? null : InventoryRoles.RolePlanning, m.Production ? null : InventoryRoles.RoleManufacturing))).Where(i => i.GateRoles != null).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo
        {
            Roles = item.GateRoles ?? ProcStepCommon.Roles(InventoryRoles.RolePlanning, InventoryRoles.RoleManufacturing), Link = MrLink(item.TargetId), Title = "تایید درخواست کالا " + item.TargetNumber,
        };

        public async Task<ResponseDto<bool>> ReturnAsync(AgentWorkItem item, string reason, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<MaterialRequestDto> rejected = await services.GetRequiredService<IInventoryWorkflowService>().RequestActionAsync(new MaterialRequestActionDto { Id = item.TargetId, Action = "reject", Reason = reason }, ct);
            return rejected.Success ? ResponseDtoHelper.Ok(true) : ResponseDtoHelper.BadRequest<bool>(rejected.Message);
        }
    }

    /// <summary>WH.MR.ISSUE: an approved material request with a quantity left to issue is picked and issued by the warehouse (physical work).</summary>
    internal sealed class MaterialIssueStep : WhStep
    {
        public override string Code => "WH.MR.ISSUE";
        public override string AgentCode => AgentCodes.Wh;
        public override string Title => "حواله‌ی کالای درخواست تاییدشده از انبار";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<InventoryDocument> drafts = uow.Set<InventoryDocument>().AsNoTracking().Where(d => !d.Deleted && d.Status == InventoryDocumentStatus.Draft && d.MaterialRequestId != null);
            var rows = await uow.Set<MaterialRequest>().AsNoTracking().Where(m => !m.Deleted && m.Status == MaterialRequestStatus.Approved && m.Lines.Any(l => l.IssuedQuantity < l.Quantity)
                                                                                 && !drafts.Any(d => d.MaterialRequestId == m.Id))
                .OrderBy(m => m.Id).Take(ProcStepCommon.Cap)
                .Select(m => new { m.Id, m.Number, m.WorkOrderId, ContractId = m.WorkOrderId == null ? (int?)null : m.WorkOrder.ContractId, Open = m.Lines.Count(l => l.IssuedQuantity < l.Quantity) }).ToListAsync(ct);
            return rows.Select(m => Item("MaterialRequest", m.Id, m.Number, m.ContractId, m.WorkOrderId, $"{m.Open} ردیف درخواست تاییدشده هنوز کامل حواله نشده است.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = InventoryRoles.RoleWarehouse, Link = MrLink(item.TargetId), Title = "حواله‌ی کالای درخواست " + item.TargetNumber };
    }

    /// <summary>WH.DOC.CONFIRM: a draft issue (of a material request) or receipt (of a purchase order) is confirmed by the storekeeper after the goods are counted: the confirmation is the physical fact.</summary>
    internal sealed class DocumentConfirmStep : WhStep
    {
        public override string Code => "WH.DOC.CONFIRM";
        public override string AgentCode => AgentCodes.Wh;
        public override string Title => "تایید سند حواله یا رسید انبار پس از شمارش";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<InventoryDocument>().AsNoTracking().Where(d => !d.Deleted && d.Status == InventoryDocumentStatus.Draft && (d.MaterialRequestId != null || d.PurchaseOrderId != null) && d.Lines.Any())
                .OrderBy(d => d.Id).Take(ProcStepCommon.Cap).Select(d => new { d.Id, d.Number, d.MaterialRequestId, d.PurchaseOrderId }).ToListAsync(ct);
            return rows.Select(d => Item("InventoryDocument", d.Id, d.Number, null, null, d.PurchaseOrderId.HasValue ? "رسید خرید پیش‌نویس منتظر تایید پس از شمارش است." : "حواله‌ی پیش‌نویس منتظر تایید پس از تحویل کالا است.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = InventoryRoles.RoleWarehouse, Link = "/inventory/documents/" + item.TargetId.ToString(CultureInfo.InvariantCulture), Title = "تایید سند انبار " + item.TargetNumber };
    }

    /// <summary>WH.INSPECT: a received lot that waits for its incoming inspection is inspected by quality (physical measurement; the result is recorded by the inspector).</summary>
    internal sealed class InspectionStep : WhStep
    {
        public override string Code => "WH.INSPECT";
        public override string AgentCode => AgentCodes.Wh;
        public override string Title => "بازرسی ورودی کالای دریافت‌شده";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<QcInspection>().AsNoTracking().Where(q => !q.Deleted && q.Status == InspectionStatus.Pending)
                .OrderBy(q => q.Id).Take(ProcStepCommon.Cap).Select(q => new { q.Id, q.Number, q.Quantity, Item = q.Item.Title }).ToListAsync(ct);
            return rows.Select(q => Item("QcInspection", q.Id, q.Number, null, null, $"بازرسی ورودی {q.Item} ({q.Quantity.ToString("0.####", CultureInfo.InvariantCulture)}) منتظر ثبت نتیجه است.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = "QC", Link = "/inventory/inspections", Title = "بازرسی ورودی " + item.TargetNumber };
    }
}
