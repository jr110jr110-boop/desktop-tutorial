using System;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.Office
{
    /// <summary>
    /// The one-line bridge used by the gates of the other modules (procurement, finance, quality, project control). It opens the inbox items of a gate while the gate waits for somebody
    /// and closes them when it does not. Call it after the gate's own save. It never throws, so a problem in the inbox can never break a procurement, finance or quality flow.
    /// </summary>
    public static class OfficeGates
    {
        public static async Task SyncAsync(INotificationService notify, string key, bool waiting, string module, string type, int id, string title, string link, string roles,
            CancellationToken cancellationToken, DateTime? due = null, OfInboxKind kind = OfInboxKind.Approval, OfUrgency priority = OfUrgency.Normal)
        {
            if (notify == null) return;
            try
            {
                if (waiting)
                    await notify.NotifyAsync(new NotifyRequest
                    {
                        SourceKey = key, Module = module, SourceType = type, SourceId = id, Title = title, Link = link, Roles = roles, DueDate = due, Kind = kind, Priority = priority, ReplaceRecipients = true,
                    }, cancellationToken);
                else
                    await notify.CloseAsync(key, "مرحله تایید گذشت", cancellationToken);
            }
            catch (Exception)
            {
                // the inbox must never block a gate
            }
        }

        /// <summary>One inbox item for the finance manager while any ledger voucher is checked and waits to be finalised (also covers the automatic vouchers of the posting job).</summary>
        public static async Task SweepVouchersAsync(IUnitOfWork uow, INotificationService notify, CancellationToken cancellationToken)
        {
            if (uow == null || notify == null) return;
            try
            {
                bool waiting = await uow.Set<FnVoucher>().AsNoTracking().AnyAsync(v => v.Status == FnVoucherStatus.Checked, cancellationToken);
                await SyncAsync(notify, "vouchers:finalise", waiting, "finance", "vouchers", 0, "اسناد کنترل‌شده منتظر قطعی شدن هستند", "/finance/vouchers?status=checked", "FINANCEMANAGER",
                    cancellationToken, null, OfInboxKind.Task);
            }
            catch (Exception)
            {
                // the inbox must never block a gate
            }
        }
    }
}
