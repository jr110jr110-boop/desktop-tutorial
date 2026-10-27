using System;
using System.Collections.Generic;
using System.Linq;
using Entities.Model.NasbNiro.Production;
using Services.NasbNiro.Production;
using ViewModels.NasbNiro.ProductionDtos;
using static Services.NasbNiro.Ceo.Drills.DrillText;

namespace Services.NasbNiro.Ceo.Drills
{
    /// <summary>The production drills: late orders, orders by status, work-centre load, machines down, overdue maintenance and OEE.</summary>
    internal static class ProductionDrills
    {
        private const string M = CeoModules.Production;

        private static readonly MfOrderStatus[] OpenStatuses = { MfOrderStatus.Released, MfOrderStatus.InProgress };

        public static IEnumerable<DrillDefinition> Build()
        {
            yield return OrdersLate();
            yield return OrdersByStatus();
            yield return WorkCenterLoad();
            yield return MachinesDown();
            yield return MaintenanceOverdue();
            yield return OeeByWorkCenter();
        }

        private static string OrderStatus(MfOrderStatus status) => status switch
        {
            MfOrderStatus.Draft => "پیش‌نویس",
            MfOrderStatus.Released => "آزادشده",
            MfOrderStatus.InProgress => "در حال تولید",
            MfOrderStatus.Completed => "تکمیل‌شده",
            MfOrderStatus.Closed => "بسته‌شده",
            _ => "لغوشده",
        };

        // ------------------------------------------------------------------------------------------ orders

        private static DrillDefinition OrdersLate() => new DrillDefinition
        {
            Key = "mf.orders_late", Module = M, Title = "دستورهای تولید دارای تأخیر",
            Description = "دستورهای تولید آزادشده و در حال تولید که آخرین اجرای زمان‌بند پایان پیش‌بینی‌شده‌شان را دیرتر از تاریخ نیاز یافته است (هم‌پایه با داشبورد تولید)؛ بیشترین روز تأخیر اول.",
            Link = "/production/orders", RecordType = "ProductionOrder", RecordLabel = "دستور تولید",
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("workOrderCode", "دستور کار"), Col.Text("itemCode", "کد کالا"), Col.Text("itemTitle", "کالا"), Col.Num("quantity", "تعداد"), Col.Status("status", "وضعیت"),
                Col.Date("needDate", "تاریخ نیاز"), Col.Date("plannedFinish", "پایان پیش‌بینی"), Col.Num("daysLate", "روز تأخیر"), Col.Num("operationsDone", "عملیات انجام‌شده"), Col.Num("operationCount", "کل عملیات"),
                Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProductionRoles.MasterReaders);
                var production = c.Get<IMfProductionService>();
                List<OrderListDto> flagged = await DrillRead.AllAsync(n => production.GetOrdersAsync(new OrderQuery { LateOnly = true, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct));
                List<OrderListDto> late = flagged.Where(o => OpenStatuses.Contains(o.Status) && o.IsLate).OrderByDescending(o => o.DaysLate).ThenBy(o => o.Number, StringComparer.Ordinal).ToList();
                List<Row> rows = late.Select(o => new Row("/production/orders/" + Id(o.Id))
                {
                    ["id"] = o.Id, ["number"] = o.Number, ["workOrderCode"] = o.WorkOrderCode, ["itemCode"] = o.ItemCode, ["itemTitle"] = o.ItemTitle, ["quantity"] = o.Quantity, ["status"] = OrderStatus(o.Status),
                    ["needDate"] = o.NeedDateText, ["plannedFinish"] = o.PlannedFinishText, ["daysLate"] = o.DaysLate, ["operationsDone"] = o.OperationsDone, ["operationCount"] = o.OperationCount,
                }).ToList();
                return DrillResult.Slice(rows, c, "تأخیر نتیجه‌ی آخرین اجرای زمان‌بند ظرفیت محدود است.", new Dictionary<string, object> { ["daysLate"] = late.Sum(o => o.DaysLate) });
            },
        };

