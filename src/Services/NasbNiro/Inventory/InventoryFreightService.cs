using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using ViewModels.NasbNiro.InventoryDtos;

namespace Services.NasbNiro.Inventory
{
    public interface IInventoryFreightService
    {
        Task<ResponseDto<List<FreightDto>>> GetAsync(FreightQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<FreightDto>> GetByIdAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<FreightDto>> SaveAsync(SaveFreightDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FreightDto>> ApproveAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<FreightDto>> VoidAsync(int id, VoidFreightDto dto, CancellationToken cancellationToken);

        /// <summary>Called inside the cancel of a receipt, before SaveChanges: drafts are voided, approved freight is taken back out of the average cost with a reversing event.</summary>
        Task VoidForCancelledReceiptAsync(InventoryDocument receipt, string reason, CancellationToken cancellationToken);
    }

    public static class FreightRoles
    {
        /// <summary>Who prepares a freight document: the storekeepers.</summary>
        public const string Writers = InventoryRoles.Documents;

        /// <summary>The accountant who approves: finance only, never the person who prepared the document.</summary>
        public const string Approvers = "FINANCIAL,FINANCEMANAGER";

        public const string Readers = InventoryRoles.Documents + ",FINANCIAL,FINANCEMANAGER,TREASURY,AUDITOR,CEO,PLANNING";
    }

    /// <summary>
    /// PB5 (F1, IAS 2.11): freight and landed cost. The accountant approves a freight document of a confirmed purchase receipt; the amount is spread over the receipt lines (weight for metals, value for the
    /// rest) and added to the weighted-average value of each item. Never in a closed period; reversed when the receipt is cancelled or the document is voided.
    /// </summary>
    public class InventoryFreightService : IInventoryFreightService
    {
        public const string SourceType = "InventoryFreight";

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ICostingService _costing;

        public InventoryFreightService(IUnitOfWork uow, IInventoryUserContext user, ICostingService costing)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _costing = costing ?? throw new ArgumentNullException(nameof(costing));
        }

        private static decimal R(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);

        // ------------------------------------------------------------------------------------------ reads

        private async Task<FreightDto> ToDtoAsync(InventoryFreight f, CancellationToken ct) => (await ToDtosAsync(new List<InventoryFreight> { f }, ct))[0];

        /// <summary>M8: all freights of a page in a fixed number of queries (receipt numbers, carriers, allocations and user names are read once for the whole list).</summary>
        private async Task<List<FreightDto>> ToDtosAsync(List<InventoryFreight> list, CancellationToken ct)
        {
            if (list.Count == 0) return new List<FreightDto>();
            List<int> ids = list.Select(f => f.Id).ToList();
            List<int> receiptIds = list.Select(f => f.ReceiptDocumentId).Distinct().ToList();
            List<int> carrierIds = list.Where(f => f.CarrierProviderId.HasValue).Select(f => f.CarrierProviderId.Value).Distinct().ToList();
            Dictionary<int, string> receiptNumbers = await _uow.Set<InventoryDocument>().AsNoTracking().Where(d => receiptIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Number, ct);
            Dictionary<int, string> carriers = carrierIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => carrierIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            var rows = await _uow.Set<InventoryFreightAllocation>().AsNoTracking().Where(a => ids.Contains(a.FreightId))
                .Select(a => new
                {
                    a.FreightId,
                    Row = new FreightAllocationDto
                    {
                        DocumentLineId = a.DocumentLineId, RowNumber = a.DocumentLine.RowNumber, ItemId = a.ItemId, ItemCode = a.Item.Code, ItemTitle = a.Item.Title,
                        Basis = a.Basis, BasisAmount = a.BasisAmount, Amount = a.Amount,
                    },
                }).ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, list.SelectMany(f => new int?[] { f.CreatorUserId, f.ApprovedByUserId }).ToArray(), ct);
            bool sees = InventoryPriceAccess.SeesValues(_user);
            var result = new List<FreightDto>();
            foreach (InventoryFreight f in list)
            {
                List<FreightAllocationDto> mine = rows.Where(r => r.FreightId == f.Id).Select(r => r.Row).OrderBy(r => r.RowNumber).ToList();
                if (!sees) foreach (FreightAllocationDto a in mine) { a.Amount = null; if (a.Basis == FreightBasis.Value) a.BasisAmount = null; }
                result.Add(new FreightDto
                {
                    Id = f.Id, Number = f.Number, ReceiptDocumentId = f.ReceiptDocumentId, ReceiptNumber = receiptNumbers.TryGetValue(f.ReceiptDocumentId, out string rn) ? rn : null, FreightDate = f.FreightDate,
                    VehiclePlate = f.VehiclePlate, GoodsType = f.GoodsType, BillOfLadingNumber = f.BillOfLadingNumber, CarrierProviderId = f.CarrierProviderId,
                    CarrierName = f.CarrierProviderId.HasValue && carriers.TryGetValue(f.CarrierProviderId.Value, out string cn) ? cn : null, Amount = sees ? f.Amount : (decimal?)null,
                    Description = f.Description, Status = f.Status, CreatorName = names.Name(f.CreatorUserId), ApprovedAt = f.ApprovedAt, ApprovedByName = names.Name(f.ApprovedByUserId),
                    VoidedAt = f.VoidedAt, VoidReason = f.VoidReason, Allocations = mine,
                });
            }
            return result;
        }

