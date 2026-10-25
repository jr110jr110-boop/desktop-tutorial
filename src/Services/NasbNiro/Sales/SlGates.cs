using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Sales;
using Services.NasbNiro.Office;

namespace Services.NasbNiro.Sales
{
    /// <summary>Inbox items of the sales gates: open while a gate waits for a person, closed when it does not.</summary>
    internal static class SlGates
    {
        public static Task BidAsync(INotificationService notify, SlBidDecision d, string inquiryNumber, string title, CancellationToken ct) =>
            OfficeGates.SyncAsync(notify, "sl:bid:" + d.InquiryId, d.Status == SlBidStatus.WaitingDecision && !d.Deleted, "sales", "bid-decision", d.InquiryId,
                $"تصمیم شرکت در استعلام {inquiryNumber} ({title}) منتظر مدیرعامل است", "/sales/bid-decisions/" + d.InquiryId, SalesRoles.Ceo, ct);

        public static Task ProposalAsync(INotificationService notify, SlProposal p, bool needsCeo, CancellationToken ct) =>
            OfficeGates.SyncAsync(notify, "sl:proposal:" + p.Id, p.Status == SlProposalStatus.Draft && !p.Deleted, "sales", "proposal", p.Id,
                $"پیشنهاد {p.Number} (بازنگری {p.Revision}) منتظر تأیید است", "/sales/proposals/" + p.Id, needsCeo ? SalesRoles.Ceo : SalesRoles.ProposalApprovers, ct);

        public static Task BelowCostAsync(INotificationService notify, SlProposal p, CancellationToken ct) =>
            OfficeGates.SyncAsync(notify, "sl:belowcost:" + p.Id, p.Status == SlProposalStatus.Draft && p.BelowCost && !p.Deleted, "sales", "proposal-below-cost", p.Id,
                $"قیمت پیشنهاد {p.Number} کمتر از بهای تمام‌شده است؛ فقط مدیرعامل می‌تواند تأیید کند", "/sales/proposals/" + p.Id, SalesRoles.Ceo, ct);

        public static Task CreditAsync(INotificationService notify, SlSalesOrder o, CancellationToken ct) =>
            OfficeGates.SyncAsync(notify, "sl:credit:" + o.Id, o.CreditApprovalRequired && o.Status == SlOrderStatus.Draft && !o.Deleted && o.CreditApprovedNetAmount != o.NetAmount, "sales", "order-credit", o.Id,
                $"سفارش فروش {o.Number} از سقف اعتبار مشتری بیشتر است و منتظر تأیید مدیر مالی است", "/sales/orders/" + o.Id, SalesRoles.CreditApprovers, ct);
    }
}
