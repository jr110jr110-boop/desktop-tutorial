using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.AfterSales;
using ViewModels.NasbNiro.ServiceDtos;
using static Services.NasbNiro.Ceo.Drills.DrillText;

namespace Services.NasbNiro.Ceo.Drills
{
    /// <summary>
    /// The after-sales drills: open requests, SLA breaches, open service orders, the warranty cost by month (payroll mask), the installed base by customer, the customer surveys and the LTSA contracts about to end.
    /// No row shows the labour amount of an order: the only cost figure is the monthly warranty cost, which follows the module's payroll rule (<see cref="SvRoles.SeesPayroll"/> and <see cref="SvPrivacy.LabourProtected"/>).
    /// </summary>
    internal static class ServiceDrills
    {
        private const string M = CeoModules.Service;

        private static readonly string[] PriorityNames = { "Critical", "High", "Normal", "Low" };
        private static readonly SvOrderStatus[] OpenOrderStatuses = { SvOrderStatus.Draft, SvOrderStatus.Planned, SvOrderStatus.InProgress };

        public static IEnumerable<DrillDefinition> Build()
        {
            yield return RequestsOpen();
            yield return SlaBreaches();
            yield return OrdersOpen();
            yield return WarrantyCostByMonth();
            yield return UnitsByCustomer();
            yield return SurveyByMonth();
            yield return LtsaExpiring();
        }

        private static SvPriority? ParsePriority(string text) => text != null && Enum.TryParse(text, true, out SvPriority priority) ? priority : (SvPriority?)null;

        private static DateTimeOffset Offset(DateTime day) => new DateTimeOffset(day, TimeZoneInfo.Local.GetUtcOffset(day));

        /// <summary>Hours past the due time: when it was done late, or when it is not done and the due time has passed (the same rule as <c>SvSla.Breached</c>); 0 when it is in time or has no due time.</summary>
        private static decimal HoursOver(DateTime? due, DateTime? doneAt, DateTime now) =>
            due.HasValue && (doneAt ?? now) > due.Value ? Round((decimal)((doneAt ?? now) - due.Value).TotalHours, 1) : 0m;

        // ------------------------------------------------------------------------------------------ requests

        private static DrillDefinition RequestsOpen() => new DrillDefinition
        {
            Key = "sv.requests_open", Module = M, Title = "درخواست‌های خدمات باز",
            Description = "درخواست‌های خدمات پس از فروش که هنوز رفع نشده‌اند (باز، واگذارشده، در حال انجام، منتظر مشتری؛ هم‌پایه با «درخواست باز» داشبورد خدمات)؛ اول نقض‌شده‌های SLA، بعد بحرانی‌ترین و قدیمی‌ترین. با priority می‌شود به یک اولویت محدود کرد.",
            Link = "/service/requests", RecordType = "ServiceRequest", RecordLabel = "درخواست خدمات",
            Parameters = new[] { Prm.Str("priority", "فقط یک اولویت (Critical بحرانی، High بالا، Normal عادی، Low پایین)؛ پیش‌فرض همه‌ی اولویت‌ها", null, PriorityNames) },
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("customerName", "مشتری"), Col.Text("unitTag", "واحد"), Col.Text("type", "نوع"), Col.Status("priority", "اولویت"), Col.Status("status", "وضعیت"),
                Col.Date("receivedAt", "دریافت"), Col.Num("ageDays", "روز از دریافت"), Col.Date("responseDue", "مهلت پاسخ"), Col.Date("resolutionDue", "مهلت رفع"), Col.Status("sla", "SLA"),
                Col.Text("assignedTo", "مسئول"), Col.Text("description", "شرح"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, SvRoles.Readers);
                SvPriority? priority = ParsePriority(c.Args.Str("priority"));
                var requests = c.Get<ISvRequestService>();
                DateTime now = DateTime.Now;
                List<SvRequestDto> loaded = await DrillRead.AllAsync(n => requests.GetRequestsAsync(new SvRequestQuery { OpenOnly = true, AsOf = now, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct));
                List<SvRequestDto> open = loaded.Where(r => r.Status != SvRequestStatus.Resolved && r.Status != SvRequestStatus.Closed).ToList();
                List<SvRequestDto> shown = open.Where(r => !priority.HasValue || r.Priority == priority.Value)
                    .OrderByDescending(r => r.Breached).ThenBy(r => (int)r.Priority).ThenBy(r => r.ReceivedAt).ThenBy(r => r.Number, StringComparer.Ordinal).ToList();
                List<Row> rows = shown.Select(r => new Row("/service/requests/" + Id(r.Id))
                {
                    ["id"] = r.Id, ["number"] = r.Number, ["customerName"] = r.CustomerName, ["unitTag"] = r.UnitTag, ["type"] = r.TypeText, ["priority"] = r.PriorityText, ["status"] = r.StatusText,
                    ["receivedAt"] = r.ReceivedAtText, ["ageDays"] = Math.Max(0, DaysBetween(r.ReceivedAt, now)), ["responseDue"] = r.ResponseDueText, ["resolutionDue"] = r.ResolutionDueText,
                    ["sla"] = r.Breached ? "نقض شده" : r.SlaMissing ? "بدون SLA" : "در مهلت", ["assignedTo"] = r.AssignedToName, ["description"] = Short(r.Description, 100),
                }).ToList();
                return DrillResult.Slice(rows, c, "نقض SLA: مهلت پاسخ یا رفع گذشته یا دیر انجام شده است؛ درخواست رفع‌شده و بسته‌شده در این فهرست نیست.", new Dictionary<string, object>
                {
                    ["open"] = open.Count, ["breached"] = open.Count(r => r.Breached),
                    ["critical"] = open.Count(r => r.Priority == SvPriority.Critical), ["high"] = open.Count(r => r.Priority == SvPriority.High),
                    ["normal"] = open.Count(r => r.Priority == SvPriority.Normal), ["low"] = open.Count(r => r.Priority == SvPriority.Low),
                });
            },
        };

