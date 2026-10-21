using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Providers;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    public interface ITreasuryService
    {
        Task<ResponseDto<PagedList<BankAccountDto>>> GetBankAccountsAsync(TreasuryQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<BankAccountDto>> SaveBankAccountAsync(SaveBankAccountDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<CashBoxDto>>> GetCashBoxesAsync(TreasuryQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<CashBoxDto>> SaveCashBoxAsync(SaveCashBoxDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PettyCashDto>>> GetPettyCashesAsync(TreasuryQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PettyCashDto>> SavePettyCashAsync(SavePettyCashDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PayableRequestDto>>> GetPayableRequestsAsync(TreasuryQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PaymentDto>>> GetPaymentsAsync(TreasuryQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PaymentDto>> GetPaymentAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PaymentDto>> SavePaymentAsync(SavePaymentDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PaymentDto>> PaymentActionAsync(DocActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<ReceiptDto>>> GetReceiptsAsync(TreasuryQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<ReceiptDto>> GetReceiptAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<ReceiptDto>> SaveReceiptAsync(SaveReceiptDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ReceiptDto>> ReceiptActionAsync(DocActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<ReceivedChequeDto>>> GetReceivedChequesAsync(TreasuryQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<ReceivedChequeDto>> ReceivedChequeActionAsync(DocActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<ChequeBookDto>>> GetChequeBooksAsync(TreasuryQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<ChequeBookDto>> SaveChequeBookAsync(SaveChequeBookDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<IssuedChequeDto>>> GetIssuedChequesAsync(IssuedChequeQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<IssuedChequeDto>> IssuedChequeActionAsync(DocActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<GuaranteeDto>>> GetGuaranteesAsync(TreasuryQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<GuaranteeDto>> SaveGuaranteeAsync(SaveGuaranteeDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<GuaranteeDto>> GuaranteeActionAsync(DocActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<SettlementDto>>> GetSettlementsAsync(TreasuryQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<SettlementDto>> SaveSettlementAsync(SaveSettlementDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<SettlementDto>> SettlementActionAsync(DocActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PettyCandidateDto>>> GetPettyCandidatesAsync(int pettyCashId, TreasuryQuery query, CancellationToken cancellationToken);

        Task<ResponseDto<BankStatementDto>> ImportStatementAsync(ImportStatementDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<BankStatementDto>>> GetStatementsAsync(TreasuryQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<BankStatementDto>> GetStatementAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<BankStatementDto>> AutoMatchAsync(int statementId, CancellationToken cancellationToken);
        Task<ResponseDto<BankStatementDto>> MatchAsync(StatementMatchDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ReconciliationDto>> CreateReconciliationAsync(CreateReconciliationDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<ReconciliationDto>>> GetReconciliationsAsync(TreasuryQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<ReconciliationDto>> ReconciliationActionAsync(DocActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<CashPositionDto>> GetCashPositionAsync(CancellationToken cancellationToken);

        /// <summary>
        /// The old "payment request paid" endpoint: records the treasury payment document and the accounting event in the caller's
        /// SaveChanges (no second Bank event). Returns an error text, or null when the request may be marked paid.
        /// </summary>
        Task<string> AddLegacyPaymentAsync(PaymentRequest request, string reference, CancellationToken cancellationToken);
    }

    /// <summary>Treasury: bank accounts, cash boxes, petty cash, payments and receipts, cheques, guarantees, bank statements and reconciliation.</summary>
    public partial class TreasuryService : ITreasuryService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ICostingService _costing;

        public TreasuryService(IUnitOfWork uow, IInventoryUserContext user, ICostingService costing)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _costing = costing ?? throw new ArgumentNullException(nameof(costing));
        }

        private int Me => _user.UserId;
        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private const string NotAllowed = "شما مجوز این کار را ندارید";
        private const string SameUser = "این کار باید توسط شخص دیگری غیر از ثبت‌کننده یا تاییدکننده قبلی انجام شود.";
        private const string Conflict = "همزمان توسط کاربر دیگری تغییر کرد؛ دوباره تلاش کنید.";
        private static readonly AccountingEventStatus[] NoEvent = { AccountingEventStatus.NotRequired };

        private static PagedList<T> Page<T>(List<T> all, int total, int number, int size)
        {
            PagedList<T> page = PagedList<T>.CreateAsync(all, 1, 0, CancellationToken.None).GetAwaiter().GetResult();
            page.TotalCount = total; page.CurrentPage = number; page.PageSize = size; page.TotalPages = (int)Math.Ceiling(total / (double)Math.Max(size, 1));
            return page;
        }

        private static string NormalizeIban(string iban) => iban == null ? null : Clean(Digits(iban.Replace(" ", "")))?.ToUpperInvariant();

        /// <summary>Balance (debit minus credit) of every party of a type, from the accounting events that are not "not required".</summary>
        internal async Task<Dictionary<int, decimal>> PartyBalancesAsync(string partyType, CancellationToken ct)
        {
            var rows = await _uow.Set<AccountingEventLine>().AsNoTracking()
                .Where(l => l.PartyType == partyType && l.PartyId != null && l.Event.Status != AccountingEventStatus.NotRequired)
                .GroupBy(l => l.PartyId.Value).Select(g => new { g.Key, Balance = g.Sum(x => x.Debit) - g.Sum(x => x.Credit) }).ToListAsync(ct);
            return rows.ToDictionary(x => x.Key, x => x.Balance);
        }

        // ================================================================================== bank accounts

        public async Task<ResponseDto<PagedList<BankAccountDto>>> GetBankAccountsAsync(TreasuryQuery query, CancellationToken ct)
        {
            query ??= new TreasuryQuery();
            await FinanceSeed.EnsureAsync(_uow, ct);
            IQueryable<FnBankAccount> q = _uow.Set<FnBankAccount>().AsNoTracking();
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(b => b.Title.Contains(text) || b.BankName.Contains(text) || b.AccountNumber.Contains(text) || b.Iban.Contains(text));
            int total = await q.CountAsync(ct);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize, number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<FnBankAccount> rows = await q.Include(b => b.Account).OrderBy(b => b.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            Dictionary<int, decimal> balances = await PartyBalancesAsync("BankAccount", ct);
            List<int> ids = rows.Select(r => r.Id).ToList();
            Dictionary<int, int> details = await _uow.Set<FnDetailAccount>().AsNoTracking().Where(d => d.Type == FnDetailType.Bank && d.LinkedEntityId != null && ids.Contains(d.LinkedEntityId.Value))
                .ToDictionaryAsync(d => d.LinkedEntityId.Value, d => d.Id, ct);
            return ResponseDtoHelper.Ok(Page(rows.Select(b => ToDto(b, balances, details)).ToList(), total, number, size));
        }

        private static BankAccountDto ToDto(FnBankAccount b, Dictionary<int, decimal> balances, Dictionary<int, int> details) => new BankAccountDto
        {
            Id = b.Id, Title = b.Title, BankName = b.BankName, Branch = b.Branch, AccountNumber = b.AccountNumber, Iban = b.Iban, CurrencyCode = b.CurrencyCode,
            AccountId = b.AccountId, AccountCode = b.Account?.Code, DetailAccountId = details.TryGetValue(b.Id, out int d) ? d : (int?)null, IsActive = b.IsActive,
            Balance = balances.TryGetValue(b.Id, out decimal bal) ? bal : 0,
        };

        public async Task<ResponseDto<BankAccountDto>> SaveBankAccountAsync(SaveBankAccountDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryUsers)) return Bad<BankAccountDto>(NotAllowed + " (خزانه‌داری)");
            if (dto == null || Clean(dto.Title) == null) return Bad<BankAccountDto>("عنوان حساب بانکی را وارد کنید.");
            string iban = NormalizeIban(dto.Iban);
            if (iban == null || !IbanValid(iban)) return Bad<BankAccountDto>("شماره شبا نامعتبر است (IR و ۲۴ رقم با کنترل mod-97).");
            if (await _uow.Set<FnBankAccount>().AnyAsync(b => b.Iban == iban && b.Id != (dto.Id ?? 0), ct)) return Bad<BankAccountDto>("این شماره شبا قبلاً ثبت شده است.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            string currency = (Clean(dto.CurrencyCode) ?? "IRR").ToUpperInvariant();
            string accountCode = currency == "IRR" ? "10102" : "10103";
            int accountId = await _uow.Set<FnAccount>().Where(a => a.Code == accountCode).Select(a => a.Id).FirstAsync(ct);
            FnBankAccount b;
            if (dto.Id.HasValue)
            {
                b = await _uow.Set<FnBankAccount>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (b == null) return ResponseDtoHelper.NotFound<BankAccountDto>("حساب بانکی پیدا نشد.");
                if (b.CurrencyCode != currency && await _uow.Set<AccountingEventLine>().AnyAsync(l => l.PartyType == "BankAccount" && l.PartyId == b.Id, ct))
                    return Bad<BankAccountDto>("ارز حساب بانکی دارای گردش تغییر نمی‌کند.");
            }
            else
            {
                b = new FnBankAccount { CreatedAt = DateTimeOffset.Now, CreatedByUserId = Me };
                _uow.Set<FnBankAccount>().Add(b);
            }
            b.Title = Clean(dto.Title); b.BankName = Clean(dto.BankName); b.Branch = Clean(dto.Branch); b.AccountNumber = Clean(dto.AccountNumber);
            b.Iban = iban; b.CurrencyCode = currency; b.AccountId = accountId; b.IsActive = dto.IsActive;
            await _uow.SaveChangesAsync(ct);
            var list = await GetBankAccountsAsync(new TreasuryQuery { PageSize = 0 }, ct);
            return ResponseDtoHelper.Ok(list.Data.First(x => x.Id == b.Id));
        }

        // ================================================================================== cash boxes, petty cash

        public async Task<ResponseDto<PagedList<CashBoxDto>>> GetCashBoxesAsync(TreasuryQuery query, CancellationToken ct)
        {
            query ??= new TreasuryQuery();
            int total = await _uow.Set<FnCashBox>().CountAsync(ct);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize, number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<FnCashBox> rows = await _uow.Set<FnCashBox>().AsNoTracking().OrderBy(c => c.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            Dictionary<int, decimal> balances = await PartyBalancesAsync("CashBox", ct);
            return ResponseDtoHelper.Ok(Page(rows.Select(c => new CashBoxDto
            {
                Id = c.Id, Title = c.Title, CustodianTitle = c.CustodianTitle, IsActive = c.IsActive, Balance = balances.TryGetValue(c.Id, out decimal b) ? b : 0,
            }).ToList(), total, number, size));
        }

        public async Task<ResponseDto<CashBoxDto>> SaveCashBoxAsync(SaveCashBoxDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryUsers)) return Bad<CashBoxDto>(NotAllowed + " (خزانه‌داری)");
            if (dto == null || Clean(dto.Title) == null) return Bad<CashBoxDto>("عنوان صندوق را وارد کنید.");
            FnCashBox c;
            if (dto.Id.HasValue)
            {
                c = await _uow.Set<FnCashBox>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (c == null) return ResponseDtoHelper.NotFound<CashBoxDto>("صندوق پیدا نشد.");
            }
            else { c = new FnCashBox(); _uow.Set<FnCashBox>().Add(c); }
            c.Title = Clean(dto.Title); c.CustodianTitle = Clean(dto.CustodianTitle); c.IsActive = dto.IsActive;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await GetCashBoxesAsync(new TreasuryQuery { PageSize = 0 }, ct)).Data.First(x => x.Id == c.Id));
        }

        public async Task<ResponseDto<PagedList<PettyCashDto>>> GetPettyCashesAsync(TreasuryQuery query, CancellationToken ct)
        {
            query ??= new TreasuryQuery();
            int total = await _uow.Set<FnPettyCash>().CountAsync(ct);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize, number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<FnPettyCash> rows = await _uow.Set<FnPettyCash>().AsNoTracking().OrderBy(c => c.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(r => (int?)r.HolderUserId), ct);
            return ResponseDtoHelper.Ok(Page(rows.Select(p => new PettyCashDto
            {
                Id = p.Id, HolderUserId = p.HolderUserId, HolderName = names.Name(p.HolderUserId), HolderTitle = p.HolderTitle, Ceiling = p.Ceiling, Balance = p.Balance, IsActive = p.IsActive,
            }).ToList(), total, number, size));
        }

        public async Task<ResponseDto<PettyCashDto>> SavePettyCashAsync(SavePettyCashDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryUsers)) return Bad<PettyCashDto>(NotAllowed + " (خزانه‌داری)");
            if (dto == null || dto.HolderUserId <= 0) return Bad<PettyCashDto>("دارنده تنخواه را مشخص کنید.");
            if (dto.Ceiling <= 0) return Bad<PettyCashDto>("سقف تنخواه باید بیشتر از صفر باشد.");
            if (!await _uow.Set<User>().AnyAsync(u => u.Id == dto.HolderUserId, ct)) return Bad<PettyCashDto>("کاربر دارنده تنخواه پیدا نشد.");
            if (await _uow.Set<FnPettyCash>().AnyAsync(p => p.HolderUserId == dto.HolderUserId && p.Id != (dto.Id ?? 0), ct)) return Bad<PettyCashDto>("برای این شخص قبلاً تنخواه تعریف شده است.");
            FnPettyCash p;
            if (dto.Id.HasValue)
            {
                p = await _uow.Set<FnPettyCash>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (p == null) return ResponseDtoHelper.NotFound<PettyCashDto>("تنخواه پیدا نشد.");
                if (p.Balance > R(dto.Ceiling)) return Bad<PettyCashDto>("سقف از مانده فعلی تنخواه کمتر است.");
            }
            else { p = new FnPettyCash(); _uow.Set<FnPettyCash>().Add(p); }
            p.HolderUserId = dto.HolderUserId; p.HolderTitle = Clean(dto.HolderTitle); p.Ceiling = R(dto.Ceiling); p.IsActive = dto.IsActive;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PettyCashDto>(Conflict); }
            return ResponseDtoHelper.Ok((await GetPettyCashesAsync(new TreasuryQuery { PageSize = 0 }, ct)).Data.First(x => x.Id == p.Id));
        }

        // ================================================================================== payments

        public async Task<ResponseDto<PagedList<PayableRequestDto>>> GetPayableRequestsAsync(TreasuryQuery query, CancellationToken ct)
        {
            query ??= new TreasuryQuery();
            IQueryable<PaymentRequest> q = _uow.Set<PaymentRequest>().AsNoTracking().Where(p => !p.Deleted && p.Status == PaymentRequestStatus.Approved);
            if (query.ProviderId.HasValue) q = q.Where(p => p.ProviderId == query.ProviderId.Value);
            int total = await q.CountAsync(ct);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize, number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            var rows = await q.OrderBy(p => p.Id).Skip((number - 1) * size).Take(size)
                .Select(p => new { p.Id, p.Number, p.ProviderId, ProviderName = p.Provider.Name, p.Type, p.RequestDate, InvoiceNumber = p.Invoice == null ? null : p.Invoice.InvoiceNumber, p.Amount, p.Deductions })
                .ToListAsync(ct);
            List<int> ids = rows.Select(r => r.Id).ToList();
            List<int> providers = rows.Select(r => r.ProviderId).Distinct().ToList();
            Dictionary<int, string> ibans = await _uow.Set<SupplierProfile>().AsNoTracking().Where(s => providers.Contains(s.ProviderId)).ToDictionaryAsync(s => s.ProviderId, s => s.Iban, ct);
            Dictionary<int, int> linked = (await _uow.Set<FnPaymentRequestLink>().AsNoTracking().Where(k => ids.Contains(k.PaymentRequestId) && k.Payment.Status != FnDocStatus.Cancelled)
                .Select(k => new { k.PaymentRequestId, k.PaymentId }).ToListAsync(ct)).GroupBy(k => k.PaymentRequestId).ToDictionary(g => g.Key, g => g.First().PaymentId);
            return ResponseDtoHelper.Ok(Page(rows.Select(r => new PayableRequestDto
            {
                Id = r.Id, Number = r.Number, ProviderId = r.ProviderId, ProviderName = r.ProviderName, Type = (int)r.Type, RequestDate = r.RequestDate, InvoiceNumber = r.InvoiceNumber,
                Amount = r.Amount, Deductions = r.Deductions, NetAmount = r.Amount - r.Deductions, SupplierIban = ibans.TryGetValue(r.ProviderId, out string iban) ? iban : null,
                PaymentId = linked.TryGetValue(r.Id, out int pid) ? pid : (int?)null,
            }).ToList(), total, number, size));
        }

        private async Task<List<PaymentDto>> LoadPaymentsAsync(IQueryable<FnPayment> query, CancellationToken ct)
        {
            List<FnPayment> rows = await query.AsNoTracking().Include(p => p.Lines).Include(p => p.Requests).AsSplitQuery().ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(p => new int?[] { p.CreatedByUserId, p.PostedByUserId }), ct);
            List<int> providerIds = rows.Where(p => p.ProviderId.HasValue).Select(p => p.ProviderId.Value).Distinct().ToList();
            Dictionary<int, string> providers = await _uow.Set<Provider>().AsNoTracking().Where(p => providerIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            List<int> requestIds = rows.SelectMany(p => p.Requests).Select(k => k.PaymentRequestId).Distinct().ToList();
            Dictionary<int, string> requestNumbers = await _uow.Set<PaymentRequest>().AsNoTracking().Where(r => requestIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Number, ct);
            List<int> leafIds = rows.SelectMany(p => p.Lines).Where(l => l.IssuedChequeId.HasValue).Select(l => l.IssuedChequeId.Value).Distinct().ToList();
            Dictionary<int, long> serials = await _uow.Set<FnIssuedCheque>().AsNoTracking().Where(c => leafIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Serial, ct);
            return rows.Select(p => new PaymentDto
            {
                Id = p.Id, Number = p.Number, Date = p.Date, Status = p.Status, Purpose = p.Purpose, ProviderId = p.ProviderId,
                ProviderName = p.ProviderId.HasValue && providers.TryGetValue(p.ProviderId.Value, out string pn) ? pn : null, PettyCashId = p.PettyCashId,
                PayeeName = p.PayeeName, Description = p.Description, GrossAmount = p.GrossAmount, DeductionAmount = p.DeductionAmount, NetAmount = p.NetAmount,
                CreatorName = names.Name(p.CreatedByUserId), PostedByName = names.Name(p.PostedByUserId), PostedAt = p.PostedAt, IbanApprovedByUserId = p.IbanApprovedByUserId,
                Lines = p.Lines.OrderBy(l => l.RowNumber).Select(l => new PaymentLineDto
                {
                    RowNumber = l.RowNumber, Instrument = l.Instrument, Amount = l.Amount, BankAccountId = l.BankAccountId, CashBoxId = l.CashBoxId, Reference = l.Reference,
                    PayeeIban = l.PayeeIban, IssuedChequeId = l.IssuedChequeId, ChequeSerial = l.IssuedChequeId.HasValue && serials.TryGetValue(l.IssuedChequeId.Value, out long sr) ? sr : (long?)null,
                    ChequeDueDate = l.ChequeDueDate, ChequeSayadId = l.ChequeSayadId,
                }).ToList(),
                Requests = p.Requests.Select(k => new PaymentRequestLinkDto
                {
                    PaymentRequestId = k.PaymentRequestId, PaymentRequestNumber = requestNumbers.TryGetValue(k.PaymentRequestId, out string rn) ? rn : null, Amount = k.Amount, Deductions = k.Deductions,
                }).ToList(),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<PaymentDto>>> GetPaymentsAsync(TreasuryQuery query, CancellationToken ct)
        {
            query ??= new TreasuryQuery();
            IQueryable<FnPayment> q = _uow.Set<FnPayment>().AsNoTracking();
            if (query.Status.HasValue) q = q.Where(p => p.Status == query.Status.Value);
            if (query.ProviderId.HasValue) q = q.Where(p => p.ProviderId == query.ProviderId.Value);
            if (query.PettyCashId.HasValue) q = q.Where(p => p.PettyCashId == query.PettyCashId.Value);
            if (query.From.HasValue) q = q.Where(p => p.Date >= query.From.Value.Date);
            if (query.To.HasValue) q = q.Where(p => p.Date <= query.To.Value.Date);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(p => p.Number.Contains(text) || p.PayeeName.Contains(text));
            int total = await q.CountAsync(ct);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize, number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<PaymentDto> list = await LoadPaymentsAsync(q.OrderByDescending(p => p.Id).Skip((number - 1) * size).Take(size), ct);
            return ResponseDtoHelper.Ok(Page(list.OrderByDescending(p => p.Id).ToList(), total, number, size));
        }

        public async Task<ResponseDto<PaymentDto>> GetPaymentAsync(int id, CancellationToken ct)
        {
            List<PaymentDto> list = await LoadPaymentsAsync(_uow.Set<FnPayment>().Where(p => p.Id == id), ct);
            return list.Count == 0 ? ResponseDtoHelper.NotFound<PaymentDto>("سند پرداخت پیدا نشد.") : ResponseDtoHelper.Ok(list[0]);
        }

        public async Task<ResponseDto<PaymentDto>> SavePaymentAsync(SavePaymentDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryUsers)) return Bad<PaymentDto>(NotAllowed + " (خزانه‌داری)");
            if (dto == null) return Bad<PaymentDto>("اطلاعات ارسال‌شده کامل نیست.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            DateTime date = dto.Date == default ? DateTime.Today : dto.Date.Date;
            List<PaymentLineDto> input = (dto.Lines ?? new List<PaymentLineDto>()).Where(l => l != null).ToList();
            if (input.Count == 0) return Bad<PaymentDto>("حداقل یک ردیف پرداخت (نقد، حواله، چک یا کارت) لازم است.");

            FnPayment payment = null;
            if (dto.Id.HasValue)
            {
                payment = await _uow.Set<FnPayment>().Include(p => p.Lines).Include(p => p.Requests).FirstOrDefaultAsync(p => p.Id == dto.Id.Value, ct);
                if (payment == null) return ResponseDtoHelper.NotFound<PaymentDto>("سند پرداخت پیدا نشد.");
                if (payment.Status != FnDocStatus.Draft) return Bad<PaymentDto>("فقط پیش‌نویس ویرایش می‌شود.");
            }

            decimal gross, deductions;
            int? providerId = null, pettyCashId = null;
            string payee;
            List<PaymentRequest> requests = new List<PaymentRequest>();
            string registeredIban = null;
            if (dto.Purpose == FnPaymentPurpose.Supplier)
            {
                List<int> ids = (dto.PaymentRequestIds ?? new List<int>()).Distinct().ToList();
                if (ids.Count == 0) return Bad<PaymentDto>("درخواست پرداخت تاییدشده را انتخاب کنید.");
                requests = await _uow.Set<PaymentRequest>().Where(p => ids.Contains(p.Id) && !p.Deleted).ToListAsync(ct);
                if (requests.Count != ids.Count) return Bad<PaymentDto>("درخواست پرداخت پیدا نشد.");
                if (requests.Any(r => r.Status != PaymentRequestStatus.Approved)) return Bad<PaymentDto>("فقط درخواست پرداخت تاییدشده (و پرداخت‌نشده) در سند پرداخت می‌آید.");
                if (requests.Select(r => r.ProviderId).Distinct().Count() > 1) return Bad<PaymentDto>("درخواست‌های یک سند پرداخت باید برای یک تامین‌کننده باشند.");
                int self = payment?.Id ?? 0;
                if (await _uow.Set<FnPaymentRequestLink>().AnyAsync(k => ids.Contains(k.PaymentRequestId) && k.Payment.Status != FnDocStatus.Cancelled && k.PaymentId != self, ct))
                    return Bad<PaymentDto>("یکی از درخواست‌ها قبلاً در سند پرداخت دیگری آمده است.");
                providerId = requests[0].ProviderId;
                gross = requests.Sum(r => r.Amount);
                deductions = requests.Sum(r => r.Deductions);
                payee = await _uow.Set<Provider>().Where(p => p.Id == providerId.Value).Select(p => p.Name).FirstOrDefaultAsync(ct);
                registeredIban = NormalizeIban(await _uow.Set<SupplierProfile>().Where(s => s.ProviderId == providerId.Value).Select(s => s.Iban).FirstOrDefaultAsync(ct));
            }
            else if (dto.Purpose == FnPaymentPurpose.PettyCashTopUp)
            {
                if (!dto.PettyCashId.HasValue) return Bad<PaymentDto>("تنخواه را مشخص کنید.");
                FnPettyCash petty = await _uow.Set<FnPettyCash>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.PettyCashId.Value, ct);
                if (petty == null || !petty.IsActive) return Bad<PaymentDto>("تنخواه پیدا نشد یا غیرفعال است.");
                decimal top = R(dto.TopUpAmount ?? 0);
                if (top <= 0) return Bad<PaymentDto>("مبلغ شارژ تنخواه را وارد کنید.");
                if (petty.Balance + top > petty.Ceiling) return Bad<PaymentDto>($"شارژ از سقف تنخواه ({petty.Ceiling:0} ریال) بیشتر می‌شود؛ مانده فعلی {petty.Balance:0}.");
                pettyCashId = petty.Id;
                gross = top; deductions = 0;
                payee = (await InventoryUsers.GetNamesAsync(_uow, new int?[] { petty.HolderUserId }, ct)).Name(petty.HolderUserId) ?? petty.HolderTitle;
            }
            else return Bad<PaymentDto>("نوع پرداخت نامعتبر است.");
            decimal net = gross - deductions;
            if (net <= 0) return Bad<PaymentDto>("مبلغ خالص پرداخت باید بیشتر از صفر باشد.");

            // lines
            var lines = new List<FnPaymentLine>();
            var usedLeaves = new HashSet<int>();
            List<int> bankIds = input.Where(l => l.BankAccountId.HasValue).Select(l => l.BankAccountId.Value).Distinct().ToList();
            Dictionary<int, FnBankAccount> banks = await _uow.Set<FnBankAccount>().Where(b => bankIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
            List<int> boxIds = input.Where(l => l.CashBoxId.HasValue).Select(l => l.CashBoxId.Value).Distinct().ToList();
            Dictionary<int, FnCashBox> boxes = await _uow.Set<FnCashBox>().Where(b => boxIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
            int row = 1;
            foreach (PaymentLineDto l in input)
            {
                decimal amount = R(l.Amount);
                if (amount <= 0) return Bad<PaymentDto>($"ردیف {row}: مبلغ باید بیشتر از صفر باشد.");
                var line = new FnPaymentLine { RowNumber = row, Instrument = l.Instrument, Amount = amount, Reference = Clean(l.Reference) };
                switch (l.Instrument)
                {
                    case FnInstrument.Cash:
                        if (!l.CashBoxId.HasValue || !boxes.TryGetValue(l.CashBoxId.Value, out FnCashBox box) || !box.IsActive) return Bad<PaymentDto>($"ردیف {row}: صندوق پیدا نشد یا غیرفعال است.");
                        line.CashBoxId = box.Id;
                        break;
                    case FnInstrument.BankTransfer:
                    case FnInstrument.Card:
                    {
                        if (!l.BankAccountId.HasValue || !banks.TryGetValue(l.BankAccountId.Value, out FnBankAccount bank) || !bank.IsActive) return Bad<PaymentDto>($"ردیف {row}: حساب بانکی پیدا نشد یا غیرفعال است.");
                        line.BankAccountId = bank.Id;
                        if (l.Instrument == FnInstrument.BankTransfer && dto.Purpose == FnPaymentPurpose.Supplier)
                        {
                            string iban = NormalizeIban(l.PayeeIban) ?? registeredIban;
                            if (iban == null) return Bad<PaymentDto>($"ردیف {row}: شبای تامین‌کننده ثبت نشده است؛ آن را در پروفایل تامین‌کننده یا در همین ردیف وارد کنید.");
                            if (!IbanValid(iban)) return Bad<PaymentDto>($"ردیف {row}: شماره شبای گیرنده نامعتبر است.");
                            line.PayeeIban = iban;
                        }
                        break;
                    }
                    case FnInstrument.Cheque:
                    {
                        if (dto.Purpose != FnPaymentPurpose.Supplier) return Bad<PaymentDto>($"ردیف {row}: تنخواه با چک شارژ نمی‌شود.");
                        if (!l.BankAccountId.HasValue || !banks.TryGetValue(l.BankAccountId.Value, out FnBankAccount bank) || !bank.IsActive) return Bad<PaymentDto>($"ردیف {row}: حساب بانکی چک پیدا نشد یا غیرفعال است.");
                        IQueryable<FnIssuedCheque> leaves = _uow.Set<FnIssuedCheque>().Include(x => x.ChequeBook);
                        FnIssuedCheque leaf = l.IssuedChequeId.HasValue
                            ? await leaves.FirstOrDefaultAsync(x => x.Id == l.IssuedChequeId.Value, ct)
                            : l.ChequeSerial.HasValue ? await leaves.FirstOrDefaultAsync(x => x.ChequeBook.BankAccountId == bank.Id && x.Serial == l.ChequeSerial.Value && x.ChequeBook.IsActive, ct) : null;
                        if (leaf == null) return Bad<PaymentDto>($"ردیف {row}: برگ چک پیدا نشد (شناسه یا شماره سریال برگ را بدهید).");
                        if (leaf.ChequeBook.BankAccountId != bank.Id) return Bad<PaymentDto>($"ردیف {row}: برگ چک متعلق به این حساب بانکی نیست.");
                        if (leaf.Status != FnIssuedChequeStatus.Blank) return Bad<PaymentDto>($"ردیف {row}: برگ چک {leaf.Serial} قبلاً استفاده شده یا باطل شده است و دوباره به کار نمی‌رود.");
                        if (!usedLeaves.Add(leaf.Id) || await _uow.Set<FnPaymentLine>().AnyAsync(x => x.IssuedChequeId == leaf.Id && x.Payment.Status != FnDocStatus.Cancelled && x.PaymentId != (payment == null ? 0 : payment.Id), ct))
                            return Bad<PaymentDto>($"ردیف {row}: برگ چک {leaf.Serial} در سند پرداخت دیگری انتخاب شده است.");
                        DateTime due = (l.ChequeDueDate ?? date).Date;
                        if (due < date) return Bad<PaymentDto>($"ردیف {row}: تاریخ سررسید چک پیش از تاریخ پرداخت است.");
                        string sayad = Clean(Digits(l.ChequeSayadId));
                        if (sayad != null && (sayad.Length != 16 || !sayad.All(char.IsDigit))) return Bad<PaymentDto>($"ردیف {row}: شناسه صیادی باید ۱۶ رقم باشد.");
                        line.BankAccountId = bank.Id; line.IssuedChequeId = leaf.Id; line.ChequeDueDate = due; line.ChequeSayadId = sayad;
                        break;
                    }
                    default:
                        return Bad<PaymentDto>($"ردیف {row}: روش پرداخت «تهاتر» در این نسخه پشتیبانی نمی‌شود.");
                }
                lines.Add(line);
                row++;
            }
            if (lines.Sum(l => l.Amount) != net) return Bad<PaymentDto>($"جمع ردیف‌های پرداخت ({lines.Sum(l => l.Amount):0}) با مبلغ خالص ({net:0}) برابر نیست.");

            if (payment == null)
            {
                payment = new FnPayment
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, FinanceNumbers.Payment, "PAYM", date, ct), Status = FnDocStatus.Draft,
                    CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now, Lines = new List<FnPaymentLine>(), Requests = new List<FnPaymentRequestLink>(),
                };
                _uow.Set<FnPayment>().Add(payment);
            }
            else
            {
                _uow.Set<FnPaymentLine>().RemoveRange(payment.Lines);
                _uow.Set<FnPaymentRequestLink>().RemoveRange(payment.Requests);
                payment.Lines = new List<FnPaymentLine>();
                payment.Requests = new List<FnPaymentRequestLink>();
            }
            payment.Date = date; payment.Purpose = dto.Purpose; payment.ProviderId = providerId; payment.PettyCashId = pettyCashId; payment.PayeeName = payee;
            payment.Description = Clean(dto.Description); payment.GrossAmount = gross; payment.DeductionAmount = deductions; payment.NetAmount = net;
            payment.IbanApprovedByUserId = null;
            foreach (FnPaymentLine l in lines) payment.Lines.Add(l);
            foreach (PaymentRequest r in requests) payment.Requests.Add(new FnPaymentRequestLink { PaymentRequestId = r.Id, Amount = r.Amount, Deductions = r.Deductions });
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PaymentDto>(Conflict); }
            return await GetPaymentAsync(payment.Id, ct);
        }

        public async Task<ResponseDto<PaymentDto>> PaymentActionAsync(DocActionDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<PaymentDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnPayment p = await _uow.Set<FnPayment>().Include(x => x.Lines).Include(x => x.Requests).FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (p == null) return ResponseDtoHelper.NotFound<PaymentDto>("سند پرداخت پیدا نشد.");
            string action = dto.Action.Trim().ToLowerInvariant();
            switch (action)
            {
                case "approve":
                {
                    if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<PaymentDto>(NotAllowed + " (مدیر مالی)");
                    if (p.Status != FnDocStatus.Draft) return Bad<PaymentDto>("فقط پیش‌نویس تایید می‌شود.");
                    if (p.CreatedByUserId == Me) return Bad<PaymentDto>(SameUser);
                    bool mismatch = await HasIbanMismatchAsync(p, ct);
                    if (mismatch)
                    {
                        if (Clean(dto.Note) == null) return Bad<PaymentDto>("شبای گیرنده با شبای ثبت‌شده تامین‌کننده فرق دارد؛ علت پذیرش را بنویسید.");
                        p.IbanApprovedByUserId = Me;
                    }
                    p.Status = FnDocStatus.Approved;
                    break;
                }
                case "post":
                {
                    if (!_user.IsInRole(FinanceRoles.TreasuryUsers)) return Bad<PaymentDto>(NotAllowed + " (خزانه‌داری)");
                    string error = await PostPaymentAsync(p, dto, ct);
                    if (error != null) return Bad<PaymentDto>(error);
                    break;
                }
                case "cancel":
                    if (!_user.IsInRole(FinanceRoles.TreasuryUsers)) return Bad<PaymentDto>(NotAllowed + " (خزانه‌داری)");
                    if (p.Status != FnDocStatus.Draft && p.Status != FnDocStatus.Approved) return Bad<PaymentDto>("سند ثبت‌شده باطل نمی‌شود؛ سند اصلاحی صادر کنید.");
                    p.Status = FnDocStatus.Cancelled;
                    break;
                default:
                    return Bad<PaymentDto>("عمل نامعتبر است.");
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PaymentDto>(Conflict); }
            catch (DbUpdateException) { return Bad<PaymentDto>("ثبت سند پرداخت ناموفق بود؛ احتمالاً همزمان توسط کاربر دیگری تغییر کرده است."); }
            return await GetPaymentAsync(p.Id, ct);
        }

        private async Task<bool> HasIbanMismatchAsync(FnPayment p, CancellationToken ct)
        {
            if (p.Purpose != FnPaymentPurpose.Supplier || !p.ProviderId.HasValue) return false;
            string registered = NormalizeIban(await _uow.Set<SupplierProfile>().Where(s => s.ProviderId == p.ProviderId.Value).Select(s => s.Iban).FirstOrDefaultAsync(ct));
            return p.Lines.Any(l => l.Instrument == FnInstrument.BankTransfer && l.PayeeIban != null && l.PayeeIban != registered);
        }

        // ---- shared posting pieces ------------------------------------------------------------------------

        private static string BankRole(FnBankAccount b) => b.CurrencyCode == null || b.CurrencyCode == "IRR" ? AccountRoles.Bank : AccountRoles.BankForeign;

        private static AccountingEventLine BankLine(FnBankAccount b, decimal debit, decimal credit, string text) => new AccountingEventLine
        {
            AccountRole = BankRole(b), Debit = debit, Credit = credit, PartyType = "BankAccount", PartyId = b.Id, Description = text,
        };

        private static AccountingEventLine CashLine(int boxId, decimal debit, decimal credit, string text) => new AccountingEventLine
        {
            AccountRole = AccountRoles.Cash, Debit = debit, Credit = credit, PartyType = "CashBox", PartyId = boxId, Description = text,
        };

        private static string DeductionRole(FnDeductionKind kind)
        {
            switch (kind)
            {
                case FnDeductionKind.Withholding: return AccountRoles.WithholdingPayable;
                case FnDeductionKind.InsuranceRetention: return AccountRoles.InsuranceRetentionPayable;
                case FnDeductionKind.PerformanceRetention: return AccountRoles.PerformanceRetentionPayable;
                default: return AccountRoles.OtherPayable;
            }
        }

        /// <summary>Debit of the supplier account (payable, or advance when there is no invoice) and the credits of the deductions of one payment request.</summary>
        private static IEnumerable<AccountingEventLine> RequestLines(PaymentRequest r, IEnumerable<FnPaymentDeduction> typed)
        {
            yield return new AccountingEventLine
            {
                AccountRole = r.InvoiceId.HasValue ? AccountRoles.AccountsPayable : AccountRoles.SupplierAdvance, Debit = r.Amount, ProviderId = r.ProviderId,
                Description = "درخواست پرداخت " + r.Number,
            };
            decimal typedSum = 0;
            foreach (var g in typed.GroupBy(d => d.Kind))
            {
                decimal amount = g.Sum(d => d.Amount);
                typedSum += amount;
                yield return new AccountingEventLine { AccountRole = DeductionRole(g.Key), Credit = amount, ProviderId = r.ProviderId, Description = "کسور " + g.Key + " - " + r.Number };
            }
            if (r.Deductions - typedSum > 0)
                yield return new AccountingEventLine { AccountRole = AccountRoles.OtherPayable, Credit = r.Deductions - typedSum, ProviderId = r.ProviderId, Description = "سایر کسور - " + r.Number };
        }

        private async Task<string> PostPaymentAsync(FnPayment p, DocActionDto dto, CancellationToken ct)
        {
            if (p.Status != FnDocStatus.Approved) return "فقط سند تاییدشده توسط مدیر مالی ثبت نهایی می‌شود.";
            if (await HasIbanMismatchAsync(p, ct) && !p.IbanApprovedByUserId.HasValue) return "شبای گیرنده با شبای ثبت‌شده فرق دارد و تایید مدیر مالی ندارد.";
            DateTimeOffset now = DateTimeOffset.Now;
            // bank transfers need the tracking number
            foreach (FnPaymentLine l in p.Lines.Where(x => x.Instrument == FnInstrument.BankTransfer))
                if (Clean(l.Reference) == null)
                {
                    if (Clean(dto.Reference) == null) return "شماره پیگیری حواله بانکی را وارد کنید.";
                    l.Reference = Clean(dto.Reference);
                }
            List<int> bankIds = p.Lines.Where(l => l.BankAccountId.HasValue).Select(l => l.BankAccountId.Value).Distinct().ToList();
            Dictionary<int, FnBankAccount> banks = await _uow.Set<FnBankAccount>().Where(b => bankIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
            if (banks.Values.Any(b => !b.IsActive)) return "یکی از حساب‌های بانکی غیرفعال شده است.";

            var eventLines = new List<AccountingEventLine>();
            AccountingEventType type;
            var requests = new List<PaymentRequest>();
            if (p.Purpose == FnPaymentPurpose.Supplier)
            {
                type = AccountingEventType.SupplierPayment;
                List<int> ids = p.Requests.Select(k => k.PaymentRequestId).ToList();
                requests = await _uow.Set<PaymentRequest>().Where(r => ids.Contains(r.Id)).ToListAsync(ct);
                foreach (PaymentRequest r in requests)
                {
                    if (r.Status != PaymentRequestStatus.Approved) return $"درخواست {r.Number} دیگر در وضعیت تاییدشده نیست.";
                    if (r.CreatorUserId == Me || r.ApprovedByUserId == Me) return SameUser;
                }
                List<FnPaymentDeduction> typed = await _uow.Set<FnPaymentDeduction>().Where(d => ids.Contains(d.PaymentRequestId)).ToListAsync(ct);
                foreach (PaymentRequest r in requests) eventLines.AddRange(RequestLines(r, typed.Where(d => d.PaymentRequestId == r.Id)));
            }
            else
            {
                type = AccountingEventType.PettyCashTopUp;
                FnPettyCash petty = await _uow.Set<FnPettyCash>().FirstOrDefaultAsync(x => x.Id == p.PettyCashId, ct);
                if (petty == null || !petty.IsActive) return "تنخواه پیدا نشد یا غیرفعال است.";
                if (petty.Balance + p.NetAmount > petty.Ceiling) return $"شارژ از سقف تنخواه ({petty.Ceiling:0} ریال) بیشتر می‌شود.";
                petty.Balance += p.NetAmount;
                eventLines.Add(new AccountingEventLine { AccountRole = AccountRoles.PettyCash, Debit = p.NetAmount, PartyType = "PettyCash", PartyId = petty.Id, Description = "شارژ تنخواه " + p.Number });
            }

            foreach (FnPaymentLine l in p.Lines.OrderBy(x => x.RowNumber))
            {
                switch (l.Instrument)
                {
                    case FnInstrument.Cash: eventLines.Add(CashLine(l.CashBoxId.Value, 0, l.Amount, p.Number)); break;
                    case FnInstrument.BankTransfer:
                    case FnInstrument.Card: eventLines.Add(BankLine(banks[l.BankAccountId.Value], 0, l.Amount, p.Number + (l.Reference != null ? " - " + l.Reference : ""))); break;
                    case FnInstrument.Cheque:
                    {
                        FnIssuedCheque leaf = await _uow.Set<FnIssuedCheque>().FirstOrDefaultAsync(c => c.Id == l.IssuedChequeId.Value, ct);
                        if (leaf == null || leaf.Status != FnIssuedChequeStatus.Blank) return "برگ چک دیگر خالی نیست و دوباره به کار نمی‌رود.";
                        leaf.Status = FnIssuedChequeStatus.Issued; leaf.PayeeName = p.PayeeName; leaf.ProviderId = p.ProviderId; leaf.DueDate = l.ChequeDueDate;
                        leaf.Amount = l.Amount; leaf.PaymentId = p.Id; leaf.IssuedAt = now; leaf.SayadId = l.ChequeSayadId;
                        eventLines.Add(new AccountingEventLine { AccountRole = AccountRoles.ChequesPayable, Credit = l.Amount, ProviderId = p.ProviderId, Description = $"چک {leaf.Serial} - {p.Number}" });
                        break;
                    }
                    default: return "روش پرداخت پشتیبانی نمی‌شود.";
                }
            }
            _costing.AddEvent(type, p.Date, "FnPayment", p.Id, p.Number, $"پرداخت {p.Number} - {p.PayeeName}", eventLines);
            foreach (PaymentRequest r in requests)
            {
                r.Status = PaymentRequestStatus.Paid; r.PaidAt = now; r.PaidByUserId = Me; r.PaymentReference = p.Number;
            }
            p.Status = FnDocStatus.Posted; p.PostedByUserId = Me; p.PostedAt = now;
            return null;
        }

        public async Task<string> AddLegacyPaymentAsync(PaymentRequest r, string reference, CancellationToken ct)
        {
            if (await _uow.Set<FnPaymentRequestLink>().AnyAsync(k => k.PaymentRequestId == r.Id && k.Payment.Status != FnDocStatus.Cancelled, ct))
                return "این درخواست در یک سند پرداخت خزانه آمده است؛ پرداخت را از خزانه ثبت کنید.";
            await FinanceSeed.EnsureAsync(_uow, ct);
            List<FnPaymentDeduction> typed = await _uow.Set<FnPaymentDeduction>().Where(d => d.PaymentRequestId == r.Id).ToListAsync(ct);
            string payee = await _uow.Set<Provider>().Where(p => p.Id == r.ProviderId).Select(p => p.Name).FirstOrDefaultAsync(ct);
            DateTime today = DateTime.Today;
            decimal net = r.Amount - r.Deductions;
            var payment = new FnPayment
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, FinanceNumbers.Payment, "PAYM", today, ct), Date = today, Status = FnDocStatus.Posted,
                Purpose = FnPaymentPurpose.Supplier, ProviderId = r.ProviderId, PayeeName = payee, Description = $"پرداخت درخواست {r.Number} (ثبت با مسیر قدیمی)",
                GrossAmount = r.Amount, DeductionAmount = r.Deductions, NetAmount = net, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now, PostedByUserId = Me, PostedAt = DateTimeOffset.Now,
                Lines = new List<FnPaymentLine> { new FnPaymentLine { RowNumber = 1, Instrument = FnInstrument.BankTransfer, Amount = net, Reference = Clean(reference) } },
                Requests = new List<FnPaymentRequestLink> { new FnPaymentRequestLink { PaymentRequestId = r.Id, Amount = r.Amount, Deductions = r.Deductions } },
            };
            _uow.Set<FnPayment>().Add(payment);
            var lines = RequestLines(r, typed).ToList();
            lines.Add(new AccountingEventLine { AccountRole = AccountRoles.Bank, Credit = net, Description = "حواله " + (Clean(reference) ?? r.Number) });
            _costing.AddEvent(AccountingEventType.SupplierPayment, today, "PaymentRequest", r.Id, r.Number, $"پرداخت درخواست {r.Number} - {payee}", lines);
            return null;
        }
    }
}
