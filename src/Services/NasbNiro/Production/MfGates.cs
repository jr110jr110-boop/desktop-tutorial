using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Production;
using Services.NasbNiro.Office;

namespace Services.NasbNiro.Production
{
    /// <summary>Inbox items of the production gates (they open while a gate waits for a person and close when it does not).</summary>
    internal static class MfGates
    {
        public static Task OrderAsync(INotificationService notify, MfProductionOrder order, CancellationToken ct) =>
            OfficeGates.SyncAsync(notify, "mf:order:" + order.Id, order.Status == MfOrderStatus.Draft && !order.Deleted, "production", "order", order.Id,
                $"دستور تولید {order.Number} منتظر صدور است", "/production/orders/" + order.Id, ProductionRoles.Supervisors, ct);

        public static Task QcAsync(INotificationService notify, MfOperation op, string orderNumber, CancellationToken ct) =>
            OfficeGates.SyncAsync(notify, "mf:qc:" + op.Id, op.Status == MfOperationStatus.QcHold && !op.QcAccepted && !op.Deleted, "production", "qc-request", op.Id,
                $"درخواست بازرسی عملیات {MfNames.Operation(orderNumber, op.Sequence)} ({op.Title})", "/production/operations/" + op.Id, ProductionRoles.Inspectors, ct);

        public static Task MaintenanceAsync(INotificationService notify, MfMaintenanceRequest request, string machineTitle, CancellationToken ct) =>
            OfficeGates.SyncAsync(notify, "mf:maint:" + request.Id, request.Status == MfMaintenanceRequestStatus.Open && !request.Deleted, "production", "maintenance-request", request.Id,
                $"خرابی ماشین {machineTitle} گزارش شد ({request.Number})", "/production/maintenance-requests/" + request.Id, ProductionRoles.Maintenance, ct);

        public static Task MaintenanceOrderAsync(INotificationService notify, MfMaintenanceOrder order, string machineTitle, CancellationToken ct) =>
            OfficeGates.SyncAsync(notify, "mf:mwo:" + order.Id, (order.Status == MfMaintenanceOrderStatus.Open || order.Status == MfMaintenanceOrderStatus.InProgress) && !order.Deleted, "production", "maintenance-order", order.Id,
                $"دستور نگهداری {order.Number} ({machineTitle}) منتظر انجام است", "/production/maintenance-orders/" + order.Id, ProductionRoles.Maintenance, ct);
    }
}
