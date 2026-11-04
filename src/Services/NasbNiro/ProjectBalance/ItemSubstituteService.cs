using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProjectBalance;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProductBank;
using ViewModels.NasbNiro.ProjectBalanceDtos;

namespace Services.NasbNiro.ProjectBalance
{
    public interface IItemSubstituteService
    {
        Task<ResponseDto<List<SubstituteDto>>> GetAsync(SubstituteQuery query, CancellationToken ct);
        Task<ResponseDto<SubstituteDto>> SaveAsync(SaveSubstituteDto dto, CancellationToken ct);
        Task<ResponseDto<SubstituteDto>> DecideAsync(SubstituteDecisionDto dto, CancellationToken ct);
    }

    /// <summary>
    /// The approved substitutes of an item (global, or only for one project). Engineering proposes; another engineering user (or PMO) approves; the balance uses only approved, valid substitutes.
    /// </summary>
    public sealed class ItemSubstituteService : PbServiceBase, IItemSubstituteService
    {
        public ItemSubstituteService(IUnitOfWork uow, IInventoryUserContext user) : base(uow, user)
        {
        }

        public async Task<ResponseDto<List<SubstituteDto>>> GetAsync(SubstituteQuery query, CancellationToken ct)
        {
            query ??= new SubstituteQuery();
            IQueryable<ItemSubstitute> q = Uow.Set<ItemSubstitute>().AsNoTracking().Where(x => !x.Deleted);
            if (query.ItemId.HasValue) q = q.Where(x => x.ItemId == query.ItemId.Value || x.SubstituteItemId == query.ItemId.Value);
            if (query.Status.HasValue) q = q.Where(x => x.Status == query.Status.Value);
            return ResponseDtoHelper.Ok(await DtosAsync(await q.OrderByDescending(x => x.Id).Take(1000).ToListAsync(ct), ct));
        }

