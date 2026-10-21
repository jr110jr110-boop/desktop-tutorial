using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Providers;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    public partial class TreasuryService
    {
        // ================================================================================== receipts

        private async Task<List<ReceiptDto>> LoadReceiptsAsync(IQueryable<FnReceipt> query, CancellationToken ct)
        {
            List<FnReceipt> rows = await query.AsNoTracking().Include(r => r.Customer).Include(r => r.Lines).Include(r => r.Allocations).ThenInclude(a => a.Invoice).AsSplitQuery().ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(r => new int?[] { r.CreatedByUserId, r.PostedByUserId }), ct);
            return rows.Select(r => new ReceiptDto
            {
                Id = r.Id, Number = r.Number, Date = r.Date, Status = r.Status, CustomerId = r.CustomerId, CustomerName = r.Customer?.Name, PayerName = r.PayerName,
                Description = r.Description, TotalAmount = r.TotalAmount, AllocatedAmount = r.AllocatedAmount, CreatorName = names.Name(r.CreatedByUserId), PostedAt = r.PostedAt,
                Lines = r.Lines.OrderBy(l => l.RowNumber).Select(l => new ReceiptLineDto
                {
                    RowNumber = l.RowNumber, Instrument = l.Instrument, Amount = l.Amount, BankAccountId = l.BankAccountId, CashBoxId = l.CashBoxId, Reference = l.Reference,
                    ReceivedChequeId = l.ReceivedChequeId, ChequeSayadId = l.ChequeSayadId, ChequeSerial = l.ChequeSerial, ChequeBank = l.ChequeBank, ChequeBranch = l.ChequeBranch,
                    ChequeHolder = l.ChequeHolder, ChequeDueDate = l.ChequeDueDate,
                }).ToList(),
                Allocations = r.Allocations.Select(a => new ReceiptAllocationDto { InvoiceId = a.InvoiceId, InvoiceNumber = a.Invoice?.Number, Amount = a.Amount }).ToList(),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<ReceiptDto>>> GetReceiptsAsync(TreasuryQuery query, CancellationToken ct)
        {
            query ??= new TreasuryQuery();
            IQueryable<FnReceipt> q = _uow.Set<FnReceipt>().AsNoTracking();
            if (query.Status.HasValue) q = q.Where(r => r.Status == query.Status.Value);
            if (query.CustomerId.HasValue) q = q.Where(r => r.CustomerId == query.CustomerId.Value);
            if (query.From.HasValue) q = q.Where(r => r.Date >= query.From.Value.Date);
            if (query.To.HasValue) q = q.Where(r => r.Date <= query.To.Value.Date);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(r => r.Number.Contains(text) || r.PayerName.Contains(text) || r.Customer.Name.Contains(text));
            int total = await q.CountAsync(ct);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize, number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<ReceiptDto> list = await LoadReceiptsAsync(q.OrderByDescending(r => r.Id).Skip((number - 1) * size).Take(size), ct);
            return ResponseDtoHelper.Ok(Page(list.OrderByDescending(r => r.Id).ToList(), total, number, size));
        }

        public async Task<ResponseDto<ReceiptDto>> GetReceiptAsync(int id, CancellationToken ct)
        {
            List<ReceiptDto> list = await LoadReceiptsAsync(_uow.Set<FnReceipt>().Where(r => r.Id == id), ct);
            return list.Count == 0 ? ResponseDtoHelper.NotFound<ReceiptDto>("سند دریافت پیدا نشد.") : ResponseDtoHelper.Ok(list[0]);
        }

        public async Task<ResponseDto<ReceiptDto>> SaveReceiptAsync(SaveReceiptDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return Bad<ReceiptDto>(NotAllowed);
            if (dto == null) return Bad<ReceiptDto>("اطلاعات ارسال‌شده کامل نیست.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            DateTime date = dto.Date == default ? DateTime.Today : dto.Date.Date;
            if (!dto.CustomerId.HasValue) return Bad<ReceiptDto>("مشتری (پرداخت‌کننده) را مشخص کنید.");
            FnCustomer customer = await _uow.Set<FnCustomer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == dto.CustomerId.Value, ct);
            if (customer == null || !customer.IsActive) return Bad<ReceiptDto>("مشتری پیدا نشد یا غیرفعال است.");
            List<ReceiptLineDto> input = (dto.Lines ?? new List<ReceiptLineDto>()).Where(l => l != null).ToList();
            if (input.Count == 0) return Bad<ReceiptDto>("حداقل یک ردیف دریافت (نقد، حواله، چک یا کارت) لازم است.");

            FnReceipt receipt = null;
            if (dto.Id.HasValue)
            {
                receipt = await _uow.Set<FnReceipt>().Include(r => r.Lines).Include(r => r.Allocations).FirstOrDefaultAsync(r => r.Id == dto.Id.Value, ct);
                if (receipt == null) return ResponseDtoHelper.NotFound<ReceiptDto>("سند دریافت پیدا نشد.");
                if (receipt.Status != FnDocStatus.Draft) return Bad<ReceiptDto>("فقط پیش‌نویس ویرایش می‌شود.");
            }

            List<int> bankIds = input.Where(l => l.BankAccountId.HasValue).Select(l => l.BankAccountId.Value).Distinct().ToList();
            Dictionary<int, FnBankAccount> banks = await _uow.Set<FnBankAccount>().Where(b => bankIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
            List<int> boxIds = input.Where(l => l.CashBoxId.HasValue).Select(l => l.CashBoxId.Value).Distinct().ToList();
            Dictionary<int, FnCashBox> boxes = await _uow.Set<FnCashBox>().Where(b => boxIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
            var lines = new List<FnReceiptLine>();
            var sayads = new HashSet<string>();
            int row = 1;
            foreach (ReceiptLineDto l in input)
            {
                decimal amount = R(l.Amount);
                if (amount <= 0) return Bad<ReceiptDto>($"ردیف {row}: مبلغ باید بیشتر از صفر باشد.");
                var line = new FnReceiptLine { RowNumber = row, Instrument = l.Instrument, Amount = amount, Reference = Clean(l.Reference) };
                switch (l.Instrument)
                {
                    case FnInstrument.Cash:
                        if (!l.CashBoxId.HasValue || !boxes.TryGetValue(l.CashBoxId.Value, out FnCashBox box) || !box.IsActive) return Bad<ReceiptDto>($"ردیف {row}: صندوق پیدا نشد یا غیرفعال است.");
                        line.CashBoxId = box.Id;
                        break;
                    case FnInstrument.BankTransfer:
                    case FnInstrument.Card:
                    {
                        if (!l.BankAccountId.HasValue || !banks.TryGetValue(l.BankAccountId.Value, out FnBankAccount bank) || !bank.IsActive) return Bad<ReceiptDto>($"ردیف {row}: حساب بانکی پیدا نشد یا غیرفعال است.");
                        line.BankAccountId = bank.Id;
                        break;
                    }
                    case FnInstrument.Cheque:
                    {
                        string sayad = Clean(Digits(l.ChequeSayadId));
                        if (sayad == null || sayad.Length != 16 || !sayad.All(char.IsDigit)) return Bad<ReceiptDto>($"ردیف {row}: شناسه صیادی چک باید ۱۶ رقم باشد.");
                        if (!l.ChequeDueDate.HasValue) return Bad<ReceiptDto>($"ردیف {row}: تاریخ سررسید چک را وارد کنید.");
                        if (Clean(l.ChequeBank) == null) return Bad<ReceiptDto>($"ردیف {row}: نام بانک چک را وارد کنید.");
                        int self = receipt?.Id ?? 0;
                        if (!sayads.Add(sayad) || await _uow.Set<FnReceivedCheque>().AnyAsync(c => c.SayadId == sayad, ct) ||
                            await _uow.Set<FnReceiptLine>().AnyAsync(x => x.ChequeSayadId == sayad && x.Receipt.Status != FnDocStatus.Cancelled && x.ReceiptId != self, ct))
                            return Bad<ReceiptDto>($"ردیف {row}: چک با این شناسه صیادی قبلاً ثبت شده است.");
                        line.ChequeSayadId = sayad; line.ChequeSerial = Clean(l.ChequeSerial); line.ChequeBank = Clean(l.ChequeBank); line.ChequeBranch = Clean(l.ChequeBranch);
                        line.ChequeHolder = Clean(l.ChequeHolder); line.ChequeDueDate = l.ChequeDueDate.Value.Date;
                        break;
                    }
                    default:
                        return Bad<ReceiptDto>($"ردیف {row}: روش دریافت «تهاتر» در این نسخه پشتیبانی نمی‌شود.");
                }
                lines.Add(line);
                row++;
            }
            decimal total = lines.Sum(l => l.Amount);

            // manual allocations (when none are given the oldest open invoices are settled first at posting)
            var allocations = new List<FnReceiptAllocation>();
            List<ReceiptAllocationDto> manual = (dto.Allocations ?? new List<ReceiptAllocationDto>()).Where(a => a != null && a.Amount != 0).ToList();
            if (manual.Count > 0)
            {
                List<int> invoiceIds = manual.Select(a => a.InvoiceId).Distinct().ToList();
                if (invoiceIds.Count != manual.Count) return Bad<ReceiptDto>("هر فاکتور فقط یک بار در تخصیص می‌آید.");
                Dictionary<int, FnSalesInvoice> invoices = await _uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
                Dictionary<int, decimal> open = await ReceivableMath.OpenAsync(_uow, invoiceIds, receipt?.Id, ct);
                foreach (ReceiptAllocationDto a in manual)
                {
                    decimal amount = R(a.Amount);
                    if (!invoices.TryGetValue(a.InvoiceId, out FnSalesInvoice inv) || inv.CustomerId != customer.Id) return Bad<ReceiptDto>("فاکتور پیدا نشد یا متعلق به این مشتری نیست.");
                    if (inv.Status != FnDocStatus.Posted || inv.Type == FnInvoiceType.CreditNote) return Bad<ReceiptDto>($"فاکتور {inv.Number} ثبت‌شده و قابل دریافت نیست.");
                    if (amount <= 0 || amount > open[inv.Id]) return Bad<ReceiptDto>($"مبلغ تخصیص به فاکتور {inv.Number} از مانده باز آن ({open[inv.Id]:0}) بیشتر یا نامعتبر است.");
                    allocations.Add(new FnReceiptAllocation { InvoiceId = inv.Id, Amount = amount });
                }
                if (allocations.Sum(a => a.Amount) > total) return Bad<ReceiptDto>("جمع تخصیص‌ها از مبلغ دریافت بیشتر است.");
            }

            if (receipt == null)
            {
                receipt = new FnReceipt
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, FinanceNumbers.Receipt, "RCPT", date, ct), Status = FnDocStatus.Draft, CreatedByUserId = Me,
                    CreatedAt = DateTimeOffset.Now, Lines = new List<FnReceiptLine>(), Allocations = new List<FnReceiptAllocation>(),
                };
                _uow.Set<FnReceipt>().Add(receipt);
            }
            else
            {
                _uow.Set<FnReceiptLine>().RemoveRange(receipt.Lines);
                _uow.Set<FnReceiptAllocation>().RemoveRange(receipt.Allocations);
                receipt.Lines = new List<FnReceiptLine>();
                receipt.Allocations = new List<FnReceiptAllocation>();
            }
            receipt.Date = date; receipt.CustomerId = customer.Id; receipt.PayerName = Clean(dto.PayerName) ?? customer.Name; receipt.Description = Clean(dto.Description);
            receipt.TotalAmount = total; receipt.AllocatedAmount = allocations.Sum(a => a.Amount);
            foreach (FnReceiptLine l in lines) receipt.Lines.Add(l);
            foreach (FnReceiptAllocation a in allocations) receipt.Allocations.Add(a);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<ReceiptDto>(Conflict); }
            return await GetReceiptAsync(receipt.Id, ct);
        }

        public async Task<ResponseDto<ReceiptDto>> ReceiptActionAsync(DocActionDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<ReceiptDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return Bad<ReceiptDto>(NotAllowed);
            FnReceipt r = await _uow.Set<FnReceipt>().Include(x => x.Lines).Include(x => x.Allocations).Include(x => x.Customer).FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (r == null) return ResponseDtoHelper.NotFound<ReceiptDto>("سند دریافت پیدا نشد.");
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "approve":
                    if (r.Status != FnDocStatus.Draft) return Bad<ReceiptDto>("فقط پیش‌نویس تایید می‌شود.");
                    if (r.CreatedByUserId == Me) return Bad<ReceiptDto>(SameUser);
                    r.Status = FnDocStatus.Approved;
                    break;
                case "post":
                {
                    string error = await PostReceiptAsync(r, ct);
                    if (error != null) return Bad<ReceiptDto>(error);
                    break;
                }
                case "cancel":
                    if (r.Status != FnDocStatus.Draft && r.Status != FnDocStatus.Approved) return Bad<ReceiptDto>("سند ثبت‌شده باطل نمی‌شود.");
                    r.Status = FnDocStatus.Cancelled;
                    break;
                default:
                    return Bad<ReceiptDto>("عمل نامعتبر است.");
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<ReceiptDto>(Conflict); }
            catch (DbUpdateException) { return Bad<ReceiptDto>("ثبت سند دریافت ناموفق بود؛ احتمالاً چک تکراری است."); }
            return await GetReceiptAsync(r.Id, ct);
        }

        private AccountingEventLine CustomerLine(string role, int? customerId, decimal debit, decimal credit, string text) => new AccountingEventLine
        {
            AccountRole = role, Debit = debit, Credit = credit, PartyType = customerId.HasValue ? "Customer" : null, PartyId = customerId, Description = text,
        };

        private async Task<string> PostReceiptAsync(FnReceipt r, CancellationToken ct)
        {
            if (r.Status != FnDocStatus.Approved) return "فقط سند تاییدشده ثبت نهایی می‌شود.";
            DateTimeOffset now = DateTimeOffset.Now;
            List<int> bankIds = r.Lines.Where(l => l.BankAccountId.HasValue).Select(l => l.BankAccountId.Value).Distinct().ToList();
            Dictionary<int, FnBankAccount> banks = await _uow.Set<FnBankAccount>().Where(b => bankIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);

            // allocation: manual rows stay, otherwise the oldest open invoices first
            var allocations = r.Allocations.ToList();
            Dictionary<int, FnSalesInvoice> invoices;
            if (allocations.Count > 0)
            {
                List<int> ids = allocations.Select(a => a.InvoiceId).ToList();
                Dictionary<int, decimal> open = await ReceivableMath.OpenAsync(_uow, ids, r.Id, ct);
                foreach (FnReceiptAllocation a in allocations)
                    if (a.Amount > open[a.InvoiceId]) return "مانده باز یکی از فاکتورها از مبلغ تخصیص کمتر شده است؛ تخصیص را اصلاح کنید.";
            }
            else
            {
                List<FnSalesInvoice> candidates = await _uow.Set<FnSalesInvoice>().AsNoTracking()
                    .Where(i => i.CustomerId == r.CustomerId && i.Status == FnDocStatus.Posted && i.Type != FnInvoiceType.CreditNote).OrderBy(i => i.Date).ThenBy(i => i.Id).ToListAsync(ct);
                Dictionary<int, decimal> open = await ReceivableMath.OpenAsync(_uow, candidates.Select(i => i.Id).ToList(), r.Id, ct);
                decimal left = r.TotalAmount;
                foreach (FnSalesInvoice i in candidates)
                {
                    if (left <= 0) break;
                    decimal take = Math.Min(left, open[i.Id]);
                    if (take <= 0) continue;
                    var a = new FnReceiptAllocation { ReceiptId = r.Id, InvoiceId = i.Id, Amount = take };
                    r.Allocations.Add(a);
                    allocations.Add(a);
                    left -= take;
                }
            }
            r.AllocatedAmount = allocations.Sum(a => a.Amount);

            var eventLines = new List<AccountingEventLine>();
            var cheques = new List<(FnReceivedCheque Cheque, FnReceiptLine Line)>();
            foreach (FnReceiptLine l in r.Lines.OrderBy(x => x.RowNumber))
            {
                switch (l.Instrument)
                {
                    case FnInstrument.Cash: eventLines.Add(CashLine(l.CashBoxId.Value, l.Amount, 0, r.Number)); break;
                    case FnInstrument.BankTransfer:
                    case FnInstrument.Card:
                        if (!banks.TryGetValue(l.BankAccountId.Value, out FnBankAccount bank) || !bank.IsActive) return "حساب بانکی یکی از ردیف‌ها غیرفعال شده است.";
                        eventLines.Add(BankLine(bank, l.Amount, 0, r.Number + (l.Reference != null ? " - " + l.Reference : "")));
                        break;
                    case FnInstrument.Cheque:
                    {
                        if (await _uow.Set<FnReceivedCheque>().AnyAsync(c => c.SayadId == l.ChequeSayadId, ct)) return $"چک {l.ChequeSayadId} قبلاً ثبت شده است.";
                        var cheque = new FnReceivedCheque
                        {
                            SayadId = l.ChequeSayadId, Serial = l.ChequeSerial, BankName = l.ChequeBank, Branch = l.ChequeBranch, AccountHolder = l.ChequeHolder ?? r.PayerName,
                            DueDate = l.ChequeDueDate.Value, Amount = l.Amount, CustomerId = r.CustomerId, ReceiptId = r.Id, Status = FnReceivedChequeStatus.InBox,
                            ReceivedDate = r.Date, History = new List<FnReceivedChequeHistory>(),
                        };
                        _uow.Set<FnReceivedCheque>().Add(cheque);
                        cheques.Add((cheque, l));
                        eventLines.Add(CustomerLine(AccountRoles.ChequesReceivable, r.CustomerId, l.Amount, 0, $"چک {l.ChequeSayadId}"));
                        break;
                    }
                    default: return "روش دریافت پشتیبانی نمی‌شود.";
                }
            }
            eventLines.Add(CustomerLine(AccountRoles.AccountsReceivable, r.CustomerId, 0, r.TotalAmount, "دریافت " + r.Number));
            AccountingEvent ev = _costing.AddEvent(AccountingEventType.CustomerReceipt, r.Date, "FnReceipt", r.Id, r.Number, $"دریافت {r.Number} - {r.Customer?.Name}", eventLines);
            foreach (var c in cheques)
            {
                c.Line.ReceivedCheque = c.Cheque;
                c.Cheque.History.Add(new FnReceivedChequeHistory { Date = r.Date, FromStatus = null, ToStatus = FnReceivedChequeStatus.InBox, Note = "دریافت با " + r.Number, UserId = Me, At = now, AccountingEvent = ev });
            }

            // milestones of fully paid invoices
            if (allocations.Count > 0)
            {
                List<int> invoiceIds = allocations.Select(a => a.InvoiceId).ToList();
                Dictionary<int, decimal> open = await ReceivableMath.OpenAsync(_uow, invoiceIds, r.Id, ct);
                List<FnSalesInvoice> settled = await _uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => invoiceIds.Contains(i.Id) && i.MilestoneId != null).ToListAsync(ct);
                foreach (FnSalesInvoice i in settled)
                {
                    decimal now2 = open[i.Id] - allocations.Where(a => a.InvoiceId == i.Id).Sum(a => a.Amount);
                    if (now2 > 0) continue;
                    PcPaymentMilestone m = await _uow.Set<PcPaymentMilestone>().FirstOrDefaultAsync(x => x.Id == i.MilestoneId.Value, ct);
                    if (m != null && m.Status != PcMilestoneStatus.Paid) { m.Status = PcMilestoneStatus.Paid; m.PaidAt = now; m.ModifyDate = now; m.ModifierUserId = Me; }
                }
            }
            r.Status = FnDocStatus.Posted; r.PostedByUserId = Me; r.PostedAt = now;
            return null;
        }

        // ================================================================================== received cheques

        public async Task<ResponseDto<PagedList<ReceivedChequeDto>>> GetReceivedChequesAsync(TreasuryQuery query, CancellationToken ct)
        {
            query ??= new TreasuryQuery();
            IQueryable<FnReceivedCheque> q = _uow.Set<FnReceivedCheque>().AsNoTracking();
            if (query.Kind.HasValue) q = q.Where(c => (int)c.Status == query.Kind.Value);
            if (query.CustomerId.HasValue) q = q.Where(c => c.CustomerId == query.CustomerId.Value);
            if (query.From.HasValue) q = q.Where(c => c.DueDate >= query.From.Value.Date);
            if (query.To.HasValue) q = q.Where(c => c.DueDate <= query.To.Value.Date);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(c => c.SayadId.Contains(text) || c.Serial.Contains(text) || c.AccountHolder.Contains(text));
            int total = await q.CountAsync(ct);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize, number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<ReceivedChequeDto> list = await LoadChequesAsync(q.OrderBy(c => c.DueDate).ThenBy(c => c.Id).Skip((number - 1) * size).Take(size), ct);
            return ResponseDtoHelper.Ok(Page(list.OrderBy(c => c.DueDate).ThenBy(c => c.Id).ToList(), total, number, size));
        }

        private async Task<List<ReceivedChequeDto>> LoadChequesAsync(IQueryable<FnReceivedCheque> query, CancellationToken ct)
        {
            List<FnReceivedCheque> rows = await query.AsNoTracking().Include(c => c.Customer).Include(c => c.History).AsSplitQuery().ToListAsync(ct);
            List<int> eventIds = rows.SelectMany(c => c.History).Where(h => h.AccountingEventId.HasValue).Select(h => h.AccountingEventId.Value).Distinct().ToList();
            var vouchers = await _uow.Set<FnVoucher>().AsNoTracking().Where(v => v.AccountingEventId != null && eventIds.Contains(v.AccountingEventId.Value))
                .Select(v => new { EventId = v.AccountingEventId.Value, v.Id, v.Number }).ToListAsync(ct);
            return rows.Select(c => new ReceivedChequeDto
            {
                Id = c.Id, SayadId = c.SayadId, Serial = c.Serial, BankName = c.BankName, Branch = c.Branch, AccountHolder = c.AccountHolder, DueDate = c.DueDate, Amount = c.Amount,
                CustomerId = c.CustomerId, CustomerName = c.Customer?.Name, Status = c.Status, DepositBankAccountId = c.DepositBankAccountId, ReceivedDate = c.ReceivedDate,
                History = c.History.OrderBy(h => h.Id).Select(h =>
                {
                    var v = h.AccountingEventId.HasValue ? vouchers.FirstOrDefault(x => x.EventId == h.AccountingEventId.Value) : null;
                    return new ChequeHistoryDto { Date = h.Date, FromStatus = h.FromStatus, ToStatus = h.ToStatus, Note = h.Note, AccountingEventId = h.AccountingEventId, VoucherId = v?.Id, VoucherNumber = v?.Number };
                }).ToList(),
            }).ToList();
        }

        private AccountingEvent ChequeStep(FnReceivedCheque c, FnReceivedChequeStatus to, DateTime date, string note, AccountingEventType type, IEnumerable<AccountingEventLine> lines)
        {
            AccountingEvent ev = _costing.AddEvent(type, date, "FnReceivedCheque", c.Id, c.SayadId ?? c.Serial, $"{note} - چک {c.SayadId ?? c.Serial}", lines);
            c.History ??= new List<FnReceivedChequeHistory>();
            _uow.Set<FnReceivedChequeHistory>().Add(new FnReceivedChequeHistory
            {
                Cheque = c, Date = date, FromStatus = c.Status, ToStatus = to, Note = note, UserId = Me, At = DateTimeOffset.Now, AccountingEvent = ev,
            });
            c.Status = to;
            return ev;
        }

        internal AccountingEvent ClearReceivedCheque(FnReceivedCheque c, FnBankAccount bank, DateTime date, string note) => ChequeStep(c, FnReceivedChequeStatus.Cleared, date, note, AccountingEventType.ChequeCleared, new[]
        {
            BankLine(bank, c.Amount, 0, "وصول چک"),
            CustomerLine(AccountRoles.ChequesInCollection, c.CustomerId, 0, c.Amount, "وصول چک"),
        });

        public async Task<ResponseDto<ReceivedChequeDto>> ReceivedChequeActionAsync(DocActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryUsers)) return Bad<ReceivedChequeDto>(NotAllowed + " (خزانه‌داری)");
            if (dto == null || Clean(dto.Action) == null) return Bad<ReceivedChequeDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnReceivedCheque c = await _uow.Set<FnReceivedCheque>().Include(x => x.History).FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (c == null) return ResponseDtoHelper.NotFound<ReceivedChequeDto>("چک پیدا نشد.");
            DateTime date = dto.Date?.Date ?? DateTime.Today;
            string note = Clean(dto.Note);
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "deposit":
                {
                    if (c.Status != FnReceivedChequeStatus.InBox) return Bad<ReceivedChequeDto>("فقط چک موجود در صندوق به بانک سپرده می‌شود.");
                    FnBankAccount bank = dto.BankAccountId.HasValue ? await _uow.Set<FnBankAccount>().FirstOrDefaultAsync(b => b.Id == dto.BankAccountId.Value, ct) : null;
                    if (bank == null || !bank.IsActive) return Bad<ReceivedChequeDto>("حساب بانکی واگذاری چک را مشخص کنید.");
                    c.DepositBankAccountId = bank.Id;
                    ChequeStep(c, FnReceivedChequeStatus.Deposited, date, note ?? "واگذاری به بانک", AccountingEventType.ChequeDeposited, new[]
                    {
                        CustomerLine(AccountRoles.ChequesInCollection, c.CustomerId, c.Amount, 0, "چک در جریان وصول"),
                        CustomerLine(AccountRoles.ChequesReceivable, c.CustomerId, 0, c.Amount, "خروج از اسناد دریافتنی"),
                    });
                    break;
                }
                case "clear":
                {
                    if (c.Status != FnReceivedChequeStatus.Deposited || !c.DepositBankAccountId.HasValue) return Bad<ReceivedChequeDto>("فقط چک واگذارشده به بانک وصول می‌شود.");
                    FnBankAccount bank = await _uow.Set<FnBankAccount>().FirstAsync(b => b.Id == c.DepositBankAccountId.Value, ct);
                    ClearReceivedCheque(c, bank, date, note ?? "وصول");
                    break;
                }
                case "bounce":
                    if (c.Status != FnReceivedChequeStatus.Deposited) return Bad<ReceivedChequeDto>("فقط چک واگذارشده به بانک برگشت می‌خورد.");
                    if (note == null) return Bad<ReceivedChequeDto>("علت برگشت چک را بنویسید.");
                    ChequeStep(c, FnReceivedChequeStatus.Bounced, date, note, AccountingEventType.ChequeBounced, new[]
                    {
                        CustomerLine(AccountRoles.ChequesBounced, c.CustomerId, c.Amount, 0, "چک برگشتی"),
                        CustomerLine(AccountRoles.ChequesInCollection, c.CustomerId, 0, c.Amount, "برگشت از وصول"),
                    });
                    break;
                case "return":
                {
                    if (c.Status != FnReceivedChequeStatus.InBox && c.Status != FnReceivedChequeStatus.Bounced) return Bad<ReceivedChequeDto>("فقط چک موجود در صندوق یا برگشتی به مشتری برگردانده می‌شود.");
                    string from = c.Status == FnReceivedChequeStatus.InBox ? AccountRoles.ChequesReceivable : AccountRoles.ChequesBounced;
                    ChequeStep(c, FnReceivedChequeStatus.ReturnedToDrawer, date, note ?? "عودت به صادرکننده", AccountingEventType.ChequeReturned, new[]
                    {
                        CustomerLine(AccountRoles.AccountsReceivable, c.CustomerId, c.Amount, 0, "بازگشت بدهی مشتری"),
                        CustomerLine(from, c.CustomerId, 0, c.Amount, "عودت چک"),
                    });
                    break;
                }
                case "endorse":
                {
                    if (c.Status != FnReceivedChequeStatus.InBox) return Bad<ReceivedChequeDto>("فقط چک موجود در صندوق خرج می‌شود.");
                    if (!dto.ProviderId.HasValue || !await _uow.Set<Provider>().AnyAsync(p => p.Id == dto.ProviderId.Value && !p.Deleted, ct)) return Bad<ReceivedChequeDto>("تامین‌کننده گیرنده چک را مشخص کنید.");
                    c.EndorsedProviderId = dto.ProviderId;
                    ChequeStep(c, FnReceivedChequeStatus.Endorsed, date, note ?? "خرج چک به تامین‌کننده", AccountingEventType.ChequeEndorsed, new[]
                    {
                        new AccountingEventLine { AccountRole = AccountRoles.AccountsPayable, Debit = c.Amount, ProviderId = dto.ProviderId, Description = "خرج چک" },
                        CustomerLine(AccountRoles.ChequesReceivable, c.CustomerId, 0, c.Amount, "خرج چک"),
                    });
                    break;
                }
                default:
                    return Bad<ReceivedChequeDto>("عمل نامعتبر است.");
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<ReceivedChequeDto>(Conflict); }
            return ResponseDtoHelper.Ok((await LoadChequesAsync(_uow.Set<FnReceivedCheque>().Where(x => x.Id == c.Id), ct))[0]);
        }

        // ================================================================================== cheque books and issued cheques

        public async Task<ResponseDto<PagedList<ChequeBookDto>>> GetChequeBooksAsync(TreasuryQuery query, CancellationToken ct)
        {
            query ??= new TreasuryQuery();
            IQueryable<FnChequeBook> q = _uow.Set<FnChequeBook>().AsNoTracking();
            if (query.BankAccountId.HasValue) q = q.Where(b => b.BankAccountId == query.BankAccountId.Value);
            int total = await q.CountAsync(ct);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize, number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            var rows = await q.OrderBy(b => b.Id).Skip((number - 1) * size).Take(size).Select(b => new
            {
                b.Id, b.BankAccountId, BankTitle = b.BankAccount.Title, b.Series, b.FromSerial, b.ToSerial, b.IsActive,
                Leaves = b.Leaves.Count(), Blank = b.Leaves.Count(l => l.Status == FnIssuedChequeStatus.Blank),
            }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(Page(rows.Select(b => new ChequeBookDto
            {
                Id = b.Id, BankAccountId = b.BankAccountId, BankAccountTitle = b.BankTitle, Series = b.Series, FromSerial = b.FromSerial, ToSerial = b.ToSerial,
                IsActive = b.IsActive, Leaves = b.Leaves, Blank = b.Blank,
            }).ToList(), total, number, size));
        }

        public async Task<ResponseDto<ChequeBookDto>> SaveChequeBookAsync(SaveChequeBookDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryUsers)) return Bad<ChequeBookDto>(NotAllowed + " (خزانه‌داری)");
            if (dto == null) return Bad<ChequeBookDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (dto.FromSerial <= 0 || dto.ToSerial < dto.FromSerial) return Bad<ChequeBookDto>("شماره سریال شروع و پایان دسته‌چک نامعتبر است.");
            if (dto.ToSerial - dto.FromSerial + 1 > 500) return Bad<ChequeBookDto>("حداکثر ۵۰۰ برگ در هر دسته‌چک.");
            FnBankAccount bank = await _uow.Set<FnBankAccount>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == dto.BankAccountId, ct);
            if (bank == null || !bank.IsActive) return Bad<ChequeBookDto>("حساب بانکی پیدا نشد یا غیرفعال است.");
            string series = Clean(dto.Series) ?? "";
            if (await _uow.Set<FnChequeBook>().AnyAsync(b => b.BankAccountId == bank.Id && b.Series == series && !(b.ToSerial < dto.FromSerial || b.FromSerial > dto.ToSerial), ct))
                return Bad<ChequeBookDto>("این بازه سریال با دسته‌چک دیگری از همین حساب هم‌پوشانی دارد.");
            var book = new FnChequeBook { BankAccountId = bank.Id, Series = series, FromSerial = dto.FromSerial, ToSerial = dto.ToSerial, CreatedAt = DateTimeOffset.Now, CreatedByUserId = Me, Leaves = new List<FnIssuedCheque>() };
            for (long s = dto.FromSerial; s <= dto.ToSerial; s++) book.Leaves.Add(new FnIssuedCheque { Serial = s, Status = FnIssuedChequeStatus.Blank });
            _uow.Set<FnChequeBook>().Add(book);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await GetChequeBooksAsync(new TreasuryQuery { BankAccountId = bank.Id, PageSize = 0 }, ct)).Data.First(b => b.Id == book.Id));
        }

        private IQueryable<IssuedChequeDto> IssuedProjection(IQueryable<FnIssuedCheque> q) => q.Select(c => new IssuedChequeDto
        {
            Id = c.Id, ChequeBookId = c.ChequeBookId, BankAccountId = c.ChequeBook.BankAccountId, BankAccountTitle = c.ChequeBook.BankAccount.Title, Series = c.ChequeBook.Series,
            Serial = c.Serial, SayadId = c.SayadId, Status = c.Status, PayeeName = c.PayeeName, ProviderId = c.ProviderId, DueDate = c.DueDate, Amount = c.Amount,
            PaymentId = c.PaymentId, IssuedAt = c.IssuedAt, ClearedAt = c.ClearedAt, VoidReason = c.VoidReason,
        });

        public async Task<ResponseDto<PagedList<IssuedChequeDto>>> GetIssuedChequesAsync(IssuedChequeQuery query, CancellationToken ct)
        {
            query ??= new IssuedChequeQuery();
            IQueryable<FnIssuedCheque> q = _uow.Set<FnIssuedCheque>().AsNoTracking();
            if (query.ChequeBookId.HasValue) q = q.Where(c => c.ChequeBookId == query.ChequeBookId.Value);
            if (query.Status.HasValue) q = q.Where(c => c.Status == query.Status.Value);
            if (query.BankAccountId.HasValue) q = q.Where(c => c.ChequeBook.BankAccountId == query.BankAccountId.Value);
            return ResponseDtoHelper.Ok(await PagedList<IssuedChequeDto>.CreateAsync(IssuedProjection(q.OrderBy(c => c.ChequeBookId).ThenBy(c => c.Serial)), query.PageNumber, query.PageSize, ct));
        }

        public async Task<ResponseDto<IssuedChequeDto>> IssuedChequeActionAsync(DocActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryUsers)) return Bad<IssuedChequeDto>(NotAllowed + " (خزانه‌داری)");
            if (dto == null || Clean(dto.Action) == null) return Bad<IssuedChequeDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnIssuedCheque c = await _uow.Set<FnIssuedCheque>().Include(x => x.ChequeBook).FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (c == null) return ResponseDtoHelper.NotFound<IssuedChequeDto>("برگ چک پیدا نشد.");
            DateTime date = dto.Date?.Date ?? DateTime.Today;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "clear":
                {
                    if (c.Status != FnIssuedChequeStatus.Issued) return Bad<IssuedChequeDto>("فقط چک صادرشده وصول می‌شود.");
                    FnBankAccount bank = await _uow.Set<FnBankAccount>().FirstAsync(b => b.Id == c.ChequeBook.BankAccountId, ct);
                    ClearIssuedCheque(c, bank, date);
                    break;
                }
                case "cancel":
                {
                    if (c.Status != FnIssuedChequeStatus.Issued) return Bad<IssuedChequeDto>("فقط چک صادرشده باطل می‌شود.");
                    if (Clean(dto.Note) == null) return Bad<IssuedChequeDto>("علت ابطال چک را بنویسید.");
                    _costing.AddEvent(AccountingEventType.IssuedChequeCancelled, date, "FnIssuedCheque", c.Id, c.Serial.ToString(), $"ابطال چک صادره {c.Serial}", new[]
                    {
                        new AccountingEventLine { AccountRole = AccountRoles.ChequesPayable, Debit = c.Amount, ProviderId = c.ProviderId, Description = "ابطال چک " + c.Serial },
                        new AccountingEventLine { AccountRole = AccountRoles.AccountsPayable, Credit = c.Amount, ProviderId = c.ProviderId, Description = "بازگشت بدهی - چک ابطال‌شده " + c.Serial },
                    });
                    c.Status = FnIssuedChequeStatus.Cancelled; c.VoidReason = Clean(dto.Note);
                    break;
                }
                case "void":
                    if (c.Status != FnIssuedChequeStatus.Blank) return Bad<IssuedChequeDto>("فقط برگ خالی به‌عنوان «ابطال‌شده/خراب» ثبت می‌شود.");
                    if (Clean(dto.Note) == null) return Bad<IssuedChequeDto>("علت خرابی برگ را بنویسید.");
                    if (await _uow.Set<FnPaymentLine>().AnyAsync(l => l.IssuedChequeId == c.Id && l.Payment.Status != FnDocStatus.Cancelled, ct)) return Bad<IssuedChequeDto>("این برگ در یک سند پرداخت انتخاب شده است.");
                    c.Status = FnIssuedChequeStatus.Voided; c.VoidReason = Clean(dto.Note);
                    break;
                default:
                    return Bad<IssuedChequeDto>("عمل نامعتبر است.");
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<IssuedChequeDto>(Conflict); }
            return ResponseDtoHelper.Ok(await IssuedProjection(_uow.Set<FnIssuedCheque>().AsNoTracking().Where(x => x.Id == c.Id)).FirstAsync(ct));
        }

        internal AccountingEvent ClearIssuedCheque(FnIssuedCheque c, FnBankAccount bank, DateTime date)
        {
            AccountingEvent ev = _costing.AddEvent(AccountingEventType.IssuedChequeCleared, date, "FnIssuedCheque", c.Id, c.Serial.ToString(), $"وصول چک صادره {c.Serial}", new[]
            {
                new AccountingEventLine { AccountRole = AccountRoles.ChequesPayable, Debit = c.Amount, ProviderId = c.ProviderId, Description = "وصول چک " + c.Serial },
                BankLine(bank, 0, c.Amount, "چک " + c.Serial),
            });
            c.Status = FnIssuedChequeStatus.Cleared;
            c.ClearedAt = new DateTimeOffset(date);
            return ev;
        }

        // ================================================================================== guarantees

        public async Task<ResponseDto<PagedList<GuaranteeDto>>> GetGuaranteesAsync(TreasuryQuery query, CancellationToken ct)
        {
            query ??= new TreasuryQuery();
            IQueryable<FnGuarantee> q = _uow.Set<FnGuarantee>().AsNoTracking();
            if (query.Kind.HasValue) q = q.Where(g => g.Direction == query.Kind.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(g => g.Number.Contains(text) || g.PartyName.Contains(text) || g.Reference.Contains(text));
            return ResponseDtoHelper.Ok(await PagedList<GuaranteeDto>.CreateAsync(q.OrderByDescending(g => g.Id).Select(g => new GuaranteeDto
            {
                Id = g.Id, Number = g.Number, Kind = g.Kind, Direction = g.Direction, PartyName = g.PartyName, ContractId = g.ContractId, Amount = g.Amount, Date = g.Date,
                DueDate = g.DueDate, Reference = g.Reference, Status = g.Status, ClosedDate = g.ClosedDate,
            }), query.PageNumber, query.PageSize, ct));
        }

        private static AccountingEventLine[] GuaranteeLines(FnGuarantee g, bool reverse)
        {
            string debitRole = g.Direction == 1 ? AccountRoles.GuaranteeChequesReceived : AccountRoles.GuaranteeIssued;
            string creditRole = g.Direction == 1 ? AccountRoles.GuaranteeChequesContra : AccountRoles.GuaranteeIssuedContra;
            string text = (g.Kind == 1 ? "چک تضمینی " : "سفته تضمینی ") + g.Number;
            return new[]
            {
                new AccountingEventLine { AccountRole = reverse ? creditRole : debitRole, Debit = g.Amount, ContractId = g.ContractId, Description = text },
                new AccountingEventLine { AccountRole = reverse ? debitRole : creditRole, Credit = g.Amount, ContractId = g.ContractId, Description = text },
            };
        }

        public async Task<ResponseDto<GuaranteeDto>> SaveGuaranteeAsync(SaveGuaranteeDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return Bad<GuaranteeDto>(NotAllowed);
            if (dto == null || Clean(dto.PartyName) == null) return Bad<GuaranteeDto>("نام طرف حساب را وارد کنید.");
            if (dto.Kind != 1 && dto.Kind != 2) return Bad<GuaranteeDto>("نوع باید ۱ (چک) یا ۲ (سفته) باشد.");
            if (dto.Direction != 1 && dto.Direction != 2) return Bad<GuaranteeDto>("جهت باید ۱ (دریافتی) یا ۲ (صادره) باشد.");
            decimal amount = R(dto.Amount);
            if (amount <= 0) return Bad<GuaranteeDto>("مبلغ باید بیشتر از صفر باشد.");
            DateTime date = dto.Date == default ? DateTime.Today : dto.Date.Date;
            await FinanceSeed.EnsureAsync(_uow, ct);
            var g = new FnGuarantee
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, FinanceNumbers.Guarantee, "GUR", date, ct), Kind = dto.Kind, Direction = dto.Direction, PartyName = Clean(dto.PartyName),
                ContractId = dto.ContractId, Amount = amount, Date = date, DueDate = dto.DueDate?.Date, Reference = Clean(dto.Reference), Status = FnGuaranteeStatus.Held,
                CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now,
            };
            _uow.Set<FnGuarantee>().Add(g);
            _costing.AddEvent(g.Direction == 1 ? AccountingEventType.GuaranteeReceived : AccountingEventType.GuaranteeGiven, date, "FnGuarantee", 0, g.Number,
                $"ثبت {(g.Kind == 1 ? "چک" : "سفته")} تضمینی {g.Number} - {g.PartyName}", GuaranteeLines(g, false));
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(g));
        }

        private static GuaranteeDto ToDto(FnGuarantee g) => new GuaranteeDto
        {
            Id = g.Id, Number = g.Number, Kind = g.Kind, Direction = g.Direction, PartyName = g.PartyName, ContractId = g.ContractId, Amount = g.Amount, Date = g.Date,
            DueDate = g.DueDate, Reference = g.Reference, Status = g.Status, ClosedDate = g.ClosedDate,
        };

        public async Task<ResponseDto<GuaranteeDto>> GuaranteeActionAsync(DocActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return Bad<GuaranteeDto>(NotAllowed);
            if (dto == null || Clean(dto.Action) == null) return Bad<GuaranteeDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnGuarantee g = await _uow.Set<FnGuarantee>().FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (g == null) return ResponseDtoHelper.NotFound<GuaranteeDto>("ضمانت‌نامه پیدا نشد.");
            if (g.Status != FnGuaranteeStatus.Held) return Bad<GuaranteeDto>("این ضمانت‌نامه قبلاً بسته شده است.");
            DateTime date = dto.Date?.Date ?? DateTime.Today;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "return": g.Status = FnGuaranteeStatus.Returned; break;
                case "collect": g.Status = FnGuaranteeStatus.Collected; break;
                default: return Bad<GuaranteeDto>("عمل نامعتبر است.");
            }
            g.ClosedDate = date;
            _costing.AddEvent(AccountingEventType.GuaranteeClosed, date, "FnGuarantee", g.Id, g.Number, $"بسته شدن ضمانت‌نامه {g.Number} ({g.Status})", GuaranteeLines(g, true));
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(g));
        }
    }

    /// <summary>Open amounts of sales invoices (receivable minus posted receipts and credit notes).</summary>
    internal static class ReceivableMath
    {
        /// <param name="excludeReceiptId">A receipt being edited or posted: its own allocations are not counted as paid.</param>
        public static async Task<Dictionary<int, decimal>> OpenAsync(DataLayer.Context.IUnitOfWork uow, List<int> invoiceIds, int? excludeReceiptId, CancellationToken ct)
        {
            if (invoiceIds.Count == 0) return new Dictionary<int, decimal>();
            int skip = excludeReceiptId ?? 0;
            var invoices = await uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).Select(i => new { i.Id, i.ReceivableAmount }).ToListAsync(ct);
            var paid = await uow.Set<FnReceiptAllocation>().AsNoTracking().Where(a => invoiceIds.Contains(a.InvoiceId) && a.Receipt.Status == FnDocStatus.Posted && a.ReceiptId != skip)
                .GroupBy(a => a.InvoiceId).Select(g => new { g.Key, Amount = g.Sum(a => a.Amount) }).ToListAsync(ct);
            var credited = await uow.Set<FnSalesInvoice>().AsNoTracking()
                .Where(i => i.CreditedInvoiceId != null && invoiceIds.Contains(i.CreditedInvoiceId.Value) && i.Type == FnInvoiceType.CreditNote && i.Status == FnDocStatus.Posted)
                .GroupBy(i => i.CreditedInvoiceId.Value).Select(g => new { g.Key, Amount = g.Sum(i => i.ReceivableAmount) }).ToListAsync(ct);
            return invoices.ToDictionary(i => i.Id, i => i.ReceivableAmount - (paid.FirstOrDefault(x => x.Key == i.Id)?.Amount ?? 0) - (credited.FirstOrDefault(x => x.Key == i.Id)?.Amount ?? 0));
        }
    }
}