        public async Task<ResponseDto<List<FreightDto>>> GetAsync(FreightQuery query, CancellationToken cancellationToken)
        {
            query ??= new FreightQuery();
            IQueryable<InventoryFreight> q = _uow.Set<InventoryFreight>().AsNoTracking().Where(f => !f.Deleted);
            if (query.ReceiptDocumentId.HasValue) q = q.Where(f => f.ReceiptDocumentId == query.ReceiptDocumentId.Value);
            if (query.Status.HasValue) q = q.Where(f => f.Status == query.Status.Value);
            List<InventoryFreight> list = await q.OrderByDescending(f => f.Id).Take(500).ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await ToDtosAsync(list, cancellationToken));
        }

        public async Task<ResponseDto<FreightDto>> GetByIdAsync(int id, CancellationToken cancellationToken)
        {
            InventoryFreight f = await _uow.Set<InventoryFreight>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            return f == null ? ResponseDtoHelper.NotFound<FreightDto>(InventoryMessages.NotFound) : ResponseDtoHelper.Ok(await ToDtoAsync(f, cancellationToken));
        }

        // ------------------------------------------------------------------------------------------ allocation

        private async Task<(List<FreightRules.Share> Shares, string Error)> ComputeAsync(InventoryDocument receipt, decimal amount, CancellationToken ct)
        {
            var lines = await _uow.Set<InventoryDocumentLine>().AsNoTracking().Where(l => l.DocumentId == receipt.Id)
                .Select(l => new { l.Id, l.ItemId, l.Quantity, l.UnitPrice, l.Item.Unit, l.Item.Weight, l.Item.GroupCode }).ToListAsync(ct);
            var inputs = lines.Select(l => new FreightRules.LineInput
            {
                LineId = l.Id, ItemId = l.ItemId, IsMetal = FreightRules.IsMetal(l.GroupCode), WeightKg = FreightRules.WeightKg(l.Unit, l.Quantity, l.Weight),
                Value = R(l.Quantity * (l.UnitPrice ?? 0m)),
            }).ToList();
            List<FreightRules.Share> shares = FreightRules.Allocate(amount, inputs, out string error);
            return (shares, error);
        }

        private List<InventoryFreightAllocation> ReplaceAllocations(InventoryFreight f, List<FreightRules.Share> shares)
        {
            List<InventoryFreightAllocation> old = _uow.Set<InventoryFreightAllocation>().Where(a => a.FreightId == f.Id).ToList();
            _uow.Set<InventoryFreightAllocation>().RemoveRange(old);
            var created = new List<InventoryFreightAllocation>();
            foreach (FreightRules.Share s in shares)
            {
                var row = new InventoryFreightAllocation { Freight = f, DocumentLineId = s.LineId, ItemId = s.ItemId, Basis = s.Basis, BasisAmount = s.BasisAmount, Amount = s.Amount };
                _uow.Set<InventoryFreightAllocation>().Add(row);
                created.Add(row);
            }
            return created;
        }