        private static DrillDefinition OrdersByStatus() => new DrillDefinition
        {
            Key = "mf.orders_by_status", Module = M, Title = "دستورهای تولید به تفکیک وضعیت",
            Description = "تعداد دستورهای تولید در هر وضعیت (پیش‌نویس، آزادشده، در حال تولید، تکمیل‌شده، بسته‌شده، لغوشده) و تعداد دارای تأخیر در وضعیت‌های باز.",
            Link = "/production/orders",
            Columns = new[] { Col.Status("status", "وضعیت"), Col.Num("count", "تعداد"), Col.Num("late", "دارای تأخیر"), Col.Pct("sharePercent", "سهم از کل") },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProductionRoles.MasterReaders);
                var production = c.Get<IMfProductionService>();
                var counts = new List<(MfOrderStatus Status, int Count, int Late)>();
                foreach (MfOrderStatus status in Enum.GetValues(typeof(MfOrderStatus)).Cast<MfOrderStatus>())
                {
                    int count = Take(await production.GetOrdersAsync(new OrderQuery { Status = status, PageNumber = 1, PageSize = 1 }, ct)).TotalCount;
                    int late = OpenStatuses.Contains(status) ? Take(await production.GetOrdersAsync(new OrderQuery { Status = status, LateOnly = true, PageNumber = 1, PageSize = 1 }, ct)).TotalCount : 0;
                    counts.Add((status, count, late));
                }
                int total = counts.Sum(x => x.Count);
                List<Row> rows = counts.Select(x => new Row { ["status"] = OrderStatus(x.Status), ["count"] = x.Count, ["late"] = x.Late, ["sharePercent"] = Percent(x.Count, total) }).ToList();
                return DrillResult.Slice(rows, c, "تأخیر فقط برای دستورهای آزادشده و در حال تولید شمرده می‌شود.", new Dictionary<string, object>
                {
                    ["total"] = total, ["open"] = counts.Where(x => OpenStatuses.Contains(x.Status)).Sum(x => x.Count), ["late"] = counts.Sum(x => x.Late),
                });
            },
        };

        // ------------------------------------------------------------------------------------------ capacity

        private static DrillDefinition WorkCenterLoad() => new DrillDefinition
        {
            Key = "mf.work_center_load", Module = M, Title = "بار مراکز کار",
            Description = "بار و ظرفیت هر مرکز کار در هفته‌های آینده (از امروز) با اوج بار و تعداد هفته‌های بیش از آستانه؛ پیش‌فرض ۴ هفته و آستانه‌ی ۱۱۰ درصد (هم‌پایه با «هفته‌ی اضافه‌بار» داشبورد مدیرعامل).",
            Link = "/production/schedule", RecordType = "WorkCenter", RecordLabel = "مرکز کار",
            Parameters = new[]
            {
                Prm.Int("weeks", "تعداد هفته‌های پیش‌رو؛ پیش‌فرض 4 هفته", 4, 1, 26),
                Prm.Int("threshold", "آستانه‌ی اضافه‌بار بر حسب درصد ظرفیت؛ پیش‌فرض 110 درصد", 110, 50, 500),
            },
            Columns = new[]
            {
                Col.Text("code", "کد مرکز کار"), Col.Text("title", "مرکز کار"), Col.Num("capacityHours", "ظرفیت (ساعت)"), Col.Num("loadHours", "بار (ساعت)"), Col.Pct("loadPercent", "بار به ظرفیت"),
                Col.Pct("peakPercent", "اوج بار هفتگی"), Col.Date("peakWeek", "هفته‌ی اوج (شروع)"), Col.Num("overloadedWeeks", "هفته‌ی اضافه‌بار"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProductionRoles.Readers);
                int weeks = c.Args.Int("weeks"), threshold = c.Args.Int("threshold");
                LoadReportDto report = Take(await c.Get<IMfScheduleService>().GetLoadAsync(new LoadQuery { From = c.Today, Weeks = weeks, Threshold = threshold }, ct));
                var items = report.Rows.Select(r =>
                {
                    LoadCellDto peak = r.Cells.Where(x => x.LoadPercent.HasValue).OrderByDescending(x => x.LoadPercent.Value).ThenBy(x => x.WeekStart).FirstOrDefault();
                    decimal capacity = r.Cells.Sum(x => x.CapacityHours), load = r.Cells.Sum(x => x.LoadHours);
                    return new { Row = r, Capacity = capacity, Load = load, Peak = peak, Overloaded = r.Cells.Count(x => x.Overloaded) };
                }).OrderByDescending(x => x.Overloaded).ThenByDescending(x => x.Peak?.LoadPercent ?? 0m).ThenBy(x => x.Row.Code, StringComparer.Ordinal).ToList();
                List<Row> rows = items.Select(x => new Row("/production/schedule")
                {
                    ["id"] = x.Row.WorkCenterId, ["code"] = x.Row.Code, ["title"] = x.Row.Title, ["capacityHours"] = Round(x.Capacity), ["loadHours"] = Round(x.Load),
                    ["loadPercent"] = x.Capacity > 0m ? Percent(x.Load, x.Capacity) : (decimal?)null, ["peakPercent"] = x.Peak?.LoadPercent, ["peakWeek"] = x.Peak?.WeekStartText, ["overloadedWeeks"] = x.Overloaded,
                }).ToList();
                return DrillResult.Slice(rows, c, "بار = ساعت‌های موردنیاز دستورهای باز اگر هر عملیات در زودترین تاریخ شروع شود؛ ظرفیت = شیفت × ساعت × منابع × بهره‌وری منهای خرابی.",
                    new Dictionary<string, object> { ["overloadWeeks"] = report.Overloads.Count, ["capacityHours"] = Round(items.Sum(x => x.Capacity)), ["loadHours"] = Round(items.Sum(x => x.Load)) });
            },
        };

        // ------------------------------------------------------------------------------------------ machines and maintenance

        private static DrillDefinition MachinesDown() => new DrillDefinition
        {
            Key = "mf.machines_down", Module = M, Title = "ماشین‌های خراب",
            Description = "ماشین‌هایی که وضعیتشان «خراب» است، با مرکز کار، زمان شروع خرابی و سفارش نگهداری باز؛ طولانی‌ترین خرابی اول (هم‌پایه با «ماشین خراب» داشبورد).",
            Link = "/production/maintenance", RecordType = "Machine", RecordLabel = "ماشین",
            Columns = new[]
            {
                Col.Text("title", "ماشین"), Col.Text("workCenterCode", "مرکز کار"), Col.Text("model", "سازنده / مدل"), Col.Date("downSince", "خراب از"), Col.Num("daysDown", "روز خرابی"),
                Col.Text("maintenanceOrder", "سفارش نگهداری"), Col.Text("task", "شرح کار"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProductionRoles.MasterReaders);
                var master = c.Get<IMfMasterService>();
                var maintenance = c.Get<IMfMaintenanceService>();
                List<MachineDto> down = await DrillRead.AllAsync(n => master.GetMachinesAsync(new MachineQuery { Status = MfMachineStatus.Down, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct));
                List<MaintenanceOrderListDto> breakdowns = await DrillRead.AllAsync(n => maintenance.GetOrdersAsync(
                    new MaintenanceOrderQuery { Kind = MfMaintenanceKind.Breakdown, OpenOnly = true, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct));
                Dictionary<int, MaintenanceOrderListDto> latest = breakdowns.GroupBy(b => b.MachineId).ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.Id).First());
                var items = down.Select(m =>
                {
                    latest.TryGetValue(m.MachineId, out MaintenanceOrderListDto order);
                    return new { Machine = m, Order = order, Days = order?.DowntimeStart.HasValue == true ? Math.Max(0, (int)(c.Today - order.DowntimeStart.Value.Date).TotalDays) : (int?)null };
                }).OrderByDescending(x => x.Days ?? -1).ThenBy(x => x.Machine.Title, StringComparer.Ordinal).ToList();
                List<Row> rows = items.Select(x => new Row(x.Order != null ? "/production/maintenance-orders/" + Id(x.Order.Id) : "/production/maintenance")
                {
                    ["id"] = x.Machine.MachineId, ["title"] = x.Machine.Title, ["workCenterCode"] = x.Machine.WorkCenterCode, ["model"] = string.Join(" ", new[] { x.Machine.Maker, x.Machine.Model }.Where(s => !string.IsNullOrWhiteSpace(s))),
                    ["downSince"] = x.Order?.DowntimeStartText, ["daysDown"] = x.Days, ["maintenanceOrder"] = x.Order?.Number, ["task"] = x.Order?.Task,
                }).ToList();
                return DrillResult.Slice(rows, c, "زمان خرابی از سفارش نگهداری باز نوع «خرابی» خوانده می‌شود؛ بدون آن خالی است.");
            },
        };

        private static string MaintenanceKind(MfMaintenanceKind kind) => kind == MfMaintenanceKind.Planned ? "پیشگیرانه" : "خرابی";

        private static DrillDefinition MaintenanceOverdue() => new DrillDefinition
        {
            Key = "mf.maintenance_overdue", Module = M, Title = "سفارش‌های نگهداری معوق",
            Description = "سفارش‌های نگهداری باز یا در حال انجام که سررسیدشان گذشته است (هم‌پایه با «نگهداری معوق» داشبورد)؛ بیشترین تأخیر اول.",
            Link = "/production/maintenance", RecordType = "MaintenanceOrder", RecordLabel = "سفارش نگهداری",
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("machine", "ماشین"), Col.Text("kind", "نوع"), Col.Text("task", "شرح کار"), Col.Status("status", "وضعیت"), Col.Date("dueDate", "سررسید"),
                Col.Num("daysOverdue", "روز تأخیر"), Col.Num("estimatedHours", "ساعت برآوردی"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProductionRoles.MasterReaders);
                var maintenance = c.Get<IMfMaintenanceService>();
                List<MaintenanceOrderListDto> open = await DrillRead.AllAsync(n => maintenance.GetOrdersAsync(new MaintenanceOrderQuery { OpenOnly = true, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct));
                List<MaintenanceOrderListDto> overdue = open.Where(o => o.DueDate.HasValue && o.DueDate.Value.Date < c.Today).OrderBy(o => o.DueDate.Value.Date).ThenBy(o => o.Number, StringComparer.Ordinal).ToList();
                List<Row> rows = overdue.Select(o => new Row("/production/maintenance-orders/" + Id(o.Id))
                {
                    ["id"] = o.Id, ["number"] = o.Number, ["machine"] = o.MachineTitle, ["kind"] = MaintenanceKind(o.Kind), ["task"] = o.Task != null && o.Task.Length > 120 ? o.Task.Substring(0, 120) : o.Task,
                    ["status"] = o.Status == MfMaintenanceOrderStatus.Open ? "باز" : "در حال انجام", ["dueDate"] = o.DueDateText, ["daysOverdue"] = (int)(c.Today - o.DueDate.Value.Date).TotalDays,
                    ["estimatedHours"] = o.EstimatedHours,
                }).ToList();
                return DrillResult.Slice(rows, c, "معوق: سفارش باز یا در حال انجام با سررسید قبل از امروز.", new Dictionary<string, object> { ["estimatedHours"] = overdue.Sum(o => o.EstimatedHours) });
            },
        };

        private static DrillDefinition OeeByWorkCenter() => new DrillDefinition
        {
            Key = "mf.oee_by_work_center", Module = M, Title = "اثربخشی کلی تجهیزات (OEE) به تفکیک مرکز کار",
            Description = "دسترس‌پذیری، عملکرد، کیفیت و OEE هر مرکز کار در بازه؛ کمترین OEE اول. بازه‌ی پیش‌فرض: از اول ماه شمسی جاری تا امروز (هم‌پایه با داشبورد تولید).",
            Link = "/production/oee", RecordType = "WorkCenter", RecordLabel = "مرکز کار",
            Parameters = Prm.MonthToDate(),
            Columns = new[]
            {
                Col.Text("code", "کد مرکز کار"), Col.Text("title", "مرکز کار"), Col.Num("plannedHours", "ساعت برنامه‌ای"), Col.Num("downtimeHours", "ساعت توقف"), Col.Num("bookedRunHours", "ساعت کارکرد"),
                Col.Num("goodQty", "تعداد سالم"), Col.Num("scrapQty", "تعداد ضایعات"), Col.Pct("availabilityPercent", "دسترس‌پذیری"), Col.Pct("performancePercent", "عملکرد"), Col.Pct("qualityPercent", "کیفیت"),
                Col.Pct("oeePercent", "OEE"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProductionRoles.Readers);
                List<OeeDto> oee = Take(await c.Get<IMfMaintenanceService>().GetOeeAsync(new KpiQuery { From = c.Args.Date("from"), To = c.Args.Date("to"), PageNumber = 1, PageSize = 500 }, ct));
                List<Row> rows = oee.OrderBy(o => o.OeePercent ?? 1000m).ThenBy(o => o.WorkCenterCode, StringComparer.Ordinal).Select(o => new Row("/production/oee")
                {
                    ["id"] = o.WorkCenterId, ["code"] = o.WorkCenterCode, ["title"] = o.WorkCenterTitle, ["plannedHours"] = o.PlannedHours, ["downtimeHours"] = o.DowntimeHours, ["bookedRunHours"] = o.BookedRunHours,
                    ["goodQty"] = o.GoodQty, ["scrapQty"] = o.ScrapQty, ["availabilityPercent"] = o.AvailabilityPercent, ["performancePercent"] = o.PerformancePercent, ["qualityPercent"] = o.QualityPercent,
                    ["oeePercent"] = o.OeePercent,
                }).ToList();
                OeeDto total = MfKpi.Compute(new OeeDto
                {
                    PlannedHours = oee.Sum(o => o.PlannedHours), DowntimeHours = oee.Sum(o => o.DowntimeHours), BookedRunHours = oee.Sum(o => o.BookedRunHours), StandardHours = oee.Sum(o => o.StandardHours),
                    GoodQty = oee.Sum(o => o.GoodQty), ScrapQty = oee.Sum(o => o.ScrapQty),
                });
                return DrillResult.Slice(rows, c, "OEE = دسترس‌پذیری × عملکرد × کیفیت؛ بدون کارکرد یا تعداد ثبت‌شده خالی است.", new Dictionary<string, object>
                {
                    ["oeePercent"] = total.OeePercent, ["availabilityPercent"] = total.AvailabilityPercent, ["performancePercent"] = total.PerformancePercent, ["qualityPercent"] = total.QualityPercent,
                });
            },
        };
    }
}
