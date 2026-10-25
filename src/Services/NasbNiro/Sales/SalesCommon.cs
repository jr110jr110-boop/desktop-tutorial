using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.ProjectControl;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Sales
{
    public static class SalesRoles
    {
        public const string Sales = "SALES";
        public const string Service = "SERVICE";
        public const string Ceo = "CEO";
        public const string MasterWriters = "SALES,PMO";
        public const string Writers = "SALES";
        public const string ActivityWriters = "SALES,SERVICE";
        public const string Readers = "SALES,CEO,PMO,COMMERCIAL,FINANCEMANAGER,FINANCIAL,PROJECTMANAGER,PLANNING,AUDITOR,SERVICE";
        public const string CommercialReaders = "SALES,CEO,PMO,COMMERCIAL,FINANCEMANAGER,AUDITOR";
        public const string ProposalApprovers = "SALES,CEO";
        /// <summary>Price lists are approved by someone who did not write them.</summary>
        public const string PriceListApprovers = "SALES,CEO,FINANCEMANAGER";

        /// <summary>The credit limit is set by the finance manager only (the same person who approves an order above it).</summary>
        public const string CreditSetters = "FINANCEMANAGER";
        public const string CreditViewers = "FINANCEMANAGER,FINANCIAL";
        public const string IdentityChangers = "FINANCEMANAGER,FINANCIAL";
        public const string CreditApprovers = "FINANCEMANAGER";
        public const string ContractMakers = "SALES,CEO";
        public const string SettingSetters = "CEO";
        public const string LegacyLinkers = "SALES,PMO,CEO";
    }

    internal static class SlNumbers
    {
        public const int Inquiry = 501;
        public const int Proposal = 503;
        public const int Quotation = 504;
        public const int SalesOrder = 505;
        public const int PriceList = 506;
        public const int Contract = 507;
        public const int Tender = 508;
    }

    public static class SlSettingKeys
    {
        public const string ProposalApprovalThreshold = "ProposalApprovalThresholdRial";
    }

    internal static class SlText
    {
        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string InvalidData = "اطلاعات ارسال‌شده کامل نیست.";
        public const string Concurrency = "اطلاعات هم‌زمان توسط کاربر دیگری تغییر کرد. صفحه را دوباره باز کنید و دوباره تلاش کنید.";
        public const string SameUser = "این کار باید توسط شخص دیگری انجام شود (تفکیک وظایف).";

        public static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        public static ResponseDto<T> Bad<T>(string message) => ResponseDtoHelper.BadRequest<T>(message);
        public static ResponseDto<T> NotFound<T>(string message) => ResponseDtoHelper.NotFound<T>(message);

        /// <summary>Rials of documents are whole numbers.</summary>
        public static decimal R0(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

        /// <summary>One document line in whole rials, the way the finance invoice computes it: gross = quantity x price, discount = gross x discount%, amount = gross - discount.</summary>
        public static (decimal Gross, decimal Discount, decimal Amount) LineAmount(decimal quantity, decimal unitPrice, decimal discountPercent)
        {
            decimal gross = R0(quantity * unitPrice);
            decimal discount = R0(gross * discountPercent / 100m);
            return (gross, discount, gross - discount);
        }

        public static decimal R2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        public static string Date(DateTime? d) => d.HasValue ? InventoryText.PersianDate(d.Value) : null;
        public static string Date(DateTimeOffset? d) => d.HasValue ? InventoryText.PersianDate(d.Value.LocalDateTime) : null;
        public static string Stamp(DateTimeOffset? d) => d.HasValue ? InventoryText.PersianDate(d.Value.LocalDateTime) + " " + d.Value.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) : null;
        public static string Number(decimal value) => InventoryText.Number(value);
        public static string Money(decimal value) => value.ToString("#,##0.##", CultureInfo.InvariantCulture);

        public static int Size(int pageSize) => pageSize <= 0 ? 25 : Math.Min(pageSize, 500);

        public static async Task<PagedList<T>> PageAsync<T>(IQueryable<T> rows, int pageNumber, int pageSize, CancellationToken ct) =>
            await PagedList<T>.CreateAsync(rows, pageNumber, Size(pageSize), ct);

        public static async Task<PagedList<T>> PageAsync<T>(List<T> rows, int pageNumber, int pageSize, CancellationToken ct) =>
            await PagedList<T>.CreateAsync(rows, pageNumber, Size(pageSize), ct);

        /// <summary>A page cut in the database (ordered query), mapped to DTOs in memory.</summary>
        public static async Task<PagedList<TDto>> CutAsync<TRow, TDto>(IQueryable<TRow> ordered, int pageNumber, int pageSize, Func<List<TRow>, Task<List<TDto>>> map, CancellationToken ct)
        {
            int total = await ordered.CountAsync(ct);
            int size = Size(pageSize);
            int number = pageNumber <= 0 ? 1 : pageNumber;
            List<TRow> rows = await ordered.Skip((number - 1) * size).Take(size).ToListAsync(ct);
            List<TDto> dtos = await map(rows);
            PagedList<TDto> page = await PagedList<TDto>.CreateAsync(dtos, 1, 0, ct);
            page.TotalCount = total;
            page.CurrentPage = number;
            page.PageSize = size;
            page.TotalPages = (int)Math.Ceiling(total / (double)Math.Max(size, 1));
            return page;
        }

        public static string Product(SlProductType type) => type switch
        {
            SlProductType.SteamTurbine => "توربین بخار",
            SlProductType.OrcUnit => "واحد ORC",
            SlProductType.Condenser => "کندانسور",
            SlProductType.Pump => "پمپ",
            SlProductType.LubeOilSystem => "سیستم روغن روانکاری",
            SlProductType.Valve => "شیر",
            SlProductType.SpareParts => "قطعات یدکی",
            SlProductType.Service => "خدمات",
            _ => "سایر",
        };

        public static string InquiryStatus(SlInquiryStatus status) => status switch
        {
            SlInquiryStatus.Received => "دریافت‌شده",
            SlInquiryStatus.UnderReview => "در حال بررسی",
            SlInquiryStatus.NoBid => "عدم شرکت",
            SlInquiryStatus.Bidding => "در حال تهیه پیشنهاد",
            SlInquiryStatus.Submitted => "ارسال‌شده",
            SlInquiryStatus.Won => "برنده",
            SlInquiryStatus.Lost => "بازنده",
            _ => "لغوشده",
        };

        public static string ProposalStatus(SlProposalStatus status) => status switch
        {
            SlProposalStatus.Draft => "پیش‌نویس",
            SlProposalStatus.Approved => "تأییدشده",
            SlProposalStatus.Submitted => "ارسال‌شده",
            SlProposalStatus.Revised => "بازنگری‌شده",
            SlProposalStatus.Won => "برنده",
            SlProposalStatus.Lost => "بازنده",
            _ => "لغوشده",
        };

        public static string OrderStatus(SlOrderStatus status) => status switch
        {
            SlOrderStatus.Draft => "پیش‌نویس",
            SlOrderStatus.Confirmed => "تأییدشده",
            SlOrderStatus.PartlyDelivered => "تحویل بخشی",
            SlOrderStatus.Delivered => "تحویل‌شده",
            SlOrderStatus.Invoiced => "فاکتورشده",
            SlOrderStatus.Closed => "بسته‌شده",
            _ => "لغوشده",
        };

        /// <summary>Exact comparison key of a company name: Arabic/Persian letter variants, digits, joiners, spaces and punctuation are made equal.</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var sb = new StringBuilder(text.Length);
            foreach (char raw in text)
            {
                char c = raw;
                if (c == '\u064a' || c == '\u0649') c = '\u06cc';
                else if (c == '\u0643') c = '\u06a9';
                else if (c >= '\u06f0' && c <= '\u06f9') c = (char)('0' + (c - '\u06f0'));
                else if (c >= '\u0660' && c <= '\u0669') c = (char)('0' + (c - '\u0660'));
                if (c == '\u200c' || c == '\u200d' || c == '\u0640' || (c >= '\u064b' && c <= '\u0652')) continue;
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || c == '\u060c' || c == '\u061b') continue;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        /// <summary>Catalogue products are a standard product for the contract review; engineered-to-order ones are special orders.</summary>
        public static PcReviewKind ReviewKind(SlProductType type) =>
            type == SlProductType.Pump || type == SlProductType.Valve || type == SlProductType.SpareParts
                ? PcReviewKind.StandardProduct : PcReviewKind.SpecialOrder;

        public static PcCategory Category(SlProductType type) =>
            type == SlProductType.SpareParts ? PcCategory.SpareParts
            : type == SlProductType.Service ? PcCategory.Services : PcCategory.Manufacturing;
    }

    internal static class SlConcurrency
    {
        public static bool Apply<T>(IUnitOfWork uow, T entity, string version) where T : class
        {
            if (string.IsNullOrWhiteSpace(version)) return true;
            try
            {
                uow.Entry(entity).Property("RowVersion").OriginalValue = Convert.FromBase64String(version);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static string Text(byte[] version) => version == null ? null : Convert.ToBase64String(version);
    }

    internal static class SlSettings
    {
        public static async Task<decimal?> GetAsync(IUnitOfWork uow, string key, CancellationToken ct) =>
            await uow.Set<SlSetting>().AsNoTracking().Where(s => s.Key == key && !s.Deleted).Select(s => s.Value).FirstOrDefaultAsync(ct);
    }

    internal static class SlReceivables
    {
        /// <summary>Open receivable of a customer in rials (posted invoices, credit notes and receipts considered): what the credit-limit test counts.</summary>
        public static async Task<decimal> OpenAsync(IUnitOfWork uow, int customerId, CancellationToken ct)
        {
            Dictionary<int, decimal> all = await OpenByCustomerAsync(uow, new List<int> { customerId }, ct);
            return all.TryGetValue(customerId, out decimal v) ? v : 0;
        }

        public static async Task<Dictionary<int, decimal>> OpenByCustomerAsync(IUnitOfWork uow, List<int> customerIds, CancellationToken ct)
        {
            var invoices = await uow.Set<FnSalesInvoice>().AsNoTracking()
                .Where(i => customerIds.Contains(i.CustomerId) && i.Status == FnDocStatus.Posted && i.Type != FnInvoiceType.CreditNote).Select(i => new { i.Id, i.CustomerId }).ToListAsync(ct);
            Dictionary<int, decimal> open = await ReceivableMath.OpenAsync(uow, invoices.Select(i => i.Id).ToList(), null, ct);
            return invoices.GroupBy(i => i.CustomerId).ToDictionary(g => g.Key, g => g.Sum(i => open.TryGetValue(i.Id, out decimal o) ? o : 0));
        }
    }

    /// <summary>The credit-limit decision (pure): open receivables plus the order, against the limit finance set.</summary>
    internal static class SlCredit
    {
        public class Result
        {
            public bool NeedsApproval;
            public string Reason;
            public decimal? Excess;
        }

        /// <param name="limit">Null: finance has not set a limit, so every order needs the finance manager.</param>
        /// <param name="open">Open receivables of the customer including VAT.</param>
        /// <param name="orderAmount">The order including VAT.</param>
        public static Result Evaluate(decimal? limit, decimal open, decimal orderAmount)
        {
            if (!limit.HasValue)
                return new Result { NeedsApproval = true, Reason = "سقف اعتبار این مشتری را مالی تعیین نکرده است؛ تأیید مدیر مالی لازم است.", Excess = null };
            decimal total = open + orderAmount;
            if (total > limit.Value)
            {
                decimal excess = total - limit.Value;
                return new Result
                {
                    NeedsApproval = true, Excess = excess,
                    Reason = $"مطالبات باز مشتری ({SlText.Money(open)} ریال) و این سفارش ({SlText.Money(orderAmount)} ریال با مالیات) از سقف اعتبار ({SlText.Money(limit.Value)} ریال) به اندازه {SlText.Money(excess)} ریال بیشتر است؛ تأیید مدیر مالی لازم است.",
                };
            }
            return new Result { NeedsApproval = false };
        }
    }

    /// <summary>Stock held for confirmed sales orders: it is not "available" for the plant.</summary>
    internal static class SlReservations
    {
        public static async Task<Dictionary<int, decimal>> ReservedAsync(IUnitOfWork uow, IEnumerable<int> itemIds, CancellationToken ct)
        {
            List<int> ids = itemIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, decimal>();
            var rows = await uow.Set<SlSalesOrder>().AsNoTracking()
                .Where(o => !o.Deleted && (o.Status == SlOrderStatus.Confirmed || o.Status == SlOrderStatus.PartlyDelivered))
                .SelectMany(o => o.Lines).Where(l => ids.Contains(l.ItemId) && l.ReservedQuantity > 0)
                .Select(l => new { l.ItemId, l.ReservedQuantity }).ToListAsync(ct);
            return rows.GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.Sum(r => r.ReservedQuantity));
        }
    }
}