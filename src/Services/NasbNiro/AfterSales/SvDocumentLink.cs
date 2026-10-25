using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.InventoryDtos;

namespace Services.NasbNiro.AfterSales
{
    /// <summary>A warehouse document that names a service order: the parts consumed (issue) or left on site (customer delivery).</summary>
    internal static class SvDocumentLink
    {
        /// <summary>Checks a draft document against its service order. Returns a Persian message, or null when it is fine.</summary>
        public static async Task<string> CheckDraftAsync(IUnitOfWork uow, SaveDocumentDto dto, CancellationToken ct)
        {
            if (!dto.ServiceOrderId.HasValue) return null;
            if (dto.Type != InventoryDocumentType.Issue && dto.Type != InventoryDocumentType.CustomerDelivery) return "دستور خدمات فقط در حواله مصرف یا سند تحویل به مشتری می‌آید.";
            if (dto.SalesOrderId.HasValue) return "سند یا برای دستور خدمات است یا برای سفارش فروش، نه هر دو.";
            if (dto.Type == InventoryDocumentType.CustomerDelivery && dto.ContractId.HasValue) return "تحویل یا برای قرارداد است یا برای دستور خدمات، نه هر دو.";
            var order = await uow.Set<SvServiceOrder>().AsNoTracking().Where(o => o.Id == dto.ServiceOrderId.Value && !o.Deleted).Select(o => new { o.Number, o.Status, o.CostCenterCode }).FirstOrDefaultAsync(ct);
            if (order == null) return "دستور خدمات پیدا نشد.";
            if (order.Status != SvOrderStatus.Planned && order.Status != SvOrderStatus.InProgress)
                return $"دستور خدمات {order.Number} در وضعیت «{SvText.OrderStatus(order.Status)}» است و سند قطعات ندارد.";
            // the parts of an issue are charged to the after-sales cost centre of the order
            if (dto.Type == InventoryDocumentType.Issue && string.IsNullOrWhiteSpace(dto.CostCenter)) dto.CostCenter = order.CostCenterCode;
            return null;
        }

        /// <summary>Confirming a parts document: the order must still be planned or in progress (its cost is posted when the order closes).</summary>
        public static async Task<string> CheckConfirmAsync(IUnitOfWork uow, InventoryDocument document, CancellationToken ct)
        {
            if (!document.ServiceOrderId.HasValue) return null;
            var order = await uow.Set<SvServiceOrder>().AsNoTracking().Where(o => o.Id == document.ServiceOrderId.Value).Select(o => new { o.Number, o.Status }).FirstOrDefaultAsync(ct);
            return order == null ? "دستور خدماتِ این سند پیدا نشد." : SvDocumentRules.CheckConfirm(order.Status, order.Number);
        }

        /// <summary>Cancelling a confirmed parts document: refused once the order is closed (its cost and event were posted from these documents).</summary>
        public static async Task<string> CheckCancelAsync(IUnitOfWork uow, InventoryDocument document, CancellationToken ct)
        {
            if (!document.ServiceOrderId.HasValue) return null;
            var order = await uow.Set<SvServiceOrder>().AsNoTracking().Where(o => o.Id == document.ServiceOrderId.Value).Select(o => new { o.Number, o.Status }).FirstOrDefaultAsync(ct);
            return order == null ? null : SvDocumentRules.CheckCancel(order.Status, order.Number);
        }
    }
}
