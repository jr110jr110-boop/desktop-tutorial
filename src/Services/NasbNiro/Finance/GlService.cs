using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    public interface IGlService
    {
        Task<ResponseDto<List<FiscalYearDto>>> GetYearsAsync(int? legalEntityId, CancellationToken cancellationToken);
        Task<ResponseDto<FiscalYearDto>> EnsureYearAsync(EnsureYearDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FiscalYearDto>> PeriodActionAsync(PeriodActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FiscalYearDto>> CloseYearAsync(int id, CloseYearDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<AccountDto>>> GetAccountsAsync(AccountQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<AccountDto>> SaveAccountAsync(SaveAccountDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> DeleteAccountAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<DetailAccountDto>>> GetDetailsAsync(DetailAccountQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<DetailAccountDto>> SaveDetailAsync(SaveDetailAccountDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<VoucherDto>>> GetVouchersAsync(VoucherQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<VoucherDto>> GetVoucherAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<VoucherDto>> SaveVoucherAsync(SaveVoucherDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> DeleteVoucherAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<VoucherDto>> VoucherActionAsync(VoucherActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FinalizeResultDto>> FinalizeUpToAsync(FinalizeUpToDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PostEventsResultDto>> PostEventsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<VoucherDto>> ImportOpeningAsync(LedgerOpeningImportDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<SetupCheckDto>> SetupCheckAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// The PMIS general ledger: fiscal years and periods (Persian calendar), chart of accounts, detail accounts and vouchers.
    /// Final vouchers are immutable; a reversal voucher corrects them. No transactions are available, so every operation
    /// validates everything first and saves once.
    /// </summary>
    public partial class GlService : IGlService, IGlControlsService, ILegalEntityService, IMigrationService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IEventPostingService _posting;
        private readonly ILedgerReportService _reports;
        private readonly IReceivablesService _receivables;

        public GlService(IUnitOfWork uow, IInventoryUserContext user, IEventPostingService posting, ILedgerReportService reports, IReceivablesService receivables)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _posting = posting ?? throw new ArgumentNullException(nameof(posting));
            _reports = reports ?? throw new ArgumentNullException(nameof(reports));
            _receivables = receivables ?? throw new ArgumentNullException(nameof(receivables));
        }

        private int Me => _user.UserId;
        private bool IsManager => _user.IsInRole(FinanceRoles.Managers);
        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private const string NotAllowed = "شما مجوز این کار را ندارید";
        private const string SameUser = "این کار باید توسط شخص دیگری غیر از تهیه‌کننده سند انجام شود.";

        // ================================================================================== fiscal years and periods

        public async Task<ResponseDto<List<FiscalYearDto>>> GetYearsAsync(int? legalEntityId, CancellationToken ct)
        {
            await FinanceSeed.EnsureAsync(_uow, ct);
            int entityId = await EntityScope.ResolveAsync(_uow, legalEntityId, Me, ct);
            return ResponseDtoHelper.Ok(await LoadYearsAsync(null, entityId, ct));
        }

        private async Task<List<FiscalYearDto>> LoadYearsAsync(int? onlyYearId, int? entityId, CancellationToken ct)
        {
            IQueryable<FnFiscalYear> q = _uow.Set<FnFiscalYear>().AsNoTracking().Include(y => y.Periods);
            if (onlyYearId.HasValue) q = q.Where(y => y.Id == onlyYearId.Value);
            if (entityId.HasValue) q = q.Where(y => y.LegalEntityId == entityId.Value);
            List<FnFiscalYear> years = await q.OrderBy(y => y.Year).ToListAsync(ct);
            List<int> yearIds = years.Select(y => y.Id).ToList();
            var counts = await _uow.Set<FnVoucher>().AsNoTracking().Where(v => yearIds.Contains(v.FiscalYearId))
                .GroupBy(v => new { v.FiscalYearId, v.Date, Final = v.Status == FnVoucherStatus.Final })
                .Select(g => new { g.Key.FiscalYearId, g.Key.Date, g.Key.Final, Count = g.Count() }).ToListAsync(ct);
            return years.Select(y => new FiscalYearDto
            {
                Id = y.Id, Year = y.Year, Title = y.Title, StartDate = y.StartDate, EndDate = y.EndDate, Status = y.Status,
                OpeningVoucherId = y.OpeningVoucherId, ClosingVoucherId = y.ClosingVoucherId, LegalEntityId = y.LegalEntityId, IsImported = y.IsImported,
                Periods = y.Periods.OrderBy(p => p.Number).Select(p => new PeriodDto
                {
                    Id = p.Id, FiscalYearId = p.FiscalYearId, Number = p.Number, Title = p.Title, StartDate = p.StartDate, EndDate = p.EndDate, Status = p.Status,
                    Vouchers = counts.Where(c => c.FiscalYearId == y.Id && c.Date >= p.StartDate && c.Date <= p.EndDate).Sum(c => c.Count),
                    NonFinalVouchers = counts.Where(c => c.FiscalYearId == y.Id && !c.Final && c.Date >= p.StartDate && c.Date <= p.EndDate).Sum(c => c.Count),
                }).ToList(),
            }).ToList();
        }

        public async Task<ResponseDto<FiscalYearDto>> EnsureYearAsync(EnsureYearDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.SystemJobs)) return Bad<FiscalYearDto>(NotAllowed);
            if (dto == null || dto.Date == default) return Bad<FiscalYearDto>("تاریخ را وارد کنید.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            int entityId = await EntityScope.ResolveAsync(_uow, dto.LegalEntityId, Me, ct);
            FnLegalEntity entity = await _uow.Set<FnLegalEntity>().FirstOrDefaultAsync(x => x.Id == entityId, ct);
            if (entity == null || !entity.IsActive) return Bad<FiscalYearDto>("واحد حقوقی پیدا نشد یا غیرفعال است.");
            int year = FinanceSeed.YearNumberOf(entity, dto.Date);
            if (year < 1300 || year > 1600) return Bad<FiscalYearDto>("سال مالی خارج از محدوده مجاز است.");
            FnFiscalYear fy = await FinanceSeed.EnsureEntityYearAsync(_uow, entity, dto.Date, ct);
            return ResponseDtoHelper.Ok((await LoadYearsAsync(fy.Id, null, ct)).First());
        }

        public async Task<ResponseDto<FiscalYearDto>> PeriodActionAsync(PeriodActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<FiscalYearDto>(NotAllowed + " (مدیر مالی)");
            if (dto == null || Clean(dto.Action) == null) return Bad<FiscalYearDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnPeriod p = await _uow.Set<FnPeriod>().Include(x => x.FiscalYear).FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (p == null) return ResponseDtoHelper.NotFound<FiscalYearDto>("دوره پیدا نشد.");
            string action = dto.Action.Trim().ToLowerInvariant();
            switch (action)
            {
                case "lock":
                    if (p.Status != FnPeriodStatus.Open) return Bad<FiscalYearDto>("فقط دوره باز قفل می‌شود.");
                    string gate = await LockGateAsync(p, ct);
                    if (gate != null) return Bad<FiscalYearDto>(gate);
                    p.Status = FnPeriodStatus.Locked;
                    p.LockedAt = DateTimeOffset.Now;
                    break;
                case "reopen":
                    if (p.Status != FnPeriodStatus.Locked) return Bad<FiscalYearDto>("فقط دوره قفل‌شده دوباره باز می‌شود؛ دوره بسته قطعی است.");
                    if (Clean(dto.Reason) == null) return Bad<FiscalYearDto>("علت بازگشایی دوره را بنویسید.");
                    if (p.FiscalYear.Status != FnYearStatus.Open) return Bad<FiscalYearDto>("سال مالی بسته است.");
                    p.Status = FnPeriodStatus.Open;
                    p.LockedAt = null;
                    break;
                case "close":
                {
                    if (p.Status == FnPeriodStatus.Closed) return Bad<FiscalYearDto>("دوره قبلاً بسته شده است.");
                    int open = await _uow.Set<FnVoucher>().CountAsync(v => v.Date >= p.StartDate && v.Date <= p.EndDate && v.Status != FnVoucherStatus.Final, ct);
                    if (open > 0) return Bad<FiscalYearDto>($"{open} سند قطعی‌نشده در این دوره هست؛ ابتدا همه اسناد قطعی شوند.");
                    p.Status = FnPeriodStatus.Closed;
                    p.LockedAt ??= DateTimeOffset.Now;
                    break;
                }
                default:
                    return Bad<FiscalYearDto>("عمل نامعتبر است.");
            }
            _uow.Set<FnPeriodLog>().Add(new FnPeriodLog { PeriodId = p.Id, Action = action, Reason = Clean(dto.Reason), UserId = Me, At = DateTimeOffset.Now });
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<FiscalYearDto>("همزمان توسط کاربر دیگری تغییر کرد؛ دوباره تلاش کنید."); }
            return ResponseDtoHelper.Ok((await LoadYearsAsync(p.FiscalYearId, null, ct)).First());
        }

        // ================================================================================== year-end close

        public async Task<ResponseDto<FiscalYearDto>> CloseYearAsync(int id, CloseYearDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<FiscalYearDto>(NotAllowed + " (مدیر مالی)");
            dto ??= new CloseYearDto();
            FnFiscalYear year = await _uow.Set<FnFiscalYear>().Include(y => y.Periods).FirstOrDefaultAsync(y => y.Id == id, ct);
            if (year == null) return ResponseDtoHelper.NotFound<FiscalYearDto>("سال مالی پیدا نشد.");
            if (year.Status != FnYearStatus.Open) return Bad<FiscalYearDto>("این سال مالی قبلاً بسته شده است.");
            if (year.Periods.Any(p => p.Status == FnPeriodStatus.Open)) return Bad<FiscalYearDto>("همه دوره‌های سال باید بسته یا قفل شده باشند.");
            int nonFinal = await _uow.Set<FnVoucher>().CountAsync(v => v.FiscalYearId == year.Id && v.Status != FnVoucherStatus.Final, ct);
            if (nonFinal > 0) return Bad<FiscalYearDto>($"{nonFinal} سند پیش‌نویس یا کنترل‌شده در این سال هست؛ ابتدا قطعی یا حذف شوند.");

            var recon = await _reports.ReconcileAsync(7, ct);
            if (recon.Success && !recon.Data.Clean)
            {
                if (!dto.OverrideReconciliation) return Bad<FiscalYearDto>("تطبیق حساب‌ها با زیرحساب‌ها مغایرت دارد؛ مغایرت را رفع کنید یا با ذکر علت تایید ویژه بدهید.");
                if (Clean(dto.Reason) == null) return Bad<FiscalYearDto>("برای عبور از مغایرت علت را بنویسید.");
            }

            Dictionary<string, FnAccount> byCode = await _uow.Set<FnAccount>().ToDictionaryAsync(a => a.Code, ct);
            if (!byCode.TryGetValue("50901", out FnAccount summary) || !byCode.TryGetValue("50301", out FnAccount retained))
                return Bad<FiscalYearDto>("حساب‌های خلاصه سود و زیان (50901) و سود انباشته (50301) در کدینگ نیست.");

            FnLegalEntity yearEntity = await _uow.Set<FnLegalEntity>().FirstOrDefaultAsync(x => x.Id == year.LegalEntityId, ct);
            FnFiscalYear next = await FinanceSeed.EnsureEntityYearAsync(_uow, yearEntity, year.EndDate.AddDays(1), ct);
            if (next.Status != FnYearStatus.Open) return Bad<FiscalYearDto>("سال مالی بعد بسته است.");
            // years close in order: the previous year of the same entity must be closed first (so a later year is never closed before an earlier one)
            FnFiscalYear previous = await _uow.Set<FnFiscalYear>().Where(y => y.LegalEntityId == year.LegalEntityId && y.EndDate < year.StartDate).OrderByDescending(y => y.EndDate).FirstOrDefaultAsync(ct);
            if (previous != null && previous.Status != FnYearStatus.Closed) return Bad<FiscalYearDto>($"ابتدا سال مالی قبل ({previous.Year}) را ببندید؛ سال‌ها به ترتیب بسته می‌شوند.");

            // balances of the year per account and detail
            var rows = await _uow.Set<FnVoucherLine>().Where(l => l.Voucher.FiscalYearId == year.Id && l.Voucher.Status == FnVoucherStatus.Final)
                .GroupBy(l => new { l.AccountId, l.Detail1Id, l.Detail2Id })
                .Select(g => new { g.Key.AccountId, g.Key.Detail1Id, g.Key.Detail2Id, Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) }).ToListAsync(ct);
            Dictionary<int, FnAccount> byId = byCode.Values.ToDictionary(a => a.Id);
            var balances = new Dictionary<(int, int?, int?), decimal>();
            foreach (var r in rows)
            {
                decimal b = r.Debit - r.Credit;
                if (b != 0) balances[(r.AccountId, r.Detail1Id, r.Detail2Id)] = b;
            }

            DateTime endDate = year.EndDate.Date;
            var created = new List<FnVoucher>();
            FnVoucher MakeVoucher(FnFiscalYear fy, DateTime date, FnVoucherType type, string description) => new FnVoucher
            {
                FiscalYearId = fy.Id, FiscalYear = fy, Date = date, Type = type, Status = FnVoucherStatus.Final, Description = description,
                CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now, CheckedByUserId = Me, CheckedAt = DateTimeOffset.Now,
                FinalizedByUserId = Me, FinalizedAt = DateTimeOffset.Now, Lines = new List<FnVoucherLine>(),
            };
            void AddLine(FnVoucher v, FnAccount account, int? d1, int? d2, decimal amountDebitMinusCredit, string text)
            {
                if (amountDebitMinusCredit == 0) return;
                v.Lines.Add(new FnVoucherLine
                {
                    RowNumber = v.Lines.Count + 1, AccountId = account.Id, Detail1Id = d1, Detail2Id = d2,
                    Debit = amountDebitMinusCredit > 0 ? amountDebitMinusCredit : 0, Credit = amountDebitMinusCredit < 0 ? -amountDebitMinusCredit : 0, Description = text,
                });
            }

            string note = Clean(dto.Reason) != null ? " — " + Clean(dto.Reason) : "";

            // (1) temporary accounts -> summary account
            FnVoucher v1 = MakeVoucher(year, endDate, FnVoucherType.YearEndTemporaryClose, $"بستن حساب‌های موقت سال {year.Title}{note}");
            var temporary = balances.Where(p => byId[p.Key.Item1].Temporary && p.Key.Item1 != summary.Id)
                .GroupBy(p => p.Key.Item1).Select(g => new { AccountId = g.Key, Balance = g.Sum(x => x.Value) }).ToList();
            foreach (var t in temporary) AddLine(v1, byId[t.AccountId], null, null, -t.Balance, "بستن حساب موقت");
            decimal toSummary = v1.Lines.Sum(l => l.Debit) - v1.Lines.Sum(l => l.Credit);
            AddLine(v1, summary, null, null, -toSummary, "خلاصه سود و زیان");
            if (v1.Lines.Count > 0) created.Add(v1);

            // (2) summary account -> retained earnings
            decimal summaryBalance = balances.Where(p => p.Key.Item1 == summary.Id).Sum(p => p.Value) - toSummary;   // debit minus credit after (1)
            FnVoucher v2 = MakeVoucher(year, endDate, FnVoucherType.YearEndTemporaryClose, $"انتقال سود (زیان) سال {year.Title} به سود انباشته");
            AddLine(v2, summary, null, null, -summaryBalance, "انتقال خلاصه سود و زیان");
            AddLine(v2, retained, null, null, summaryBalance, "سود (زیان) انباشته");
            if (v2.Lines.Count > 0) created.Add(v2);

            // balances after (1) and (2) for the permanent accounts
            var closing = new Dictionary<(int, int?, int?), decimal>();
            foreach (var p in balances)
                if (!byId[p.Key.Item1].Temporary) closing[p.Key] = p.Value;
            if (summaryBalance != 0)
            {
                var key = (retained.Id, (int?)null, (int?)null);
                closing[key] = (closing.TryGetValue(key, out decimal old) ? old : 0) + summaryBalance;
            }

            // (3) closing voucher of the permanent accounts, (4) opening voucher of the next year
            FnVoucher v3 = MakeVoucher(year, endDate, FnVoucherType.Closing, $"سند اختتامیه سال {year.Title}");
            FnVoucher v4 = MakeVoucher(next, next.StartDate.Date, FnVoucherType.Opening, $"سند افتتاحیه سال {next.Title}");
            foreach (var p in closing.Where(x => x.Value != 0).OrderBy(x => byId[x.Key.Item1].Code))
            {
                AddLine(v3, byId[p.Key.Item1], p.Key.Item2, p.Key.Item3, -p.Value, "اختتامیه");
                AddLine(v4, byId[p.Key.Item1], p.Key.Item2, p.Key.Item3, p.Value, "افتتاحیه");
            }
            if (v3.Lines.Count > 0) created.Add(v3);
            if (v4.Lines.Count > 0) created.Add(v4);

            // numbers: gapless after the existing final vouchers of each year
            int numberInYear = await _uow.Set<FnVoucher>().Where(v => v.FiscalYearId == year.Id && v.Number != null).MaxAsync(v => (int?)v.Number, ct) ?? 0;
            int numberInNext = await _uow.Set<FnVoucher>().Where(v => v.FiscalYearId == next.Id && v.Number != null).MaxAsync(v => (int?)v.Number, ct) ?? 0;
            // the opening voucher of the new year is number 1 when the year has no final voucher yet; otherwise it takes the next number and
            // NOTHING already numbered is touched (numbers that were printed or exported never change; review 2, finding 8)
            foreach (FnVoucher v in created)
            {
                v.TemporaryNumber = await InventoryNumbers.NextAsync(_uow, FinanceNumbers.Voucher, v.FiscalYear.Year, ct);
                v.Number = v.FiscalYearId == year.Id ? ++numberInYear : ++numberInNext;
                v.TotalAmount = v.Lines.Sum(l => l.Debit);
                _uow.Set<FnVoucher>().Add(v);
            }
            if (v3.Lines.Count > 0) year.ClosingVoucher = v3;
            if (v4.Lines.Count > 0) next.OpeningVoucher = v4;
            year.Status = FnYearStatus.Closed;
            year.ClosedAt = DateTimeOffset.Now;
            year.ClosedByUserId = Me;
            foreach (FnPeriod p in year.Periods)
            {
                if (p.Status != FnPeriodStatus.Closed) { p.Status = FnPeriodStatus.Closed; p.LockedAt ??= DateTimeOffset.Now; }
            }
            _uow.Set<FnAuditLog>().Add(new FnAuditLog { EntityType = "FnFiscalYear", EntityId = year.Id, Action = "YearClose", Summary = $"بستن سال مالی {year.Title}: {created.Count} سند، شماره سند افتتاحیه سال بعد {(created.FirstOrDefault(x => x.Type == FnVoucherType.Opening)?.Number.ToString() ?? "-")}", UserId = Me, At = DateTimeOffset.Now });
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) { return Bad<FiscalYearDto>("بستن سال انجام نشد: " + (ex.InnerException?.Message ?? ex.Message)); }
            return ResponseDtoHelper.Ok((await LoadYearsAsync(year.Id, null, ct)).First());
        }

        // ================================================================================== chart of accounts

        private static AccountDto ToDto(FnAccount a, string parentCode, bool hasPostings) => new AccountDto
        {
            Id = a.Id, Code = a.Code, Title = a.Title, Level = a.Level, ParentId = a.ParentId, ParentCode = parentCode, Nature = a.Nature, Type = a.Type,
            Temporary = a.Temporary, DetailTypes = a.DetailTypes, Detail2Types = a.Detail2Types, RequiresDetail = a.RequiresDetail, CurrencyAllowed = a.CurrencyAllowed, IsMonetaryForeign = a.IsMonetaryForeign,
            StatementLine = a.StatementLine, IsActive = a.IsActive, SepidarCode = a.SepidarCode, HasPostings = hasPostings,
        };

        public async Task<ResponseDto<PagedList<AccountDto>>> GetAccountsAsync(AccountQuery query, CancellationToken ct)
        {
            await FinanceSeed.EnsureAsync(_uow, ct);
            query ??= new AccountQuery();
            IQueryable<FnAccount> q = _uow.Set<FnAccount>().AsNoTracking();
            if (!query.IncludeInactive) q = q.Where(a => a.IsActive);
            if (query.Level.HasValue) q = q.Where(a => a.Level == query.Level.Value);
            if (query.Type.HasValue) q = q.Where(a => a.Type == query.Type.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(a => a.Code.StartsWith(text) || a.Title.Contains(text));
            IQueryable<AccountDto> projection = q.OrderBy(a => a.Code).Select(a => new AccountDto
            {
                Id = a.Id, Code = a.Code, Title = a.Title, Level = a.Level, ParentId = a.ParentId, ParentCode = a.Parent.Code, Nature = a.Nature, Type = a.Type,
                Temporary = a.Temporary, DetailTypes = a.DetailTypes, Detail2Types = a.Detail2Types, RequiresDetail = a.RequiresDetail, CurrencyAllowed = a.CurrencyAllowed, IsMonetaryForeign = a.IsMonetaryForeign,
                StatementLine = a.StatementLine, IsActive = a.IsActive, SepidarCode = a.SepidarCode,
                HasPostings = _uow.Set<FnVoucherLine>().Any(l => l.AccountId == a.Id),
            });
            return ResponseDtoHelper.Ok(await PagedList<AccountDto>.CreateAsync(projection, query.PageNumber, query.PageSize, ct));
        }

        public async Task<ResponseDto<AccountDto>> SaveAccountAsync(SaveAccountDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<AccountDto>(NotAllowed + " (مدیر مالی)");
            if (dto == null || Clean(dto.Title) == null) return Bad<AccountDto>("عنوان حساب را وارد کنید.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            if (dto.StatementLine != null && Clean(dto.StatementLine) != null && !FinanceSeed.StatementLines.Any(s => s.Code == dto.StatementLine.Trim()))
                return Bad<AccountDto>("کد سطر صورت مالی نامعتبر است.");
            FnAccount a;
            if (dto.Id.HasValue)
            {
                a = await _uow.Set<FnAccount>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (a == null) return ResponseDtoHelper.NotFound<AccountDto>("حساب پیدا نشد.");
                string newCode = Clean(dto.Code) ?? a.Code;
                if (newCode != a.Code)
                {
                    if (await _uow.Set<FnVoucherLine>().AnyAsync(l => l.AccountId == a.Id, ct)) return Bad<AccountDto>("کد حسابی که سند دارد تغییر نمی‌کند؛ آن را غیرفعال کنید.");
                    return Bad<AccountDto>("تغییر کد حساب پشتیبانی نمی‌شود؛ حساب جدید بسازید.");
                }
            }
            else
            {
                string code = Digits(Clean(dto.Code));
                if (code == null || !code.All(char.IsDigit) || (code.Length != 3 && code.Length != 5)) return Bad<AccountDto>("کد حساب باید ۳ رقم (کل) یا ۵ رقم (معین) باشد.");
                if (await _uow.Set<FnAccount>().AnyAsync(x => x.Code == code, ct)) return Bad<AccountDto>("این کد قبلاً ثبت شده است: " + code);
                string parentCode = code.Length == 3 ? code.Substring(0, 1) : code.Substring(0, 3);
                FnAccount parent = await _uow.Set<FnAccount>().FirstOrDefaultAsync(x => x.Code == parentCode, ct);
                if (parent == null) return Bad<AccountDto>("حساب بالاتر (" + parentCode + ") در کدینگ نیست.");
                a = new FnAccount
                {
                    Code = code, Level = code.Length == 3 ? FnAccountLevel.General : FnAccountLevel.Subsidiary, ParentId = parent.Id, Jurisdiction = FinanceSeed.Iran,
                    Type = dto.Type ?? FinanceSeed.TypeOf(code),
                };
                _uow.Set<FnAccount>().Add(a);
            }
            a.Title = Clean(dto.Title);
            a.Nature = dto.Nature;
            if (dto.Type.HasValue) a.Type = dto.Type.Value;
            a.Temporary = dto.Temporary;
            a.DetailTypes = Clean(dto.DetailTypes) ?? "";
            a.Detail2Types = Clean(dto.Detail2Types) ?? "";
            a.RequiresDetail = dto.RequiresDetail;
            a.CurrencyAllowed = dto.CurrencyAllowed;
            a.IsMonetaryForeign = dto.CurrencyAllowed && dto.IsMonetaryForeign;
            a.StatementLine = Clean(dto.StatementLine);
            a.IsActive = dto.IsActive;
            a.SepidarCode = Clean(dto.SepidarCode);
            await _uow.SaveChangesAsync(ct);
            string pc = a.ParentId.HasValue ? await _uow.Set<FnAccount>().Where(x => x.Id == a.ParentId.Value).Select(x => x.Code).FirstOrDefaultAsync(ct) : null;
            return ResponseDtoHelper.Ok(ToDto(a, pc, await _uow.Set<FnVoucherLine>().AnyAsync(l => l.AccountId == a.Id, ct)));
        }

        public async Task<ResponseDto<bool>> DeleteAccountAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<bool>(NotAllowed + " (مدیر مالی)");
            FnAccount a = await _uow.Set<FnAccount>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a == null) return ResponseDtoHelper.NotFound<bool>("حساب پیدا نشد.");
            if (await _uow.Set<FnVoucherLine>().AnyAsync(l => l.AccountId == id, ct)) return Bad<bool>("حساب دارای سند حذف نمی‌شود؛ آن را غیرفعال کنید.");
            if (await _uow.Set<FnAccount>().AnyAsync(x => x.ParentId == id, ct)) return Bad<bool>("حساب زیرمجموعه دارد.");
            if (await _uow.Set<AccountMapping>().AnyAsync(m => m.AccountId == id, ct) || await _uow.Set<FnBankAccount>().AnyAsync(b => b.AccountId == id, ct))
                return Bad<bool>("حساب در نگاشت نقش‌ها یا حساب بانکی استفاده شده است.");
            _uow.Set<FnAccount>().Remove(a);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(true);
        }

        public async Task<ResponseDto<PagedList<DetailAccountDto>>> GetDetailsAsync(DetailAccountQuery query, CancellationToken ct)
        {
            query ??= new DetailAccountQuery();
            IQueryable<FnDetailAccount> q = _uow.Set<FnDetailAccount>().AsNoTracking();
            if (query.Type.HasValue) q = q.Where(d => d.Type == query.Type.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(d => d.Code.Contains(text) || d.Title.Contains(text));
            IQueryable<DetailAccountDto> projection = q.OrderBy(d => d.Type).ThenBy(d => d.Code).Select(d => new DetailAccountDto
            {
                Id = d.Id, Code = d.Code, Title = d.Title, Type = d.Type, LinkedEntityId = d.LinkedEntityId, IsActive = d.IsActive,
            });
            return ResponseDtoHelper.Ok(await PagedList<DetailAccountDto>.CreateAsync(projection, query.PageNumber, query.PageSize, ct));
        }

        private static readonly string[] ReservedDetailPrefixes = { "SUP-", "CUS-", "EMP-", "BNK-", "CSH-", "PTY-", "PRJ-", "WO-", "CC-", "AST-", "DT-" };

        public async Task<ResponseDto<DetailAccountDto>> SaveDetailAsync(SaveDetailAccountDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<DetailAccountDto>(NotAllowed);
            if (dto == null || Clean(dto.Title) == null) return Bad<DetailAccountDto>("عنوان تفصیلی را وارد کنید.");
            FnDetailAccount d;
            if (dto.Id.HasValue)
            {
                d = await _uow.Set<FnDetailAccount>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (d == null) return ResponseDtoHelper.NotFound<DetailAccountDto>("تفصیلی پیدا نشد.");
                if (d.LinkedEntityId.HasValue) return Bad<DetailAccountDto>("تفصیلی‌های مرتبط با تامین‌کننده، مشتری، پروژه و ... را سیستم می‌سازد و دستی ویرایش نمی‌شود.");
            }
            else
            {
                if (dto.Type != FnDetailType.Person && dto.Type != FnDetailType.Other) return Bad<DetailAccountDto>("فقط تفصیلی «شخص» و «سایر» دستی ساخته می‌شود؛ بقیه را سیستم می‌سازد.");
                string code = Clean(dto.Code);
                if (code == null) return Bad<DetailAccountDto>("کد تفصیلی را وارد کنید.");
                // the system makes details like SUP-12 itself: a manual code with such a prefix would collide later and stop the posting job
                if (ReservedDetailPrefixes.Any(x => code.StartsWith(x, StringComparison.OrdinalIgnoreCase))) return Bad<DetailAccountDto>("پیشوندهای SUP-، CUS-، EMP-، BNK-، CSH-، PTY-، PRJ-، WO-، CC-، AST- و DT- برای تفصیلی‌های خودکار سیستم رزرو است.");
                if (await _uow.Set<FnDetailAccount>().AnyAsync(x => x.Code == code, ct)) return Bad<DetailAccountDto>("این کد قبلاً ثبت شده است: " + code);
                d = new FnDetailAccount { Code = code, Type = dto.Type };
                _uow.Set<FnDetailAccount>().Add(d);
            }
            d.Title = Clean(dto.Title);
            d.IsActive = dto.IsActive;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new DetailAccountDto { Id = d.Id, Code = d.Code, Title = d.Title, Type = d.Type, LinkedEntityId = d.LinkedEntityId, IsActive = d.IsActive });
        }

        // ================================================================================== vouchers: reading

        private async Task<List<VoucherDto>> LoadVoucherDtosAsync(IQueryable<FnVoucher> query, bool withLines, CancellationToken ct)
        {
            IQueryable<FnVoucher> q = query.AsNoTracking();
            if (withLines) q = q.Include(v => v.Lines).ThenInclude(l => l.Account).Include(v => v.Lines).ThenInclude(l => l.Detail1).Include(v => v.Lines).ThenInclude(l => l.Detail2);
            List<FnVoucher> rows = await q.Include(v => v.FiscalYear).AsSplitQuery().ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(v => new int?[] { v.CreatedByUserId, v.CheckedByUserId, v.FinalizedByUserId }), ct);
            List<int> ids = rows.Select(v => v.Id).ToList();
            Dictionary<int, int> reversedBy = await _uow.Set<FnVoucher>().Where(v => v.ReversesVoucherId != null && ids.Contains(v.ReversesVoucherId.Value))
                .ToDictionaryAsync(v => v.ReversesVoucherId.Value, v => v.Id, ct);
            Dictionary<int, int> attachmentCounts = await _uow.Set<FnVoucherAttachment>().Where(a => ids.Contains(a.VoucherId))
                .GroupBy(a => a.VoucherId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            return rows.Select(v => new VoucherDto
            {
                Id = v.Id, FiscalYearId = v.FiscalYearId, FiscalYearTitle = v.FiscalYear?.Title, Number = v.Number, TemporaryNumber = v.TemporaryNumber, Date = v.Date,
                Type = v.Type, Status = v.Status, Description = v.Description, SourceType = v.SourceType, SourceId = v.SourceId, SourceNumber = v.SourceNumber,
                AccountingEventId = v.AccountingEventId, ReversesVoucherId = v.ReversesVoucherId, ReverseNextPeriod = v.ReverseNextPeriod, AutoReversalVoucherId = v.AutoReversalVoucherId, ReversedByVoucherId = reversedBy.TryGetValue(v.Id, out int rb) ? rb : (int?)null,
                LatePosting = v.LatePosting, TotalAmount = v.TotalAmount, CreatedByUserId = v.CreatedByUserId,
                LegalEntityId = v.FiscalYear == null ? 0 : v.FiscalYear.LegalEntityId, IsImported = v.IsImported,
                CeoApprovedByUserId = v.CeoApprovedByUserId, CeoApprovedAt = v.CeoApprovedAt,
                AttachmentCount = attachmentCounts.TryGetValue(v.Id, out int ac) ? ac : 0,
                CreatorName = v.CreatedByUserId == 0 ? "سیستم" : names.Name(v.CreatedByUserId), CreatedAt = v.CreatedAt,
                CheckedByName = v.CheckedByUserId == 0 ? "سیستم" : names.Name(v.CheckedByUserId), CheckedAt = v.CheckedAt,
                FinalizedByName = names.Name(v.FinalizedByUserId), FinalizedAt = v.FinalizedAt,
                Lines = withLines ? v.Lines.OrderBy(l => l.RowNumber).Select(l => new VoucherLineDto
                {
                    RowNumber = l.RowNumber, AccountId = l.AccountId, AccountCode = l.Account?.Code, AccountTitle = l.Account?.Title,
                    Detail1Id = l.Detail1Id, Detail1Code = l.Detail1?.Code, Detail1Title = l.Detail1?.Title,
                    Detail2Id = l.Detail2Id, Detail2Code = l.Detail2?.Code, Detail2Title = l.Detail2?.Title,
                    Debit = l.Debit, Credit = l.Credit, CurrencyCode = l.CurrencyCode, CurrencyAmount = l.CurrencyAmount, Rate = l.Rate, Quantity = l.Quantity,
                    Description = l.Description, WorkOrderId = l.WorkOrderId, ContractId = l.ContractId, CostCenterId = l.CostCenterId, SourceRole = l.SourceRole,
                }).ToList() : new List<VoucherLineDto>(),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<VoucherDto>>> GetVouchersAsync(VoucherQuery query, CancellationToken ct)
        {
            await FinanceSeed.EnsureAsync(_uow, ct);
            query ??= new VoucherQuery();
            IQueryable<FnVoucher> q = _uow.Set<FnVoucher>();
            if (query.FiscalYearId.HasValue) q = q.Where(v => v.FiscalYearId == query.FiscalYearId.Value);
            else
            {
                int entityId = await EntityScope.ResolveAsync(_uow, query.LegalEntityId, Me, ct);
                q = q.Where(v => v.FiscalYear.LegalEntityId == entityId);
            }
            if (query.From.HasValue) q = q.Where(v => v.Date >= query.From.Value.Date);
            if (query.To.HasValue) q = q.Where(v => v.Date <= query.To.Value.Date);
            if (query.Status.HasValue) q = q.Where(v => v.Status == query.Status.Value);
            if (query.Type.HasValue) q = q.Where(v => v.Type == query.Type.Value);
            if (query.AccountId.HasValue) q = q.Where(v => v.Lines.Any(l => l.AccountId == query.AccountId.Value));
            if (query.Detail1Id.HasValue) q = q.Where(v => v.Lines.Any(l => l.Detail1Id == query.Detail1Id.Value));
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(v => v.Description.Contains(text) || v.SourceNumber.Contains(text));
            int total = await q.CountAsync(ct);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize, number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<VoucherDto> list = await LoadVoucherDtosAsync(q.OrderByDescending(v => v.Date).ThenByDescending(v => v.Id).Skip((number - 1) * size).Take(size), query.WithLines, ct);
            list = list.OrderByDescending(v => v.Date).ThenByDescending(v => v.Id).ToList();
            PagedList<VoucherDto> page = await PagedList<VoucherDto>.CreateAsync(list, 1, 0, ct);
            page.TotalCount = total; page.CurrentPage = number; page.PageSize = size; page.TotalPages = (int)Math.Ceiling(total / (double)Math.Max(size, 1));
            return ResponseDtoHelper.Ok(page);
        }

        public async Task<ResponseDto<VoucherDto>> GetVoucherAsync(int id, CancellationToken ct)
        {
            List<VoucherDto> list = await LoadVoucherDtosAsync(_uow.Set<FnVoucher>().Where(v => v.Id == id), true, ct);
            return list.Count == 0 ? ResponseDtoHelper.NotFound<VoucherDto>("سند پیدا نشد.") : ResponseDtoHelper.Ok(list[0]);
        }

        // ================================================================================== vouchers: writing

        /// <summary>Checks the date against the periods. Locked periods are open to the finance manager only; closed ones to nobody.</summary>
        private async Task<(FnFiscalYear Year, string Error)> CheckDateAsync(DateTime date, int entityId, CancellationToken ct)
        {
            PeriodCache cache = await PeriodCache.LoadAsync(_uow, ct, entityId);
            FnPeriod p = cache.Find(date);
            if (p == null)
            {
                FnLegalEntity entity = await _uow.Set<FnLegalEntity>().FirstOrDefaultAsync(x => x.Id == entityId, ct);
                if (entity == null || !entity.IsActive) return (null, "واحد حقوقی پیدا نشد یا غیرفعال است.");
                int y = FinanceSeed.YearNumberOf(entity, date);
                int latest = cache.Years.Count == 0 ? 0 : cache.Years.Max(x => x.Year);
                if (cache.Years.Count == 0 || y <= latest + 1)
                {
                    cache.Add(await FinanceSeed.EnsureEntityYearAsync(_uow, entity, date, ct));
                    p = cache.Find(date);
                }
                if (p == null) return (null, $"سال مالی {y} تعریف نشده است.");
            }
            if (p.FiscalYear.Status != FnYearStatus.Open) return (null, "سال مالی این تاریخ بسته است.");
            if (p.Status == FnPeriodStatus.Closed) return (null, "دوره مالی این تاریخ بسته است و سندی در آن ثبت نمی‌شود.");
            if (p.Status == FnPeriodStatus.Locked && !IsManager) return (null, "دوره مالی این تاریخ قفل است؛ فقط مدیر مالی می‌تواند در آن سند ثبت کند.");
            return (p.FiscalYear, null);
        }

        private async Task<(List<FnVoucherLine> Lines, string Error)> BuildLinesAsync(IEnumerable<SaveVoucherLineDto> dtoLines, bool manual, CancellationToken ct)
        {
            List<SaveVoucherLineDto> input = (dtoLines ?? new List<SaveVoucherLineDto>()).Where(l => l != null).ToList();
            if (input.Count < 2) return (null, "سند حداقل دو ردیف لازم دارد.");
            List<int> accountIds = input.Where(l => l.AccountId.HasValue).Select(l => l.AccountId.Value).Distinct().ToList();
            List<string> accountCodes = input.Where(l => !l.AccountId.HasValue && Clean(l.AccountCode) != null).Select(l => Digits(l.AccountCode.Trim())).Distinct().ToList();
            List<FnAccount> accounts = await _uow.Set<FnAccount>().Where(a => accountIds.Contains(a.Id) || accountCodes.Contains(a.Code)).ToListAsync(ct);
            List<int> detailIds = input.SelectMany(l => new int?[] { l.Detail1Id, l.Detail2Id }).Where(x => x.HasValue).Select(x => x.Value).Distinct().ToList();
            List<string> detailCodes = input.SelectMany(l => new[] { l.Detail1Code, l.Detail2Code }).Where(x => Clean(x) != null).Select(x => x.Trim()).Distinct().ToList();
            List<FnDetailAccount> details = await _uow.Set<FnDetailAccount>().Where(d => detailIds.Contains(d.Id) || detailCodes.Contains(d.Code)).ToListAsync(ct);

            var lines = new List<FnVoucherLine>();
            int row = 1;
            foreach (SaveVoucherLineDto l in input)
            {
                FnAccount account = l.AccountId.HasValue ? accounts.FirstOrDefault(a => a.Id == l.AccountId.Value)
                    : accounts.FirstOrDefault(a => a.Code == Digits(Clean(l.AccountCode) ?? ""));
                if (account == null) return (null, $"ردیف {row}: حساب پیدا نشد.");
                if (!account.IsActive) return (null, $"ردیف {row}: حساب {account.Code} غیرفعال است.");
                if (account.Level != FnAccountLevel.Subsidiary) return (null, $"ردیف {row}: سند فقط به حساب معین ثبت می‌شود ({account.Code} سطح کل یا گروه است).");
                decimal debit = R(l.Debit), credit = R(l.Credit);
                if (debit < 0 || credit < 0 || (debit > 0) == (credit > 0)) return (null, $"ردیف {row}: هر ردیف فقط یکی از بدهکار یا بستانکار را با مبلغ مثبت دارد.");

                FnDetailAccount d1 = null, d2 = null;
                if (l.Detail1Id.HasValue || Clean(l.Detail1Code) != null)
                {
                    d1 = l.Detail1Id.HasValue ? details.FirstOrDefault(d => d.Id == l.Detail1Id.Value) : details.FirstOrDefault(d => d.Code == l.Detail1Code.Trim());
                    if (d1 == null || !d1.IsActive) return (null, $"ردیف {row}: تفصیلی اول پیدا نشد یا غیرفعال است.");
                    if (manual && !Has(account.DetailTypes, d1.Type)) return (null, $"ردیف {row}: حساب {account.Code} تفصیلی از نوع {d1.Type} نمی‌پذیرد.");
                }
                if (l.Detail2Id.HasValue || Clean(l.Detail2Code) != null)
                {
                    d2 = l.Detail2Id.HasValue ? details.FirstOrDefault(d => d.Id == l.Detail2Id.Value) : details.FirstOrDefault(d => d.Code == l.Detail2Code.Trim());
                    if (d2 == null || !d2.IsActive) return (null, $"ردیف {row}: تفصیلی دوم پیدا نشد یا غیرفعال است.");
                    if (manual && !Has(account.Detail2Types, d2.Type)) return (null, $"ردیف {row}: حساب {account.Code} تفصیلی دوم از نوع {d2.Type} نمی‌پذیرد.");
                }
                if (manual && account.RequiresDetail && d1 == null) return (null, $"ردیف {row}: حساب {account.Code} ({account.Title}) تفصیلی اجباری دارد.");

                bool anyCurrency = Clean(l.CurrencyCode) != null || l.CurrencyAmount.HasValue || l.Rate.HasValue;
                if (anyCurrency && !FinanceFeatures.ForeignCurrency) return (null, FinanceFeatures.OnlyRial);
                if (anyCurrency)
                {
                    if (Clean(l.CurrencyCode) == null || !l.CurrencyAmount.HasValue || !l.Rate.HasValue || l.Rate.Value <= 0 || l.CurrencyAmount.Value < 0)
                        return (null, $"ردیف {row}: کد ارز، مبلغ ارزی و نرخ باید با هم و مثبت وارد شوند.");
                    if (!account.CurrencyAllowed) return (null, $"ردیف {row}: حساب {account.Code} ارزی نیست.");
                }
                lines.Add(new FnVoucherLine
                {
                    RowNumber = row++, AccountId = account.Id, Detail1Id = d1?.Id, Detail2Id = d2?.Id, Debit = debit, Credit = credit,
                    CurrencyCode = anyCurrency ? Clean(l.CurrencyCode) : null, CurrencyAmount = anyCurrency ? l.CurrencyAmount : null, Rate = anyCurrency ? l.Rate : null,
                    Quantity = l.Quantity, Description = Clean(l.Description), WorkOrderId = l.WorkOrderId, ContractId = l.ContractId, CostCenterId = l.CostCenterId,
                });
            }
            decimal totalDebit = lines.Sum(x => x.Debit), totalCredit = lines.Sum(x => x.Credit);
            if (totalDebit != totalCredit) return (null, $"سند متوازن نیست: جمع بدهکار {totalDebit:0} و جمع بستانکار {totalCredit:0}.");
            if (totalDebit <= 0) return (null, "مبلغ سند باید بیشتر از صفر باشد.");
            return (lines, null);
        }

        public async Task<ResponseDto<VoucherDto>> SaveVoucherAsync(SaveVoucherDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<VoucherDto>(NotAllowed);
            if (dto == null || dto.Date == default) return Bad<VoucherDto>("تاریخ سند را وارد کنید.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            int entityId = await EntityScope.ResolveAsync(_uow, dto.LegalEntityId, Me, ct);
            if (dto.Id.HasValue && !dto.LegalEntityId.HasValue)
            {
                int? existing = await _uow.Set<FnVoucher>().Where(x => x.Id == dto.Id.Value).Select(x => (int?)x.FiscalYear.LegalEntityId).FirstOrDefaultAsync(ct);
                if (existing.HasValue) entityId = existing.Value;
            }
            var date = await CheckDateAsync(dto.Date.Date, entityId, ct);
            if (date.Error != null) return Bad<VoucherDto>(date.Error);
            var built = await BuildLinesAsync(dto.Lines, true, ct);
            if (built.Error != null) return Bad<VoucherDto>(built.Error);

            FnVoucher v;
            if (dto.Id.HasValue)
            {
                v = await _uow.Set<FnVoucher>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (v == null) return ResponseDtoHelper.NotFound<VoucherDto>("سند پیدا نشد.");
                if (v.Status == FnVoucherStatus.Final) return Bad<VoucherDto>("سند قطعی ویرایش نمی‌شود؛ سند اصلاحی (برگشتی) صادر کنید.");
                if (v.IsImported) return Bad<VoucherDto>("سند واردشده از سپیدار فقط خواندنی است.");
                if (v.Type == FnVoucherType.Auto || v.Type == FnVoucherType.Closing || v.Type == FnVoucherType.YearEndTemporaryClose)
                    return Bad<VoucherDto>("سند خودکار یا پایان سال ویرایش نمی‌شود.");
                if (v.Type == FnVoucherType.Reversal)
                {
                    // a reversal mirrors its original: only the date may move (inside an open period of the same year); the lines never change
                    if (date.Year.Id != v.FiscalYearId) return Bad<VoucherDto>("تاریخ سند برگشتی باید در همان سال مالی بماند.");
                    v.Date = dto.Date.Date;
                    v.EditorUserIds = WithEditor(v.EditorUserIds, Me);
                    v.Status = FnVoucherStatus.Draft; v.CheckedByUserId = null; v.CheckedAt = null;
                    await _uow.SaveChangesAsync(ct);
                    return await GetVoucherAsync(v.Id, ct);
                }
                _uow.Set<FnVoucherLine>().RemoveRange(v.Lines);
                v.Lines = new List<FnVoucherLine>();
                v.EditorUserIds = WithEditor(v.EditorUserIds, Me);
                v.Status = FnVoucherStatus.Draft;
                v.CheckedByUserId = null;
                v.CheckedAt = null;
                if (v.FiscalYearId != date.Year.Id)
                {
                    v.FiscalYearId = date.Year.Id;
                    v.TemporaryNumber = await InventoryNumbers.NextAsync(_uow, FinanceNumbers.Voucher, date.Year.Year, ct);
                }
            }
            else
            {
                v = new FnVoucher
                {
                    FiscalYearId = date.Year.Id, Type = FnVoucherType.Normal, Status = FnVoucherStatus.Draft, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now,
                    TemporaryNumber = await InventoryNumbers.NextAsync(_uow, FinanceNumbers.Voucher, date.Year.Year, ct), Lines = new List<FnVoucherLine>(),
                };
                _uow.Set<FnVoucher>().Add(v);
            }
            v.Date = dto.Date.Date;
            v.Description = Clean(dto.Description);
            v.ReverseNextPeriod = dto.ReverseNextPeriod;
            foreach (FnVoucherLine l in built.Lines) v.Lines.Add(l);
            v.TotalAmount = built.Lines.Sum(l => l.Debit);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<VoucherDto>("همزمان توسط کاربر دیگری تغییر کرد؛ دوباره تلاش کنید."); }
            return await GetVoucherAsync(v.Id, ct);
        }

        public async Task<ResponseDto<bool>> DeleteVoucherAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<bool>(NotAllowed);
            FnVoucher v = await _uow.Set<FnVoucher>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (v == null) return ResponseDtoHelper.NotFound<bool>("سند پیدا نشد.");
            if (v.Status != FnVoucherStatus.Draft || v.Type != FnVoucherType.Normal) return Bad<bool>("فقط پیش‌نویس دستی حذف می‌شود.");
            _uow.Set<FnVoucher>().Remove(v);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(true);
        }

        public async Task<ResponseDto<VoucherDto>> VoucherActionAsync(VoucherActionDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<VoucherDto>("اطلاعات ارسال‌شده کامل نیست.");
            string action = dto.Action.Trim().ToLowerInvariant();
            if (action == "finalize") return await FinalizeOneAsync(dto.Id, ct);
            if (action == "ceo-approve") return await CeoApproveAsync(dto.Id, ct);
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<VoucherDto>(NotAllowed);
            FnVoucher v = await _uow.Set<FnVoucher>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (v == null) return ResponseDtoHelper.NotFound<VoucherDto>("سند پیدا نشد.");
            switch (action)
            {
                case "check":
                    if (v.Status != FnVoucherStatus.Draft) return Bad<VoucherDto>("فقط پیش‌نویس کنترل می‌شود.");
                    if (v.Type != FnVoucherType.Auto && Touched(v.CreatedByUserId, v.EditorUserIds, Me)) return Bad<VoucherDto>(SameUser);
                    string checkGate = (await VoucherGateAsync(new List<int> { v.Id }, false, ct)).GetValueOrDefault(v.Id);
                    if (checkGate != null) return Bad<VoucherDto>(checkGate);
                    v.Status = FnVoucherStatus.Checked;
                    v.CheckedByUserId = Me;
                    v.CheckedAt = DateTimeOffset.Now;
                    break;
                case "uncheck":
                    if (v.Status != FnVoucherStatus.Checked) return Bad<VoucherDto>("فقط سند کنترل‌شده به پیش‌نویس برمی‌گردد.");
                    if (v.Type == FnVoucherType.Auto) return Bad<VoucherDto>("سند خودکار به پیش‌نویس برنمی‌گردد.");
                    v.Status = FnVoucherStatus.Draft;
                    v.CheckedByUserId = null;
                    v.CheckedAt = null;
                    break;
                case "reverse":
                    return await ReverseAsync(v, dto, ct);
                default:
                    return Bad<VoucherDto>("عمل نامعتبر است.");
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<VoucherDto>("همزمان توسط کاربر دیگری تغییر کرد؛ دوباره تلاش کنید."); }
            return await GetVoucherAsync(v.Id, ct);
        }

        private async Task<ResponseDto<VoucherDto>> ReverseAsync(FnVoucher original, VoucherActionDto dto, CancellationToken ct)
        {
            if (original.Status != FnVoucherStatus.Final) return Bad<VoucherDto>("فقط سند قطعی با سند برگشتی اصلاح می‌شود.");
            if (original.Type == FnVoucherType.Opening || original.Type == FnVoucherType.Closing || original.Type == FnVoucherType.YearEndTemporaryClose)
                return Bad<VoucherDto>("سند افتتاحیه و اختتامیه برگشت نمی‌خورد.");
            if (await _uow.Set<FnVoucher>().AnyAsync(x => x.ReversesVoucherId == original.Id, ct)) return Bad<VoucherDto>("برای این سند قبلاً سند برگشتی صادر شده است.");
            if (original.IsImported) return Bad<VoucherDto>("سند واردشده از سپیدار فقط خواندنی است.");
            int originalEntity = await _uow.Set<FnFiscalYear>().Where(y => y.Id == original.FiscalYearId).Select(y => y.LegalEntityId).FirstAsync(ct);
            DateTime date = (dto.Date ?? original.Date).Date;
            var check = await CheckDateAsync(date, originalEntity, ct);
            if (check.Error != null && dto.Date == null)
            {
                date = DateTime.Today;
                check = await CheckDateAsync(date, originalEntity, ct);
            }
            if (check.Error != null) return Bad<VoucherDto>(check.Error);
            var reversal = new FnVoucher
            {
                FiscalYearId = check.Year.Id, Date = date, Type = FnVoucherType.Reversal, Status = FnVoucherStatus.Draft, ReversesVoucherId = original.Id,
                Description = $"برگشت سند شماره {original.Number}" + (Clean(dto.Note) != null ? " - " + Clean(dto.Note) : ""),
                CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now, TotalAmount = original.TotalAmount,
                TemporaryNumber = await InventoryNumbers.NextAsync(_uow, FinanceNumbers.Voucher, check.Year.Year, ct),
                Lines = original.Lines.OrderBy(l => l.RowNumber).Select(l => new FnVoucherLine
                {
                    RowNumber = l.RowNumber, AccountId = l.AccountId, Detail1Id = l.Detail1Id, Detail2Id = l.Detail2Id, Debit = l.Credit, Credit = l.Debit,
                    CurrencyCode = l.CurrencyCode, CurrencyAmount = l.CurrencyAmount, Rate = l.Rate, Quantity = l.Quantity, Description = l.Description,
                    WorkOrderId = l.WorkOrderId, ContractId = l.ContractId, CostCenterId = l.CostCenterId, SourceRole = l.SourceRole,
                }).ToList(),
            };
            _uow.Set<FnVoucher>().Add(reversal);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<VoucherDto>("برای این سند قبلاً سند برگشتی صادر شده است."); }
            return await GetVoucherAsync(reversal.Id, ct);
        }

        private async Task<ResponseDto<VoucherDto>> FinalizeOneAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<VoucherDto>(NotAllowed + " (مدیر مالی)");
            for (int attempt = 0; attempt < 3; attempt++)
            {
                FnVoucher v = await _uow.Set<FnVoucher>().FirstOrDefaultAsync(x => x.Id == id, ct);
                if (v == null) return ResponseDtoHelper.NotFound<VoucherDto>("سند پیدا نشد.");
                if (v.Status != FnVoucherStatus.Checked) return Bad<VoucherDto>("فقط سند کنترل‌شده قطعی می‌شود.");
                if (Touched(v.CreatedByUserId, v.EditorUserIds, Me)) return Bad<VoucherDto>(SameUser);
                string finGate = (await VoucherGateAsync(new List<int> { v.Id }, true, ct)).GetValueOrDefault(v.Id);
                if (finGate != null) return Bad<VoucherDto>(finGate);
                var period = await _uow.Set<FnPeriod>().Include(p => p.FiscalYear).FirstOrDefaultAsync(p => p.FiscalYearId == v.FiscalYearId && p.StartDate <= v.Date && p.EndDate >= v.Date, ct);
                if (period == null || period.Status == FnPeriodStatus.Closed || period.FiscalYear.Status != FnYearStatus.Open) return Bad<VoucherDto>("دوره مالی سند بسته است.");
                int max = await _uow.Set<FnVoucher>().Where(x => x.FiscalYearId == v.FiscalYearId && x.Number != null).MaxAsync(x => (int?)x.Number, ct) ?? 0;
                v.Number = max + 1;
                v.Status = FnVoucherStatus.Final;
                v.FinalizedByUserId = Me;
                v.FinalizedAt = DateTimeOffset.Now;
                try
                {
                    await _uow.SaveChangesAsync(ct);
                    var done = await GetVoucherAsync(v.Id, ct);
                    if (done.Success && done.Data != null && await _uow.Set<FnVoucher>().AsNoTracking().AnyAsync(x => x.FiscalYearId == v.FiscalYearId && x.Number != null && x.Number < v.Number && x.Date > v.Date, ct))
                        done.Data.Warning = "تاریخ این سند از تاریخ سند قطعی با شماره کمتر عقب‌تر است؛ شماره قطعی به ترتیب قطعی شدن داده می‌شود نه به ترتیب تاریخ.";
                    return done;
                }
                catch (DbUpdateException)
                {
                    _uow.Entry(v).State = EntityState.Detached;   // number taken by someone else: read again and retry
                }
            }
            return Bad<VoucherDto>("شماره‌گذاری قطعی همزمان با کاربر دیگر انجام شد؛ دوباره تلاش کنید.");
        }

        public async Task<ResponseDto<FinalizeResultDto>> FinalizeUpToAsync(FinalizeUpToDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<FinalizeResultDto>(NotAllowed + " (مدیر مالی)");
            if (dto == null || dto.UpTo == default) return Bad<FinalizeResultDto>("تاریخ پایان را وارد کنید.");
            var result = new FinalizeResultDto();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                result = new FinalizeResultDto();
                IQueryable<FnVoucher> q = _uow.Set<FnVoucher>().Where(v => v.Status == FnVoucherStatus.Checked && v.Date <= dto.UpTo.Date);
                if (dto.FiscalYearId.HasValue) q = q.Where(v => v.FiscalYearId == dto.FiscalYearId.Value);
                else
                {
                    int entityId = await EntityScope.ResolveAsync(_uow, dto.LegalEntityId, Me, ct);
                    q = q.Where(v => v.FiscalYear.LegalEntityId == entityId);
                }
                List<FnVoucher> vouchers = await q.OrderBy(v => v.Date).ThenBy(v => v.TemporaryNumber).ThenBy(v => v.Id).ToListAsync(ct);
                Dictionary<int, string> gates = await VoucherGateAsync(vouchers.Select(v => v.Id).ToList(), true, ct);
                List<FnFiscalYear> years = await _uow.Set<FnFiscalYear>().Include(y => y.Periods).ToListAsync(ct);
                var touched = new List<FnVoucher>();
                foreach (var group in vouchers.GroupBy(v => v.FiscalYearId))
                {
                    FnFiscalYear year = years.First(y => y.Id == group.Key);
                    int max = await _uow.Set<FnVoucher>().Where(x => x.FiscalYearId == group.Key && x.Number != null).MaxAsync(x => (int?)x.Number, ct) ?? 0;
                    foreach (FnVoucher v in group)
                    {
                        FnPeriod p = year.Periods.FirstOrDefault(x => x.StartDate <= v.Date && x.EndDate >= v.Date);
                        if (year.Status != FnYearStatus.Open || p == null || p.Status == FnPeriodStatus.Closed) { result.Skipped.Add($"سند موقت {v.TemporaryNumber}: دوره بسته است."); continue; }
                        if (Touched(v.CreatedByUserId, v.EditorUserIds, Me)) { result.Skipped.Add($"سند موقت {v.TemporaryNumber}: تهیه‌کننده نمی‌تواند قطعی کند."); continue; }
                        if (gates.TryGetValue(v.Id, out string gateMessage)) { result.Skipped.Add($"سند موقت {v.TemporaryNumber}: {gateMessage}"); continue; }
                        v.Number = ++max;
                        v.Status = FnVoucherStatus.Final;
                        v.FinalizedByUserId = Me;
                        v.FinalizedAt = DateTimeOffset.Now;
                        touched.Add(v);
                        result.Finalized++;
                        result.FirstNumber ??= v.Number;
                        result.LastNumber = v.Number;
                    }
                }
                if (touched.Count == 0) return ResponseDtoHelper.Ok(result);
                try
                {
                    await _uow.SaveChangesAsync(ct);
                    return ResponseDtoHelper.Ok(result);
                }
                catch (DbUpdateException)
                {
                    foreach (FnVoucher v in touched) _uow.Entry(v).State = EntityState.Detached;
                }
            }
            return Bad<FinalizeResultDto>("شماره‌گذاری قطعی همزمان با کاربر دیگر انجام شد؛ دوباره تلاش کنید.");
        }

        public async Task<ResponseDto<PostEventsResultDto>> PostEventsAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.SystemJobs)) return Bad<PostEventsResultDto>(NotAllowed);
            return ResponseDtoHelper.Ok(await _posting.PostPendingAsync(ct));
        }

        // ================================================================================== opening balance

        public Task<ResponseDto<VoucherDto>> ImportOpeningAsync(LedgerOpeningImportDto dto, CancellationToken ct) => ImportOpeningCoreAsync(dto, true, ct);

        /// <summary>The opening import; the migration wizard calls it with manualRules = false (imported details keep the guessed type).</summary>
        private async Task<ResponseDto<VoucherDto>> ImportOpeningCoreAsync(LedgerOpeningImportDto dto, bool manualRules, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<VoucherDto>(NotAllowed);
            List<OpeningRowDto> rows = (dto?.Rows ?? new List<OpeningRowDto>()).Where(r => r != null).ToList();
            if (rows.Count == 0) return Bad<VoucherDto>("فایل هیچ ردیفی ندارد.");
            if (rows.Count > 20000) return Bad<VoucherDto>("حداکثر ۲۰٬۰۰۰ ردیف در هر بار.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            int defaultEntity = await EntityScope.ResolveAsync(_uow, null, Me, ct);
            FnFiscalYear year = dto.FiscalYearId.HasValue
                ? await _uow.Set<FnFiscalYear>().FirstOrDefaultAsync(y => y.Id == dto.FiscalYearId.Value, ct)
                : await FinanceSeed.EnsureEntityYearAsync(_uow, await _uow.Set<FnLegalEntity>().FirstOrDefaultAsync(x => x.Id == defaultEntity, ct), DateTime.Today, ct);
            if (year == null) return ResponseDtoHelper.NotFound<VoucherDto>("سال مالی پیدا نشد.");
            if (year.Status != FnYearStatus.Open) return Bad<VoucherDto>("سال مالی بسته است.");
            if (await _uow.Set<FnVoucher>().AnyAsync(v => v.FiscalYearId == year.Id && v.Type == FnVoucherType.Opening, ct))
                return Bad<VoucherDto>("برای این سال قبلاً سند افتتاحیه ثبت شده است.");
            var lines = new List<SaveVoucherLineDto>();
            int row = 0;
            foreach (OpeningRowDto r in rows)
            {
                row++;
                if (Clean(r.AccountCode) == null) return Bad<VoucherDto>($"ردیف {row}: کد حساب خالی است.");
                lines.Add(new SaveVoucherLineDto { AccountCode = r.AccountCode, Detail1Code = r.DetailCode, Debit = r.Debit, Credit = r.Credit, Description = "تراز افتتاحیه" });
            }
            // an opening line may carry both amounts (a trial balance row): split it into two lines
            var split = new List<SaveVoucherLineDto>();
            foreach (SaveVoucherLineDto l in lines)
            {
                if (l.Debit > 0 && l.Credit > 0)
                {
                    split.Add(new SaveVoucherLineDto { AccountCode = l.AccountCode, Detail1Code = l.Detail1Code, Debit = l.Debit, Description = l.Description });
                    split.Add(new SaveVoucherLineDto { AccountCode = l.AccountCode, Detail1Code = l.Detail1Code, Credit = l.Credit, Description = l.Description });
                }
                else if (l.Debit != 0 || l.Credit != 0) split.Add(l);
            }
            var built = await BuildLinesAsync(split, manualRules, ct);
            if (built.Error != null) return Bad<VoucherDto>(built.Error);
            var voucher = new FnVoucher
            {
                FiscalYearId = year.Id, Date = year.StartDate.Date, Type = FnVoucherType.Opening, Status = FnVoucherStatus.Checked,
                Description = Clean(dto.Description) ?? $"سند افتتاحیه سال {year.Title} (تراز آزمایشی ورودی)",
                CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now, CheckedByUserId = 0, CheckedAt = DateTimeOffset.Now,
                TemporaryNumber = await InventoryNumbers.NextAsync(_uow, FinanceNumbers.Voucher, year.Year, ct),
                TotalAmount = built.Lines.Sum(l => l.Debit), Lines = built.Lines,
            };
            _uow.Set<FnVoucher>().Add(voucher);
            await _uow.SaveChangesAsync(ct);
            return await GetVoucherAsync(voucher.Id, ct);
        }

        // ================================================================================== setup check

        public async Task<ResponseDto<SetupCheckDto>> SetupCheckAsync(CancellationToken ct)
        {
            await FinanceSeed.EnsureAsync(_uow, ct);
            var result = new SetupCheckDto();
            DateTime today = DateTime.Today;
            void Problem(string code, string message) => result.Problems.Add(new SetupProblemDto { Code = code, Message = message });

            FnPeriod period = await _uow.Set<FnPeriod>().Include(p => p.FiscalYear).FirstOrDefaultAsync(p => p.FiscalYear.LegalEntityId == FinanceSeed.MainEntityId && p.StartDate <= today && p.EndDate >= today, ct);
            if (period == null) Problem("FISCAL_YEAR_MISSING", "برای امروز سال مالی تعریف نشده است.");
            else if (period.Status != FnPeriodStatus.Open || period.FiscalYear.Status != FnYearStatus.Open) Problem("PERIOD_NOT_OPEN", "دوره مالی امروز باز نیست.");
            if (!await _uow.Set<FnAccount>().AnyAsync(a => a.Level == FnAccountLevel.Subsidiary, ct)) Problem("CHART_EMPTY", "کدینگ حساب‌ها خالی است.");

            HashSet<string> mapped = (await _uow.Set<AccountMapping>().Where(m => m.IsActive && m.AccountId != null).Select(m => m.Role).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (role, title) in AccountRoles.Standard())
                if (!mapped.Contains(role)) Problem("ROLE_UNMAPPED:" + role, $"نقش حساب «{title}» ({role}) به حساب دفتر کل نگاشت نشده است.");

            List<string> usedRoles = await _uow.Set<AccountingEventLine>().Where(l => l.Event.Status != AccountingEventStatus.Posted && l.Event.Status != AccountingEventStatus.NotRequired)
                .Select(l => l.AccountRole).Distinct().ToListAsync(ct);
            foreach (string role in usedRoles)
            {
                if (role.StartsWith(EventPostingService.DirectRolePrefix, StringComparison.Ordinal)) continue;
                string baseRole = role.IndexOf('.') > 0 ? role.Substring(0, role.IndexOf('.')) : role;
                if (!mapped.Contains(role) && !mapped.Contains(baseRole)) Problem("EVENT_ROLE_UNMAPPED:" + role, $"رویدادهای ثبت‌نشده نقش «{role}» را دارند که نگاشت ندارد.");
            }

            bool vat = await _uow.Set<FnTaxRate>().AnyAsync(r => r.Kind == FnTaxKind.Vat && r.EffectiveFrom <= today && (r.EffectiveTo == null || r.EffectiveTo >= today), ct);
            if (!vat) Problem("VAT_RATE_MISSING", "نرخ مالیات بر ارزش افزوده برای سال جاری در جدول نرخ‌ها تعریف نشده است.");

            result.ToVerify = (await _uow.Set<FnTaxRate>().Where(r => r.Verify).Select(r => r.Kind).Distinct().ToListAsync(ct)).Select(k => k.ToString()).ToList();
            // release F2: the optional modules report what they still need (rates are entered by finance, never invented); this is not a ledger problem
            result.Modules.Add(await AssetService.CheckSetupAsync(_uow, ct));
            result.Modules.Add(await PayrollMasterService.CheckSetupAsync(_uow, PersianCal.Year(today), ct));
            result.Ok = result.Problems.Count == 0;
            return ResponseDtoHelper.Ok(result);
        }
    }
}
