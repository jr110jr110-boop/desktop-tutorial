using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Production;

namespace Services.NasbNiro.Finance.Sp
{
    // Sepidar transfer (per document): which Orchestra documents can be transferred, in which FINAL state, and what the neutral snapshot of each holds.
    // A snapshot carries no secret and no per-person payroll line: a payroll run goes as its totals only; an account number or an IBAN of a person is never copied.

    /// <summary>The codes of the transferable kinds.</summary>
    public static class SpTypeCodes
    {
        public const string Voucher = "VOUCHER";
        public const string SalesInvoice = "SALES_INVOICE";
        public const string PurchaseInvoice = "PURCHASE_INVOICE";
        public const string Receipt = "RECEIPT";
        public const string Payment = "PAYMENT";
        public const string PayrollSummary = "PAYROLL_SUMMARY";
    }

    /// <summary>The roles of the module.</summary>
    public static class SpRoles
    {
        /// <summary>Read the queue, the kinds and the summary (the auditor reads only).</summary>
        public const string Readers = "FINANCEMANAGER,CEO,AUDITOR";

        /// <summary>Approve, reject, retry and cancel an item (a person who is not the maker of the source document). The CEO is the second decider, so a document made by the only finance manager can still be approved.</summary>
        public const string Deciders = "FINANCEMANAGER,CEO";

        /// <summary>Switch a kind on or off.</summary>
        public const string TypeSwitchers = "FINANCEMANAGER,CEO";

        /// <summary>Prepares and sends (the finance agent seat holds the FINANCIAL role).</summary>
        public const string Operators = FinanceRoles.Accountants;
    }