        private static DrillDefinition SlaBreaches() => new DrillDefinition
        {
            Key = "sv.sla_breaches", Module = M, Title = "نقض SLA درخواست‌های خدمات",
            Description = "درخواست‌های خدمات باز که مهلت پاسخ یا مهلت رفعشان گذشته یا دیر انجام شده (هم‌پایه با «نقض SLA» داشبورد خدمات)، با ساعت‌های تاخیر؛ بیشترین تاخیر اول.",
            Link = "/service/requests", RecordType = "ServiceRequest", RecordLabel = "درخواست خدمات",
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("customerName", "مشتری"), Col.Text("unitTag", "واحد"), Col.Status("priority", "اولویت"), Col.Status("status", "وضعیت"), Col.Text("breachKind", "نقض در"),
                Col.Date("responseDue", "مهلت پاسخ"), Col.Date("resolutionDue", "مهلت رفع"), Col.Num("hoursOver", "ساعت تاخیر"), Col.Text("assignedTo", "مسئول"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, SvRoles.Readers);
                var requests = c.Get<ISvRequestService>();
                DateTime now = DateTime.Now;
                List<SvRequestDto> breached = await DrillRead.AllAsync(n => requests.GetRequestsAsync(new SvRequestQuery { Breached = true, AsOf = now, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct));
                var items = breached.Select(r =>
                {
                    decimal response = HoursOver(r.ResponseDue, r.FirstResponseAt, now), resolution = HoursOver(r.ResolutionDue, r.ResolvedAt, now);
                    return new { Request = r, Response = response, Resolution = resolution, Over = Math.Max(response, resolution) };
                }).OrderByDescending(x => x.Over).ThenBy(x => (int)x.Request.Priority).ThenBy(x => x.Request.Number, StringComparer.Ordinal).ToList();
                List<Row> rows = items.Select(x => new Row("/service/requests/" + Id(x.Request.Id))
                {
                    ["id"] = x.Request.Id, ["number"] = x.Request.Number, ["customerName"] = x.Request.CustomerName, ["unitTag"] = x.Request.UnitTag, ["priority"] = x.Request.PriorityText, ["status"] = x.Request.StatusText,
                    ["breachKind"] = x.Response > 0m && x.Resolution > 0m ? "پاسخ و رفع" : x.Response > 0m ? "پاسخ" : "رفع",
                    ["responseDue"] = x.Request.ResponseDueText, ["resolutionDue"] = x.Request.ResolutionDueText, ["hoursOver"] = x.Over, ["assignedTo"] = x.Request.AssignedToName,
                }).ToList();
                return DrillResult.Slice(rows, c, "ساعت تاخیر تا همین لحظه حساب می‌شود؛ پاسخی که دیر داده شده یا رفعی که منتظر مانده هر دو نقض‌اند.", new Dictionary<string, object>
                {
                    ["breached"] = items.Count, ["responseBreaches"] = items.Count(x => x.Response > 0m), ["resolutionBreaches"] = items.Count(x => x.Resolution > 0m),
                    ["critical"] = items.Count(x => x.Request.Priority == SvPriority.Critical), ["unassigned"] = items.Count(x => !x.Request.AssignedToUserId.HasValue),
                });
            },
        };

