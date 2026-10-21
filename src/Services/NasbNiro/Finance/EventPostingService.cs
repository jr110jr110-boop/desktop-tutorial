using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    public interface IEventPostingService
    {
        /// <summary>Turns every accounting event that has no voucher yet into one Auto voucher (status Checked). Idempotent.</summary>
        Task<PostEventsResultDto> PostPendingAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// The one posting path: every money movement of the sub-ledgers (stock, procurement, treasury, receivables, ...) is an
    /// accounting event with account roles; this service maps roles to ledger accounts and creates the vouchers.
    /// The system is the preparer (status Checked); the finance manager finalises.
    /// </summary>
    public class EventPostingService : IEventPostingService
    {
        public const string DirectRolePrefix = "Account:";
        private readonly IUnitOfWork _uow;
        private static readonly AccountingEventStatus[] Postable =
        {
            AccountingEventStatus.Pending, AccountingEventStatus.Exported, AccountingEventStatus.Failed, AccountingEventStatus.Sent,
        };

        public EventPostingService(IUnitOfWork uow)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
        }

        private static string Trunc(string s, int max) => s != null && s.Length > max ? s.Substring(0, max) : s;

        /// <summary>A role is either a mapped account role or "Account:NNNNN" (a ledger account by code, for postings chosen by people such as petty-cash expenses).</summary>
        private static FnAccount AccountFor(Dictionary<string, FnAccount> map, Dictionary<string, FnAccount> byCode, string role)
        {
            if (role.StartsWith(DirectRolePrefix, StringComparison.Ordinal))
                return byCode.TryGetValue(role.Substring(DirectRolePrefix.Length), out FnAccount direct) && direct.IsActive && direct.Level == FnAccountLevel.Subsidiary ? direct : null;
            if (map.TryGetValue(role, out FnAccount found)) return found;
            int dot = role.IndexOf('.');
            return dot > 0 && map.TryGetValue(role.Substring(0, dot), out found) ? found : null;
        }

        public async Task<PostEventsResultDto> PostPendingAsync(CancellationToken ct)
        {
            var result = new PostEventsResultDto();
            await FinanceSeed.EnsureAsync(_uow, ct);

            Dictionary<int, FnAccount> accountById = await _uow.Set<FnAccount>().ToDictionaryAsync(a => a.Id, ct);
            List<AccountMapping> maps = await _uow.Set<AccountMapping>().Where(m => m.IsActive && m.AccountId != null).ToListAsync(ct);
            var roleAccount = new Dictionary<string, FnAccount>(StringComparer.OrdinalIgnoreCase);
            foreach (AccountMapping m in maps)
                if (accountById.TryGetValue(m.AccountId.Value, out FnAccount a) && a.IsActive && a.Level == FnAccountLevel.Subsidiary) roleAccount[m.Role] = a;

            Dictionary<string, FnAccount> accountByCode = accountById.Values.ToDictionary(a => a.Code);
            // periods per legal entity (F3): the main company keeps its Persian calendar, other entities have their own years
            var periodsByEntity = new Dictionary<int, PeriodCache>();
            Dictionary<int, FnLegalEntity> entities = await _uow.Set<FnLegalEntity>().ToDictionaryAsync(x => x.Id, ct);
            var details = new DetailResolver(_uow);
            Dictionary<string, int> costCenters = await _uow.Set<CostCenter>().ToDictionaryAsync(c => c.Code, c => c.Id, StringComparer.OrdinalIgnoreCase, ct);
            var voucherOfEvent = new Dictionary<int, FnVoucher>();
            int lastId = 0;

            while (true)
            {
                List<AccountingEvent> batch = await _uow.Set<AccountingEvent>().Include(e => e.Lines)
                    .Where(e => e.Id > lastId && Postable.Contains(e.Status)).OrderBy(e => e.Id).Take(200).AsSplitQuery().ToListAsync(ct);
                if (batch.Count == 0) break;
                lastId = batch[batch.Count - 1].Id;

                List<int> ids = batch.Select(e => e.Id).ToList();
                List<int> already = await _uow.Set<FnVoucher>().Where(v => v.AccountingEventId != null && ids.Contains(v.AccountingEventId.Value))
                    .Select(v => v.AccountingEventId.Value).ToListAsync(ct);
                List<int> reverses = batch.Where(e => e.ReversesEventId.HasValue).Select(e => e.ReversesEventId.Value).Distinct().ToList();
                Dictionary<int, int> originalVoucher = reverses.Count == 0 ? new Dictionary<int, int>()
                    : await _uow.Set<FnVoucher>().Where(v => v.AccountingEventId != null && reverses.Contains(v.AccountingEventId.Value))
                        .ToDictionaryAsync(v => v.AccountingEventId.Value, v => v.Id, ct);
                List<int> workOrderIds = batch.SelectMany(e => e.Lines).Where(l => l.WorkOrderId.HasValue).Select(l => l.WorkOrderId.Value).Distinct().ToList();
                Dictionary<int, int> workOrderContract = workOrderIds.Count == 0 ? new Dictionary<int, int>()
                    : await _uow.Set<WorkOrder>().Where(w => workOrderIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.ContractId, ct);

                foreach (AccountingEvent e in batch)
                {
                    if (already.Contains(e.Id)) { e.Status = AccountingEventStatus.Posted; continue; }
                    List<AccountingEventLine> lines = e.Lines.OrderBy(l => l.RowNumber).Where(l => R(l.Debit) != 0 || R(l.Credit) != 0).ToList();
                    if (lines.Count == 0) { e.Status = AccountingEventStatus.NotRequired; result.NotRequired++; continue; }

                    string error = null;
                    int entityId = e.LegalEntityId <= 0 ? FinanceSeed.MainEntityId : e.LegalEntityId;
                    if (!periodsByEntity.TryGetValue(entityId, out PeriodCache periods))
                    {
                        periods = await PeriodCache.LoadAsync(_uow, ct, entityId);
                        periodsByEntity[entityId] = periods;
                    }
                    entities.TryGetValue(entityId, out FnLegalEntity entity);
                    var resolved = await ResolveDateAsync(e.EventDate, periods, entity, ct);
                    if (resolved.Error != null) error = resolved.Error;

                    // accounts first: no half-built voucher when a role has no mapping
                    var accounts = new List<FnAccount>();
                    if (error == null)
                    {
                        foreach (AccountingEventLine l in lines)
                        {
                            FnAccount acc = AccountFor(roleAccount, accountByCode, l.AccountRole);
                            if (acc == null) { error = $"نقش حساب «{l.AccountRole}» به حساب دفتر کل نگاشت نشده است."; break; }
                            accounts.Add(acc);
                        }
                    }
                    if (error == null)
                    {
                        decimal d = lines.Sum(l => R(l.Debit)), c = lines.Sum(l => R(l.Credit));
                        if (d != c)
                        {
                            // rounding at line level may leave a few rial: absorb into the largest line of the larger side
                            decimal diff = d - c;
                            if (Math.Abs(diff) > lines.Count) error = "رویداد پس از گرد کردن به ریال متوازن نیست.";
                        }
                    }
                    if (error != null)
                    {
                        e.Error = error.Length > 2000 ? error.Substring(0, 2000) : error;
                        e.Attempts++;
                        result.Failed++;
                        if (result.Errors.Count < 20) result.Errors.Add($"رویداد {e.Id} ({e.SourceNumber}): {error}");
                        continue;
                    }

                    FnFiscalYear year = resolved.Year;
                    DateTime date = resolved.Late ? resolved.Period.StartDate : e.EventDate.Date;
                    string lateNote = resolved.Late ? $"[ثبت با تاخیر - تاریخ اصلی {PersianDate(e.EventDate)}] " : "";
                    var voucher = new FnVoucher
                    {
                        FiscalYearId = year.Id, FiscalYear = year, Date = date, Status = FnVoucherStatus.Checked, Type = FnVoucherType.Auto,
                        TemporaryNumber = await InventoryNumbers.NextAsync(_uow, FinanceNumbers.Voucher, year.Year, ct),
                        Description = Trunc((lateNote + (e.Description ?? "")).Trim(), 1000), SourceType = e.SourceType, SourceId = e.SourceId, SourceNumber = e.SourceNumber,
                        AccountingEventId = e.Id, LatePosting = resolved.Late, CreatedByUserId = 0, CreatedAt = DateTimeOffset.Now,
                        CheckedByUserId = 0, CheckedAt = DateTimeOffset.Now, Lines = new List<FnVoucherLine>(),
                    };
                    if (e.ReversesEventId.HasValue)
                    {
                        if (voucherOfEvent.TryGetValue(e.ReversesEventId.Value, out FnVoucher original)) { voucher.Type = FnVoucherType.Reversal; voucher.ReversesVoucher = original; }
                        else if (originalVoucher.TryGetValue(e.ReversesEventId.Value, out int originalId)) { voucher.Type = FnVoucherType.Reversal; voucher.ReversesVoucherId = originalId; }
                    }

                    int row = 1;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        AccountingEventLine l = lines[i];
                        FnDetailAccount d1 = null, d2 = null;
                        if (l.ProviderId.HasValue) d1 = await details.GetAsync(FnDetailType.Supplier, l.ProviderId.Value, ct);
                        else if (!string.IsNullOrEmpty(l.PartyType) && l.PartyId.HasValue)
                        {
                            FnDetailType? pt = DetailResolver.PartyType(l.PartyType);
                            if (pt.HasValue) d1 = await details.GetAsync(pt.Value, l.PartyId.Value, ct);
                        }
                        int? contractId = l.ContractId;
                        int? costCenterId = null;
                        if (l.WorkOrderId.HasValue)
                        {
                            d2 = await details.GetAsync(FnDetailType.WorkOrder, l.WorkOrderId.Value, ct);
                            if (!contractId.HasValue && workOrderContract.TryGetValue(l.WorkOrderId.Value, out int woContract) && woContract > 0) contractId = woContract;
                        }
                        else if (contractId.HasValue) d2 = await details.GetAsync(FnDetailType.Project, contractId.Value, ct);
                        else if (!string.IsNullOrWhiteSpace(l.CostCenter) && costCenters.TryGetValue(l.CostCenter.Trim(), out int ccId))
                        {
                            costCenterId = ccId;
                            d2 = await details.GetAsync(FnDetailType.CostCenter, ccId, ct);
                        }
                        var line = new FnVoucherLine
                        {
                            RowNumber = row++, Account = accounts[i], Detail1 = d1, Detail2 = d2, Debit = R(l.Debit), Credit = R(l.Credit),
                            CurrencyCode = l.CurrencyCode, CurrencyAmount = l.CurrencyAmount, Rate = l.Rate, Quantity = l.Quantity,
                            Description = Trunc((lateNote + (l.Description ?? "")).Trim(), 500),
                            WorkOrderId = l.WorkOrderId, ContractId = contractId, CostCenterId = costCenterId, SourceRole = l.AccountRole,
                        };
                        voucher.Lines.Add(line);
                    }
                    // absorb a rounding difference of a few rial
                    decimal debit = voucher.Lines.Sum(x => x.Debit), credit = voucher.Lines.Sum(x => x.Credit);
                    if (debit != credit)
                    {
                        decimal diff = debit - credit;
                        FnVoucherLine target = diff > 0
                            ? voucher.Lines.Where(x => x.Credit > 0).OrderByDescending(x => x.Credit).First()
                            : voucher.Lines.Where(x => x.Debit > 0).OrderByDescending(x => x.Debit).First();
                        if (diff > 0) target.Credit += diff; else target.Debit += -diff;
                    }
                    voucher.TotalAmount = voucher.Lines.Sum(x => x.Debit);
                    _uow.Set<FnVoucher>().Add(voucher);
                    voucherOfEvent[e.Id] = voucher;
                    e.Status = AccountingEventStatus.Posted;
                    e.Error = null;
                    result.Posted++;
                    if (resolved.Late) result.LatePosted++;
                }

                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateException ex)
                {
                    result.Errors.Add("ثبت اسناد ناموفق بود: " + (ex.InnerException?.Message ?? ex.Message));
                    result.Failed += batch.Count;
                    return result;
                }
            }
            return result;
        }

        private async Task<(FnFiscalYear Year, FnPeriod Period, bool Late, string Error)> ResolveDateAsync(DateTime date, PeriodCache cache, FnLegalEntity entity, CancellationToken ct)
        {
            FnPeriod p = cache.Find(date);
            if (p == null)
            {
                int y = FinanceSeed.YearNumberOf(entity, date);
                int latest = cache.Years.Count == 0 ? 0 : cache.Years.Max(x => x.Year);
                if (cache.Years.Count == 0 || y <= latest + 1)
                {
                    FnFiscalYear created = await FinanceSeed.EnsureEntityYearAsync(_uow, entity, date, ct);
                    cache.Add(created);
                    p = cache.Find(date);
                }
                if (p == null) return (null, null, false, $"سال مالی {y} تعریف نشده است.");
            }
            if (p.Status == FnPeriodStatus.Open && p.FiscalYear.Status == FnYearStatus.Open) return (p.FiscalYear, p, false, null);
            FnPeriod target = cache.FirstOpenAfter(date) ?? cache.FirstOpen();
            if (target == null) return (null, null, false, "هیچ دوره مالی بازی برای ثبت وجود ندارد.");
            return (target.FiscalYear, target, true, null);
        }
    }
}