    /// <summary>The cheap shape of a final source document: its id and its row version (a change after the item was prepared shows as another row version).</summary>
    internal sealed class SpRef
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; }

        /// <summary>When the document became final (the stamp of its finalising or posting); a document without one is never queued.</summary>
        public DateTimeOffset? At { get; set; }
    }

    internal sealed class SpBuilt
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public object Data { get; set; }
        public string Makers { get; set; }
        public bool Restricted { get; set; }
        public byte[] RowVersion { get; set; }
    }

    internal static class SpPayload
    {
        public const int SchemaVersion = 1;

        private static readonly JsonSerializerOptions Compact = new JsonSerializerOptions { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        private static readonly JsonSerializerOptions Pretty = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>A decimal without trailing zeros, so the same value always serialises the same way.</summary>
        public static decimal N(decimal v) => v / 1.0000000000000000000000000000m;
        public static decimal? N(decimal? v) => v.HasValue ? N(v.Value) : (decimal?)null;

        public static string D(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public static string D(DateTime? d) => d.HasValue ? D(d.Value) : null;

        /// <summary>The canonical text: schemaVersion, the kind, the source and the data, in a fixed order, compact.</summary>
        public static string Canonical(string typeCode, string sourceType, int sourceId, string number, object data) =>
            JsonSerializer.Serialize(new { schemaVersion = SchemaVersion, kind = typeCode, source = new { type = sourceType, id = sourceId, number }, data }, Compact);

        public static string Hash(string json)
        {
            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(json)).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }

        /// <summary>The text for people: the same JSON, indented. A text that is not JSON comes back as it is.</summary>
        public static string PrettyOf(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return json;
            try { using (JsonDocument doc = JsonDocument.Parse(json)) return JsonSerializer.Serialize(doc.RootElement, Pretty); }
            catch (JsonException) { return json; }
        }

        /// <summary>",5,7," of the people who made or edited the document (0 and null are the system and are left out).</summary>
        public static string Makers(string editorsCsv, params int?[] ids)
        {
            var set = new SortedSet<int>();
            foreach (int? id in ids) if (id.HasValue && id.Value > 0) set.Add(id.Value);
            if (!string.IsNullOrEmpty(editorsCsv))
                foreach (string part in editorsCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v > 0) set.Add(v);
            return set.Count == 0 ? null : "," + string.Join(",", set) + ",";
        }

        public static bool IsMaker(string makersCsv, int userId) => userId > 0 && makersCsv != null && makersCsv.Contains("," + userId.ToString(CultureInfo.InvariantCulture) + ",");
    }

    internal abstract class SpSource
    {
        public abstract string Code { get; }
        public abstract string Title { get; }
        public abstract string SourceType { get; }
        public abstract string Note { get; }

        /// <summary>The old monthly package kinds (api/v1/finance/sepidar) that carry the same data: this kind cannot be switched on while one of them covers the period.</summary>
        public virtual FnOutboxKind[] OldKinds => Array.Empty<FnOutboxKind>();

        /// <summary>The same, for people (shown on the kinds tab and in the refusal).</summary>
        public virtual string OldKindsText => null;

        /// <summary>The documents in their FINAL state, and no other.</summary>
        public abstract IQueryable<SpRef> Final(IUnitOfWork uow);

        public abstract Task<List<SpBuilt>> BuildAsync(IUnitOfWork uow, IReadOnlyCollection<int> ids, CancellationToken ct);

        protected static List<int> Ids(IReadOnlyCollection<int> ids) => ids.ToList();
    }

    internal sealed class SpVoucherSource : SpSource
    {
        public override string Code => SpTypeCodes.Voucher;
        public override string Title => "سند حسابداری (نهایی‌شده)";
        public override string SourceType => nameof(FnVoucher);
        public override string Note => "سند در وضعیت نهایی با همهٔ ردیف‌ها (سند کامل و تراز)؛ سند واردشده از سپیدار منتقل نمی‌شود. سندی که ردیف محرمانهٔ حقوق یا ردیف تفصیلی «کارمند» دارد قلم محرمانه است و فقط خوانندگان حقوق آن را می‌بینند.";

        public override IQueryable<SpRef> Final(IUnitOfWork uow) =>
            uow.Set<FnVoucher>().AsNoTracking().Where(v => v.Status == FnVoucherStatus.Final && !v.IsImported && v.Number != null).Select(v => new SpRef { Id = v.Id, RowVersion = v.RowVersion, At = v.FinalizedAt });

        public override async Task<List<SpBuilt>> BuildAsync(IUnitOfWork uow, IReadOnlyCollection<int> idSet, CancellationToken ct)
        {
            List<int> ids = Ids(idSet);
            var heads = await uow.Set<FnVoucher>().AsNoTracking().Where(v => ids.Contains(v.Id) && v.Status == FnVoucherStatus.Final && !v.IsImported && v.Number != null).OrderBy(v => v.Id)
                .Select(v => new { v.Id, v.Number, v.Date, v.Type, v.Description, v.TotalAmount, v.SourceType, v.FiscalYear.Year, v.CreatedByUserId, v.TriggeredByUserId, v.EditorUserIds, v.RowVersion }).ToListAsync(ct);
            // EVERY line goes (Sepidar must get a complete, balanced voucher); a voucher with a protected labour line or a line on an employee detail account is a restricted item instead (visible to the readers of payroll only)
            var lines = await uow.Set<FnVoucherLine>().AsNoTracking().Where(l => ids.Contains(l.VoucherId)).OrderBy(l => l.VoucherId).ThenBy(l => l.RowNumber)
                .Select(l => new
                {
                    l.VoucherId, l.RowNumber, AccountCode = l.Account.Code, SepidarCode = l.Account.SepidarCode, Detail1 = l.Detail1.Code, Detail2 = l.Detail2.Code,
                    Employee = l.LabourProtected || (l.Detail1 != null && l.Detail1.Type == FnDetailType.Employee) || (l.Detail2 != null && l.Detail2.Type == FnDetailType.Employee),
                    l.Debit, l.Credit, l.Description, l.CurrencyCode, l.CurrencyAmount, l.Rate, l.Quantity,
                }).ToListAsync(ct);
            return heads.Select(h => new SpBuilt
            {
                Id = h.Id, Number = h.Number.Value.ToString(CultureInfo.InvariantCulture), Title = "سند حسابداری " + h.Number.Value.ToString(CultureInfo.InvariantCulture), RowVersion = h.RowVersion,
                Makers = SpPayload.Makers(h.EditorUserIds, h.CreatedByUserId, h.TriggeredByUserId),
                Restricted = lines.Any(l => l.VoucherId == h.Id && l.Employee) || (h.SourceType != null && h.SourceType.StartsWith("FnPayroll", StringComparison.Ordinal)),
                Data = new
                {
                    number = h.Number.Value, fiscalYear = h.Year, date = SpPayload.D(h.Date), type = h.Type.ToString(), description = h.Description, totalAmount = SpPayload.N(h.TotalAmount),
                    lines = lines.Where(l => l.VoucherId == h.Id).Select(l => new
                    {
                        row = l.RowNumber, accountCode = l.AccountCode, sepidarAccountCode = l.SepidarCode, detail1 = l.Detail1, detail2 = l.Detail2,
                        debit = SpPayload.N(l.Debit), credit = SpPayload.N(l.Credit), description = l.Description, currency = l.CurrencyCode, currencyAmount = SpPayload.N(l.CurrencyAmount), rate = SpPayload.N(l.Rate), quantity = SpPayload.N(l.Quantity),
                    }).ToList(),
                },
            }).ToList();
        }
    }

    internal sealed class SpSalesInvoiceSource : SpSource
    {
        public override string Code => SpTypeCodes.SalesInvoice;
        public override string Title => "فاکتور فروش (ثبت‌شده)";
        public override string SourceType => nameof(FnSalesInvoice);
        public override string Note => "فاکتور فروش در وضعیت «ثبت‌شده در دفتر». بسته‌های ماهانهٔ «صورت‌حساب فروش (مودیان)» و «اظهارنامه ارزش افزوده» همین داده را می‌برند.";
        public override FnOutboxKind[] OldKinds => new[] { FnOutboxKind.SalesInvoicesTaxpayer, FnOutboxKind.VatSeason };
        public override string OldKindsText => "بستهٔ ماهانهٔ «صورت‌حساب فروش (مودیان)» و «اظهارنامه ارزش افزوده»";

        public override IQueryable<SpRef> Final(IUnitOfWork uow) =>
            uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => i.Status == FnDocStatus.Posted).Select(i => new SpRef { Id = i.Id, RowVersion = i.RowVersion, At = i.ApprovedAt });

        public override async Task<List<SpBuilt>> BuildAsync(IUnitOfWork uow, IReadOnlyCollection<int> idSet, CancellationToken ct)
        {
            List<int> ids = Ids(idSet);
            var heads = await uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => ids.Contains(i.Id) && i.Status == FnDocStatus.Posted).OrderBy(i => i.Id)
                .Select(i => new
                {
                    i.Id, i.Number, i.Date, i.DueDate, i.Type, i.Description, i.CustomerId, i.Customer.EconomicCode, i.Customer.NationalId, i.Customer.PostalCode, i.NetAmount, i.DiscountAmount, i.VatAmount, i.GrossAmount,
                    i.AdvanceRecovery, i.PerformanceRetention, i.InsuranceRetention, i.Withholding, i.ReceivableAmount, i.TaxId, i.CreditedInvoiceId, i.CreatedByUserId, i.EditorUserIds, i.RowVersion,
                }).ToListAsync(ct);
            var lines = await uow.Set<FnSalesInvoiceLine>().AsNoTracking().Where(l => ids.Contains(l.InvoiceId)).OrderBy(l => l.InvoiceId).ThenBy(l => l.RowNumber)
                .Select(l => new { l.InvoiceId, l.RowNumber, l.ItemCode, l.Description, l.Quantity, l.Unit, l.UnitPrice, l.Discount, l.VatExempt, l.Amount, l.VatRatePercent, l.VatAmount, l.Total }).ToListAsync(ct);
            return heads.Select(h => new SpBuilt
            {
                Id = h.Id, Number = h.Number, Title = "فاکتور فروش " + h.Number, RowVersion = h.RowVersion, Makers = SpPayload.Makers(h.EditorUserIds, h.CreatedByUserId),
                Data = new
                {
                    number = h.Number, date = SpPayload.D(h.Date), dueDate = SpPayload.D(h.DueDate), type = h.Type.ToString(), description = h.Description,
                    customer = new { id = h.CustomerId, economicCode = h.EconomicCode, nationalId = h.NationalId, postalCode = h.PostalCode },
                    netAmount = SpPayload.N(h.NetAmount), discountAmount = SpPayload.N(h.DiscountAmount), vatAmount = SpPayload.N(h.VatAmount), grossAmount = SpPayload.N(h.GrossAmount),
                    advanceRecovery = SpPayload.N(h.AdvanceRecovery), performanceRetention = SpPayload.N(h.PerformanceRetention), insuranceRetention = SpPayload.N(h.InsuranceRetention), withholding = SpPayload.N(h.Withholding),
                    receivableAmount = SpPayload.N(h.ReceivableAmount), taxId = h.TaxId, creditedInvoiceId = h.CreditedInvoiceId,
                    lines = lines.Where(l => l.InvoiceId == h.Id).Select(l => new
                    {
                        row = l.RowNumber, itemCode = l.ItemCode, description = l.Description, quantity = SpPayload.N(l.Quantity), unit = l.Unit, unitPrice = SpPayload.N(l.UnitPrice), discount = SpPayload.N(l.Discount),
                        vatExempt = l.VatExempt, netAmount = SpPayload.N(l.Amount), vatRatePercent = SpPayload.N(l.VatRatePercent), vatAmount = SpPayload.N(l.VatAmount), total = SpPayload.N(l.Total),
                    }).ToList(),
                },
            }).ToList();
        }
    }

    internal sealed class SpPurchaseInvoiceSource : SpSource
    {
        public override string Code => SpTypeCodes.PurchaseInvoice;
        public override string Title => "فاکتور خرید (تاییدشده)";
        public override string SourceType => nameof(SupplierInvoice);
        public override string Note => "فاکتور تامین‌کننده در وضعیت «تاییدشده». بستهٔ ماهانهٔ «اظهارنامه ارزش افزوده» بخشی از همین داده را می‌برد.";
        public override FnOutboxKind[] OldKinds => new[] { FnOutboxKind.VatSeason };
        public override string OldKindsText => "بستهٔ ماهانهٔ «اظهارنامه ارزش افزوده»";

        public override IQueryable<SpRef> Final(IUnitOfWork uow) =>
            uow.Set<SupplierInvoice>().AsNoTracking().Where(i => !i.Deleted && i.Status == SupplierInvoiceStatus.Approved).Select(i => new SpRef { Id = i.Id, RowVersion = i.RowVersion, At = i.ApprovedAt });

        public override async Task<List<SpBuilt>> BuildAsync(IUnitOfWork uow, IReadOnlyCollection<int> idSet, CancellationToken ct)
        {
            List<int> ids = Ids(idSet);
            var heads = await uow.Set<SupplierInvoice>().AsNoTracking().Where(i => ids.Contains(i.Id) && !i.Deleted && i.Status == SupplierInvoiceStatus.Approved).OrderBy(i => i.Id)
                .Select(i => new
                {
                    i.Id, i.Number, i.InvoiceNumber, i.InvoiceDate, i.IsOfficial, i.ProviderId, OrderNumber = i.PurchaseOrder.Number, i.NetAmount, i.VatAmount, i.NonRecoverableVatAmount, i.TotalAmount, i.PriceVariance,
                    i.CreatorUserId, i.RowVersion,
                }).ToListAsync(ct);
            var lines = await uow.Set<SupplierInvoiceLine>().AsNoTracking().Where(l => ids.Contains(l.InvoiceId)).OrderBy(l => l.InvoiceId).ThenBy(l => l.Id)
                .Select(l => new { l.InvoiceId, l.Id, l.OrderLine.RowNumber, l.OrderLine.Description, l.OrderLine.Unit, l.Quantity, l.UnitPrice, l.NonRecoverableVat }).ToListAsync(ct);
            return heads.Select(h => new SpBuilt
            {
                Id = h.Id, Number = h.Number, Title = "فاکتور خرید " + h.Number, RowVersion = h.RowVersion, Makers = SpPayload.Makers(null, h.CreatorUserId),
                Data = new
                {
                    number = h.Number, supplierInvoiceNumber = h.InvoiceNumber, supplierInvoiceDate = SpPayload.D(h.InvoiceDate), isOfficial = h.IsOfficial, supplier = new { id = h.ProviderId }, orderNumber = h.OrderNumber,
                    netAmount = SpPayload.N(h.NetAmount), vatAmount = SpPayload.N(h.VatAmount), nonRecoverableVatAmount = SpPayload.N(h.NonRecoverableVatAmount), totalAmount = SpPayload.N(h.TotalAmount),
                    lines = lines.Where(l => l.InvoiceId == h.Id).Select((l, index) => new
                    {
                        row = index + 1, orderRow = l.RowNumber, description = l.Description, unit = l.Unit, quantity = SpPayload.N(l.Quantity), unitPrice = SpPayload.N(l.UnitPrice), nonRecoverableVat = l.NonRecoverableVat,
                    }).ToList(),
                },
            }).ToList();
        }
    }

    internal sealed class SpReceiptSource : SpSource
    {
        public override string Code => SpTypeCodes.Receipt;
        public override string Title => "دریافت وجه (ثبت‌شده)";
        public override string SourceType => nameof(FnReceipt);
        public override string Note => "دریافت در وضعیت «ثبت‌شده در دفتر»؛ شماره حساب بانکی منتقل نمی‌شود.";

        public override IQueryable<SpRef> Final(IUnitOfWork uow) =>
            uow.Set<FnReceipt>().AsNoTracking().Where(r => r.Status == FnDocStatus.Posted).Select(r => new SpRef { Id = r.Id, RowVersion = r.RowVersion, At = r.PostedAt });

        public override async Task<List<SpBuilt>> BuildAsync(IUnitOfWork uow, IReadOnlyCollection<int> idSet, CancellationToken ct)
        {
            List<int> ids = Ids(idSet);
            var heads = await uow.Set<FnReceipt>().AsNoTracking().Where(r => ids.Contains(r.Id) && r.Status == FnDocStatus.Posted).OrderBy(r => r.Id)
                .Select(r => new { r.Id, r.Number, r.Date, r.Description, r.PayerName, r.CustomerId, r.TotalAmount, r.CreatedByUserId, r.EditorUserIds, r.RowVersion }).ToListAsync(ct);
            var lines = await uow.Set<FnReceiptLine>().AsNoTracking().Where(l => ids.Contains(l.ReceiptId)).OrderBy(l => l.ReceiptId).ThenBy(l => l.RowNumber)
                .Select(l => new { l.ReceiptId, l.RowNumber, l.Instrument, l.Amount, l.Reference, l.ChequeSayadId, l.ChequeSerial, l.ChequeDueDate, l.CurrencyAmount, l.Rate }).ToListAsync(ct);
            return heads.Select(h => new SpBuilt
            {
                Id = h.Id, Number = h.Number, Title = "دریافت وجه " + h.Number, RowVersion = h.RowVersion, Makers = SpPayload.Makers(h.EditorUserIds, h.CreatedByUserId),
                Data = new
                {
                    number = h.Number, date = SpPayload.D(h.Date), description = h.Description, payer = h.PayerName, customerId = h.CustomerId, totalAmount = SpPayload.N(h.TotalAmount),
                    lines = lines.Where(l => l.ReceiptId == h.Id).Select(l => new
                    {
                        row = l.RowNumber, instrument = l.Instrument.ToString(), amount = SpPayload.N(l.Amount), reference = l.Reference, chequeSayadId = l.ChequeSayadId, chequeSerial = l.ChequeSerial,
                        chequeDueDate = SpPayload.D(l.ChequeDueDate), currencyAmount = SpPayload.N(l.CurrencyAmount), rate = SpPayload.N(l.Rate),
                    }).ToList(),
                },
            }).ToList();
        }
    }

    internal sealed class SpPaymentSource : SpSource
    {
        public override string Code => SpTypeCodes.Payment;
        public override string Title => "پرداخت وجه (ثبت‌شده)";
        public override string SourceType => nameof(FnPayment);
        public override string Note => "پرداخت در وضعیت «ثبت‌شده در دفتر»؛ پرداخت حقوق، وام و پاداش و تسویهٔ کارکنان و شماره شبا منتقل نمی‌شود.";

        // the payments to people (payroll, a loan or an advance, a bonus, an end-of-service settlement) belong to payroll and stay out: one of them is one person's pay

        public override IQueryable<SpRef> Final(IUnitOfWork uow) =>
            uow.Set<FnPayment>().AsNoTracking().Where(p => p.Status == FnDocStatus.Posted && (p.Purpose == FnPaymentPurpose.Supplier || p.Purpose == FnPaymentPurpose.PettyCashTopUp || p.Purpose == FnPaymentPurpose.FacilityLeg))
                .Select(p => new SpRef { Id = p.Id, RowVersion = p.RowVersion, At = p.PostedAt });

        public override async Task<List<SpBuilt>> BuildAsync(IUnitOfWork uow, IReadOnlyCollection<int> idSet, CancellationToken ct)
        {
            List<int> ids = Ids(idSet);
            var heads = await uow.Set<FnPayment>().AsNoTracking().Where(p => ids.Contains(p.Id) && p.Status == FnDocStatus.Posted && (p.Purpose == FnPaymentPurpose.Supplier || p.Purpose == FnPaymentPurpose.PettyCashTopUp || p.Purpose == FnPaymentPurpose.FacilityLeg))
                .OrderBy(p => p.Id).Select(p => new { p.Id, p.Number, p.Date, p.Purpose, p.PayeeName, p.Description, p.GrossAmount, p.DeductionAmount, p.NetAmount, p.CreatedByUserId, p.EditorUserIds, p.RowVersion }).ToListAsync(ct);
            var lines = await uow.Set<FnPaymentLine>().AsNoTracking().Where(l => ids.Contains(l.PaymentId)).OrderBy(l => l.PaymentId).ThenBy(l => l.RowNumber)
                .Select(l => new { l.PaymentId, l.RowNumber, l.Instrument, l.Amount, l.Reference, l.ChequeSayadId, l.ChequeDueDate, l.CurrencyAmount, l.Rate }).ToListAsync(ct);
            return heads.Select(h => new SpBuilt
            {
                Id = h.Id, Number = h.Number, Title = "پرداخت وجه " + h.Number, RowVersion = h.RowVersion, Makers = SpPayload.Makers(h.EditorUserIds, h.CreatedByUserId),
                Data = new
                {
                    number = h.Number, date = SpPayload.D(h.Date), purpose = h.Purpose.ToString(), payee = h.PayeeName, description = h.Description,
                    grossAmount = SpPayload.N(h.GrossAmount), deductionAmount = SpPayload.N(h.DeductionAmount), netAmount = SpPayload.N(h.NetAmount),
                    lines = lines.Where(l => l.PaymentId == h.Id).Select(l => new
                    {
                        row = l.RowNumber, instrument = l.Instrument.ToString(), amount = SpPayload.N(l.Amount), reference = l.Reference, chequeSayadId = l.ChequeSayadId, chequeDueDate = SpPayload.D(l.ChequeDueDate),
                        currencyAmount = SpPayload.N(l.CurrencyAmount), rate = SpPayload.N(l.Rate),
                    }).ToList(),
                },
            }).ToList();
        }
    }

    internal sealed class SpPayrollSummarySource : SpSource
    {
        public override string Code => SpTypeCodes.PayrollSummary;
        public override string Title => "خلاصه حقوق (اجرای ثبت‌شده)";
        public override string SourceType => nameof(FnPayrollRun);
        public override string Note => "فقط جمع‌های اجرای حقوق در وضعیت «ثبت‌شده» یا «پرداخت‌شده»؛ هیچ ردیف فردی منتقل نمی‌شود. همهٔ این اقلام محرمانه‌اند و فقط خوانندگان حقوق آن‌ها را می‌بینند. بسته‌های ماهانهٔ «لیست بیمه» و «لیست مالیات حقوق» همین موضوع را می‌برند.";
        public override FnOutboxKind[] OldKinds => new[] { FnOutboxKind.PayrollInsuranceList, FnOutboxKind.PayrollTaxList };
        public override string OldKindsText => "بستهٔ ماهانهٔ «لیست بیمه» و «لیست مالیات حقوق»";

        public override IQueryable<SpRef> Final(IUnitOfWork uow) =>
            uow.Set<FnPayrollRun>().AsNoTracking().Where(r => r.Status >= FnPayrollStatus.Posted).Select(r => new SpRef { Id = r.Id, RowVersion = r.RowVersion, At = r.PostedAt });

        public override async Task<List<SpBuilt>> BuildAsync(IUnitOfWork uow, IReadOnlyCollection<int> idSet, CancellationToken ct)
        {
            List<int> ids = Ids(idSet);
            var heads = await uow.Set<FnPayrollRun>().AsNoTracking().Where(r => ids.Contains(r.Id) && r.Status >= FnPayrollStatus.Posted).OrderBy(r => r.Id)
                .Select(r => new
                {
                    r.Id, r.Number, r.Year, r.Month, r.EmployeeCount, r.TotalGross, r.TotalInsurable, r.TotalTaxable, r.TotalEmployeeInsurance, r.TotalEmployerInsurance, r.TotalTax, r.TotalOtherDeductions, r.TotalNet, r.TotalProvisions,
                    r.CreatedByUserId, r.PreparedByUserId, r.PostedByUserId, r.EditorUserIds, r.RowVersion,
                }).ToListAsync(ct);
            return heads.Select(h => new SpBuilt
            {
                Id = h.Id, Number = h.Number, Title = "خلاصه حقوق " + h.Year.ToString("0000", CultureInfo.InvariantCulture) + "/" + h.Month.ToString("00", CultureInfo.InvariantCulture), RowVersion = h.RowVersion,
                Makers = SpPayload.Makers(h.EditorUserIds, h.CreatedByUserId, h.PreparedByUserId, h.PostedByUserId),
                // every payroll item is restricted (the old payroll packages are for the readers of payroll too): the totals of a run are pay data, whatever the number of people
                Restricted = true,
                Data = new
                {
                    number = h.Number, year = h.Year, month = h.Month, employeeCount = h.EmployeeCount,
                    totals = new
                    {
                        gross = SpPayload.N(h.TotalGross), insurable = SpPayload.N(h.TotalInsurable), taxable = SpPayload.N(h.TotalTaxable), employeeInsurance = SpPayload.N(h.TotalEmployeeInsurance),
                        employerInsurance = SpPayload.N(h.TotalEmployerInsurance), tax = SpPayload.N(h.TotalTax), otherDeductions = SpPayload.N(h.TotalOtherDeductions), net = SpPayload.N(h.TotalNet), provisions = SpPayload.N(h.TotalProvisions),
                    },
                },
            }).ToList();
        }
    }

    internal static class SpCatalog
    {
        public static readonly IReadOnlyList<SpSource> All = new SpSource[]
        {
            new SpVoucherSource(), new SpSalesInvoiceSource(), new SpPurchaseInvoiceSource(), new SpReceiptSource(), new SpPaymentSource(), new SpPayrollSummarySource(),
        };

        public static SpSource Find(string code) => string.IsNullOrWhiteSpace(code) ? null : All.FirstOrDefault(s => string.Equals(s.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>The statuses of an item that a person or the sender still has to deal with: one such item per document and kind.</summary>
        public static readonly SpOutboxStatus[] Live = { SpOutboxStatus.Draft, SpOutboxStatus.AwaitingApproval, SpOutboxStatus.Approved, SpOutboxStatus.Sending, SpOutboxStatus.Failed };

        public static string StatusTitle(SpOutboxStatus s)
        {
            switch (s)
            {
                case SpOutboxStatus.Draft: return "پیش‌نویس";
                case SpOutboxStatus.AwaitingApproval: return "منتظر تایید";
                case SpOutboxStatus.Approved: return "تاییدشده، آماده ارسال";
                case SpOutboxStatus.Sending: return "در حال ارسال";
                case SpOutboxStatus.Sent: return "ارسال‌شده";
                case SpOutboxStatus.Failed: return "ناموفق";
                case SpOutboxStatus.Rejected: return "ردشده";
                case SpOutboxStatus.Cancelled: return "لغوشده";
                case SpOutboxStatus.Superseded: return "جایگزین‌شده";
                default: return s.ToString();
            }
        }

        /// <summary>The seeded kinds: every one OFF until the owner enables it.</summary>
        public static async Task EnsureAsync(IUnitOfWork uow, CancellationToken ct)
        {
            List<string> have = await uow.Set<SpTransferType>().AsNoTracking().Select(t => t.Code).ToListAsync(ct);
            bool added = false;
            foreach (SpSource s in All)
            {
                if (have.Contains(s.Code)) continue;
                uow.Set<SpTransferType>().Add(new SpTransferType { Code = s.Code, Title = s.Title, IsEnabled = false, RequiresApproval = true, Note = s.Note });
                added = true;
            }
            if (!added) return;
            try { await uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { Agents.AgentDb.Clear(uow); }   // another process made the same rows at the same moment (the unique index of the code): they are read again by the caller
        }
    }
}