        // ------------------------------------------------------------------------------------------ service orders

        private static DrillDefinition OrdersOpen() => new DrillDefinition
        {
            Key = "sv.orders_open", Module = M, Title = "دستورهای خدمات باز",
            Description = "دستورهای خدمات پس از فروش پیش‌نویس، برنامه‌ریزی‌شده و در حال انجام (هم‌پایه با «دستور خدمات باز» داشبورد)؛ اول دارای تاخیر نسبت به پایان برنامه. با goodwillOnly فقط دستورهایی که درخواست حسن نیتشان منتظر تصمیم مدیرعامل است. مبلغ دستمزد نمایش داده نمی‌شود.",
            Link = "/service/orders", RecordType = "ServiceOrder", RecordLabel = "دستور خدمات",
            Parameters = new[] { Prm.Bool("goodwillOnly", "فقط دستورهایی که درخواست حسن نیت دارند و هنوز تصمیم‌گیری نشده؛ پیش‌فرض false") },
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("requestNumber", "درخواست"), Col.Text("unitTag", "واحد"), Col.Text("customerName", "مشتری"), Col.Text("title", "عنوان"), Col.Status("status", "وضعیت"),
                Col.Status("decision", "تصمیم گارانتی"), Col.Date("plannedStart", "شروع برنامه"), Col.Date("plannedEnd", "پایان برنامه"), Col.Num("daysLate", "روز تاخیر"), Col.Status("goodwill", "حسن نیت"),
                Col.Num("totalHours", "ساعت ثبت‌شده"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, SvRoles.OrderReaders);
                var orders = c.Get<ISvOrderService>();
                var open = new List<SvOrderDto>();
                foreach (SvOrderStatus status in OpenOrderStatuses)
                {
                    SvOrderStatus current = status;
                    open.AddRange(await DrillRead.AllAsync(n => orders.GetOrdersAsync(new SvOrderQuery { Status = current, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct)));
                }
                bool goodwillOnly = c.Args.Bool("goodwillOnly");
                var items = open.Select(o => new
                {
                    Order = o,
                    DaysLate = o.PlannedEnd.HasValue && o.PlannedEnd.Value.Date < c.Today ? DaysBetween(o.PlannedEnd.Value, c.Today) : 0,
                    GoodwillWaiting = o.GoodwillRequested && o.Decision != SvWarrantyDecision.Goodwill,
                }).ToList();
                List<Row> rows = items.Where(x => !goodwillOnly || x.GoodwillWaiting).OrderByDescending(x => x.DaysLate).ThenByDescending(x => x.GoodwillWaiting)
                    .ThenBy(x => x.Order.PlannedStart ?? DateTime.MaxValue).ThenBy(x => x.Order.Number, StringComparer.Ordinal)
                    .Select(x => new Row("/service/orders/" + Id(x.Order.Id))
                    {
                        ["id"] = x.Order.Id, ["number"] = x.Order.Number, ["requestNumber"] = x.Order.RequestNumber, ["unitTag"] = x.Order.UnitTag, ["customerName"] = x.Order.CustomerName, ["title"] = Short(x.Order.Title, 100),
                        ["status"] = x.Order.StatusText, ["decision"] = x.Order.DecisionText, ["plannedStart"] = x.Order.PlannedStartText, ["plannedEnd"] = x.Order.PlannedEndText, ["daysLate"] = x.DaysLate,
                        ["goodwill"] = x.GoodwillWaiting ? "منتظر تصمیم مدیرعامل" : null, ["totalHours"] = x.Order.TotalHours,
                    }).ToList();
                return DrillResult.Slice(rows, c, "تاخیر = گذشتن از پایان برنامه‌ی دستور باز؛ ساعت‌ها جمع ساعت‌های سفر، کار و انتظار ثبت‌شده است.", new Dictionary<string, object>
                {
                    ["open"] = items.Count, ["inProgress"] = items.Count(x => x.Order.Status == SvOrderStatus.InProgress), ["late"] = items.Count(x => x.DaysLate > 0), ["goodwillWaiting"] = items.Count(x => x.GoodwillWaiting),
                });
            },
        };

