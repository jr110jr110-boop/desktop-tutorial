using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    public interface IReceivablesService
    {
        Task<ResponseDto<PagedList<CustomerDto>>> GetCustomersAsync(TreasuryQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<CustomerDto>> SaveCustomerAsync(SaveCustomerDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<SalesInvoiceDto>>> GetInvoicesAsync(InvoiceQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<SalesInvoiceDto>> GetInvoiceAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<SalesInvoiceDto>> SaveInvoiceAsync(SaveInvoiceDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<SalesInvoiceDto>> InvoiceActionAsync(DocActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<DraftFromMilestonesResultDto>> DraftFromMilestonesAsync(CancellationToken cancellationToken);
        Task<ResponseDto<AgeingDto>> GetAgeingAsync(DateTime? asOf, CancellationToken cancellationToken);
        Task<ResponseDto<CustomerStatementDto>> GetCustomerStatementAsync(int customerId, CancellationToken cancellationToken);
    }

    /// <summary>Customers, sales invoices and credit notes, ageing and customer statements.</summary>
    public class ReceivablesService : IReceivablesService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ICostingService _costing;

        public ReceivablesService(IUnitOfWork uow, IInventoryUserContext user, ICostingService costing)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _costing = costing ?? throw new ArgumentNullException(nameof(costing));
        }

        private int Me => _user.UserId;
        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private const string NotAllowed = "شما مجوز این کار را ندارید";
        private const string Conflict = "همزمان توسط کاربر دیگری تغییر کرد؛ دوباره تلاش کنید.";

        private static PagedList<T> Page<T>(List<T> all, int total, int number, int size)
        {
            PagedList<T> page = PagedList<T>.CreateAsync(all, 1, 0, CancellationToken.None).GetAwaiter().GetResult();
            page.TotalCount = total; page.CurrentPage = number; page.PageSize = size; page.TotalPages = (int)Math.Ceiling(total / (double)Math.Max(size, 1));
            return page;
        }

        // ================================================================================== customers

        public async Task<ResponseDto<PagedList<CustomerDto>>> GetCustomersAsync(TreasuryQuery query, CancellationToken ct)
        {
            query ??= new TreasuryQuery();
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<PagedList<CustomerDto>>(NotAllowed);
            IQueryable<FnCustomer> q = _uow.Set<FnCustomer>().AsNoTracking();
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(c => c.Name.Contains(text) || c.EmployerName.Contains(text) || c.EconomicCode.Contains(text) || c.NationalId.Contains(text));
            int total = await q.CountAsync(ct);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize, number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<FnCustomer> rows = await q.OrderBy(c => c.Name).ThenBy(c => c.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            Dictionary<int, decimal> open = await OpenByCustomerAsync(rows.Select(r => r.Id).ToList(), ct);
            return ResponseDtoHelper.Ok(Page(rows.Select(c => ToDto(c, open)).ToList(), total, number, size));
        }

        private async Task<Dictionary<int, decimal>> OpenByCustomerAsync(List<int> customerIds, CancellationToken ct)
        {
            var invoices = await _uow.Set<FnSalesInvoice>().AsNoTracking()
                .Where(i => customerIds.Contains(i.CustomerId) && i.Status == FnDocStatus.Posted && i.Type != FnInvoiceType.CreditNote).Select(i => new { i.Id, i.CustomerId }).ToListAsync(ct);
            Dictionary<int, decimal> open = await ReceivableMath.OpenAsync(_uow, invoices.Select(i => i.Id).ToList(), null, ct);
            return invoices.GroupBy(i => i.CustomerId).ToDictionary(g => g.Key, g => g.Sum(i => open.TryGetValue(i.Id, out decimal o) ? o : 0));
        }

        private static CustomerDto ToDto(FnCustomer c, Dictionary<int, decimal> open) => new CustomerDto
        {
            Id = c.Id, Name = c.Name, EconomicCode = c.EconomicCode, NationalId = c.NationalId, PostalCode = c.PostalCode, Address = c.Address, Phone = c.Phone,
            EmployerName = c.EmployerName, IsActive = c.IsActive, OpenAmount = open.TryGetValue(c.Id, out decimal o) ? o : 0,
        };

        public async Task<ResponseDto<CustomerDto>> SaveCustomerAsync(SaveCustomerDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<CustomerDto>(NotAllowed);
            if (dto == null || Clean(dto.Name) == null) return Bad<CustomerDto>("نام مشتری لازم است.");
            string eco = Clean(dto.EconomicCode) == null ? null : Digits(dto.EconomicCode);
            string nat = Clean(dto.NationalId) == null ? null : Digits(dto.NationalId);
            if (eco != null && eco.Length != 12 && eco.Length != 14) return Bad<CustomerDto>("کد اقتصادی باید ۱۲ یا ۱۴ رقم باشد.");
            if (nat != null && nat.Length != 10 && nat.Length != 11) return Bad<CustomerDto>("شناسه یا کد ملی نامعتبر است.");
            string postal = Clean(dto.PostalCode) == null ? null : Digits(dto.PostalCode);
            if (postal != null && postal.Length != 10) return Bad<CustomerDto>("کد پستی باید ۱۰ رقم باشد.");
            string name = dto.Name.Trim();
            FnCustomer c;
            if (dto.Id.HasValue)
            {
                c = await _uow.Set<FnCustomer>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (c == null) return ResponseDtoHelper.NotFound<CustomerDto>("مشتری پیدا نشد.");
            }
            else
            {
                c = new FnCustomer { CreatedAt = DateTimeOffset.Now, CreatedByUserId = Me };
                _uow.Set<FnCustomer>().Add(c);
            }
            int self = c.Id;
            if (await _uow.Set<FnCustomer>().AnyAsync(x => x.Name == name && x.Id != self, ct)) return Bad<CustomerDto>("مشتری دیگری با همین نام وجود دارد.");
            c.Name = name; c.EconomicCode = eco; c.NationalId = nat; c.PostalCode = postal; c.Address = Clean(dto.Address); c.Phone = Clean(dto.Phone);
            c.EmployerName = Clean(dto.EmployerName); c.IsActive = dto.IsActive;
            await _uow.SaveChangesAsync(ct);
            Dictionary<int, decimal> open = await OpenByCustomerAsync(new List<int> { c.Id }, ct);
            return ResponseDtoHelper.Ok(ToDto(c, open));
        }

        // ================================================================================== invoices

        private async Task<List<SalesInvoiceDto>> LoadAsync(IQueryable<FnSalesInvoice> query, CancellationToken ct)
        {
            List<FnSalesInvoice> rows = await query.AsNoTracking().Include(i => i.Customer).Include(i => i.Lines).AsSplitQuery().ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(r => new int?[] { r.CreatedByUserId, r.ApprovedByUserId }), ct);
            List<int> ids = rows.Where(r => r.Status == FnDocStatus.Posted && r.Type != FnInvoiceType.CreditNote).Select(r => r.Id).ToList();
            Dictionary<int, decimal> open = await ReceivableMath.OpenAsync(_uow, ids, null, ct);
            return rows.Select(i => new SalesInvoiceDto
            {
                Id = i.Id, Number = i.Number, Date = i.Date, DueDate = i.DueDate, CustomerId = i.CustomerId, CustomerName = i.Customer?.Name, ContractId = i.ContractId, MilestoneId = i.MilestoneId,
                Type = i.Type, Status = i.Status, CreditedInvoiceId = i.CreditedInvoiceId, Description = i.Description, NetAmount = i.NetAmount, DiscountAmount = i.DiscountAmount,
                VatAmount = i.VatAmount, GrossAmount = i.GrossAmount, AdvanceRecovery = i.AdvanceRecovery, PerformanceRetentionPercent = i.PerformanceRetentionPercent,
                PerformanceRetention = i.PerformanceRetention, InsuranceRetentionPercent = i.InsuranceRetentionPercent, InsuranceRetention = i.InsuranceRetention,
                WithholdingPercent = i.WithholdingPercent, Withholding = i.Withholding, ReceivableAmount = i.ReceivableAmount,
                OpenAmount = open.TryGetValue(i.Id, out decimal o) ? o : 0, PaidAmount = open.TryGetValue(i.Id, out decimal o2) ? i.ReceivableAmount - o2 : 0,
                TaxId = i.TaxId, CreatorName = names.Name(i.CreatedByUserId), ApprovedByName = names.Name(i.ApprovedByUserId),
                Lines = i.Lines.OrderBy(l => l.RowNumber).Select(l => new InvoiceLineDto
                {
                    RowNumber = l.RowNumber, Description = l.Description, ItemCode = l.ItemCode, Quantity = l.Quantity, Unit = l.Unit, UnitPrice = l.UnitPrice, Discount = l.Discount,
                    VatExempt = l.VatExempt, Amount = l.Amount, VatRatePercent = l.VatRatePercent, VatAmount = l.VatAmount, Total = l.Total,
                }).ToList(),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<SalesInvoiceDto>>> GetInvoicesAsync(InvoiceQuery query, CancellationToken ct)
        {
            query ??= new InvoiceQuery();
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<PagedList<SalesInvoiceDto>>(NotAllowed);
            IQueryable<FnSalesInvoice> q = _uow.Set<FnSalesInvoice>().AsNoTracking();
            if (query.Status.HasValue) q = q.Where(i => i.Status == query.Status.Value);
            if (query.CustomerId.HasValue) q = q.Where(i => i.CustomerId == query.CustomerId.Value);
            if (query.ContractId.HasValue) q = q.Where(i => i.ContractId == query.ContractId.Value);
            if (query.Type.HasValue) q = q.Where(i => i.Type == query.Type.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(i => i.Number.Contains(text) || i.Customer.Name.Contains(text) || i.Description.Contains(text));
            if (query.OpenOnly)
            {
                var posted = await q.Where(i => i.Status == FnDocStatus.Posted && i.Type != FnInvoiceType.CreditNote).Select(i => i.Id).ToListAsync(ct);
                Dictionary<int, decimal> open = await ReceivableMath.OpenAsync(_uow, posted, null, ct);
                List<int> openIds = open.Where(o => o.Value > 0).Select(o => o.Key).ToList();
                q = q.Where(i => openIds.Contains(i.Id));
            }
            int total = await q.CountAsync(ct);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize, number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<SalesInvoiceDto> list = await LoadAsync(q.OrderByDescending(i => i.Date).ThenByDescending(i => i.Id).Skip((number - 1) * size).Take(size), ct);
            return ResponseDtoHelper.Ok(Page(list.OrderByDescending(i => i.Date).ThenByDescending(i => i.Id).ToList(), total, number, size));
        }

        public async Task<ResponseDto<SalesInvoiceDto>> GetInvoiceAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<SalesInvoiceDto>(NotAllowed);
            List<SalesInvoiceDto> list = await LoadAsync(_uow.Set<FnSalesInvoice>().Where(i => i.Id == id), ct);
            return list.Count == 0 ? ResponseDtoHelper.NotFound<SalesInvoiceDto>("فاکتور پیدا نشد.") : ResponseDtoHelper.Ok(list[0]);
        }

        private sealed class Computed
        {
            public List<FnSalesInvoiceLine> Lines = new List<FnSalesInvoiceLine>();
            public decimal Net, Discount, Vat, Gross, Perf, Ins, With, Receivable, PerfPct, InsPct, WithPct;
            public string Error;
        }

        private async Task<Computed> ComputeAsync(SaveInvoiceDto dto, DateTime date, CancellationToken ct)
        {
            var c = new Computed();
            List<InvoiceLineDto> input = (dto.Lines ?? new List<InvoiceLineDto>()).Where(l => l != null).ToList();
            if (input.Count == 0) { c.Error = "فاکتور حداقل یک ردیف لازم دارد."; return c; }
            decimal? vatRate = null;
            bool vatLooked = false;
            int row = 1;
            foreach (InvoiceLineDto l in input)
            {
                if (Clean(l.Description) == null) { c.Error = $"ردیف {row}: شرح خالی است."; return c; }
                if (l.Quantity <= 0 || l.UnitPrice < 0 || l.Discount < 0) { c.Error = $"ردیف {row}: تعداد، قیمت یا تخفیف نامعتبر است."; return c; }
                decimal gross = R(l.Quantity * l.UnitPrice);
                decimal discount = R(l.Discount);
                if (discount > gross) { c.Error = $"ردیف {row}: تخفیف از مبلغ ردیف بیشتر است."; return c; }
                decimal amount = gross - discount;
                decimal rate = 0;
                // releasing a retention is not a sale: no revenue and no VAT again (the VAT was charged on the original invoice)
                if (!l.VatExempt && dto.Type != FnInvoiceType.RetentionRelease)
                {
                    if (!vatLooked) { vatRate = await TaxRates.RateAtAsync(_uow, FnTaxKind.Vat, date, ct); vatLooked = true; }
                    if (vatRate == null) { c.Error = "نرخ مالیات بر ارزش افزوده برای این تاریخ در جدول نرخ‌ها ثبت نشده است؛ ابتدا نرخ را ثبت کنید."; return c; }
                    rate = vatRate.Value;
                }
                decimal vat = R(amount * rate / 100m);
                c.Lines.Add(new FnSalesInvoiceLine
                {
                    RowNumber = row++, Description = l.Description.Trim(), ItemCode = Clean(l.ItemCode), Quantity = l.Quantity, Unit = Clean(l.Unit), UnitPrice = l.UnitPrice,
                    Discount = discount, VatExempt = l.VatExempt, Amount = amount, VatRatePercent = rate, VatAmount = vat, Total = amount + vat,
                });
            }
            c.Net = c.Lines.Sum(l => l.Amount); c.Discount = c.Lines.Sum(l => l.Discount); c.Vat = c.Lines.Sum(l => l.VatAmount); c.Gross = c.Net + c.Vat;
            if (dto.Type == FnInvoiceType.CreditNote || dto.Type == FnInvoiceType.RetentionRelease)
            {
                c.Receivable = c.Gross;
                return c;
            }
            async Task<decimal> PercentAsync(decimal? given, FnTaxKind kind)
            {
                if (given.HasValue) return given.Value;
                return dto.UseDefaultDeductionRates ? await TaxRates.RateAtAsync(_uow, kind, date, ct) ?? 0 : 0;
            }
            c.PerfPct = await PercentAsync(dto.PerformanceRetentionPercent, FnTaxKind.PerformanceRetention);
            c.InsPct = await PercentAsync(dto.InsuranceRetentionPercent, FnTaxKind.InsuranceRetention);
            c.WithPct = await PercentAsync(dto.WithholdingPercent, FnTaxKind.WithholdingContract);
            if (c.PerfPct < 0 || c.PerfPct > 100 || c.InsPct < 0 || c.InsPct > 100 || c.WithPct < 0 || c.WithPct > 100) { c.Error = "درصد کسورات باید بین ۰ و ۱۰۰ باشد."; return c; }
            c.Perf = R(c.Net * c.PerfPct / 100m); c.Ins = R(c.Net * c.InsPct / 100m); c.With = R(c.Net * c.WithPct / 100m);
            c.Receivable = c.Gross - R(dto.AdvanceRecovery) - c.Perf - c.Ins - c.With;
            if (c.Receivable < 0) c.Error = "مجموع کسورات از مبلغ ناخالص بیشتر است.";
            return c;
        }

        public async Task<ResponseDto<SalesInvoiceDto>> SaveInvoiceAsync(SaveInvoiceDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<SalesInvoiceDto>(NotAllowed);
            if (dto == null) return Bad<SalesInvoiceDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!Enum.IsDefined(typeof(FnInvoiceType), dto.Type)) return Bad<SalesInvoiceDto>("نوع فاکتور نامعتبر است.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            DateTime date = dto.Date == default ? DateTime.Today : dto.Date.Date;
            if (dto.DueDate.HasValue && dto.DueDate.Value.Date < date) return Bad<SalesInvoiceDto>("سررسید قبل از تاریخ فاکتور است.");
            FnCustomer customer = await _uow.Set<FnCustomer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == dto.CustomerId, ct);
            if (customer == null || !customer.IsActive) return Bad<SalesInvoiceDto>("مشتری پیدا نشد یا غیرفعال است.");
            if (dto.AdvanceRecovery < 0) return Bad<SalesInvoiceDto>("کسر پیش‌پرداخت نمی‌تواند منفی باشد.");
            if (dto.AdvanceRecovery > 0 && (dto.Type == FnInvoiceType.Advance || dto.Type == FnInvoiceType.CreditNote)) return Bad<SalesInvoiceDto>("کسر پیش‌پرداخت برای این نوع فاکتور مجاز نیست.");

            int selfId = dto.Id ?? 0;
            IQueryable<FnSalesInvoice> ofCustomer = _uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => i.CustomerId == customer.Id && i.Status != FnDocStatus.Cancelled && i.Id != selfId && (!dto.ContractId.HasValue || i.ContractId == dto.ContractId));
            if (dto.AdvanceRecovery > 0)
            {
                // the recovery cannot exceed the advance the customer actually paid and that is not recovered yet
                decimal advanceReceived = await ofCustomer.Where(i => i.Type == FnInvoiceType.Advance && i.Status == FnDocStatus.Posted).SumAsync(i => (decimal?)i.NetAmount, ct) ?? 0;
                decimal recovered = await ofCustomer.Where(i => i.Type != FnInvoiceType.Advance && i.Type != FnInvoiceType.CreditNote).SumAsync(i => (decimal?)i.AdvanceRecovery, ct) ?? 0;
                if (R(dto.AdvanceRecovery) > advanceReceived - recovered) return Bad<SalesInvoiceDto>($"کسر پیش‌دریافت ({R(dto.AdvanceRecovery):0}) از پیش‌دریافت بازیابی‌نشده این مشتری ({advanceReceived - recovered:0} ریال) بیشتر است.");
            }
            if (dto.Type == FnInvoiceType.RetentionRelease)
            {
                if (dto.AdvanceRecovery > 0 || dto.PerformanceRetentionPercent.GetValueOrDefault() > 0 || dto.InsuranceRetentionPercent.GetValueOrDefault() > 0 || dto.WithholdingPercent.GetValueOrDefault() > 0 || dto.UseDefaultDeductionRates)
                    return Bad<SalesInvoiceDto>("آزادسازی حسن انجام کار کسورات ندارد.");
                decimal held = await ofCustomer.Where(i => i.Status == FnDocStatus.Posted && i.Type != FnInvoiceType.CreditNote && i.Type != FnInvoiceType.RetentionRelease).SumAsync(i => (decimal?)i.PerformanceRetention, ct) ?? 0;
                decimal released = await ofCustomer.Where(i => i.Type == FnInvoiceType.RetentionRelease).SumAsync(i => (decimal?)i.NetAmount, ct) ?? 0;
                decimal requested = (dto.Lines ?? new List<InvoiceLineDto>()).Where(l => l != null).Sum(l => Math.Max(0, R(l.Quantity * l.UnitPrice - l.Discount)));
                if (requested > held - released) return Bad<SalesInvoiceDto>($"مبلغ آزادسازی از حسن انجام کار نگهداری‌شده ({held - released:0} ریال) بیشتر است.");
            }

            FnSalesInvoice credited = null;
            if (dto.Type == FnInvoiceType.CreditNote)
            {
                if (!dto.CreditedInvoiceId.HasValue) return Bad<SalesInvoiceDto>("فاکتور اصلی برای اعلامیه بستانکار را مشخص کنید.");
                credited = await _uow.Set<FnSalesInvoice>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == dto.CreditedInvoiceId.Value, ct);
                if (credited == null || credited.Status != FnDocStatus.Posted || credited.Type == FnInvoiceType.CreditNote || credited.CustomerId != customer.Id)
                    return Bad<SalesInvoiceDto>("فاکتور اصلی باید فاکتور فروش ثبت‌شده‌ی همین مشتری باشد.");
            }
            else if (dto.CreditedInvoiceId.HasValue) return Bad<SalesInvoiceDto>("فاکتور اصلی فقط برای اعلامیه بستانکار است.");

            if (dto.MilestoneId.HasValue)
            {
                PcPaymentMilestone ms = await _uow.Set<PcPaymentMilestone>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == dto.MilestoneId.Value && !m.Deleted, ct);
                if (ms == null) return Bad<SalesInvoiceDto>("مرحله پرداخت پیدا نشد.");
                if (dto.ContractId.HasValue && dto.ContractId.Value != ms.ContractId) return Bad<SalesInvoiceDto>("مرحله پرداخت متعلق به این قرارداد نیست.");
                dto.ContractId = ms.ContractId;
            }
            if (dto.ContractId.HasValue && !await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId.Value && !c.Deleted, ct)) return Bad<SalesInvoiceDto>("قرارداد پیدا نشد.");

            FnSalesInvoice inv = null;
            if (dto.Id.HasValue)
            {
                inv = await _uow.Set<FnSalesInvoice>().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == dto.Id.Value, ct);
                if (inv == null) return ResponseDtoHelper.NotFound<SalesInvoiceDto>("فاکتور پیدا نشد.");
                if (inv.Status != FnDocStatus.Draft) return Bad<SalesInvoiceDto>("فقط پیش‌نویس ویرایش می‌شود.");
                inv.EditorUserIds = WithEditor(inv.EditorUserIds, Me);
            }
            if (dto.MilestoneId.HasValue)
            {
                int self = inv?.Id ?? 0;
                if (await _uow.Set<FnSalesInvoice>().AnyAsync(i => i.MilestoneId == dto.MilestoneId.Value && i.Status != FnDocStatus.Cancelled && i.Id != self, ct))
                    return Bad<SalesInvoiceDto>("برای این مرحله پرداخت قبلاً فاکتور صادر شده است.");
            }

            Computed c = await ComputeAsync(dto, date, ct);
            if (c.Error != null) return Bad<SalesInvoiceDto>(c.Error);
            if (credited != null)
            {
                Dictionary<int, decimal> open = await ReceivableMath.OpenAsync(_uow, new List<int> { credited.Id }, null, ct);
                decimal available = open.TryGetValue(credited.Id, out decimal o) ? o : 0;
                if (c.Gross > available) return Bad<SalesInvoiceDto>($"مبلغ اعلامیه بستانکار از مانده باز فاکتور اصلی ({available:0} ریال) بیشتر است.");
            }

            if (inv == null)
            {
                inv = new FnSalesInvoice
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, FinanceNumbers.SalesInvoice, dto.Type == FnInvoiceType.CreditNote ? "SCN" : "SI", date, ct),
                    Status = FnDocStatus.Draft, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now, Lines = new List<FnSalesInvoiceLine>(),
                };
                _uow.Set<FnSalesInvoice>().Add(inv);
            }
            else
            {
                _uow.Set<FnSalesInvoiceLine>().RemoveRange(inv.Lines);
                inv.Lines = new List<FnSalesInvoiceLine>();
            }
            Apply(inv, dto, date, customer.Id, c);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SalesInvoiceDto>(Conflict); }
            catch (DbUpdateException) { return Bad<SalesInvoiceDto>("ذخیره فاکتور ناموفق بود؛ احتمالاً برای این مرحله پرداخت فاکتور دیگری صادر شده است."); }
            return ResponseDtoHelper.Ok((await LoadAsync(_uow.Set<FnSalesInvoice>().Where(i => i.Id == inv.Id), ct))[0]);
        }

        private static void Apply(FnSalesInvoice inv, SaveInvoiceDto dto, DateTime date, int customerId, Computed c)
        {
            inv.Date = date; inv.DueDate = dto.DueDate?.Date; inv.CustomerId = customerId; inv.ContractId = dto.ContractId; inv.MilestoneId = dto.MilestoneId;
            inv.Type = dto.Type; inv.CreditedInvoiceId = dto.CreditedInvoiceId; inv.Description = Clean(dto.Description);
            inv.NetAmount = c.Net; inv.DiscountAmount = c.Discount; inv.VatAmount = c.Vat; inv.GrossAmount = c.Gross;
            inv.AdvanceRecovery = dto.Type == FnInvoiceType.CreditNote ? 0 : R(dto.AdvanceRecovery);
            inv.PerformanceRetentionPercent = c.PerfPct; inv.PerformanceRetention = c.Perf;
            inv.InsuranceRetentionPercent = c.InsPct; inv.InsuranceRetention = c.Ins;
            inv.WithholdingPercent = c.WithPct; inv.Withholding = c.With; inv.ReceivableAmount = c.Receivable;
            foreach (FnSalesInvoiceLine l in c.Lines) inv.Lines.Add(l);
        }

        public async Task<ResponseDto<SalesInvoiceDto>> InvoiceActionAsync(DocActionDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<SalesInvoiceDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<SalesInvoiceDto>(NotAllowed);
            FnSalesInvoice inv = await _uow.Set<FnSalesInvoice>().Include(i => i.Customer).FirstOrDefaultAsync(i => i.Id == dto.Id, ct);
            if (inv == null) return ResponseDtoHelper.NotFound<SalesInvoiceDto>("فاکتور پیدا نشد.");
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "approve":
                {
                    if (inv.Status != FnDocStatus.Draft) return Bad<SalesInvoiceDto>("فقط پیش‌نویس تایید می‌شود.");
                    if (Touched(inv.CreatedByUserId, inv.EditorUserIds, Me)) return Bad<SalesInvoiceDto>("تاییدکننده فاکتور باید غیر از ثبت‌کننده آن باشد.");
                    if (!inv.Customer.IsActive) return Bad<SalesInvoiceDto>("مشتری غیرفعال است.");
                    PcPaymentMilestone milestone = null;
                    if (inv.MilestoneId.HasValue) milestone = await _uow.Set<PcPaymentMilestone>().FirstOrDefaultAsync(m => m.Id == inv.MilestoneId.Value, ct);
                    if (inv.Type == FnInvoiceType.CreditNote)
                    {
                        Dictionary<int, decimal> open = await ReceivableMath.OpenAsync(_uow, new List<int> { inv.CreditedInvoiceId.Value }, null, ct);
                        if (inv.GrossAmount > (open.TryGetValue(inv.CreditedInvoiceId.Value, out decimal o) ? o : 0)) return Bad<SalesInvoiceDto>("مبلغ اعلامیه بستانکار از مانده باز فاکتور اصلی بیشتر است.");
                    }
                    _costing.AddEvent(inv.Type == FnInvoiceType.CreditNote ? AccountingEventType.SalesCreditNote : AccountingEventType.SalesInvoice, inv.Date, "FnSalesInvoice", inv.Id, inv.Number,
                        (inv.Type == FnInvoiceType.CreditNote ? "اعلامیه بستانکار " : "فاکتور فروش ") + inv.Number + " - " + inv.Customer.Name, EventLines(inv));
                    inv.Status = FnDocStatus.Posted; inv.ApprovedByUserId = Me; inv.ApprovedAt = DateTimeOffset.Now;
                    if (milestone != null && milestone.Status < PcMilestoneStatus.Invoiced) milestone.Status = PcMilestoneStatus.Invoiced;
                    break;
                }
                case "cancel":
                    if (inv.Status != FnDocStatus.Draft) return Bad<SalesInvoiceDto>("فقط پیش‌نویس باطل می‌شود؛ فاکتور ثبت‌شده با اعلامیه بستانکار اصلاح می‌شود.");
                    inv.Status = FnDocStatus.Cancelled;
                    break;
                default:
                    return Bad<SalesInvoiceDto>("عمل نامعتبر است.");
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SalesInvoiceDto>(Conflict); }
            return ResponseDtoHelper.Ok((await LoadAsync(_uow.Set<FnSalesInvoice>().Where(i => i.Id == inv.Id), ct))[0]);
        }

        private static List<AccountingEventLine> EventLines(FnSalesInvoice inv)
        {
            var lines = new List<AccountingEventLine>();
            AccountingEventLine Customer(string role, decimal debit, decimal credit, string text) => new AccountingEventLine
            {
                AccountRole = role, Debit = debit, Credit = credit, PartyType = "Customer", PartyId = inv.CustomerId, ContractId = inv.ContractId, Description = text,
            };
            if (inv.Type == FnInvoiceType.CreditNote)
            {
                lines.Add(new AccountingEventLine { AccountRole = AccountRoles.SalesReturns, Debit = inv.NetAmount, ContractId = inv.ContractId, Description = "برگشت از فروش " + inv.Number });
                lines.Add(new AccountingEventLine { AccountRole = AccountRoles.VatSales, Debit = inv.VatAmount, Description = "مالیات ارزش افزوده " + inv.Number });
                lines.Add(Customer(AccountRoles.AccountsReceivable, 0, inv.GrossAmount, inv.Number));
                return lines;
            }
            lines.Add(Customer(AccountRoles.AccountsReceivable, inv.ReceivableAmount, 0, inv.Number));
            if (inv.AdvanceRecovery > 0) lines.Add(Customer(AccountRoles.CustomerAdvance, inv.AdvanceRecovery, 0, "کسر پیش‌دریافت " + inv.Number));
            if (inv.PerformanceRetention > 0) lines.Add(Customer(AccountRoles.RetentionReceivable, inv.PerformanceRetention, 0, "حسن انجام کار " + inv.Number));
            if (inv.InsuranceRetention > 0) lines.Add(Customer(AccountRoles.InsuranceRetentionReceivable, inv.InsuranceRetention, 0, "سپرده بیمه " + inv.Number));
            if (inv.Withholding > 0) lines.Add(Customer(AccountRoles.WithholdingReceivable, inv.Withholding, 0, "مالیات تکلیفی " + inv.Number));
            if (inv.Type == FnInvoiceType.RetentionRelease)
            {
                // Dr receivable / Cr retention receivable: the money held back becomes collectable, nothing is earned again
                lines.Clear();
                lines.Add(new AccountingEventLine { AccountRole = AccountRoles.AccountsReceivable, Debit = inv.ReceivableAmount, PartyType = "Customer", PartyId = inv.CustomerId, ContractId = inv.ContractId, Description = "آزادسازی حسن انجام کار " + inv.Number });
                lines.Add(new AccountingEventLine { AccountRole = AccountRoles.RetentionReceivable, Credit = inv.ReceivableAmount, PartyType = "Customer", PartyId = inv.CustomerId, ContractId = inv.ContractId, Description = "آزادسازی حسن انجام کار " + inv.Number });
                return lines;
            }
            string revenue = inv.Type == FnInvoiceType.Advance ? AccountRoles.CustomerAdvance : inv.Type == FnInvoiceType.Goods || inv.Type == FnInvoiceType.Service ? AccountRoles.RevenueSales : AccountRoles.RevenueContracts;
            if (inv.Type == FnInvoiceType.Advance) lines.Add(Customer(revenue, 0, inv.NetAmount, "پیش‌دریافت " + inv.Number));
            else lines.Add(new AccountingEventLine { AccountRole = revenue, Credit = inv.NetAmount, ContractId = inv.ContractId, Description = "درآمد " + inv.Number });
            lines.Add(new AccountingEventLine { AccountRole = AccountRoles.VatSales, Credit = inv.VatAmount, Description = "مالیات ارزش افزوده " + inv.Number });
            return lines;
        }

        // ================================================================================== drafts from contract milestones

        public async Task<ResponseDto<DraftFromMilestonesResultDto>> DraftFromMilestonesAsync(CancellationToken ct)
        {
            if (_user.UserId != 0 && !_user.IsInRole(FinanceRoles.SystemJobs)) return Bad<DraftFromMilestonesResultDto>(NotAllowed);
            await FinanceSeed.EnsureAsync(_uow, ct);
            var result = new DraftFromMilestonesResultDto();
            DateTime today = DateTime.Today;
            decimal? vat = await TaxRates.RateAtAsync(_uow, FnTaxKind.Vat, today, ct);
            List<PcPaymentMilestone> milestones = await _uow.Set<PcPaymentMilestone>().Include(m => m.Contract)
                .Where(m => !m.Deleted && m.Status == PcMilestoneStatus.Due && m.Amount > 0 && !_uow.Set<FnSalesInvoice>().Any(i => i.MilestoneId == m.Id && i.Status != FnDocStatus.Cancelled))
                .OrderBy(m => m.Id).Take(200).ToListAsync(ct);
            if (milestones.Count == 0) return ResponseDtoHelper.Ok(result);
            if (vat == null)
            {
                result.Skipped.Add("نرخ مالیات بر ارزش افزوده در جدول نرخ‌ها ثبت نشده است؛ پیش‌نویس فاکتور ساخته نشد.");
                return ResponseDtoHelper.Ok(result);
            }
            var customers = new Dictionary<string, FnCustomer>();
            foreach (PcPaymentMilestone m in milestones)
            {
                string employer = Clean(m.Contract?.Employer);
                if (employer == null) { result.Skipped.Add($"مرحله «{m.Title}»: کارفرمای قرارداد مشخص نیست."); continue; }
                if (!customers.TryGetValue(employer, out FnCustomer customer))
                {
                    customer = await _uow.Set<FnCustomer>().FirstOrDefaultAsync(x => x.IsActive && (x.EmployerName == employer || x.Name == employer), ct);
                    if (customer == null)
                    {
                        customer = new FnCustomer { Name = employer, EmployerName = employer, IsActive = true, CreatedAt = DateTimeOffset.Now, CreatedByUserId = Me };
                        _uow.Set<FnCustomer>().Add(customer);
                    }
                    customers[employer] = customer;
                }
                decimal net = R(m.Amount);
                FnInvoiceType invoiceType = MilestoneType(m.Kind);
                decimal vatRate = invoiceType == FnInvoiceType.RetentionRelease ? 0m : vat.Value;
                decimal vatAmount = R(net * vatRate / 100m);
                var inv = new FnSalesInvoice
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, FinanceNumbers.SalesInvoice, "SI", today, ct), Date = today, Customer = customer, ContractId = m.ContractId,
                    MilestoneId = m.Id, ProvisionalStatementId = m.ProvisionalStatementId, Status = FnDocStatus.Draft, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now,
                    Type = invoiceType, Description = "پیش‌نویس خودکار از مرحله پرداخت: " + m.Title,
                    NetAmount = net, VatAmount = vatAmount, GrossAmount = net + vatAmount, ReceivableAmount = net + vatAmount,
                    Lines = new List<FnSalesInvoiceLine>
                    {
                        new FnSalesInvoiceLine { RowNumber = 1, Description = m.Title, Quantity = 1, UnitPrice = net, Amount = net, VatRatePercent = vatRate, VatAmount = vatAmount, Total = net + vatAmount },
                    },
                };
                _uow.Set<FnSalesInvoice>().Add(inv);
                result.Drafted++;
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<DraftFromMilestonesResultDto>("ثبت پیش‌نویس‌ها ناموفق بود؛ دوباره تلاش کنید."); }
            return ResponseDtoHelper.Ok(result);
        }

        private static FnInvoiceType MilestoneType(PcMilestoneKind kind)
        {
            switch (kind)
            {
                case PcMilestoneKind.Advance: return FnInvoiceType.Advance;
                case PcMilestoneKind.Retention: return FnInvoiceType.RetentionRelease;
                case PcMilestoneKind.Delivery: return FnInvoiceType.Goods;
                default: return FnInvoiceType.Progress;
            }
        }

        // ================================================================================== ageing and statements

        public async Task<ResponseDto<AgeingDto>> GetAgeingAsync(DateTime? asOf, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<AgeingDto>(NotAllowed);
            DateTime day = (asOf ?? DateTime.Today).Date;
            var invoices = await _uow.Set<FnSalesInvoice>().AsNoTracking()
                .Where(i => i.Status == FnDocStatus.Posted && i.Type != FnInvoiceType.CreditNote && i.Date <= day)
                .Select(i => new { i.Id, i.CustomerId, CustomerName = i.Customer.Name, i.Date, i.DueDate }).ToListAsync(ct);
            Dictionary<int, decimal> open = await OpenAsOfAsync(invoices.Select(i => i.Id).ToList(), day, ct);
            var dto = new AgeingDto { AsOf = day };
            foreach (var g in invoices.GroupBy(i => i.CustomerId))
            {
                var row = new AgeingRowDto { CustomerId = g.Key, CustomerName = g.First().CustomerName };
                foreach (var i in g)
                {
                    decimal amount = open.TryGetValue(i.Id, out decimal o) ? o : 0;
                    if (amount <= 0) continue;
                    int days = (int)(day - (i.DueDate ?? i.Date).Date).TotalDays;
                    if (days <= 30) row.Current += amount;
                    else if (days <= 60) row.Days31To60 += amount;
                    else if (days <= 90) row.Days61To90 += amount;
                    else row.Over90 += amount;
                }
                row.Total = row.Current + row.Days31To60 + row.Days61To90 + row.Over90;
                if (row.Total <= 0) continue;
                dto.Rows.Add(row);
                dto.Current += row.Current; dto.Days31To60 += row.Days31To60; dto.Days61To90 += row.Days61To90; dto.Over90 += row.Over90; dto.Total += row.Total;
            }
            dto.Rows = dto.Rows.OrderByDescending(r => r.Total).ThenBy(r => r.CustomerName).ToList();
            return ResponseDtoHelper.Ok(dto);
        }

        /// <summary>Open amount of invoices on a date: receipts and credit notes dated after it are not counted as paid.</summary>
        private async Task<Dictionary<int, decimal>> OpenAsOfAsync(List<int> ids, DateTime day, CancellationToken ct)
        {
            if (ids.Count == 0) return new Dictionary<int, decimal>();
            var receivable = await _uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.ReceivableAmount }).ToListAsync(ct);
            var paid = await _uow.Set<FnReceiptAllocation>().AsNoTracking().Where(a => ids.Contains(a.InvoiceId) && a.Receipt.Status == FnDocStatus.Posted && a.Receipt.Date <= day)
                .GroupBy(a => a.InvoiceId).Select(g => new { g.Key, Amount = g.Sum(a => a.Amount) }).ToListAsync(ct);
            var credited = await _uow.Set<FnSalesInvoice>().AsNoTracking()
                .Where(i => i.CreditedInvoiceId != null && ids.Contains(i.CreditedInvoiceId.Value) && i.Type == FnInvoiceType.CreditNote && i.Status == FnDocStatus.Posted && i.Date <= day)
                .GroupBy(i => i.CreditedInvoiceId.Value).Select(g => new { g.Key, Amount = g.Sum(i => i.ReceivableAmount) }).ToListAsync(ct);
            return receivable.ToDictionary(r => r.Id, r => r.ReceivableAmount - (paid.FirstOrDefault(p => p.Key == r.Id)?.Amount ?? 0) - (credited.FirstOrDefault(p => p.Key == r.Id)?.Amount ?? 0));
        }

        public async Task<ResponseDto<CustomerStatementDto>> GetCustomerStatementAsync(int customerId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<CustomerStatementDto>(NotAllowed);
            FnCustomer customer = await _uow.Set<FnCustomer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct);
            if (customer == null) return ResponseDtoHelper.NotFound<CustomerStatementDto>("مشتری پیدا نشد.");
            var entries = new List<CustomerStatementRowDto>();
            foreach (var i in await _uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => i.CustomerId == customerId && i.Status == FnDocStatus.Posted)
                         .Select(i => new { i.Id, i.Number, i.Date, i.Type, i.Description, i.GrossAmount, i.ReceivableAmount }).ToListAsync(ct))
            {
                bool credit = i.Type == FnInvoiceType.CreditNote;
                entries.Add(new CustomerStatementRowDto
                {
                    Date = i.Date, Kind = credit ? "CreditNote" : "Invoice", Number = i.Number, Description = i.Description,
                    Debit = credit ? 0 : i.ReceivableAmount, Credit = credit ? i.GrossAmount : 0,
                });
            }
            foreach (var r in await _uow.Set<FnReceipt>().AsNoTracking().Where(r => r.CustomerId == customerId && r.Status == FnDocStatus.Posted)
                         .Select(r => new { r.Number, r.Date, r.Description, r.TotalAmount }).ToListAsync(ct))
                entries.Add(new CustomerStatementRowDto { Date = r.Date, Kind = "Receipt", Number = r.Number, Description = r.Description, Credit = r.TotalAmount });
            decimal balance = 0;
            var dto = new CustomerStatementDto { CustomerId = customer.Id, CustomerName = customer.Name };
            foreach (CustomerStatementRowDto e in entries.OrderBy(e => e.Date).ThenBy(e => e.Kind == "Invoice" ? 0 : 1).ThenBy(e => e.Number))
            {
                balance += e.Debit - e.Credit;
                e.Balance = balance;
                dto.Rows.Add(e);
            }
            dto.Balance = balance;
            return ResponseDtoHelper.Ok(dto);
        }
    }
}
