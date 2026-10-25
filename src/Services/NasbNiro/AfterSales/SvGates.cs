using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.AfterSales;
using Services.NasbNiro.Office;

namespace Services.NasbNiro.AfterSales
{
    /// <summary>Inbox items of the service gates: open while a gate waits for a person, closed when it does not.</summary>
    internal static class SvGates
    {
        public static Task UnitAsync(INotificationService notify, SvInstalledUnit u, CancellationToken ct) =>
            OfficeGates.SyncAsync(notify, "sv:unit:" + u.Id, !u.IsConfirmed && !u.Decommissioned && !u.Deleted, "service", "unit-confirm", u.Id,
                $"واحد نصب‌شده {u.Title} پیش‌نویس سیستم است؛ سایت، شماره tag و گارانتی را تأیید کنید", "/service/units/" + u.Id, SvRoles.Service, ct);

        public static Task AssignAsync(INotificationService notify, SvServiceRequest r, CancellationToken ct) =>
            OfficeGates.SyncAsync(notify, "sv:assign:" + r.Id, r.Status == SvRequestStatus.Open && !r.Deleted, "service", "request-assign", r.Id,
                $"درخواست خدمات {r.Number} منتظر واگذاری است", "/service/requests/" + r.Id, SvRoles.Service, ct, r.ResponseDue);

        public static Task GoodwillAsync(INotificationService notify, SvServiceOrder o, CancellationToken ct) =>
            OfficeGates.SyncAsync(notify, "sv:goodwill:" + o.Id, o.GoodwillRequestedAt.HasValue && o.Decision != SvWarrantyDecision.Goodwill && o.Status != SvOrderStatus.Closed && o.Status != SvOrderStatus.Cancelled && !o.Deleted,
                "service", "order-goodwill", o.Id, $"درخواست حسن نیت دستور خدمات {o.Number} منتظر تصمیم مدیرعامل است", "/service/orders/" + o.Id, SvRoles.GoodwillDeciders, ct);

        public static Task CloseAsync(INotificationService notify, SvServiceOrder o, CancellationToken ct) =>
            OfficeGates.SyncAsync(notify, "sv:close:" + o.Id, o.Status == SvOrderStatus.InProgress && !o.Deleted, "service", "order-close", o.Id,
                $"دستور خدمات {o.Number} در حال انجام است؛ پس از پیوست گزارش امضاشده ببندید", "/service/orders/" + o.Id, SvRoles.Service, ct);

        /// <summary>A visit order the system drafted from a service agreement waits for service to choose the engineers and confirm it.</summary>
        public static Task VisitAsync(INotificationService notify, SvServiceOrder o, CancellationToken ct) =>
            OfficeGates.SyncAsync(notify, "sv:visit:" + o.Id, o.Status == SvOrderStatus.Draft && o.LtsaContractId.HasValue && !o.Deleted, "service", "visit-confirm", o.Id,
                $"دستور خدمات {o.Number} پیش‌نویس سیستم از بازدید دوره‌ای است؛ مهندسان را انتخاب و دستور را تأیید کنید", "/service/orders/" + o.Id, SvRoles.Service, ct, o.PlannedStart);

        /// <summary>A survey the system drafted waits for a person to send it to the customer (or to skip it).</summary>
        public static Task SurveyAsync(INotificationService notify, SlSurvey s, CancellationToken ct) =>
            OfficeGates.SyncAsync(notify, "sv:survey:" + s.Id, s.Status == SlSurveyStatus.Drafted && !s.Deleted, "service", "survey-send", s.Id,
                $"نظرسنجی {s.Number} پیش‌نویس سیستم است؛ برای مشتری ارسال یا رد کنید", "/service/surveys/" + s.Id, SvRoles.Service + ",SALES", ct);

        public static Task ContractAsync(INotificationService notify, SvContract c, CancellationToken ct) =>
            OfficeGates.SyncAsync(notify, "sv:ltsa:" + c.Id, c.Status == SvContractStatus.Draft && !c.Deleted, "service", "ltsa-activate", c.Id,
                $"قرارداد خدمات {c.Number} منتظر فعال‌سازی توسط شخص دیگری است", "/service/contracts/" + c.Id, SvRoles.Service, ct);

        public static Task BreachAsync(INotificationService notify, SvServiceRequest r, bool breached, CancellationToken ct) =>
            OfficeGates.SyncAsync(notify, "sv:sla:" + r.Id, breached && !r.Deleted, "service", "sla-breach", r.Id,
                $"موعد SLA درخواست خدمات {r.Number} گذشته است", "/service/requests/" + r.Id, SvRoles.Service + "," + SvRoles.Ceo, ct, r.ResponseDue);
    }
}