        // ------------------------------------------------------------------------------------------ warranty cost

        private static DrillDefinition WarrantyCostByMonth() => new DrillDefinition
        {
            Key = "sv.warranty_cost_by_month", Module = M, Title = "هزینه‌ی گارانتی به تفکیک ماه",
            Description = "تعداد و هزینه‌ی (دستمزد + قطعات) دستورهای خدمات گارانتی بسته‌شده در هر ماه شمسی. هزینه‌ی ماهی که دستمزدش به کمتر از سه نفر برمی‌گردد برای نقشی که حقوق را نمی‌بیند (و برای هر کار هوش مصنوعی) پنهان است و فقط هزینه‌ی قطعات می‌آید. بازه‌ی پیش‌فرض: ۱۲ ماه شمسی اخیر؛ جمع «از ابتدای سال» هم‌پایه با داشبورد خدمات است.",
            Link = "/service/dashboard", RecordType = "WarrantyMonth", RecordLabel = "ماه",
            Parameters = Prm.Last12Months(" بسته‌شدن دستور"),
            Columns = new[] { Col.Text("month", "ماه"), Col.Num("orders", "دستور گارانتی"), Col.Rial("partsAmount", "هزینه‌ی قطعات"), Col.Rial("totalCost", "هزینه‌ی کل"), Col.Status("visibility", "نمایش") },
            Run = async (c, ct) =>
            {
                Guard.Require(c, SvRoles.Readers);
                DateTime from = c.Args.Date("from").Date, to = c.Args.Date("to").Date;
                bool sees = SvRoles.SeesPayroll(c.User.IsInRole(SvRoles.PayrollSeers));
                DateTime yearStart = SvPersianYear.Start(to);
                DateTimeOffset readFrom = Offset(from < yearStart ? from : yearStart), readTo = Offset(to.AddDays(1));
                var closed = await c.Get<IUnitOfWork>().Set<SvServiceOrder>().AsNoTracking()
                    .Where(o => !o.Deleted && o.Status == SvOrderStatus.Closed && o.Decision == SvWarrantyDecision.Warranty && o.ClosedAt >= readFrom && o.ClosedAt < readTo)
                    .Select(o => new { o.ClosedAt, o.LabourAmount, o.PartsAmount, o.LabourEmployeeCount, o.LabourPayrollEmployeeCount }).ToListAsync(ct);
                var orders = closed.Where(o => o.ClosedAt.HasValue).Select(o => new
                {
                    Day = o.ClosedAt.Value.LocalDateTime, o.LabourAmount, o.PartsAmount,
                    Protected = SvPrivacy.LabourProtected(o.LabourEmployeeCount, o.LabourPayrollEmployeeCount),
                }).ToList();
                var window = orders.Where(o => o.Day.Date >= from && o.Day.Date <= to).ToList();
                var rows = new List<Row>();
                bool anyHidden = false;
                foreach (DateTime month in DrillDates.Months(from, to))
                {
                    var inMonth = window.Where(o => DrillDates.MonthStart(o.Day) == month).ToList();
                    bool hidden = !sees && inMonth.Any(o => o.Protected);
                    anyHidden |= hidden;
                    rows.Add(new Row("/service/orders")
                    {
                        ["month"] = DrillDates.MonthLabel(month), ["orders"] = inMonth.Count, ["partsAmount"] = inMonth.Sum(o => o.PartsAmount),
                        ["totalCost"] = hidden ? (decimal?)null : inMonth.Sum(o => o.LabourAmount + o.PartsAmount), ["visibility"] = hidden ? "محرمانه (حقوق)" : "نمایان",
                    });
                }
                var year = orders.Where(o => o.Day >= yearStart).ToList();
                bool yearHidden = !sees && year.Any(o => o.Protected);
                var totals = new Dictionary<string, object>
                {
                    ["orders"] = window.Count, ["partsAmount"] = window.Sum(o => o.PartsAmount), ["totalCost"] = anyHidden ? (decimal?)null : window.Sum(o => o.LabourAmount + o.PartsAmount),
                    ["ytdOrders"] = year.Count, ["ytdCost"] = yearHidden ? (decimal?)null : year.Sum(o => o.LabourAmount + o.PartsAmount),
                };
                DrillResult result = DrillResult.Slice(rows, c, anyHidden || yearHidden
                    ? "هزینه‌ی ماه‌هایی که دستمزدشان به کمتر از سه نفر برمی‌گردد برای نقش شما محرمانه است (حقوق)؛ هزینه‌ی قطعات نمایش داده می‌شود."
                    : "هزینه = دستمزد + قطعات دستورهای گارانتی بسته‌شده؛ «از ابتدای سال» از اول فروردین سال شمسی است.", totals);
                result.Hidden = anyHidden || yearHidden;
                return result;
            },
        };

