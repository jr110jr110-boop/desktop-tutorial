using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    public partial class TreasuryService
    {
        // ================================================================================== petty cash settlements

        private async Task<List<SettlementDto>> LoadSettlementsAsync(IQueryable<FnPettyCashSettlement> query, CancellationToken ct)
        {
            List<FnPettyCashSettlement> rows = await query.AsNoTracking().Include(s => s.PettyCash).Include(s => s.Lines).AsSplitQuery().ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(s => new int?[] { s.CreatedByUserId, s.ApprovedByUserId, s.PettyCash.HolderUserId }), ct);
            List<int> accountIds = rows.SelectMany(s => s.Lines).Select(l => l.AccountId).Distinct().ToList();
            Dictionary<int, string> codes = await _uow.Set<FnAccount>().AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code, ct);
            return rows.Select(s => new SettlementDto
            {
                Id = s.Id, Number = s.Number, PettyCashId = s.PettyCashId, HolderName = names.Name(s.PettyCash.HolderUserId), Date = s.Date, Status = s.Status,
                TotalAmount = s.TotalAmount, VatAmount = s.VatAmount, CreatorName = names.Name(s.CreatedByUserId), ApprovedByName = names.Name(s.ApprovedByUserId),
                Lines = s.Lines.OrderBy(l => l.RowNumber).Select(l => new SettlementLineDto
                {
                    RowNumber = l.RowNumber, Description = l.Description, AccountId = l.AccountId, AccountCode = codes.TryGetValue(l.AccountId, out string c) ? c : null,
                    CostCenterId = l.CostCenterId, WorkOrderId = l.WorkOrderId, Amount = l.Amount, VatAmount = l.VatAmount, SupplierName = l.SupplierName,
                    DocumentId = l.DocumentId, InventoryDocumentId = l.InventoryDocumentId,
                }).ToList(),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<SettlementDto>>> GetSettlementsAsync(TreasuryQuery query, CancellationToken ct)
        {
            query ??= new TreasuryQuery();
            IQueryable<FnPettyCashSettlement> q = _uow.Set<FnPettyCashSettlement>().AsNoTracking();
            if (query.PettyCashId.HasValue) q = q.Where(s => s.PettyCashId == query.PettyCashId.Value);
            if (query.Status.HasValue) q = q.Where(s => s.Status == query.Status.Value);
            int total = await q.CountAsync(ct);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize, number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<SettlementDto> list = await LoadSettlementsAsync(q.OrderByDescending(s => s.Id).Skip((number - 1) * size).Take(size), ct);
            return ResponseDtoHelper.Ok(Page(list.OrderByDescending(s => s.Id).ToList(), total, number, size));
        }

        public async Task<ResponseDto<SettlementDto>> SaveSettlementAsync(SaveSettlementDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<SettlementDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnPettyCash petty = await _uow.Set<FnPettyCash>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.PettyCashId, ct);
            if (petty == null || !petty.IsActive) return Bad<SettlementDto>("تنخواه پیدا نشد یا غیرفعال است.");
            if (petty.HolderUserId != Me && !_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return Bad<SettlementDto>(NotAllowed);
            await FinanceSeed.EnsureAsync(_uow, ct);
            DateTime date = dto.Date == default ? DateTime.Today : dto.Date.Date;
            List<SettlementLineDto> input = (dto.Lines ?? new List<SettlementLineDto>()).Where(l => l != null).ToList();
            if (input.Count == 0) return Bad<SettlementDto>("تسویه حداقل یک ردیف هزینه لازم دارد.");

            FnPettyCashSettlement s = null;
            if (dto.Id.HasValue)
            {
                s = await _uow.Set<FnPettyCashSettlement>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (s == null) return ResponseDtoHelper.NotFound<SettlementDto>("تسویه پیدا نشد.");
                if (s.Status != FnDocStatus.Draft) return Bad<SettlementDto>("فقط پیش‌نویس ویرایش می‌شود.");
                if (s.PettyCashId != petty.Id) return Bad<SettlementDto>("تنخواه تسویه قابل تغییر نیست.");
                s.EditorUserIds = WithEditor(s.EditorUserIds, Me);
            }

            List<int> accountIds = input.Where(l => l.AccountId.HasValue).Select(l => l.AccountId.Value).ToList();
            List<string> accountCodes = input.Where(l => !l.AccountId.HasValue && Clean(l.AccountCode) != null).Select(l => Digits(l.AccountCode.Trim())).ToList();
            List<FnAccount> accounts = await _uow.Set<FnAccount>().AsNoTracking().Where(a => accountIds.Contains(a.Id) || accountCodes.Contains(a.Code)).ToListAsync(ct);
            var lines = new List<FnPettyCashSettlementLine>();
            int row = 1;
            foreach (SettlementLineDto l in input)
            {
                FnAccount account;
                decimal amount = R(l.Amount), vat = R(l.VatAmount);
                if (amount <= 0 || vat < 0) return Bad<SettlementDto>($"ردیف {row}: مبلغ هزینه باید بیشتر از صفر و مالیات نامنفی باشد.");
                if (l.InventoryDocumentId.HasValue)
                {
                    // a petty-cash purchase receipt was already costed (Dr inventory / Cr GRNI): the settlement clears the GRNI, it is not a second expense
                    int self = s?.Id ?? 0;
                    InventoryDocument doc = await _uow.Set<InventoryDocument>().AsNoTracking().Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == l.InventoryDocumentId.Value && !d.Deleted, ct);
                    if (doc == null || doc.Type != InventoryDocumentType.PurchaseReceipt || doc.PurchaseOrderId.HasValue) return Bad<SettlementDto>($"ردیف {row}: رسید خرید تنخواهی پیدا نشد.");
                    if (await _uow.Set<FnPettyCashSettlementLine>().AnyAsync(x => x.InventoryDocumentId == doc.Id && x.Settlement.Status != FnDocStatus.Cancelled && x.SettlementId != self, ct))
                        return Bad<SettlementDto>($"ردیف {row}: این رسید قبلاً تسویه شده است.");
                    decimal receiptValue = R(doc.Lines.Sum(x => x.Quantity * (x.UnitPrice ?? 0)));
                    if (amount != receiptValue) return Bad<SettlementDto>($"ردیف {row}: مبلغ باید برابر ارزش رسید ({receiptValue:0} ریال) باشد؛ اختلاف قیمت را با اصلاح رسید رفع کنید.");
                    account = await _uow.Set<FnAccount>().AsNoTracking().FirstOrDefaultAsync(a => a.Code == "30103" && a.IsActive, ct);
                    if (account == null) return Bad<SettlementDto>($"ردیف {row}: حساب کالای دریافتی فاکتورنشده (۳۰۱۰۳) پیدا نشد.");
                }
                else
                {
                    account = l.AccountId.HasValue ? accounts.FirstOrDefault(a => a.Id == l.AccountId.Value) : accounts.FirstOrDefault(a => a.Code == Digits(Clean(l.AccountCode) ?? ""));
                    if (account == null || !account.IsActive || account.Level != FnAccountLevel.Subsidiary || account.Type != FnAccountType.Expense)
                        return Bad<SettlementDto>($"ردیف {row}: حساب هزینه (معین) پیدا نشد یا مجاز نیست.");
                }
                lines.Add(new FnPettyCashSettlementLine
                {
                    RowNumber = row++, Description = Clean(l.Description), AccountId = account.Id, CostCenterId = l.CostCenterId, WorkOrderId = l.WorkOrderId, Amount = amount, VatAmount = vat,
                    SupplierName = Clean(l.SupplierName), DocumentId = l.DocumentId, InventoryDocumentId = l.InventoryDocumentId,
                });
            }
            if (s == null)
            {
                s = new FnPettyCashSettlement
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, FinanceNumbers.PettyCashSettlement, "PCS", date, ct), PettyCashId = petty.Id, Status = FnDocStatus.Draft,
                    CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now, Lines = new List<FnPettyCashSettlementLine>(),
                };
                _uow.Set<FnPettyCashSettlement>().Add(s);
            }
            else
            {
                _uow.Set<FnPettyCashSettlementLine>().RemoveRange(s.Lines);
                s.Lines = new List<FnPettyCashSettlementLine>();
            }
            s.Date = date; s.VatAmount = lines.Sum(l => l.VatAmount); s.TotalAmount = lines.Sum(l => l.Amount + l.VatAmount);
            foreach (FnPettyCashSettlementLine l in lines) s.Lines.Add(l);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<SettlementDto>("ذخیره تسویه ناموفق بود؛ احتمالاً رسید انتخاب‌شده قبلاً تسویه شده است."); }
            return ResponseDtoHelper.Ok((await LoadSettlementsAsync(_uow.Set<FnPettyCashSettlement>().Where(x => x.Id == s.Id), ct))[0]);
        }

        public async Task<ResponseDto<SettlementDto>> SettlementActionAsync(DocActionDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<SettlementDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnPettyCashSettlement s = await _uow.Set<FnPettyCashSettlement>().Include(x => x.PettyCash).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (s == null) return ResponseDtoHelper.NotFound<SettlementDto>("تسویه پیدا نشد.");
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "approve":
                {
                    if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<SettlementDto>(NotAllowed + " (مالی)");
                    if (s.Status != FnDocStatus.Draft) return Bad<SettlementDto>("فقط پیش‌نویس تایید می‌شود.");
                    if (s.PettyCash.HolderUserId == Me || Touched(s.CreatedByUserId, s.EditorUserIds, Me)) return Bad<SettlementDto>("تسویه تنخواه را دارنده تنخواه یا ثبت‌کننده آن نمی‌تواند تایید کند.");
                    if (s.TotalAmount > s.PettyCash.Balance) return Bad<SettlementDto>($"مانده تنخواه ({s.PettyCash.Balance:0} ریال) برای این تسویه کافی نیست.");
                    Dictionary<int, FnAccount> accounts = await _uow.Set<FnAccount>().Where(a => s.Lines.Select(l => l.AccountId).Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
                    List<int> ccIds = s.Lines.Where(l => l.CostCenterId.HasValue).Select(l => l.CostCenterId.Value).Distinct().ToList();
                    Dictionary<int, string> ccCodes = await _uow.Set<CostCenter>().Where(c => ccIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
                    var lines = new List<AccountingEventLine>();
                    foreach (FnPettyCashSettlementLine l in s.Lines.OrderBy(x => x.RowNumber))
                    {
                        if (!accounts.TryGetValue(l.AccountId, out FnAccount a) || !a.IsActive) return Bad<SettlementDto>($"ردیف {l.RowNumber}: حساب هزینه غیرفعال شده است.");
                        lines.Add(new AccountingEventLine
                        {
                            AccountRole = EventPostingService.DirectRolePrefix + a.Code, Debit = l.Amount, WorkOrderId = l.WorkOrderId,
                            CostCenter = l.CostCenterId.HasValue && ccCodes.TryGetValue(l.CostCenterId.Value, out string cc) ? cc : null,
                            Description = Clean(l.Description) ?? l.SupplierName ?? "هزینه تنخواه",
                        });
                    }
                    if (s.VatAmount > 0) lines.Add(new AccountingEventLine { AccountRole = AccountRoles.Vat, Debit = s.VatAmount, Description = "مالیات ارزش افزوده تنخواه " + s.Number });
                    lines.Add(new AccountingEventLine { AccountRole = AccountRoles.PettyCash, Credit = s.TotalAmount, PartyType = "PettyCash", PartyId = s.PettyCashId, Description = "تسویه " + s.Number });
                    _costing.AddEvent(AccountingEventType.PettyCashSettlement, s.Date, "FnPettyCashSettlement", s.Id, s.Number, $"تسویه تنخواه {s.Number}", lines);
                    s.PettyCash.Balance -= s.TotalAmount;
                    s.Status = FnDocStatus.Posted; s.ApprovedByUserId = Me; s.ApprovedAt = DateTimeOffset.Now;
                    break;
                }
                case "cancel":
                    if (s.Status != FnDocStatus.Draft) return Bad<SettlementDto>("فقط پیش‌نویس باطل می‌شود.");
                    if (s.CreatedByUserId != Me && !_user.IsInRole(FinanceRoles.Accountants)) return Bad<SettlementDto>(NotAllowed);
                    s.Status = FnDocStatus.Cancelled;
                    break;
                default:
                    return Bad<SettlementDto>("عمل نامعتبر است.");
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SettlementDto>(Conflict); }
            return ResponseDtoHelper.Ok((await LoadSettlementsAsync(_uow.Set<FnPettyCashSettlement>().Where(x => x.Id == s.Id), ct))[0]);
        }

        public async Task<ResponseDto<PagedList<PettyCandidateDto>>> GetPettyCandidatesAsync(int pettyCashId, TreasuryQuery query, CancellationToken ct)
        {
            query ??= new TreasuryQuery();
            FnPettyCash petty = await _uow.Set<FnPettyCash>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == pettyCashId, ct);
            if (petty == null) return ResponseDtoHelper.NotFound<PagedList<PettyCandidateDto>>("تنخواه پیدا نشد.");
            if (petty.HolderUserId != Me && !_user.IsInRole(FinanceRoles.Readers)) return Bad<PagedList<PettyCandidateDto>>(NotAllowed);
            List<int> used = await _uow.Set<FnPettyCashSettlementLine>().AsNoTracking().Where(l => l.InventoryDocumentId != null && l.Settlement.Status != FnDocStatus.Cancelled)
                .Select(l => l.InventoryDocumentId.Value).ToListAsync(ct);
            IQueryable<PettyCandidateDto> q = _uow.Set<InventoryDocument>().AsNoTracking()
                .Where(d => !d.Deleted && d.Type == InventoryDocumentType.PurchaseReceipt && d.Status == InventoryDocumentStatus.Confirmed && d.PurchaseOrderId == null &&
                            d.CreatorUserId == petty.HolderUserId && !used.Contains(d.Id))
                .OrderBy(d => d.DocumentDate).ThenBy(d => d.Id)
                .Select(d => new PettyCandidateDto
                {
                    InventoryDocumentId = d.Id, Number = d.Number, Date = d.DocumentDate, Reference = d.ReferenceNumber,
                    Amount = d.Lines.Sum(l => l.Quantity * (l.UnitPrice ?? 0)),
                });
            return ResponseDtoHelper.Ok(await PagedList<PettyCandidateDto>.CreateAsync(q, query.PageNumber, query.PageSize, ct));
        }

        // ================================================================================== bank statements

        private async Task<int> MatchDaysAsync(CancellationToken ct)
        {
            string v = await _uow.Set<FnSetting>().AsNoTracking().Where(s => s.Key == "MatchDays").Select(s => s.Value).FirstOrDefaultAsync(ct);
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int d) && d >= 0 && d <= 30 ? d : 3;
        }

        private async Task<List<BankStatementDto>> LoadStatementsAsync(IQueryable<FnBankStatement> query, bool withLines, CancellationToken ct)
        {
            List<FnBankStatement> rows = await query.AsNoTracking().Include(s => s.BankAccount).Include(s => s.Lines).AsSplitQuery().ToListAsync(ct);
            return rows.Select(s => new BankStatementDto
            {
                Id = s.Id, BankAccountId = s.BankAccountId, BankAccountTitle = s.BankAccount.Title, FromDate = s.FromDate, ToDate = s.ToDate, OpeningBalance = s.OpeningBalance,
                ClosingBalance = s.ClosingBalance, Matched = s.Lines.Count(l => l.MatchKind != FnStatementMatch.None), Unmatched = s.Lines.Count(l => l.MatchKind == FnStatementMatch.None),
                Lines = withLines ? s.Lines.OrderBy(l => l.RowNumber).Select(l => new StatementLineViewDto
                {
                    Id = l.Id, RowNumber = l.RowNumber, Date = l.Date, Description = l.Description, Withdrawal = l.Withdrawal, Deposit = l.Deposit, Reference = l.Reference,
                    Balance = l.Balance, MatchKind = l.MatchKind, MatchedId = l.MatchedId,
                }).ToList() : new List<StatementLineViewDto>(),
            }).ToList();
        }

        public async Task<ResponseDto<BankStatementDto>> ImportStatementAsync(ImportStatementDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return Bad<BankStatementDto>(NotAllowed);
            if (dto == null) return Bad<BankStatementDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnBankAccount bank = await _uow.Set<FnBankAccount>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == dto.BankAccountId, ct);
            if (bank == null || !bank.IsActive) return Bad<BankStatementDto>("حساب بانکی پیدا نشد یا غیرفعال است.");
            List<StatementRowDto> rows = (dto.Rows ?? new List<StatementRowDto>()).Where(r => r != null).ToList();
            if (rows.Count == 0) return Bad<BankStatementDto>("صورت‌حساب هیچ ردیفی ندارد.");
            if (rows.Count > 5000) return Bad<BankStatementDto>("حداکثر ۵٬۰۰۰ ردیف در هر بار.");
            var lines = new List<FnBankStatementLine>();
            int n = 1;
            foreach (StatementRowDto r in rows)
            {
                decimal w = R(r.Withdrawal), d = R(r.Deposit);
                if (r.Date == default) return Bad<BankStatementDto>($"ردیف {n}: تاریخ خالی است.");
                if (w < 0 || d < 0 || (w > 0) == (d > 0)) return Bad<BankStatementDto>($"ردیف {n}: فقط یکی از برداشت یا واریز باید مبلغ مثبت داشته باشد.");
                lines.Add(new FnBankStatementLine { RowNumber = n++, Date = r.Date.Date, Description = Clean(r.Description), Withdrawal = w, Deposit = d, Reference = Clean(r.Reference), Balance = r.Balance });
            }
            decimal opening = R(dto.OpeningBalance), closing = R(dto.ClosingBalance);
            if (opening + lines.Sum(l => l.Deposit) - lines.Sum(l => l.Withdrawal) != closing)
                return Bad<BankStatementDto>("مانده پایانی با مانده ابتدایی و گردش‌ها نمی‌خواند؛ فایل صورت‌حساب را بررسی کنید.");
            DateTime from = lines.Min(l => l.Date), to = lines.Max(l => l.Date);
            bool already = await _uow.Set<FnBankStatement>().AnyAsync(x => x.BankAccountId == bank.Id && x.FromDate == from && x.ToDate == to
                && x.OpeningBalance == opening && x.ClosingBalance == closing && x.Lines.Count == lines.Count, ct);
            if (already) return Bad<BankStatementDto>("این صورت‌حساب (همین بازه و مانده‌ها) قبلاً وارد شده است.");
            var st = new FnBankStatement
            {
                BankAccountId = bank.Id, FromDate = lines.Min(l => l.Date), ToDate = lines.Max(l => l.Date), OpeningBalance = opening, ClosingBalance = closing,
                ImportedAt = DateTimeOffset.Now, ImportedByUserId = Me, Lines = lines,
            };
            _uow.Set<FnBankStatement>().Add(st);
            await _uow.SaveChangesAsync(ct);
            return await GetStatementAsync(st.Id, ct);
        }

        public async Task<ResponseDto<PagedList<BankStatementDto>>> GetStatementsAsync(TreasuryQuery query, CancellationToken ct)
        {
            query ??= new TreasuryQuery();
            IQueryable<FnBankStatement> q = _uow.Set<FnBankStatement>().AsNoTracking();
            if (query.BankAccountId.HasValue) q = q.Where(s => s.BankAccountId == query.BankAccountId.Value);
            int total = await q.CountAsync(ct);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize, number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<BankStatementDto> list = await LoadStatementsAsync(q.OrderByDescending(s => s.Id).Skip((number - 1) * size).Take(size), false, ct);
            return ResponseDtoHelper.Ok(Page(list.OrderByDescending(s => s.Id).ToList(), total, number, size));
        }

        public async Task<ResponseDto<BankStatementDto>> GetStatementAsync(int id, CancellationToken ct)
        {
            List<BankStatementDto> list = await LoadStatementsAsync(_uow.Set<FnBankStatement>().Where(s => s.Id == id), true, ct);
            return list.Count == 0 ? ResponseDtoHelper.NotFound<BankStatementDto>("صورت‌حساب پیدا نشد.") : ResponseDtoHelper.Ok(list[0]);
        }

        private static bool Mentions(FnBankStatementLine line, params string[] keys)
        {
            string text = (line.Reference ?? "") + " " + (line.Description ?? "");
            return keys.Any(k => !string.IsNullOrWhiteSpace(k) && k.Length >= 3 && text.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        private sealed class BankItem
        {
            public int Id;
            public DateTime Date;
            public decimal Amount;
            public string[] Refs;
        }

        /// <summary>Posted bank transfers and card payments (or receipts) of one bank account, one item per document.</summary>
        private async Task<List<BankItem>> BankItemsAsync(int bankId, bool payments, DateTime from, DateTime to, CancellationToken ct)
        {
            if (payments)
            {
                var rows = await _uow.Set<FnPaymentLine>().AsNoTracking()
                    .Where(l => l.BankAccountId == bankId && (l.Instrument == FnInstrument.BankTransfer || l.Instrument == FnInstrument.Card) && l.Payment.Status == FnDocStatus.Posted &&
                                l.Payment.Date >= from && l.Payment.Date <= to)
                    .Select(l => new { l.PaymentId, l.Payment.Date, l.Amount, l.Reference }).ToListAsync(ct);
                return rows.GroupBy(x => x.PaymentId).Select(g => new BankItem { Id = g.Key, Date = g.First().Date, Amount = g.Sum(x => x.Amount), Refs = g.Select(x => x.Reference).ToArray() }).ToList();
            }
            var rr = await _uow.Set<FnReceiptLine>().AsNoTracking()
                .Where(l => l.BankAccountId == bankId && (l.Instrument == FnInstrument.BankTransfer || l.Instrument == FnInstrument.Card) && l.Receipt.Status == FnDocStatus.Posted &&
                            l.Receipt.Date >= from && l.Receipt.Date <= to)
                .Select(l => new { l.ReceiptId, l.Receipt.Date, l.Amount, l.Reference }).ToListAsync(ct);
            return rr.GroupBy(x => x.ReceiptId).Select(g => new BankItem { Id = g.Key, Date = g.First().Date, Amount = g.Sum(x => x.Amount), Refs = g.Select(x => x.Reference).ToArray() }).ToList();
        }

        public async Task<ResponseDto<BankStatementDto>> AutoMatchAsync(int statementId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryUsers)) return Bad<BankStatementDto>(NotAllowed + " (خزانه‌داری)");
            FnBankStatement st = await _uow.Set<FnBankStatement>().Include(s => s.Lines).Include(s => s.BankAccount).FirstOrDefaultAsync(s => s.Id == statementId, ct);
            if (st == null) return ResponseDtoHelper.NotFound<BankStatementDto>("صورت‌حساب پیدا نشد.");
            FnBankAccount bank = st.BankAccount;
            int days = await MatchDaysAsync(ct);
            bool Near(DateTime a, DateTime b) => Math.Abs((a.Date - b.Date).TotalDays) <= days;

            var usedBefore = await _uow.Set<FnBankStatementLine>().AsNoTracking()
                .Where(l => l.Statement.BankAccountId == bank.Id && l.MatchKind != FnStatementMatch.None && l.MatchedId != null).Select(l => new { l.MatchKind, Id = l.MatchedId.Value }).ToListAsync(ct);
            var used = new HashSet<(FnStatementMatch, int)>(usedBefore.Select(x => (x.MatchKind, x.Id)));
            DateTime from = st.FromDate.AddDays(-60), to = st.ToDate.AddDays(days);

            List<FnIssuedCheque> issued = await _uow.Set<FnIssuedCheque>().Include(c => c.ChequeBook).Where(c => c.Status == FnIssuedChequeStatus.Issued && c.ChequeBook.BankAccountId == bank.Id).ToListAsync(ct);
            List<FnReceivedCheque> received = await _uow.Set<FnReceivedCheque>().Include(c => c.History).Where(c => c.Status == FnReceivedChequeStatus.Deposited && c.DepositBankAccountId == bank.Id).ToListAsync(ct);
            List<BankItem> payments = await BankItemsAsync(bank.Id, true, from, to, ct);
            List<BankItem> receipts = await BankItemsAsync(bank.Id, false, from, to, ct);

            int matched = 0;
            foreach (FnBankStatementLine line in st.Lines.Where(l => l.MatchKind == FnStatementMatch.None).OrderBy(l => l.RowNumber))
            {
                if (line.Withdrawal > 0)
                {
                    FnIssuedCheque c = issued.Where(x => !used.Contains((FnStatementMatch.Cheque, x.Id)) && x.Amount == line.Withdrawal &&
                                                         (Mentions(line, x.Serial.ToString(CultureInfo.InvariantCulture), x.SayadId) || (x.DueDate.HasValue && Near(x.DueDate.Value, line.Date))))
                        .OrderByDescending(x => Mentions(line, x.Serial.ToString(CultureInfo.InvariantCulture), x.SayadId)).ThenBy(x => x.DueDate.HasValue ? Math.Abs((x.DueDate.Value - line.Date).TotalDays) : 999).FirstOrDefault();
                    if (c != null)
                    {
                        AccountingEvent ev = ClearIssuedCheque(c, bank, line.Date);
                        line.MatchKind = FnStatementMatch.Cheque; line.MatchedId = c.Id; line.AccountingEvent = ev;
                        used.Add((FnStatementMatch.Cheque, c.Id)); matched++;
                        continue;
                    }
                    BankItem p = payments.Where(x => !used.Contains((FnStatementMatch.Payment, x.Id)) && x.Amount == line.Withdrawal && (Mentions(line, x.Refs) || Near(x.Date, line.Date)))
                        .OrderByDescending(x => Mentions(line, x.Refs)).ThenBy(x => Math.Abs((x.Date - line.Date).TotalDays)).FirstOrDefault();
                    if (p != null)
                    {
                        line.MatchKind = FnStatementMatch.Payment; line.MatchedId = p.Id; used.Add((FnStatementMatch.Payment, p.Id)); matched++;
                    }
                }
                else if (line.Deposit > 0)
                {
                    FnReceivedCheque c = received.Where(x => !used.Contains((FnStatementMatch.Cheque, x.Id)) && x.Amount == line.Deposit &&
                                                             (Mentions(line, x.Serial, x.SayadId) || Near(x.DueDate, line.Date)))
                        .OrderByDescending(x => Mentions(line, x.Serial, x.SayadId)).ThenBy(x => Math.Abs((x.DueDate - line.Date).TotalDays)).FirstOrDefault();
                    if (c != null)
                    {
                        AccountingEvent ev = ClearReceivedCheque(c, bank, line.Date, "وصول طبق صورت‌حساب بانک");
                        line.MatchKind = FnStatementMatch.Cheque; line.MatchedId = c.Id; line.AccountingEvent = ev;
                        used.Add((FnStatementMatch.Cheque, c.Id)); matched++;
                        continue;
                    }
                    BankItem r = receipts.Where(x => !used.Contains((FnStatementMatch.Receipt, x.Id)) && x.Amount == line.Deposit && (Mentions(line, x.Refs) || Near(x.Date, line.Date)))
                        .OrderByDescending(x => Mentions(line, x.Refs)).ThenBy(x => Math.Abs((x.Date - line.Date).TotalDays)).FirstOrDefault();
                    if (r != null)
                    {
                        line.MatchKind = FnStatementMatch.Receipt; line.MatchedId = r.Id; used.Add((FnStatementMatch.Receipt, r.Id)); matched++;
                    }
                }
            }
            if (matched > 0)
            {
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException) { return Bad<BankStatementDto>(Conflict); }
            }
            return await GetStatementAsync(st.Id, ct);
        }

        public async Task<ResponseDto<BankStatementDto>> MatchAsync(StatementMatchDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Kind) == null) return Bad<BankStatementDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnBankStatement st = await _uow.Set<FnBankStatement>().Include(s => s.BankAccount).FirstOrDefaultAsync(s => s.Id == dto.StatementId, ct);
            if (st == null) return ResponseDtoHelper.NotFound<BankStatementDto>("صورت‌حساب پیدا نشد.");
            FnBankStatementLine line = await _uow.Set<FnBankStatementLine>().FirstOrDefaultAsync(l => l.Id == dto.LineId && l.StatementId == st.Id, ct);
            if (line == null) return ResponseDtoHelper.NotFound<BankStatementDto>("ردیف صورت‌حساب پیدا نشد.");
            if (line.MatchKind != FnStatementMatch.None) return Bad<BankStatementDto>("این ردیف قبلاً تطبیق داده شده است.");
            FnBankAccount bank = st.BankAccount;
            string kind = dto.Kind.Trim().ToLowerInvariant();
            async Task<bool> UsedAsync(FnStatementMatch k, int id) => await _uow.Set<FnBankStatementLine>().AnyAsync(l => l.Statement.BankAccountId == bank.Id && l.MatchKind == k && l.MatchedId == id, ct);

            switch (kind)
            {
                case "cheque":
                {
                    if (!_user.IsInRole(FinanceRoles.TreasuryUsers)) return Bad<BankStatementDto>(NotAllowed + " (خزانه‌داری)");
                    if (!dto.TargetId.HasValue) return Bad<BankStatementDto>("چک را مشخص کنید.");
                    if (line.Deposit > 0)
                    {
                        FnReceivedCheque c = await _uow.Set<FnReceivedCheque>().Include(x => x.History).FirstOrDefaultAsync(x => x.Id == dto.TargetId.Value, ct);
                        if (c == null || c.Status != FnReceivedChequeStatus.Deposited || c.DepositBankAccountId != bank.Id) return Bad<BankStatementDto>("چک واگذارشده به این بانک پیدا نشد.");
                        if (c.Amount != line.Deposit) return Bad<BankStatementDto>("مبلغ چک با مبلغ واریز برابر نیست.");
                        line.AccountingEvent = ClearReceivedCheque(c, bank, line.Date, "وصول طبق صورت‌حساب بانک");
                    }
                    else
                    {
                        FnIssuedCheque c = await _uow.Set<FnIssuedCheque>().Include(x => x.ChequeBook).FirstOrDefaultAsync(x => x.Id == dto.TargetId.Value, ct);
                        if (c == null || c.Status != FnIssuedChequeStatus.Issued || c.ChequeBook.BankAccountId != bank.Id) return Bad<BankStatementDto>("چک صادرشده این بانک پیدا نشد.");
                        if (c.Amount != line.Withdrawal) return Bad<BankStatementDto>("مبلغ چک با مبلغ برداشت برابر نیست.");
                        line.AccountingEvent = ClearIssuedCheque(c, bank, line.Date);
                    }
                    line.MatchKind = FnStatementMatch.Cheque; line.MatchedId = dto.TargetId;
                    break;
                }
                case "payment":
                case "receipt":
                {
                    if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return Bad<BankStatementDto>(NotAllowed);
                    if (!dto.TargetId.HasValue) return Bad<BankStatementDto>("سند را مشخص کنید.");
                    bool pay = kind == "payment";
                    if (pay != (line.Withdrawal > 0)) return Bad<BankStatementDto>(pay ? "پرداخت فقط با ردیف برداشت تطبیق می‌خورد." : "دریافت فقط با ردیف واریز تطبیق می‌خورد.");
                    List<BankItem> items = await BankItemsAsync(bank.Id, pay, DateTime.MinValue, DateTime.MaxValue, ct);
                    BankItem item = items.FirstOrDefault(x => x.Id == dto.TargetId.Value);
                    if (item == null) return Bad<BankStatementDto>("سند بانکی ثبت‌شده برای این حساب پیدا نشد.");
                    if (item.Amount != (pay ? line.Withdrawal : line.Deposit)) return Bad<BankStatementDto>("مبلغ سند با ردیف صورت‌حساب برابر نیست.");
                    FnStatementMatch k = pay ? FnStatementMatch.Payment : FnStatementMatch.Receipt;
                    if (await UsedAsync(k, item.Id)) return Bad<BankStatementDto>("این سند قبلاً با ردیف دیگری تطبیق خورده است.");
                    line.MatchKind = k; line.MatchedId = item.Id;
                    break;
                }
                case "fee":
                case "interest":
                {
                    if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<BankStatementDto>(NotAllowed + " (مالی)");
                    bool fee = kind == "fee";
                    if (fee && line.Withdrawal <= 0) return Bad<BankStatementDto>("کارمزد باید ردیف برداشت باشد.");
                    if (!fee && line.Deposit <= 0) return Bad<BankStatementDto>("سود سپرده باید ردیف واریز باشد.");
                    decimal amount = fee ? line.Withdrawal : line.Deposit;
                    line.AccountingEvent = _costing.AddEvent(fee ? AccountingEventType.BankFee : AccountingEventType.BankInterest, line.Date, "FnBankStatement", st.Id, $"{st.Id}-{line.RowNumber}",
                        (fee ? "کارمزد بانکی - " : "سود سپرده - ") + (line.Description ?? bank.Title), fee
                            ? new[] { new AccountingEventLine { AccountRole = AccountRoles.BankFees, Debit = amount, Description = line.Description }, BankLine(bank, 0, amount, line.Description) }
                            : new[] { BankLine(bank, amount, 0, line.Description), new AccountingEventLine { AccountRole = AccountRoles.InterestIncome, Credit = amount, Description = line.Description } });
                    line.MatchKind = FnStatementMatch.Voucher;
                    break;
                }
                default:
                    return Bad<BankStatementDto>("نوع تطبیق نامعتبر است (cheque, payment, receipt, fee, interest).");
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<BankStatementDto>(Conflict); }
            return await GetStatementAsync(st.Id, ct);
        }

        // ================================================================================== bank reconciliation

        private sealed class ReconFigures
        {
            public decimal Book, Bank, DepositsInTransit, OutstandingPayments, OutstandingCheques, Difference;
            public int Unmatched;
        }

        private async Task<ReconFigures> ComputeAsync(FnBankStatement st, DateTime asOf, CancellationToken ct)
        {
            int bankId = st.BankAccountId;
            var f = new ReconFigures { Bank = st.ClosingBalance };
            f.Book = await _uow.Set<AccountingEventLine>().AsNoTracking()
                .Where(l => l.PartyType == "BankAccount" && l.PartyId == bankId && l.Event.Status != AccountingEventStatus.NotRequired && l.Event.EventDate <= asOf)
                .SumAsync(l => (decimal?)(l.Debit - l.Credit), ct) ?? 0;
            var matched = await _uow.Set<FnBankStatementLine>().AsNoTracking().Where(l => l.Statement.BankAccountId == bankId && l.MatchKind != FnStatementMatch.None && l.MatchedId != null)
                .Select(l => new { l.MatchKind, Id = l.MatchedId.Value }).ToListAsync(ct);
            List<BankItem> payments = await BankItemsAsync(bankId, true, DateTime.MinValue, asOf, ct);
            List<BankItem> receipts = await BankItemsAsync(bankId, false, DateTime.MinValue, asOf, ct);
            f.OutstandingPayments = payments.Where(p => !matched.Any(m => m.MatchKind == FnStatementMatch.Payment && m.Id == p.Id)).Sum(p => p.Amount);
            f.DepositsInTransit = receipts.Where(r => !matched.Any(m => m.MatchKind == FnStatementMatch.Receipt && m.Id == r.Id)).Sum(r => r.Amount);
            DateTimeOffset limit = new DateTimeOffset(asOf.Date.AddDays(1));
            f.OutstandingCheques = await _uow.Set<FnIssuedCheque>().AsNoTracking()
                .Where(c => c.Status == FnIssuedChequeStatus.Issued && c.ChequeBook.BankAccountId == bankId && c.IssuedAt != null && c.IssuedAt < limit).SumAsync(c => (decimal?)c.Amount, ct) ?? 0;
            // issued cheques are informational: issuing one credits 30102 (payable notes), not the bank account, so it is in neither the bank nor the book balance
            f.Difference = f.Bank + f.DepositsInTransit - f.OutstandingPayments - f.Book;
            f.Unmatched = await _uow.Set<FnBankStatementLine>().CountAsync(l => l.StatementId == st.Id && l.MatchKind == FnStatementMatch.None, ct);
            return f;
        }

        private async Task<List<ReconciliationDto>> LoadReconciliationsAsync(IQueryable<FnBankReconciliation> query, CancellationToken ct)
        {
            List<FnBankReconciliation> rows = await query.AsNoTracking().Include(r => r.BankAccount).ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(r => new int?[] { r.PreparedByUserId, r.ApprovedByUserId }), ct);
            List<int> statementIds = rows.Select(r => r.StatementId).ToList();
            Dictionary<int, int> unmatched = (await _uow.Set<FnBankStatementLine>().AsNoTracking().Where(l => statementIds.Contains(l.StatementId) && l.MatchKind == FnStatementMatch.None)
                .GroupBy(l => l.StatementId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.Count);
            return rows.Select(r => new ReconciliationDto
            {
                Id = r.Id, BankAccountId = r.BankAccountId, BankAccountTitle = r.BankAccount.Title, StatementId = r.StatementId, AsOfDate = r.AsOfDate, BookBalance = r.BookBalance,
                BankBalance = r.BankBalance, DepositsInTransit = r.DepositsInTransit, OutstandingPayments = r.OutstandingPayments, OutstandingCheques = r.OutstandingCheques,
                Difference = r.Difference, Status = r.Status, PreparedByName = names.Name(r.PreparedByUserId), ApprovedByName = names.Name(r.ApprovedByUserId),
                UnmatchedLines = unmatched.TryGetValue(r.StatementId, out int u) ? u : 0,
            }).ToList();
        }

        public async Task<ResponseDto<ReconciliationDto>> CreateReconciliationAsync(CreateReconciliationDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return Bad<ReconciliationDto>(NotAllowed);
            if (dto == null) return Bad<ReconciliationDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnBankStatement st = await _uow.Set<FnBankStatement>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == dto.StatementId, ct);
            if (st == null) return ResponseDtoHelper.NotFound<ReconciliationDto>("صورت‌حساب پیدا نشد.");
            DateTime asOf = dto.AsOfDate?.Date ?? st.ToDate;
            ReconFigures f = await ComputeAsync(st, asOf, ct);
            var rec = new FnBankReconciliation
            {
                BankAccountId = st.BankAccountId, StatementId = st.Id, AsOfDate = asOf, BookBalance = f.Book, BankBalance = f.Bank, DepositsInTransit = f.DepositsInTransit,
                OutstandingPayments = f.OutstandingPayments, OutstandingCheques = f.OutstandingCheques, Difference = f.Difference, Status = 0, PreparedByUserId = Me, PreparedAt = DateTimeOffset.Now,
            };
            _uow.Set<FnBankReconciliation>().Add(rec);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await LoadReconciliationsAsync(_uow.Set<FnBankReconciliation>().Where(r => r.Id == rec.Id), ct))[0]);
        }

        public async Task<ResponseDto<PagedList<ReconciliationDto>>> GetReconciliationsAsync(TreasuryQuery query, CancellationToken ct)
        {
            query ??= new TreasuryQuery();
            IQueryable<FnBankReconciliation> q = _uow.Set<FnBankReconciliation>().AsNoTracking();
            if (query.BankAccountId.HasValue) q = q.Where(r => r.BankAccountId == query.BankAccountId.Value);
            if (query.Kind.HasValue) q = q.Where(r => r.Status == query.Kind.Value);
            int total = await q.CountAsync(ct);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize, number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<ReconciliationDto> list = await LoadReconciliationsAsync(q.OrderByDescending(r => r.Id).Skip((number - 1) * size).Take(size), ct);
            return ResponseDtoHelper.Ok(Page(list.OrderByDescending(r => r.Id).ToList(), total, number, size));
        }

        public async Task<ResponseDto<ReconciliationDto>> ReconciliationActionAsync(DocActionDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<ReconciliationDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnBankReconciliation rec = await _uow.Set<FnBankReconciliation>().FirstOrDefaultAsync(r => r.Id == dto.Id, ct);
            if (rec == null) return ResponseDtoHelper.NotFound<ReconciliationDto>("مغایرت‌گیری پیدا نشد.");
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "refresh":
                {
                    if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return Bad<ReconciliationDto>(NotAllowed);
                    if (rec.Status != 0) return Bad<ReconciliationDto>("مغایرت‌گیری تاییدشده تغییر نمی‌کند.");
                    FnBankStatement st = await _uow.Set<FnBankStatement>().AsNoTracking().FirstAsync(s => s.Id == rec.StatementId, ct);
                    ReconFigures f = await ComputeAsync(st, rec.AsOfDate, ct);
                    rec.BookBalance = f.Book; rec.BankBalance = f.Bank; rec.DepositsInTransit = f.DepositsInTransit; rec.OutstandingPayments = f.OutstandingPayments;
                    rec.OutstandingCheques = f.OutstandingCheques; rec.Difference = f.Difference;
                    break;
                }
                case "approve":
                {
                    if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<ReconciliationDto>(NotAllowed + " (مدیر مالی)");
                    if (rec.Status != 0) return Bad<ReconciliationDto>("این مغایرت‌گیری قبلاً تایید شده است.");
                    if (rec.PreparedByUserId == Me) return Bad<ReconciliationDto>("تهیه‌کننده مغایرت‌گیری نمی‌تواند آن را تایید کند.");
                    FnBankStatement st = await _uow.Set<FnBankStatement>().AsNoTracking().FirstAsync(s => s.Id == rec.StatementId, ct);
                    ReconFigures f = await ComputeAsync(st, rec.AsOfDate, ct);
                    if (f.Difference != 0) return Bad<ReconciliationDto>($"مغایرت صفر نیست ({f.Difference:0} ریال)؛ ردیف‌های تطبیق‌نخورده را رفع کنید.");
                    if (f.Unmatched > 0) return Bad<ReconciliationDto>($"{f.Unmatched} ردیف صورت‌حساب هنوز تطبیق نخورده است.");
                    rec.BookBalance = f.Book; rec.BankBalance = f.Bank; rec.DepositsInTransit = f.DepositsInTransit; rec.OutstandingPayments = f.OutstandingPayments;
                    rec.OutstandingCheques = f.OutstandingCheques; rec.Difference = 0;
                    rec.Status = 1; rec.ApprovedByUserId = Me; rec.ApprovedAt = DateTimeOffset.Now;
                    break;
                }
                default:
                    return Bad<ReconciliationDto>("عمل نامعتبر است.");
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<ReconciliationDto>(Conflict); }
            return ResponseDtoHelper.Ok((await LoadReconciliationsAsync(_uow.Set<FnBankReconciliation>().Where(r => r.Id == rec.Id), ct))[0]);
        }

        // ================================================================================== cash position

        public async Task<ResponseDto<CashPositionDto>> GetCashPositionAsync(CancellationToken ct)
        {
            await FinanceSeed.EnsureAsync(_uow, ct);
            DateTime today = DateTime.Today;
            var dto = new CashPositionDto();
            Dictionary<int, decimal> bankBalances = await PartyBalancesAsync("BankAccount", ct);
            Dictionary<int, decimal> boxBalances = await PartyBalancesAsync("CashBox", ct);
            foreach (FnBankAccount b in await _uow.Set<FnBankAccount>().AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).ToListAsync(ct))
            {
                decimal bal = bankBalances.TryGetValue(b.Id, out decimal v) ? v : 0;
                dto.Banks += bal;
                dto.Accounts.Add(new CashPositionAccountDto { Kind = "Bank", Id = b.Id, Title = b.Title, Balance = bal });
            }
            foreach (FnCashBox c in await _uow.Set<FnCashBox>().AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).ToListAsync(ct))
            {
                decimal bal = boxBalances.TryGetValue(c.Id, out decimal v) ? v : 0;
                dto.Cash += bal;
                dto.Accounts.Add(new CashPositionAccountDto { Kind = "CashBox", Id = c.Id, Title = c.Title, Balance = bal });
            }
            foreach (FnPettyCash p in await _uow.Set<FnPettyCash>().AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).ToListAsync(ct))
            {
                dto.PettyCash += p.Balance;
                dto.Accounts.Add(new CashPositionAccountDto { Kind = "PettyCash", Id = p.Id, Title = p.HolderTitle ?? ("تنخواه " + p.Id), Balance = p.Balance });
            }
            dto.Total = dto.Cash + dto.Banks + dto.PettyCash;

            var receivedByStatus = await _uow.Set<FnReceivedCheque>().AsNoTracking().Where(c => c.Status == FnReceivedChequeStatus.InBox || c.Status == FnReceivedChequeStatus.Deposited)
                .GroupBy(c => c.Status).Select(g => new { g.Key, Amount = g.Sum(c => c.Amount) }).ToListAsync(ct);
            dto.ReceivedChequesInBox = receivedByStatus.FirstOrDefault(x => x.Key == FnReceivedChequeStatus.InBox)?.Amount ?? 0;
            dto.ReceivedChequesDeposited = receivedByStatus.FirstOrDefault(x => x.Key == FnReceivedChequeStatus.Deposited)?.Amount ?? 0;
            dto.IssuedChequesOutstanding = await _uow.Set<FnIssuedCheque>().AsNoTracking().Where(c => c.Status == FnIssuedChequeStatus.Issued).SumAsync(c => (decimal?)c.Amount, ct) ?? 0;
            dto.ApprovedPaymentRequests = await _uow.Set<PaymentRequest>().AsNoTracking().Where(p => !p.Deleted && p.Status == PaymentRequestStatus.Approved)
                .SumAsync(p => (decimal?)(p.Amount - p.Deductions), ct) ?? 0;

            // eight weeks forecast
            var weeks = Enumerable.Range(0, 8).Select(i => new CashWeekDto { Week = i + 1, From = today.AddDays(7 * i), To = today.AddDays(7 * i + 6) }).ToList();
            CashWeekDto WeekOf(DateTime d) => d < today ? weeks[0] : weeks.FirstOrDefault(w => d >= w.From && d <= w.To);
            foreach (var c in await _uow.Set<FnReceivedCheque>().AsNoTracking().Where(c => c.Status == FnReceivedChequeStatus.InBox || c.Status == FnReceivedChequeStatus.Deposited)
                         .Select(c => new { c.DueDate, c.Amount }).ToListAsync(ct))
                { var w = WeekOf(c.DueDate); if (w != null) w.Inflows += c.Amount; }
            var invoices = await _uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => i.Status == FnDocStatus.Posted && i.Type != FnInvoiceType.CreditNote).Select(i => new { i.Id, i.Date, i.DueDate }).ToListAsync(ct);
            Dictionary<int, decimal> open = await ReceivableMath.OpenAsync(_uow, invoices.Select(i => i.Id).ToList(), null, ct);
            foreach (var i in invoices)
            {
                decimal amount = open.TryGetValue(i.Id, out decimal o) ? o : 0;
                var w = amount > 0 ? WeekOf((i.DueDate ?? i.Date.AddDays(30)).Date) : null;
                if (w != null) w.Inflows += amount;
            }
            foreach (var m in await _uow.Set<PcPaymentMilestone>().AsNoTracking().Where(m => !m.Deleted && m.Status == PcMilestoneStatus.Due).Select(m => new { m.Id, m.Amount, m.PlannedDate }).ToListAsync(ct))
            {
                if (await _uow.Set<FnSalesInvoice>().AnyAsync(i => i.MilestoneId == m.Id && i.Status != FnDocStatus.Cancelled, ct)) continue;   // already an invoice: counted above
                var w = WeekOf((m.PlannedDate ?? today).Date); if (w != null) w.Inflows += m.Amount;
            }
            foreach (var c in await _uow.Set<FnIssuedCheque>().AsNoTracking().Where(c => c.Status == FnIssuedChequeStatus.Issued && c.DueDate != null).Select(c => new { c.DueDate, c.Amount }).ToListAsync(ct))
                { var w = WeekOf(c.DueDate.Value); if (w != null) w.Outflows += c.Amount; }
            weeks[0].Outflows += dto.ApprovedPaymentRequests;
            var orderLines = await _uow.Set<PurchaseOrderLine>().AsNoTracking()
                .Where(l => !l.PurchaseOrder.Deleted && (l.PurchaseOrder.Status == PurchaseOrderStatus.Released || l.PurchaseOrder.Status == PurchaseOrderStatus.PartlyReceived) && l.ReceivedQuantity < l.Quantity)
                .Select(l => new { Date = l.DeliveryDate ?? l.PurchaseOrder.DeliveryDate, Amount = (l.Quantity - l.ReceivedQuantity) * l.UnitPrice }).ToListAsync(ct);
            foreach (var l in orderLines) { var w = WeekOf((l.Date ?? today).Date); if (w != null) w.Outflows += Math.Round(l.Amount, 0); }
            decimal cumulative = dto.Total;
            foreach (CashWeekDto w in weeks)
            {
                w.Net = w.Inflows - w.Outflows;
                cumulative += w.Net;
                w.Cumulative = cumulative;
            }
            dto.Forecast = weeks;

            // alerts
            int chequesDue3 = await _uow.Set<FnReceivedCheque>().CountAsync(c => c.Status == FnReceivedChequeStatus.InBox && c.DueDate <= today.AddDays(3), ct);
            if (chequesDue3 > 0) dto.Alerts.Add($"{chequesDue3} چک دریافتی تا ۳ روز دیگر سررسید می‌شود و هنوز به بانک واگذار نشده است.");
            decimal issuedDue7 = await _uow.Set<FnIssuedCheque>().Where(c => c.Status == FnIssuedChequeStatus.Issued && c.DueDate != null && c.DueDate <= today.AddDays(7)).SumAsync(c => (decimal?)c.Amount, ct) ?? 0;
            if (issuedDue7 > dto.Banks) dto.Alerts.Add($"چک‌های صادره تا ۷ روز آینده ({issuedDue7:0} ریال) از موجودی بانک‌ها ({dto.Banks:0}) بیشتر است.");
            int bounced = await _uow.Set<FnReceivedCheque>().CountAsync(c => c.Status == FnReceivedChequeStatus.Bounced, ct);
            if (bounced > 0) dto.Alerts.Add($"{bounced} چک برگشتی دریافتی پیگیری نشده است.");
            return ResponseDtoHelper.Ok(dto);
        }
    }
}
