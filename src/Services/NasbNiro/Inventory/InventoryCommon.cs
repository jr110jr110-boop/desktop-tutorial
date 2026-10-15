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

        public bool IsInRole(string role) => _httpContextAccessor.HttpContext?.User?.IsInRole(role) == true;
    }

    /// <summary>Role names (as written in the JWT) allowed to change each part of the module.</summary>
    public static class InventoryRoles
    {
        public const string MasterData = "PMO,WAREHOUSE,ENGINEERING";
        public const string Documents = "PMO,WAREHOUSE";
        public const string Production = "PMO,WAREHOUSE,ENGINEERING,ENGINEER,ASSEMBLER,QC";
        public const string Routing = "PMO,ENGINEERING";
        public const string Qc = "PMO,QC";
        public const string Finance = "PMO,FINANCIAL";
    }

    public static class InventoryDocumentRules
    {
        public static bool IsIncoming(InventoryDocumentType type) =>
            type == InventoryDocumentType.PurchaseReceipt ||
            type == InventoryDocumentType.ProductionReceipt ||
            type == InventoryDocumentType.ProductionReturn ||
            type == InventoryDocumentType.AdjustmentIncrease;

        public static bool IsOutgoing(InventoryDocumentType type) =>
            type == InventoryDocumentType.Issue ||
            type == InventoryDocumentType.SupplierReturn ||
            type == InventoryDocumentType.AdjustmentDecrease;

        public static bool IsTransfer(InventoryDocumentType type) => type == InventoryDocumentType.Transfer;

        public static bool IsKnown(InventoryDocumentType type) => IsIncoming(type) || IsOutgoing(type) || IsTransfer(type);

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
                default: return type.ToString();
            }
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
    }
}