        // ------------------------------------------------------------------------------------------ save

        public async Task<ResponseDto<FreightDto>> SaveAsync(SaveFreightDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<FreightDto>(InventoryMessages.InvalidData);
            if (dto.FreightDate == default) return ResponseDtoHelper.BadRequest<FreightDto>("تاریخ کرایه را وارد کنید.");
            decimal amount = R(dto.Amount);
            if (amount <= 0) return ResponseDtoHelper.BadRequest<FreightDto>("مبلغ کرایه باید بیشتر از صفر باشد.");
            InventoryDocument receipt = await _uow.Set<InventoryDocument>().FirstOrDefaultAsync(d => d.Id == dto.ReceiptDocumentId && !d.Deleted, cancellationToken);
            if (receipt == null) return ResponseDtoHelper.NotFound<FreightDto>(InventoryMessages.DocumentNotFound);
            if (receipt.Type != InventoryDocumentType.PurchaseReceipt || receipt.Status != InventoryDocumentStatus.Confirmed)
                return ResponseDtoHelper.BadRequest<FreightDto>("کرایه حمل فقط برای رسید خرید تاییدشده ثبت می‌شود.");
            if (dto.CarrierProviderId.HasValue && !await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AnyAsync(p => p.Id == dto.CarrierProviderId.Value && !p.Deleted, cancellationToken))
                return ResponseDtoHelper.BadRequest<FreightDto>("حمل‌کننده پیدا نشد.");

            InventoryFreight f;
            if (dto.Id.HasValue)
            {
                f = await _uow.Set<InventoryFreight>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (f == null) return ResponseDtoHelper.NotFound<FreightDto>(InventoryMessages.NotFound);
                if (f.Status != FreightStatus.Draft) return ResponseDtoHelper.BadRequest<FreightDto>("فقط سند کرایه پیش‌نویس قابل ویرایش است.");
                if (f.ReceiptDocumentId != receipt.Id) return ResponseDtoHelper.BadRequest<FreightDto>("رسید سند کرایه قابل تغییر نیست.");
            }
            else
            {
                f = new InventoryFreight { ReceiptDocumentId = receipt.Id, Status = FreightStatus.Draft, CreateDate = DateTimeOffset.Now, CreatorUserId = _user.UserId };
                _uow.Set<InventoryFreight>().Add(f);
            }

            var computed = await ComputeAsync(receipt, amount, cancellationToken);
            if (computed.Error != null) return ResponseDtoHelper.BadRequest<FreightDto>(computed.Error);

            f.FreightDate = dto.FreightDate.Date;
            f.VehiclePlate = InventoryText.Clean(dto.VehiclePlate);
            f.GoodsType = InventoryText.Clean(dto.GoodsType);
            f.BillOfLadingNumber = InventoryText.Clean(dto.BillOfLadingNumber);
            f.CarrierProviderId = dto.CarrierProviderId;
            f.Amount = amount;
            f.Description = InventoryText.Clean(dto.Description);
            f.ModifyDate = DateTimeOffset.Now;
            f.ModifierUserId = _user.UserId;
            if (f.Number == null) f.Number = await InventoryNumbers.NextNumberAsync(_uow, InventoryNumbers.Freight, "FR", f.FreightDate, cancellationToken);
            if (f.Id != 0) ReplaceAllocations(f, computed.Shares);
            else foreach (FreightRules.Share s in computed.Shares)
                _uow.Set<InventoryFreightAllocation>().Add(new InventoryFreightAllocation { Freight = f, DocumentLineId = s.LineId, ItemId = s.ItemId, Basis = s.Basis, BasisAmount = s.BasisAmount, Amount = s.Amount });
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await ToDtoAsync(f, cancellationToken));
        }

        // ------------------------------------------------------------------------------------------ approve

        public async Task<ResponseDto<FreightDto>> ApproveAsync(int id, CancellationToken cancellationToken)
        {
            if (Ai.AiActing.IsActing) return ResponseDtoHelper.BadRequest<FreightDto>("تایید کرایه کار انسان است؛ صندلی هوش مصنوعی تایید نمی‌کند.");
            if (!_user.IsInRole(FreightRoles.Approvers)) return ResponseDtoHelper.BadRequest<FreightDto>(InventoryMessages.NotAllowed + " (نقش مالی لازم است)");
            InventoryFreight f = await _uow.Set<InventoryFreight>().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (f == null) return ResponseDtoHelper.NotFound<FreightDto>(InventoryMessages.NotFound);
            if (f.Status != FreightStatus.Draft) return ResponseDtoHelper.BadRequest<FreightDto>("این سند کرایه پیش‌نویس نیست.");
            int me = _user.UserId;
            if (me == f.CreatorUserId || me == f.ModifierUserId) return ResponseDtoHelper.BadRequest<FreightDto>(InventoryMessages.SameUser);

            InventoryDocument receipt = await _uow.Set<InventoryDocument>().FirstOrDefaultAsync(d => d.Id == f.ReceiptDocumentId, cancellationToken);
            if (receipt == null || receipt.Status != InventoryDocumentStatus.Confirmed) return ResponseDtoHelper.BadRequest<FreightDto>("رسید این کرایه تاییدشده نیست (ابطال شده است).");

            // never in a closed period: neither the date of the freight nor the day of the posting
            foreach (DateTime day in new[] { f.FreightDate, DateTime.Today })
            {
                string closed = await ClosedPeriodGuard.MessageAsync(_uow, day, cancellationToken);
                if (closed != null) return ResponseDtoHelper.BadRequest<FreightDto>("تایید کرایه ممکن نیست: " + closed);
            }

            var computed = await ComputeAsync(receipt, f.Amount, cancellationToken);
            if (computed.Error != null) return ResponseDtoHelper.BadRequest<FreightDto>(computed.Error);
            List<InventoryFreightAllocation> allocations = ReplaceAllocations(f, computed.Shares);

            // weighted average: the amount joins the value of the item; no quantity is added
            List<int> itemIds = computed.Shares.Select(s => s.ItemId).Distinct().ToList();
            Dictionary<int, ItemCost> costs = await _uow.Set<ItemCost>().Where(c => itemIds.Contains(c.ItemId)).ToDictionaryAsync(c => c.ItemId, cancellationToken);
            Dictionary<int, string> codes = await _uow.Set<InventoryItem>().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Code, cancellationToken);
            var roleNet = new Dictionary<string, decimal>();
            decimal residual = 0;
            foreach (var g in computed.Shares.GroupBy(s => s.ItemId))
            {
                decimal amount = g.Sum(s => s.Amount);
                if (amount == 0) continue;
                bool onStock = costs.TryGetValue(g.Key, out ItemCost cost) && cost.Quantity > 0;
                // H2: remember per allocation how much went into stock and how much to revaluation; a void reverses exactly those amounts
                foreach (InventoryFreightAllocation a in allocations.Where(x => x.ItemId == g.Key))
                {
                    a.StockAmount = onStock ? a.Amount : 0m;
                    a.RevaluationAmount = onStock ? 0m : a.Amount;
                }
                if (onStock)
                {
                    cost.Value += amount;
                    cost.AverageCost = Math.Round(cost.Value / cost.Quantity, 4);
                    cost.UpdatedAt = DateTimeOffset.Now;
                    string role = AccountRoles.InventoryOf(codes[g.Key]);
                    roleNet[role] = (roleNet.TryGetValue(role, out decimal v) ? v : 0) + amount;
                }
                else
                {
                    // the stock of the item has been used up already: the freight is a cost of the period, not of stock
                    residual += amount;
                }
            }
            var eventLines = new List<AccountingEventLine>();
            foreach (var pair in roleNet.OrderBy(p => p.Key)) eventLines.Add(new AccountingEventLine { AccountRole = pair.Key, Debit = pair.Value });
            if (residual != 0) eventLines.Add(new AccountingEventLine { AccountRole = AccountRoles.Revaluation, Debit = residual });
            eventLines.Add(new AccountingEventLine
            {
                AccountRole = f.CarrierProviderId.HasValue ? AccountRoles.AccountsPayable : AccountRoles.OtherPayable, Credit = f.Amount, ProviderId = f.CarrierProviderId,
                Description = "کرایه حمل " + (f.BillOfLadingNumber ?? f.VehiclePlate),
            });
            _costing.AddEvent(AccountingEventType.PurchaseReceipt, f.FreightDate, SourceType, f.Id, f.Number, "کرایه حمل رسید " + receipt.Number, eventLines);

            f.Status = FreightStatus.Approved;
            f.ApprovedAt = DateTimeOffset.Now;
            f.ApprovedByUserId = me;
            f.ModifyDate = DateTimeOffset.Now;
            f.ModifierUserId = me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await ToDtoAsync(f, cancellationToken));
        }

        // ------------------------------------------------------------------------------------------ void

        public async Task<ResponseDto<FreightDto>> VoidAsync(int id, VoidFreightDto dto, CancellationToken cancellationToken)
        {
            string reason = InventoryText.Clean(dto?.Reason);
            if (reason == null) return ResponseDtoHelper.BadRequest<FreightDto>("علت ابطال را بنویسید.");
            if (Ai.AiActing.IsActing) return ResponseDtoHelper.BadRequest<FreightDto>("ابطال کرایه کار انسان است.");
            if (!_user.IsInRole(FreightRoles.Approvers + "," + FreightRoles.Writers)) return ResponseDtoHelper.BadRequest<FreightDto>(InventoryMessages.NotAllowed);
            InventoryFreight f = await _uow.Set<InventoryFreight>().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (f == null) return ResponseDtoHelper.NotFound<FreightDto>(InventoryMessages.NotFound);
            if (f.Status == FreightStatus.Voided) return ResponseDtoHelper.BadRequest<FreightDto>("این سند کرایه قبلاً ابطال شده است.");
            // an approved freight is reversed by an accountant only (the writer of the document cannot undo the accountant's approval)
            if (f.Status == FreightStatus.Approved && !_user.IsInRole(FreightRoles.Approvers)) return ResponseDtoHelper.BadRequest<FreightDto>(InventoryMessages.NotAllowed + " (ابطال کرایه تاییدشده با مالی است)");
            if (f.Status == FreightStatus.Approved)
            {
                string closed = await ClosedPeriodGuard.MessageAsync(_uow, DateTime.Today, cancellationToken);
                if (closed != null) return ResponseDtoHelper.BadRequest<FreightDto>("ابطال کرایه ممکن نیست: " + closed);
            }
            await VoidCoreAsync(f, reason, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await ToDtoAsync(f, cancellationToken));
        }

        public async Task VoidForCancelledReceiptAsync(InventoryDocument receipt, string reason, CancellationToken cancellationToken)
        {
            List<InventoryFreight> list = await _uow.Set<InventoryFreight>().Where(f => f.ReceiptDocumentId == receipt.Id && !f.Deleted && f.Status != FreightStatus.Voided).ToListAsync(cancellationToken);
            foreach (InventoryFreight f in list) await VoidCoreAsync(f, reason, cancellationToken);
        }

        private async Task VoidCoreAsync(InventoryFreight f, string reason, CancellationToken ct)
        {
            if (f.Status == FreightStatus.Approved)
            {
                List<InventoryFreightAllocation> rows = await _uow.Set<InventoryFreightAllocation>().Where(a => a.FreightId == f.Id).ToListAsync(ct);
                List<int> itemIds = rows.Select(a => a.ItemId).Distinct().ToList();
                Dictionary<int, ItemCost> costs = await _uow.Set<ItemCost>().Where(c => itemIds.Contains(c.ItemId)).ToDictionaryAsync(c => c.ItemId, ct);
                Dictionary<int, string> codes = await _uow.Set<InventoryItem>().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Code, ct);
                var leftover = new List<AccountingEventLine>();
                foreach (var g in rows.GroupBy(a => a.ItemId))
                {
                    // exactly the amount that was added at approval, no clamp: the ledger and the item cost move by the same figure
                    decimal stock = g.Sum(a => a.StockAmount);
                    if (stock == 0 || !costs.TryGetValue(g.Key, out ItemCost cost)) continue;
                    cost.Value -= stock;
                    if (cost.Quantity > 0)
                    {
                        cost.AverageCost = Math.Round(cost.Value / cost.Quantity, 4);
                    }
                    else if (cost.Value != 0)
                    {
                        // no stock is left to carry the difference: it goes to revaluation, as the costing does when the quantity reaches zero
                        decimal rest = cost.Value;
                        cost.Value = 0;
                        string role = AccountRoles.InventoryOf(codes[g.Key]);
                        leftover.Add(new AccountingEventLine { AccountRole = role, Credit = rest > 0 ? rest : 0, Debit = rest < 0 ? -rest : 0 });
                        leftover.Add(new AccountingEventLine { AccountRole = AccountRoles.Revaluation, Debit = rest > 0 ? rest : 0, Credit = rest < 0 ? -rest : 0 });
                    }
                    cost.UpdatedAt = DateTimeOffset.Now;
                }
                await ReverseEventsAsync(f, ct);
                if (leftover.Count > 0)
                    _costing.AddEvent(AccountingEventType.Reversal, DateTime.Today, SourceType, f.Id, f.Number, "مانده ارزش پس از ابطال کرایه", leftover);
            }
            f.Status = FreightStatus.Voided;
            f.VoidedAt = DateTimeOffset.Now;
            f.VoidedByUserId = _user.UserId;
            f.VoidReason = reason.Length > 500 ? reason.Substring(0, 500) : reason;
            f.ModifyDate = DateTimeOffset.Now;
            f.ModifierUserId = _user.UserId;
        }

        /// <summary>One reversing event per original event; an event that never left the system is simply marked not required.</summary>
        private async Task ReverseEventsAsync(InventoryFreight f, CancellationToken ct)
        {
            List<AccountingEvent> events = await _uow.Set<AccountingEvent>().Include(e => e.Lines)
                .Where(e => e.SourceType == SourceType && e.SourceId == f.Id && e.Type != AccountingEventType.Reversal).ToListAsync(ct);
            List<int> eventIds = events.Select(x => x.Id).ToList();
            List<int> voucherEvents = await _uow.Set<FnVoucher>().Where(v => v.AccountingEventId != null && eventIds.Contains(v.AccountingEventId.Value)).Select(v => v.AccountingEventId.Value).ToListAsync(ct);
            foreach (AccountingEvent e in events)
            {
                if ((e.Status == AccountingEventStatus.Pending || e.Status == AccountingEventStatus.Failed) && !voucherEvents.Contains(e.Id))
                {
                    e.Status = AccountingEventStatus.NotRequired;
                    e.Error = "سند مبدا باطل شد پیش از ارسال.";
                    continue;
                }
                if (e.Status == AccountingEventStatus.NotRequired) continue;
                AccountingEvent reversal = _costing.AddEvent(AccountingEventType.Reversal, DateTime.Today, e.SourceType, e.SourceId, e.SourceNumber, "ابطال " + e.Description,
                    e.Lines.Select(l => new AccountingEventLine
                    {
                        AccountRole = l.AccountRole, Debit = l.Credit, Credit = l.Debit, ProviderId = l.ProviderId, WorkOrderId = l.WorkOrderId, ContractId = l.ContractId,
                        CostCenter = l.CostCenter, ItemId = l.ItemId, Quantity = l.Quantity, Description = l.Description, LabourProtected = l.LabourProtected,
                    }).ToList());
                reversal.ReversesEventId = e.Id;
            }
        }
    }
}
