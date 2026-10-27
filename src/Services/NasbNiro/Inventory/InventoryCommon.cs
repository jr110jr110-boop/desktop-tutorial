using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.IdentityToolkit;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.Inventory
{
    /// <summary>The signed-in user, for "who did it" on every warehouse and traceability record.</summary>
    public interface IInventoryUserContext
    {
        int UserId { get; }
        bool IsInRole(string role);
    }

    public class InventoryUserContext : IInventoryUserContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public InventoryUserContext(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        }

        public int UserId =>
            int.TryParse(_httpContextAccessor.HttpContext?.User?.Identity?.GetUserId(), out int id) ? id : 0;

        /// <summary>One role, or several separated by commas (any of them).</summary>
        public bool IsInRole(string role) =>
            !string.IsNullOrWhiteSpace(role) && role.Split(',').Any(r => _httpContextAccessor.HttpContext?.User?.IsInRole(r.Trim()) == true);
    }

    /// <summary>Role names (as written in the JWT) allowed to change each part of the module.</summary>
    public static class InventoryRoles
    {
        public const string MasterData = "PMO,PLANNING,WAREHOUSE,WHSUPERVISOR,ENGINEERING";
        public const string Documents = "PMO,WAREHOUSE,WHSUPERVISOR";

        /// <summary>Stock counts: the warehouse counts, the supervisor and planning approve.</summary>
        public const string Counts = "PMO,WAREHOUSE,WHSUPERVISOR,PLANNING";
        public const string Production = "PMO,WAREHOUSE,ENGINEERING,ENGINEER,ASSEMBLER,QC";
        public const string Routing = "PMO,ENGINEERING";
        public const string Qc = "PMO,QC";
        public const string Finance = "PMO,FINANCIAL,FINANCEMANAGER";

        /// <summary>Who may write a material request: production and planning.</summary>
        public const string MaterialRequest = "PMO,PLANNING,CONSTRUCTION,ENGINEER,ASSEMBLER,ENGINEERING,WAREHOUSE,WHSUPERVISOR,PRODUCTIONMANAGER,SHOPSUPERVISOR";

        /// <summary>Approvals of material requests (planning or manufacturing) and of NCRs (quality, engineering, manufacturing).</summary>
        public const string Approvers = "PMO,PLANNING,WHSUPERVISOR,CONSTRUCTION,QC,ENGINEERING,WAREHOUSE,FINANCIAL,PRODUCTIONMANAGER";

        /// <summary>Exit check of goods leaving the premises (guard).</summary>
        public const string ExitCheck = "PMO,WAREHOUSE,WHSUPERVISOR,SECURITY";

        /// <summary>Requests for new item codes: engineering asks, planning approves.</summary>
        public const string ItemRequest = "PMO,PLANNING,ENGINEERING,ENGINEER,WAREHOUSE,WHSUPERVISOR,QC,CONSTRUCTION";

        /// <summary>WI-WH-002: planning and project control gives the codes and keeps the coding tables.</summary>
        public const string Planning = "PMO,PLANNING";

        // single roles used for the approvals
        // single roles used for the approvals (decision 1404-07-24: adjustments and counts need the warehouse supervisor
        // and the planning and project control manager, never the storekeeper who prepared them)
        public const string RolePlanning = "PLANNING,PMO";
        public const string RoleWarehouse = "WAREHOUSE,WHSUPERVISOR";
        public const string RoleWarehouseSupervisor = "WHSUPERVISOR";
        public const string RoleManufacturing = "CONSTRUCTION,PRODUCTIONMANAGER";
        public const string RoleQuality = "QC";
        public const string RoleEngineering = "ENGINEERING";

        /// <summary>
        /// R1q D3 ★: the QC signature of an NCR comes from a QC user or the QA manager. The person who raised or edited the NCR cannot give it, so
        /// a company with one QC user is not stuck: QA can sign.
        /// </summary>
        public const string RoleQualitySigner = "QC,QA";

        /// <summary>Close or cancel an NCR (QC and QA only, review L24: PMO reads NCRs but does not close them; who may do which is decided inside, with the maker-checker rule).</summary>
        public const string QcOrQa = "QC,QA";

        /// <summary>The NCR approval endpoint: the approvers of the module and the QA manager, who may give the QC signature.</summary>
        public const string NcrApprovers = Approvers + ",QA";

        /// <summary>
        /// R1q D11: the quality fields of an item (inspection, certificate, shelf life) are changed by QC, QA or PMO only; the storekeepers and planners
        /// who maintain the master data cannot switch a quality gate off.
        /// </summary>
        public const string ItemQuality = "PMO,QC,QA";
    }

    public static class InventoryDocumentRules
    {
        /// <summary>Purchase receipt and supplier return may reference a PMIS purchase order.</summary>
        public static bool UsesPurchaseOrder(InventoryDocumentType type) =>
            type == InventoryDocumentType.PurchaseReceipt || type == InventoryDocumentType.SupplierReturn;

        public static bool IsIncoming(InventoryDocumentType type) =>
            type == InventoryDocumentType.PurchaseReceipt ||
            type == InventoryDocumentType.ProductionReceipt ||
            type == InventoryDocumentType.ProductionReturn ||
            type == InventoryDocumentType.AdjustmentIncrease ||
            type == InventoryDocumentType.ScrapReceipt ||
            type == InventoryDocumentType.OpeningBalance ||
            type == InventoryDocumentType.CustomerPropertyReceipt;

        public static bool IsOutgoing(InventoryDocumentType type) =>
            type == InventoryDocumentType.Issue ||
            type == InventoryDocumentType.SupplierReturn ||
            type == InventoryDocumentType.AdjustmentDecrease ||
            type == InventoryDocumentType.CustomerDelivery ||
            type == InventoryDocumentType.ScrapSale;

        public static bool IsTransfer(InventoryDocumentType type) =>
            type == InventoryDocumentType.Transfer ||
            type == InventoryDocumentType.SubcontractIssue ||
            type == InventoryDocumentType.SubcontractReturn ||
            type == InventoryDocumentType.QcRelease;

        public static bool IsConversion(InventoryDocumentType type) => type == InventoryDocumentType.StageConversion;

        public static bool IsKnown(InventoryDocumentType type) => IsIncoming(type) || IsOutgoing(type) || IsTransfer(type) || IsConversion(type);

        /// <summary>Made only by the system (inspection or NCR), never typed by a user.</summary>
        public static bool IsSystemOnly(InventoryDocumentType type) => type == InventoryDocumentType.QcRelease;

        /// <summary>Goods leave the premises: driver, vehicle and receiver are written and the guard checks the exit.</summary>
        public static bool LeavesPremises(InventoryDocumentType type) =>
            type == InventoryDocumentType.SupplierReturn ||
            type == InventoryDocumentType.SubcontractIssue ||
            type == InventoryDocumentType.CustomerDelivery ||
            type == InventoryDocumentType.ScrapSale;

        /// <summary>Needs the warehouse supervisor and the planning manager to approve before it is confirmed.</summary>
        public static bool NeedsApprovals(InventoryDocumentType type) =>
            type == InventoryDocumentType.AdjustmentIncrease || type == InventoryDocumentType.AdjustmentDecrease ||
            type == InventoryDocumentType.OpeningBalance;

        /// <summary>Status of a serial unit that leaves the stock with this document.</summary>
        public static SerialUnitStatus OutgoingStatus(InventoryDocumentType type)
        {
            switch (type)
            {
                case InventoryDocumentType.Issue: return SerialUnitStatus.InProduction;
                case InventoryDocumentType.SupplierReturn: return SerialUnitStatus.ReturnedToSupplier;
                case InventoryDocumentType.CustomerDelivery: return SerialUnitStatus.Delivered;
                case InventoryDocumentType.StageConversion: return SerialUnitStatus.Converted;
                default: return SerialUnitStatus.Scrapped;
            }
        }

        public static string NumberPrefix(InventoryDocumentType type)
        {
            switch (type)
            {
                case InventoryDocumentType.PurchaseReceipt: return "RC";
                case InventoryDocumentType.ProductionReceipt: return "RM";
                case InventoryDocumentType.ProductionReturn: return "RT";
                case InventoryDocumentType.Issue: return "IS";
                case InventoryDocumentType.SupplierReturn: return "SR";
                case InventoryDocumentType.Transfer: return "TR";
                case InventoryDocumentType.AdjustmentIncrease: return "AI";
                case InventoryDocumentType.AdjustmentDecrease: return "AD";
                case InventoryDocumentType.SubcontractIssue: return "SI";
                case InventoryDocumentType.SubcontractReturn: return "SB";
                case InventoryDocumentType.CustomerDelivery: return "CD";
                case InventoryDocumentType.StageConversion: return "SC";
                case InventoryDocumentType.ScrapReceipt: return "WR";
                case InventoryDocumentType.ScrapSale: return "WS";
                case InventoryDocumentType.OpeningBalance: return "OB";
                case InventoryDocumentType.QcRelease: return "QR";
                case InventoryDocumentType.CustomerPropertyReceipt: return "CP";
                default: return "DC";
            }
        }

        public static string Title(InventoryDocumentType type)
        {
            switch (type)
            {
                case InventoryDocumentType.PurchaseReceipt: return "رسید خرید";
                case InventoryDocumentType.ProductionReceipt: return "رسید ساخت";
                case InventoryDocumentType.ProductionReturn: return "برگشت از تولید";
                case InventoryDocumentType.Issue: return "حواله";
                case InventoryDocumentType.SupplierReturn: return "برگشت به تامین‌کننده";
                case InventoryDocumentType.Transfer: return "انتقال";
                case InventoryDocumentType.AdjustmentIncrease: return "تعدیل افزایشی";
                case InventoryDocumentType.AdjustmentDecrease: return "تعدیل کاهشی";
                case InventoryDocumentType.SubcontractIssue: return "ارسال به پیمانکار";
                case InventoryDocumentType.SubcontractReturn: return "برگشت از پیمانکار";
                case InventoryDocumentType.CustomerDelivery: return "تحویل به مشتری";
                case InventoryDocumentType.StageConversion: return "تبدیل مرحله";
                case InventoryDocumentType.ScrapReceipt: return "رسید ضایعات";
                case InventoryDocumentType.ScrapSale: return "فروش ضایعات";
                case InventoryDocumentType.OpeningBalance: return "موجودی اول دوره";
                case InventoryDocumentType.QcRelease: return "ترخیص از قرنطینه";
                case InventoryDocumentType.CustomerPropertyReceipt: return "رسید اموال مشتری";
                default: return type.ToString();
            }
        }
    }

    /// <summary>Running numbers of documents, requests and labels, one counter per kind and Persian year.</summary>
    internal static class InventoryNumbers
    {
        // counter kinds that are not warehouse document types (those use their own type number)
        public const int Serial = 99;
        public const int Label = 98;
        public const int Inspection = 97;
        public const int Ncr = 96;
        public const int MaterialRequest = 95;
        public const int StockCount = 94;
        public const int ItemRequest = 93;

        public static async Task<int> NextAsync(IUnitOfWork uow, int kind, int year, CancellationToken cancellationToken)
        {
            DbSet<DocumentNumberSequence> sequences = uow.Set<DocumentNumberSequence>();
            DocumentNumberSequence sequence = sequences.Local.FirstOrDefault(s => s.Type == kind && s.Year == year)
                ?? await sequences.FirstOrDefaultAsync(s => s.Type == kind && s.Year == year, cancellationToken);
            if (sequence == null)
            {
                sequence = new DocumentNumberSequence { Type = kind, Year = year, LastNumber = 0 };
                sequences.Add(sequence);
            }
            sequence.LastNumber++;
            return sequence.LastNumber;
        }

        /// <summary>For example "NCR-1405-00012".</summary>
        public static async Task<string> NextNumberAsync(IUnitOfWork uow, int kind, string prefix, DateTime date, CancellationToken cancellationToken)
        {
            int year = InventoryText.PersianYear(date);
            int number = await NextAsync(uow, kind, year, cancellationToken);
            return $"{prefix}-{year}-{number:00000}";
        }

        /// <summary>
        /// Company label number YYDDDNNNN: two digits of the Persian year, the day of the year and a daily counter
        /// (the format already printed on the warehouse labels).
        /// </summary>
        public static async Task<List<string>> NextLabelsAsync(IUnitOfWork uow, DateTime date, int count, CancellationToken cancellationToken)
        {
            var calendar = new PersianCalendar();
            int yy = calendar.GetYear(date) % 100;
            int ddd = calendar.GetDayOfYear(date);
            int day = yy * 1000 + ddd; // one counter per day
            var labels = new List<string>();
            for (int i = 0; i < count; i++)
            {
                int n = await NextAsync(uow, Label, day, cancellationToken);
                labels.Add(string.Format(CultureInfo.InvariantCulture, "{0:00}{1:000}{2:0000}", yy, ddd, n));
            }
            return labels;
        }
    }

    internal static class InventoryText
    {
        public static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        public static int PersianYear(DateTime date) => new PersianCalendar().GetYear(date);

        public static string PersianDate(DateTime date)
        {
            var calendar = new PersianCalendar();
            return string.Format(CultureInfo.InvariantCulture, "{0:0000}/{1:00}/{2:00}",
                calendar.GetYear(date), calendar.GetMonth(date), calendar.GetDayOfMonth(date));
        }

        public static string Number(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>Serial items move in whole pieces.</summary>
        public static bool IsWhole(decimal value) => value == decimal.Truncate(value);
    }

    internal static class InventoryUsers
    {
        public static async Task<Dictionary<int, string>> GetNamesAsync(
            IUnitOfWork uow, IEnumerable<int?> userIds, CancellationToken cancellationToken)
        {
            List<int> ids = userIds.Where(id => id.HasValue && id.Value > 0).Select(id => id.Value).Distinct().ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<int, string>();
            }

            var users = await uow.Set<User>()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.Name, u.LastName, u.UserName })
                .ToListAsync(cancellationToken);

            return users.ToDictionary(
                u => u.Id,
                u =>
                {
                    string name = $"{u.Name} {u.LastName}".Trim();
                    return string.IsNullOrEmpty(name) ? u.UserName : name;
                });
        }

        public static string Name(this Dictionary<int, string> names, int? userId) =>
            userId.HasValue && names.TryGetValue(userId.Value, out string name) ? name : null;
    }

    internal static class InventoryMessages
    {
        public const string InvalidData = "اطلاعات ارسال‌شده کامل نیست.";
        public const string CodeRequired = "کد را وارد کنید.";
        public const string TitleRequired = "عنوان را وارد کنید.";
        public const string UnitRequired = "واحد را وارد کنید.";
        public const string DuplicateCode = "این کد قبلاً ثبت شده است: ";
        public const string NotFound = "پیدا نشد.";
        public const string GroupNotFound = "گروه کالا پیدا نشد.";
        public const string ItemNotFound = "کالا پیدا نشد.";
        public const string WarehouseNotFound = "انبار پیدا نشد.";
        public const string LocationNotFound = "محل انبار پیدا نشد.";
        public const string DocumentNotFound = "سند انبار پیدا نشد.";
        public const string OnlyDraftEditable = "فقط سند پیش‌نویس قابل ویرایش یا حذف است.";
        public const string ConcurrencyConflict = "اطلاعات هم‌زمان توسط کاربر دیگری تغییر کرد. صفحه را دوباره باز کنید و دوباره تلاش کنید.";
        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string SameUser = "تایید باید توسط شخص دیگری غیر از ثبت‌کننده یا تاییدکننده قبلی انجام شود.";
    }
}
