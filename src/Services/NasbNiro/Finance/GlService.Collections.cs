using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using ViewModels.NasbNiro.FinanceDtos;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    public interface ICollectionsService
    {
        Task<ResponseDto<FnBadDebtDto>> BadDebtAsync(FnBadDebtQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<FnCreditHoldDto>> SetCreditHoldAsync(FnSetHoldDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<FnCreditHoldDto>>> GetCreditHoldsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<List<FnDunningDto>>> RunDunningAsync(DateTime? asOf, CancellationToken cancellationToken);
        Task<ResponseDto<List<FnDunningDto>>> GetDunningAsync(CancellationToken cancellationToken);
        Task<ResponseDto<FnDunningDto>> ApproveDunningAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<FnProposalDto>> CreateProposalAsync(FnProposalQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<FnProposalDto>> GetProposalAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<List<FnProposalDto>>> GetProposalsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<FnProposalDto>> ApproveProposalAsync(int id, CancellationToken cancellationToken);
    }

    // Receivables and payables control (F3 section 10): doubtful-debt provision, dunning, credit hold, payment proposal.
    public partial class GlService : ICollectionsService
    {
        private sealed class OpenInvoice { public int CustomerId; public string CustomerName; public decimal Open; public int Days; }

        /// <summary>Open posted invoices on a date with the days past due (due date, or the invoice date when there is none).</summary>
        private async Task<List<OpenInvoice>> OpenInvoicesAsync(DateTime day, CancellationToken ct)
        {
            var invoices = await _uow.Set<FnSalesInvoice>().AsNoTracking()
                .Where(i => i.Status == FnDocStatus.Posted && i.Type != FnInvoiceType.CreditNote && i.Date <= day)
                .Select(i => new { i.Id, i.CustomerId, CustomerName = i.Customer.Name, i.Date, i.DueDate, i.ReceivableAmount }).ToListAsync(ct);
            var ids = invoices.Select(i => i.Id).ToList();
            var paid = ids.Count == 0 ? new Dictionary<int, decimal>() : (await _uow.Set<FnReceiptAllocation>().AsNoTracking().Where(a => ids.Contains(a.InvoiceId) && a.Receipt.Status == FnDocStatus.Posted && a.Receipt.Date <= day)
                .GroupBy(a => a.InvoiceId).Select(g => new { g.Key, Amount = g.Sum(a => a.Amount) }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.Amount);
            var credited = ids.Count == 0 ? new Dictionary<int, decimal>() : (await _uow.Set<FnSalesInvoice>().AsNoTracking()
                .Where(i => i.CreditedInvoiceId != null && ids.Contains(i.CreditedInvoiceId.Value) && i.Type == FnInvoiceType.CreditNote && i.Status == FnDocStatus.Posted && i.Date <= day)
                .GroupBy(i => i.CreditedInvoiceId.Value).Select(g => new { g.Key, Amount = g.Sum(i => i.ReceivableAmount) }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.Amount);
            var list = new List<OpenInvoice>();
            foreach (var i in invoices)
            {
                decimal open = i.ReceivableAmount - (paid.TryGetValue(i.Id, out decimal p) ? p : 0) - (credited.TryGetValue(i.Id, out decimal c) ? c : 0);
                if (open <= 0) continue;
                list.Add(new OpenInvoice { CustomerId = i.CustomerId, CustomerName = i.CustomerName, Open = open, Days = (int)(day - (i.DueDate ?? i.Date).Date).TotalDays });
            }
            return list;
        }

        private static string BucketOf(int days) => days <= 30 ? "Current" : days <= 60 ? "31-60" : days <= 90 ? "61-90" : "Over90";

        public async Task<ResponseDto<FnBadDebtDto>> BadDebtAsync(FnBadDebtQuery q, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnBadDebtDto>(NotAllowed);
            if (q == null || !q.CurrentRate.HasValue || !q.Days31To60Rate.HasValue || !q.Days61To90Rate.HasValue || !q.Over90Rate.HasValue)
                return Bad<FnBadDebtDto>("نرخ ذخیره هر گروه سنی را وارد کنید (نرخ‌ها ورودی مالی است و پیش‌فرض ندارد).");
            decimal[] rates = { q.CurrentRate.Value, q.Days31To60Rate.Value, q.Days61To90Rate.Value, q.Over90Rate.Value };
            if (rates.Any(r => r < 0 || r > 100)) return Bad<FnBadDebtDto>("نرخ باید بین صفر و ۱۰۰ باشد.");
            if (q.Post && !_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnBadDebtDto>(NotAllowed);
            DateTime day = (q.AsOf ?? DateTime.Today).Date;
            var open = await OpenInvoicesAsync(day, ct);
            string[] names = { "Current", "31-60", "61-90", "Over90" };
            var dto = new FnBadDebtDto { AsOf = day };
            for (int k = 0; k < 4; k++)
            {
                decimal receivable = open.Where(o => BucketOf(o.Days) == names[k]).Sum(o => o.Open);
                decimal required = R(receivable * rates[k] / 100m);
                dto.Buckets.Add(new FnBadDebtBucketDto { Bucket = names[k], Receivable = receivable, RatePercent = rates[k], Required = required });
            }
            dto.Required = dto.Buckets.Sum(b => b.Required);
            FnAccount allowance = await AccountByCodeAsync("10309", ct), expense = await AccountByCodeAsync("80304", ct);
            if (allowance == null || expense == null) return Bad<FnBadDebtDto>("حساب‌های ۱۰۳۰۹ و ۸۰۳۰۴ پیدا نشد.");
            int entityId = await EntityScope.ResolveAsync(_uow, null, Me, ct);
            // the balance of the allowance account (credit) up to the date, all vouchers except drafts
            decimal credit = await _uow.Set<FnVoucherLine>().AsNoTracking().Where(l => l.AccountId == allowance.Id && l.Voucher.Status != FnVoucherStatus.Draft && l.Voucher.Date <= day && l.Voucher.Type != FnVoucherType.Closing)
                .SumAsync(l => (decimal?)(l.Credit - l.Debit), ct) ?? 0;
            dto.PreviousBalance = credit;
            dto.Change = dto.Required - credit;
            if (!q.Post) return ResponseDtoHelper.Ok(dto);
            if (dto.Change == 0) return Bad<FnBadDebtDto>("ذخیره فعلی با مقدار لازم برابر است؛ سندی لازم نیست.");
            string number = await NextNumberAsync("BD", FinanceNumbers.BadDebt, day, ct);
            string text = (dto.Change > 0 ? "افزایش" : "کاهش") + $" ذخیره مطالبات مشکوک‌الوصول تا {PersianDate(day)} ({number})";
            decimal abs = Math.Abs(dto.Change);
            var lines = dto.Change > 0
                ? new List<AutoLine> { new AutoLine { Account = expense, Debit = abs, Description = text }, new AutoLine { Account = allowance, Credit = abs, Description = text } }
                : new List<AutoLine> { new AutoLine { Account = allowance, Debit = abs, Description = text }, new AutoLine { Account = expense, Credit = abs, Description = text } };
            var v = await AddAutoVoucherAsync(entityId, day, text, "BadDebt", number, lines, FnVoucherType.Auto, null, ct);
            if (v.Error != null) return Bad<FnBadDebtDto>(v.Error);
            await _uow.SaveChangesAsync(ct);
            var run = new FnBadDebtRun { Number = number, LegalEntityId = entityId, AsOf = day, Required = dto.Required, PreviousBalance = credit, Change = dto.Change, RatesJson = JsonConvert.SerializeObject(new { rates }), VoucherId = v.Voucher.Id, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now };
            _uow.Set<FnBadDebtRun>().Add(run);
            await _uow.SaveChangesAsync(ct);
            dto.RunId = run.Id; dto.Number = number; dto.VoucherId = v.Voucher.Id;
            return ResponseDtoHelper.Ok(dto);
        }

        // ------------------------------------------------------------------------------------------ credit hold

        public async Task<ResponseDto<FnCreditHoldDto>> SetCreditHoldAsync(FnSetHoldDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnCreditHoldDto>(NotAllowed);
            if (dto == null) return Bad<FnCreditHoldDto>("اطلاعات ناقص است.");
            if (dto.Active && Clean(dto.Reason) == null) return Bad<FnCreditHoldDto>("دلیل توقف اعتبار را بنویسید.");
            FnCustomer c = await _uow.Set<FnCustomer>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.CustomerId, ct);
            if (c == null) return ResponseDtoHelper.NotFound<FnCreditHoldDto>("مشتری پیدا نشد.");
            FnCreditHold h = await _uow.Set<FnCreditHold>().FirstOrDefaultAsync(x => x.CustomerId == dto.CustomerId, ct);
            if (h == null) { h = new FnCreditHold { CustomerId = dto.CustomerId }; _uow.Set<FnCreditHold>().Add(h); }
            h.Active = dto.Active; h.Reason = Clean(dto.Reason); h.SetByUserId = Me; h.SetAt = DateTimeOffset.Now;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new FnCreditHoldDto { CustomerId = c.Id, CustomerName = c.Name, Active = h.Active, Reason = h.Reason });
        }

        public async Task<ResponseDto<List<FnCreditHoldDto>>> GetCreditHoldsAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FnCreditHoldDto>>(NotAllowed);
            var rows = await _uow.Set<FnCreditHold>().AsNoTracking().Where(h => h.Active).Join(_uow.Set<FnCustomer>().AsNoTracking(), h => h.CustomerId, c => c.Id, (h, c) => new FnCreditHoldDto { CustomerId = c.Id, CustomerName = c.Name, Active = h.Active, Reason = h.Reason }).OrderBy(x => x.CustomerName).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows);
        }

        // ------------------------------------------------------------------------------------------ dunning

        private async Task<FnDunningDto> DunningDtoAsync(FnDunning d, string name, CancellationToken ct) =>
            new FnDunningDto { Id = d.Id, Number = d.Number, CustomerId = d.CustomerId, CustomerName = name ?? await _uow.Set<FnCustomer>().AsNoTracking().Where(c => c.Id == d.CustomerId).Select(c => c.Name).FirstOrDefaultAsync(ct), AsOf = d.AsOf, Bucket = d.Bucket, Amount = d.Amount, Text = d.Text, Status = d.Status };

        /// <summary>Drafts one reminder per customer with amounts more than 30 days past due. The letter module sends approved drafts later; nothing is sent here.</summary>
        public async Task<ResponseDto<List<FnDunningDto>>> RunDunningAsync(DateTime? asOf, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<List<FnDunningDto>>(NotAllowed);
            DateTime day = (asOf ?? DateTime.Today).Date;
            var late = (await OpenInvoicesAsync(day, ct)).Where(o => o.Days > 30).GroupBy(o => o.CustomerId).OrderBy(g => g.Key).ToList();
            var result = new List<FnDunningDto>();
            var made = new List<(FnDunning D, string Name)>();
            foreach (var g in late)
            {
                // one draft per customer and date
                if (await _uow.Set<FnDunning>().AnyAsync(x => x.CustomerId == g.Key && x.AsOf == day, ct)) continue;
                int worst = g.Max(o => o.Days);
                decimal amount = g.Sum(o => o.Open);
                string name = g.First().CustomerName;
                var d = new FnDunning
                {
                    Number = await NextNumberAsync("DN", FinanceNumbers.DunningLetter, day, ct), CustomerId = g.Key, AsOf = day, Bucket = BucketOf(worst), Amount = amount, Status = 0, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now,
                    Text = $"{name} گرامی، بر اساس حساب‌های ما تا تاریخ {PersianDate(day)} مبلغ {amount:N0} ریال از صورت‌حساب‌های شما بیش از ۳۰ روز از سررسید گذشته است (بیشترین تاخیر {worst} روز). لطفاً نسبت به تسویه اقدام فرمایید.",
                };
                _uow.Set<FnDunning>().Add(d);
                await _uow.SaveChangesAsync(ct); // the number counter needs each draft saved before the next number is drawn
                made.Add((d, name));
            }
            foreach (var m in made) result.Add(await DunningDtoAsync(m.D, m.Name, ct));
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<List<FnDunningDto>>> GetDunningAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FnDunningDto>>(NotAllowed);
            var rows = await _uow.Set<FnDunning>().AsNoTracking().OrderByDescending(d => d.Id).Take(500).Join(_uow.Set<FnCustomer>().AsNoTracking(), d => d.CustomerId, c => c.Id,
                (d, c) => new FnDunningDto { Id = d.Id, Number = d.Number, CustomerId = d.CustomerId, CustomerName = c.Name, AsOf = d.AsOf, Bucket = d.Bucket, Amount = d.Amount, Text = d.Text, Status = d.Status }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows);
        }

        public async Task<ResponseDto<FnDunningDto>> ApproveDunningAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<FnDunningDto>(NotAllowed + " (مدیر مالی)");
            FnDunning d = await _uow.Set<FnDunning>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (d == null) return ResponseDtoHelper.NotFound<FnDunningDto>("یادآوری پیدا نشد.");
            if (d.Status != 0) return Bad<FnDunningDto>("این یادآوری قبلاً تایید شده است.");
            d.Status = 1; d.ApprovedByUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await DunningDtoAsync(d, null, ct));
        }

        // ------------------------------------------------------------------------------------------ payment proposal

        private static int TermsDays(string terms, int fallback)
        {
            if (string.IsNullOrWhiteSpace(terms)) return fallback;
            Match m = Regex.Match(Digits(terms), "\\d{1,3}");
            return m.Success ? int.Parse(m.Value) : fallback;
        }

        private static FnProposalDto ProposalDto(FnPaymentProposal p) => new FnProposalDto
        {
            Id = p.Id, Number = p.Number, AsOf = p.AsOf, CashLimit = p.CashLimit, Total = p.Total, Status = p.Status,
            Lines = (p.Lines ?? new List<FnPaymentProposalLine>()).OrderBy(l => l.DueDate).ThenBy(l => l.Id).Select(l => new FnProposalLineDto { SupplierInvoiceId = l.SupplierInvoiceId, ProviderId = l.ProviderId, ProviderName = l.ProviderName, InvoiceNumber = l.InvoiceNumber, DueDate = l.DueDate, Amount = l.Amount, Included = l.Included }).ToList(),
        };

        /// <summary>Approved supplier invoices that no payment request covers yet, due on or before the date (invoice date + days of the order's payment terms), earliest first, while the cash limit allows.</summary>
        public async Task<ResponseDto<FnProposalDto>> CreateProposalAsync(FnProposalQuery q, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return Bad<FnProposalDto>(NotAllowed);
            DateTime day = (q?.AsOf ?? DateTime.Today).Date;
            decimal limit = q?.CashLimit ?? 0;
            if (limit < 0) return Bad<FnProposalDto>("سقف نقدی نامعتبر است.");
            int fallback = int.TryParse(await SettingAsync(FinanceSeed.SettingPayableTerms, "30", ct), out int f) ? f : 30;
            var covered = _uow.Set<Entities.Model.NasbNiro.Procurement.PaymentRequest>().AsNoTracking().Where(r => !r.Deleted && r.RejectedAt == null && r.InvoiceId != null).Select(r => r.InvoiceId.Value);
            var invoices = await _uow.Set<Entities.Model.NasbNiro.Procurement.SupplierInvoice>().AsNoTracking()
                .Where(i => !i.Deleted && i.Status == Entities.Model.NasbNiro.Procurement.SupplierInvoiceStatus.Approved && !covered.Contains(i.Id))
                .Select(i => new { i.Id, i.ProviderId, ProviderName = i.Provider.Name, i.InvoiceNumber, i.Number, i.InvoiceDate, i.TotalAmount, Terms = i.PurchaseOrder.PaymentTerms }).ToListAsync(ct);
            var due = invoices.Select(i => new { i, Due = i.InvoiceDate.Date.AddDays(TermsDays(i.Terms, fallback)) }).Where(x => x.Due <= day).OrderBy(x => x.Due).ThenBy(x => x.i.Id).ToList();
            if (due.Count == 0) return Bad<FnProposalDto>("فاکتور سررسیدشده‌ای برای پیشنهاد پرداخت نیست.");
            var p = new FnPaymentProposal { Number = await NextNumberAsync("PP", FinanceNumbers.PaymentProposal, day, ct), AsOf = day, CashLimit = limit, Status = 0, PreparedByUserId = Me, PreparedAt = DateTimeOffset.Now, Lines = new List<FnPaymentProposalLine>() };
            decimal running = 0;
            foreach (var x in due)
            {
                bool include = limit == 0 || running + x.i.TotalAmount <= limit;
                if (include) running += x.i.TotalAmount;
                p.Lines.Add(new FnPaymentProposalLine { SupplierInvoiceId = x.i.Id, ProviderId = x.i.ProviderId, ProviderName = x.i.ProviderName, InvoiceNumber = x.i.InvoiceNumber ?? x.i.Number, DueDate = x.Due, Amount = x.i.TotalAmount, Included = include });
            }
            p.Total = running;
            _uow.Set<FnPaymentProposal>().Add(p);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ProposalDto(p));
        }

        public async Task<ResponseDto<FnProposalDto>> GetProposalAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants) && !_user.IsInRole(FinanceRoles.Readers)) return Bad<FnProposalDto>(NotAllowed);
            FnPaymentProposal p = await _uow.Set<FnPaymentProposal>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            return p == null ? ResponseDtoHelper.NotFound<FnProposalDto>("پیشنهاد پرداخت پیدا نشد.") : ResponseDtoHelper.Ok(ProposalDto(p));
        }

        public async Task<ResponseDto<List<FnProposalDto>>> GetProposalsAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FnProposalDto>>(NotAllowed);
            var rows = await _uow.Set<FnPaymentProposal>().AsNoTracking().OrderByDescending(x => x.Id).Take(200).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(ProposalDto).ToList());
        }

        public async Task<ResponseDto<FnProposalDto>> ApproveProposalAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<FnProposalDto>(NotAllowed + " (مدیر مالی)");
            FnPaymentProposal p = await _uow.Set<FnPaymentProposal>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p == null) return ResponseDtoHelper.NotFound<FnProposalDto>("پیشنهاد پرداخت پیدا نشد.");
            if (p.Status != 0) return Bad<FnProposalDto>("این پیشنهاد قبلاً تایید شده است.");
            if (p.PreparedByUserId == Me) return Bad<FnProposalDto>("تهیه‌کننده نمی‌تواند پیشنهاد پرداخت خودش را تایید کند.");
            p.Status = 1; p.ApprovedByUserId = Me; p.ApprovedAt = DateTimeOffset.Now;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ProposalDto(p));
        }
    }
}