        // ------------------------------------------------------------------------------------------ installed base

        private static DrillDefinition UnitsByCustomer() => new DrillDefinition
        {
            Key = "sv.units_by_customer", Module = M, Title = "واحدهای نصب‌شده به تفکیک مشتری",
            Description = "تعداد واحدهای نصب‌شده‌ی هر مشتری به تفکیک وضعیت (در گارانتی، تحت قرارداد خدمات، خارج از گارانتی) و واحدهای در انتظار تایید؛ واحد از رده خارج شمرده نمی‌شود و جمع‌ها با داشبورد خدمات هم‌پایه‌اند. بیشترین واحد اول.",
            Link = "/service/units", RecordType = "Customer", RecordLabel = "مشتری",
            Columns = new[]
            {
                Col.Text("customerName", "مشتری"), Col.Num("units", "واحد نصب‌شده"), Col.Num("inWarranty", "در گارانتی"), Col.Num("underLtsa", "تحت قرارداد خدمات"), Col.Num("outOfWarranty", "خارج از گارانتی"),
                Col.Num("toConfirm", "در انتظار تایید"), Col.Date("nextWarrantyEnd", "نزدیک‌ترین پایان گارانتی"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, SvRoles.Readers);
                var service = c.Get<ISvUnitService>();
                List<SvUnitDto> loaded = await DrillRead.AllAsync(n => service.GetUnitsAsync(new SvUnitQuery { PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct));
                List<SvUnitDto> units = loaded.Where(u => !u.Decommissioned).ToList();
                var groups = units.GroupBy(u => u.CustomerId ?? 0).Select(g => new
                {
                    CustomerId = g.Key,
                    Name = g.Select(u => u.CustomerName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "بدون مشتری",
                    Units = g.Count(),
                    InWarranty = g.Count(u => u.Status == SvUnitStatus.InWarranty),
                    UnderLtsa = g.Count(u => u.Status == SvUnitStatus.UnderLtsa),
                    OutOfWarranty = g.Count(u => u.Status == SvUnitStatus.OutOfWarranty),
                    ToConfirm = g.Count(u => !u.IsConfirmed),
                    NextEnd = g.Where(u => u.Status == SvUnitStatus.InWarranty && u.WarrantyEnd.HasValue && u.WarrantyEnd.Value.Date >= c.Today).Select(u => u.WarrantyEnd.Value.Date).DefaultIfEmpty(DateTime.MinValue).Min(),
                }).OrderByDescending(x => x.Units).ThenBy(x => x.Name, StringComparer.Ordinal).ToList();
                List<Row> rows = groups.Select(x => new Row(x.CustomerId > 0 ? "/sales/customers/" + Id(x.CustomerId) : "/service/units")
                {
                    ["id"] = x.CustomerId, ["customerName"] = x.Name, ["units"] = x.Units, ["inWarranty"] = x.InWarranty, ["underLtsa"] = x.UnderLtsa, ["outOfWarranty"] = x.OutOfWarranty,
                    ["toConfirm"] = x.ToConfirm, ["nextWarrantyEnd"] = x.NextEnd == DateTime.MinValue ? null : DrillDates.Text(x.NextEnd),
                }).ToList();
                return DrillResult.Slice(rows, c, "وضعیت هر واحد امروز سنجیده می‌شود: گارانتی بر قرارداد خدمات مقدم است.", new Dictionary<string, object>
                {
                    ["units"] = units.Count, ["inWarranty"] = units.Count(u => u.Status == SvUnitStatus.InWarranty), ["underLtsa"] = units.Count(u => u.Status == SvUnitStatus.UnderLtsa),
                    ["outOfWarranty"] = units.Count(u => u.Status == SvUnitStatus.OutOfWarranty), ["toConfirm"] = units.Count(u => !u.IsConfirmed), ["customers"] = groups.Count,
                });
            },
        };

        // ------------------------------------------------------------------------------------------ surveys

        private static DrillDefinition SurveyByMonth() => new DrillDefinition
        {
            Key = "sv.survey_by_month", Module = M, Title = "رضایت مشتریان به تفکیک ماه",
            Description = "تعداد نظرسنجی‌های پاسخ‌داده‌شده و میانگین امتیاز (۱ تا ۵) هر ماه شمسی (بر پایه‌ی تاریخ پاسخ)، به تفکیک نظرسنجی تحویل و خدمات و تعداد امتیازهای زیر ۳. بازه‌ی پیش‌فرض: ۱۲ ماه شمسی اخیر؛ میانگین داشبورد روی کل سابقه است (در جمع‌ها به نام averageAll).",
            Link = "/service/surveys", RecordType = "SurveyMonth", RecordLabel = "ماه",
            Parameters = Prm.Last12Months(" پاسخ"),
            Columns = new[]
            {
                Col.Text("month", "ماه"), Col.Num("answered", "پاسخ‌داده‌شده"), Col.Num("averageScore", "میانگین امتیاز"), Col.Num("deliveryAverage", "میانگین تحویل"), Col.Num("serviceAverage", "میانگین خدمات"),
                Col.Num("lowScores", "امتیاز زیر ۳"),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, SvRoles.Readers);
                DateTime from = c.Args.Date("from").Date, to = c.Args.Date("to").Date;
                var uow = c.Get<IUnitOfWork>();
                var answered = await uow.Set<SlSurvey>().AsNoTracking().Where(s => !s.Deleted && s.Status == SlSurveyStatus.Answered && s.AverageScore != null)
                    .Select(s => new { s.AnsweredAt, Score = s.AverageScore.Value, s.Kind }).ToListAsync(ct);
                int drafted = await uow.Set<SlSurvey>().AsNoTracking().CountAsync(s => !s.Deleted && s.Status == SlSurveyStatus.Drafted, ct);
                int sent = await uow.Set<SlSurvey>().AsNoTracking().CountAsync(s => !s.Deleted && s.Status == SlSurveyStatus.Sent, ct);
                var inWindow = answered.Where(s => s.AnsweredAt.HasValue && s.AnsweredAt.Value.LocalDateTime.Date >= from && s.AnsweredAt.Value.LocalDateTime.Date <= to)
                    .Select(s => new { Month = DrillDates.MonthStart(s.AnsweredAt.Value.LocalDateTime), s.Score, s.Kind }).ToList();
                var rows = new List<Row>();
                foreach (DateTime month in DrillDates.Months(from, to))
                {
                    var inMonth = inWindow.Where(s => s.Month == month).ToList();
                    var delivery = inMonth.Where(s => s.Kind == SlSurveyKind.Delivery).ToList();
                    var service = inMonth.Where(s => s.Kind == SlSurveyKind.Service).ToList();
                    rows.Add(new Row("/service/surveys")
                    {
                        ["month"] = DrillDates.MonthLabel(month), ["answered"] = inMonth.Count, ["averageScore"] = inMonth.Count == 0 ? (decimal?)null : Round(inMonth.Average(s => s.Score)),
                        ["deliveryAverage"] = delivery.Count == 0 ? (decimal?)null : Round(delivery.Average(s => s.Score)), ["serviceAverage"] = service.Count == 0 ? (decimal?)null : Round(service.Average(s => s.Score)),
                        ["lowScores"] = inMonth.Count(s => s.Score < 3m),
                    });
                }
                return DrillResult.Slice(rows, c, "امتیاز هر نظرسنجی میانگین پاسخ‌های ۱ تا ۵ مشتری است؛ ماه بدون پاسخ میانگین ندارد.", new Dictionary<string, object>
                {
                    ["answered"] = inWindow.Count, ["averageScore"] = inWindow.Count == 0 ? (decimal?)null : Round(inWindow.Average(s => s.Score)), ["lowScores"] = inWindow.Count(s => s.Score < 3m),
                    ["averageAll"] = answered.Count == 0 ? (decimal?)null : Round(answered.Average(s => s.Score)), ["answeredAll"] = answered.Count, ["drafted"] = drafted, ["sent"] = sent,
                });
            },
        };

        // ------------------------------------------------------------------------------------------ LTSA

        private static DrillDefinition LtsaExpiring() => new DrillDefinition
        {
            Key = "sv.ltsa_expiring", Module = M, Title = "قراردادهای خدمات در حال پایان",
            Description = "قراردادهای خدمات بلندمدت (LTSA) فعال که تا چند روز آینده تمام می‌شوند یا پایانشان گذشته ولی هنوز فعال مانده‌اند؛ نزدیک‌ترین پایان اول. پیش‌فرض ۶۰ روز.",
            Link = "/service/contracts", RecordType = "ServiceContract", RecordLabel = "قرارداد خدمات",
            Parameters = new[] { Prm.Days(60, 730, "تعداد روز تا پایان قرارداد") },
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("title", "عنوان"), Col.Text("customerName", "مشتری"), Col.Date("endDate", "پایان"), Col.Num("daysLeft", "روز مانده"), Col.Date("nextVisitDate", "بازدید بعدی"),
                Col.Num("units", "واحد"), Col.Rial("priceRial", "مبلغ قرارداد"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, SvRoles.Readers);
                var contracts = c.Get<ISvContractService>();
                List<SvContractDto> active = await DrillRead.AllAsync(n => contracts.GetContractsAsync(new SvContractQuery { Status = SvContractStatus.Active, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct));
                DateTime limit = c.Today.AddDays(c.Args.Int("days"));
                List<SvContractDto> ending = active.Where(x => x.EndDate.Date <= limit).OrderBy(x => x.EndDate.Date).ThenBy(x => x.Number, StringComparer.Ordinal).ToList();
                List<Row> rows = ending.Select(x => new Row("/service/contracts/" + Id(x.Id))
                {
                    ["id"] = x.Id, ["number"] = x.Number, ["title"] = Short(x.Title, 100), ["customerName"] = x.CustomerName, ["endDate"] = x.EndDateText, ["daysLeft"] = DaysBetween(c.Today, x.EndDate),
                    ["nextVisitDate"] = x.NextVisitDateText, ["units"] = x.UnitIds.Count, ["priceRial"] = x.PriceRial,
                }).ToList();
                return DrillResult.Slice(rows, c, "روز مانده‌ی منفی یعنی پایان قرارداد گذشته ولی کار شبانه هنوز آن را منقضی نکرده است.", new Dictionary<string, object>
                {
                    ["expiring"] = ending.Count, ["expired"] = ending.Count(x => x.EndDate.Date < c.Today), ["priceRial"] = ending.Sum(x => x.PriceRial ?? 0m), ["activeContracts"] = active.Count,
                });
            },
        };
    }
}