        private async Task<List<SubstituteDto>> DtosAsync(List<ItemSubstitute> rows, CancellationToken ct)
        {
            List<int> ids = rows.Select(r => r.ItemId).Concat(rows.Select(r => r.SubstituteItemId)).Distinct().ToList();
            var items = ids.Count == 0 ? new Dictionary<int, (string Code, string Title, string Unit)>() : (await Uow.Set<InventoryItem>().AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.Code, i.Title, i.Unit }).ToListAsync(ct)).ToDictionary(i => i.Id, i => (i.Code, i.Title, i.Unit));
            List<int> woIds = rows.Where(r => r.WorkOrderId.HasValue).Select(r => r.WorkOrderId.Value).Distinct().ToList();
            var orders = woIds.Count == 0 ? new Dictionary<int, string>() : (await Uow.Set<WorkOrder>().AsNoTracking().Where(w => woIds.Contains(w.Id)).Select(w => new { w.Id, w.Code }).ToListAsync(ct)).ToDictionary(w => w.Id, w => w.Code);
            List<int> cIds = rows.Where(r => r.ContractId.HasValue).Select(r => r.ContractId.Value).Distinct().ToList();
            var contracts = cIds.Count == 0 ? new Dictionary<int, string>() : (await Uow.Set<ContractEntity>().AsNoTracking().Where(c => cIds.Contains(c.Id)).Select(c => new { c.Id, c.Code }).ToListAsync(ct)).ToDictionary(c => c.Id, c => c.Code);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(Uow, rows.Select(r => (int?)r.ProposedByUserId).Concat(rows.Select(r => r.DecidedByUserId)), ct);
            int me = User.UserId;
            bool human = !IsAi;
            return rows.Select(r =>
            {
                items.TryGetValue(r.ItemId, out var a);
                items.TryGetValue(r.SubstituteItemId, out var b);
                return new SubstituteDto
                {
                    Id = r.Id, ItemId = r.ItemId, ItemCode = a.Code, ItemTitle = a.Title, ItemUnit = a.Unit, SubstituteItemId = r.SubstituteItemId, SubstituteItemCode = b.Code, SubstituteItemTitle = b.Title, SubstituteItemUnit = b.Unit,
                    Ratio = r.Ratio, Scope = r.Scope, ContractId = r.ContractId, ContractCode = r.ContractId.HasValue && contracts.TryGetValue(r.ContractId.Value, out string cc) ? cc : null,
                    WorkOrderId = r.WorkOrderId, WorkOrderCode = r.WorkOrderId.HasValue && orders.TryGetValue(r.WorkOrderId.Value, out string wc) ? wc : null, ValidFrom = r.ValidFrom, ValidTo = r.ValidTo, Reason = r.Reason, Status = r.Status,
                    ProposedByUserId = r.ProposedByUserId, ProposedByName = names.Name(r.ProposedByUserId), DecidedByUserId = r.DecidedByUserId, DecidedByName = names.Name(r.DecidedByUserId), DecidedAt = r.DecidedAt, DecisionNote = r.DecisionNote,
                    CanDecide = human && r.Status == SubstituteStatus.Proposed && InRole(BalanceRoles.SubstituteApprovers) && r.ProposedByUserId != me,
                    CanEdit = r.Status == SubstituteStatus.Proposed && r.ProposedByUserId == me && InRole(BalanceRoles.SubstituteProposers),
                    CanRevoke = human && r.Status == SubstituteStatus.Approved && InRole(BalanceRoles.SubstituteApprovers),
                };
            }).ToList();
        }

        public async Task<ResponseDto<SubstituteDto>> SaveAsync(SaveSubstituteDto dto, CancellationToken ct)
        {
            if (!InRole(BalanceRoles.SubstituteProposers)) return ResponseDtoHelper.BadRequest<SubstituteDto>(BalanceMessages.NotAllowed);
            if (dto == null || dto.ItemId <= 0 || dto.SubstituteItemId <= 0) return ResponseDtoHelper.BadRequest<SubstituteDto>(BalanceMessages.InvalidData);
            if (dto.ItemId == dto.SubstituteItemId) return ResponseDtoHelper.BadRequest<SubstituteDto>("کالا نمی‌تواند جایگزین خودش باشد.");
            decimal ratio = BalanceRules.Round(dto.Ratio);
            if (ratio <= 0) return ResponseDtoHelper.BadRequest<SubstituteDto>("نسبت جایگزینی باید بیشتر از صفر باشد (چند واحد جایگزین به‌جای یک واحد کالای اصلی).");
            if (dto.ValidFrom.HasValue && dto.ValidTo.HasValue && dto.ValidTo.Value.Date < dto.ValidFrom.Value.Date) return ResponseDtoHelper.BadRequest<SubstituteDto>("پایان اعتبار قبل از شروع آن است.");
            string reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();
            if (reason == null) return ResponseDtoHelper.BadRequest<SubstituteDto>("دلیل فنی جایگزینی را بنویسید.");
            if (await Uow.Set<InventoryItem>().CountAsync(i => (i.Id == dto.ItemId || i.Id == dto.SubstituteItemId) && !i.Deleted && i.IsActive, ct) != 2) return ResponseDtoHelper.BadRequest<SubstituteDto>("کالای فعال پیدا نشد.");

            int? contractId = null; int? workOrderId = null;
            if (dto.Scope == SubstituteScope.Project)
            {
                if (!dto.WorkOrderId.HasValue) return ResponseDtoHelper.BadRequest<SubstituteDto>("برای جایگزین مخصوص پروژه، دستور کار را انتخاب کنید.");
                var wo = await Uow.Set<WorkOrder>().AsNoTracking().Where(w => w.Id == dto.WorkOrderId.Value).Select(w => new { w.Id, w.ContractId }).FirstOrDefaultAsync(ct);
                if (wo == null) return ResponseDtoHelper.BadRequest<SubstituteDto>("دستور کار پیدا نشد.");
                contractId = wo.ContractId; workOrderId = wo.Id;
            }

            ItemSubstitute row;
            if (dto.Id.HasValue && dto.Id.Value > 0)
            {
                row = await Uow.Set<ItemSubstitute>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (row == null) return ResponseDtoHelper.NotFound<SubstituteDto>(BalanceMessages.NotFound);
                if (row.Status != SubstituteStatus.Proposed) return ResponseDtoHelper.BadRequest<SubstituteDto>("جایگزین تصمیم‌گیری‌شده ویرایش نمی‌شود؛ یکی تازه پیشنهاد دهید.");
                if (row.ProposedByUserId != User.UserId) return ResponseDtoHelper.BadRequest<SubstituteDto>("فقط پیشنهاد‌دهنده پیشنهاد خودش را ویرایش می‌کند.");
            }
            else
            {
                row = new ItemSubstitute { Status = SubstituteStatus.Proposed, ProposedByUserId = User.UserId };
                Uow.Set<ItemSubstitute>().Add(row);
            }
            bool duplicate = await Uow.Set<ItemSubstitute>().AnyAsync(x => !x.Deleted && x.Id != row.Id && x.ItemId == dto.ItemId && x.SubstituteItemId == dto.SubstituteItemId && x.Scope == dto.Scope && x.ContractId == contractId && x.WorkOrderId == workOrderId
                && (x.Status == SubstituteStatus.Proposed || x.Status == SubstituteStatus.Approved), ct);
            if (duplicate) return ResponseDtoHelper.BadRequest<SubstituteDto>("همین جایگزین قبلاً پیشنهاد یا تایید شده است.");
            row.ItemId = dto.ItemId; row.SubstituteItemId = dto.SubstituteItemId; row.Ratio = ratio; row.Scope = dto.Scope; row.ContractId = contractId; row.WorkOrderId = workOrderId;
            row.ValidFrom = dto.ValidFrom?.Date; row.ValidTo = dto.ValidTo?.Date; row.Reason = reason.Length > 500 ? reason.Substring(0, 500) : reason;
            Stamp(row, row.Id == 0);
            string saved = await TrySaveAsync(ct);
            if (saved != null) return ResponseDtoHelper.BadRequest<SubstituteDto>(saved);
            return ResponseDtoHelper.Ok((await DtosAsync(new List<ItemSubstitute> { row }, ct)).First());
        }

        public async Task<ResponseDto<SubstituteDto>> DecideAsync(SubstituteDecisionDto dto, CancellationToken ct)
        {
            if (!InRole(BalanceRoles.SubstituteApprovers)) return ResponseDtoHelper.BadRequest<SubstituteDto>(BalanceMessages.NotAllowed);
            if (IsAi) return ResponseDtoHelper.BadRequest<SubstituteDto>(BalanceMessages.AiNeverApproves);
            if (dto == null) return ResponseDtoHelper.BadRequest<SubstituteDto>(BalanceMessages.InvalidData);
            ItemSubstitute row = await Uow.Set<ItemSubstitute>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (row == null) return ResponseDtoHelper.NotFound<SubstituteDto>(BalanceMessages.NotFound);
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            string note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            switch (action)
            {
                case "approve":
                    if (row.Status != SubstituteStatus.Proposed) return ResponseDtoHelper.BadRequest<SubstituteDto>("فقط پیشنهاد در انتظار تایید می‌شود.");
                    if (row.ProposedByUserId == User.UserId) return ResponseDtoHelper.BadRequest<SubstituteDto>(BalanceMessages.SameUser);
                    row.Status = SubstituteStatus.Approved;
                    break;
                case "reject":
                    if (row.Status != SubstituteStatus.Proposed) return ResponseDtoHelper.BadRequest<SubstituteDto>("فقط پیشنهاد در انتظار رد می‌شود.");
                    if (note == null) return ResponseDtoHelper.BadRequest<SubstituteDto>("برای رد، دلیل را بنویسید.");
                    row.Status = SubstituteStatus.Rejected;
                    break;
                case "revoke":
                    if (row.Status != SubstituteStatus.Approved) return ResponseDtoHelper.BadRequest<SubstituteDto>("فقط جایگزین تاییدشده لغو می‌شود.");
                    if (note == null) return ResponseDtoHelper.BadRequest<SubstituteDto>("برای لغو، دلیل را بنویسید.");
                    row.Status = SubstituteStatus.Revoked;
                    break;
                default:
                    return ResponseDtoHelper.BadRequest<SubstituteDto>(BalanceMessages.InvalidData);
            }
            row.DecidedByUserId = User.UserId; row.DecidedAt = DateTimeOffset.Now; row.DecisionNote = note != null && note.Length > 500 ? note.Substring(0, 500) : note;
            Stamp(row, false);
            string s2 = await TrySaveAsync(ct);
            if (s2 != null) return ResponseDtoHelper.BadRequest<SubstituteDto>(s2);
            return ResponseDtoHelper.Ok((await DtosAsync(new List<ItemSubstitute> { row }, ct)).First());
        }
    }
}
