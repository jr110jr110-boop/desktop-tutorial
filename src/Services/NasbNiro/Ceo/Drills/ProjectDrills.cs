using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;
using static Services.NasbNiro.Ceo.Drills.DrillText;

namespace Services.NasbNiro.Ceo.Drills
{
    /// <summary>The project drills: the portfolio, late activities, payment milestones, change requests, guarantees, penalties, initiation steps and client responses.</summary>
    internal static class ProjectDrills
    {
        private const string M = CeoModules.Projects;

        public static IEnumerable<DrillDefinition> Build()
        {
            yield return Portfolio();
            yield return LateActivities();
            yield return MilestonesNext();
            yield return ChangeRequestsOpen();
            yield return GuaranteesExpiring();
            yield return PenaltyExposure();
            yield return InitiationOverdue();
            yield return ClientResponsesOverdue();
        }

        private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

        private static string ProjectLink(int contractId) => "/project-control/project?contract=" + Id(contractId);

        private sealed class ProjectInfo
        {
            public string Code { get; set; }
            public string Title { get; set; }

            /// <summary>"code — title" for a row that has no separate columns.</summary>
            public string Label => string.IsNullOrEmpty(Code) ? Title : string.IsNullOrEmpty(Title) ? Code : Code + " — " + Title;
        }

        /// <summary>The title and the project code of contracts (read only lookup for the rows).</summary>
        private static async Task<Dictionary<int, ProjectInfo>> ProjectsAsync(IUnitOfWork uow, IEnumerable<int> contractIds, CancellationToken ct)
        {
            List<int> ids = contractIds.Distinct().ToList();
            var result = new Dictionary<int, ProjectInfo>();
            if (ids.Count == 0) return result;
            foreach (var c in await uow.Set<ContractEntity>().AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Title }).ToListAsync(ct))
                result[c.Id] = new ProjectInfo { Title = c.Title };
            foreach (var t in await uow.Set<PcContractTerms>().AsNoTracking().Where(x => !x.Deleted && ids.Contains(x.ContractId)).Select(x => new { x.ContractId, x.ProjectCode }).ToListAsync(ct))
            {
                if (!result.TryGetValue(t.ContractId, out ProjectInfo info)) result[t.ContractId] = info = new ProjectInfo();
                info.Code = t.ProjectCode;
            }
            return result;
        }

        private static ProjectInfo InfoOf(Dictionary<int, ProjectInfo> projects, int contractId) => projects.TryGetValue(contractId, out ProjectInfo info) ? info : new ProjectInfo();

        /// <summary>The module writes red, amber, green or none (no value yet).</summary>
        private static bool HasValue(string status) => !string.IsNullOrEmpty(status) && status != "none";

        private static string Traffic(string status) => status switch
        {
            "red" => "قرمز",
            "amber" => "زرد",
            "green" => "سبز",
            _ => "بدون داده",
        };

        /// <summary>The worse of the two lights; without a value on either index there is no light.</summary>
        internal static string Traffic(string spi, string cpi) => spi == "red" || cpi == "red" ? "قرمز" : spi == "amber" || cpi == "amber" ? "زرد" : !HasValue(spi) && !HasValue(cpi) ? "بدون داده" : "سبز";

        /// <summary>The filter of the portfolio drill: danger = SPI or CPI red, warn = SPI or CPI amber (the same test as the counters of the project dashboard, so a project can be on both lists), ok = no red and no amber and at least one value.</summary>
        /// <summary>One colour per project, the rule of the portfolio dashboard and the overview (PcReportService.IsRed / IsAmber): red when an index is red, amber when one is amber and none is red, else ok.</summary>
        internal static bool InStatus(PcPortfolioRowDto r, string status) => status switch
        {
            "danger" => PcReportService.IsRed(r),
            "warn" => PcReportService.IsAmber(r),
            "ok" => !PcReportService.IsRed(r) && !PcReportService.IsAmber(r) && (HasValue(r.SpiStatus) || HasValue(r.CpiStatus)),
            _ => true,
        };

        private static string Short(string text, int length = 120) => text != null && text.Length > length ? text.Substring(0, length) : text;

        // ------------------------------------------------------------------------------------------ portfolio

        private static DrillDefinition Portfolio() => new DrillDefinition
        {
            Key = "pc.portfolio", Module = M, Title = "سبد پروژه‌ها",
            Description = "همه‌ی پروژه‌های فعال با پیشرفت فیزیکی و برنامه‌ای، شاخص‌های SPI و CPI، وضعیت (قرمز/زرد/سبز)، تاخیرها و هشدارها؛ بدترین SPI اول (هم‌پایه با داشبورد پروژه‌ها). با status فقط پروژه‌های یک وضعیت: danger (SPI یا CPI قرمز)، warn (SPI یا CPI زرد و هیچ‌کدام قرمز) یا ok (نه قرمز و نه زرد)؛ هر پروژه یک رنگ دارد، پس قرمز + زرد + سبز = شمار پروژه‌ها (هم‌پایه با سرفصل «پروژه‌های قرمز/زرد» داشبورد مدیرعامل).",
            Link = "/project-control/portfolio", RecordType = "Project", RecordLabel = "پروژه",
            Parameters = new[]
            {
                Prm.Str("status", "فقط پروژه‌های یک وضعیت: danger (قرمز)، warn (زرد) یا ok (سبز)؛ پیش‌فرض همه‌ی پروژه‌های فعال", null, new[] { "danger", "warn", "ok" }),
            },
            Columns = new[]
            {
                Col.Text("projectCode", "کد پروژه"), Col.Text("projectTitle", "عنوان"), Col.Status("status", "وضعیت"), Col.Pct("physicalPercent", "پیشرفت فیزیکی"), Col.Pct("plannedPercent", "پیشرفت برنامه‌ای"),
                Col.Num("spi", "SPI"), Col.Status("spiStatus", "رنگ SPI"), Col.Num("cpi", "CPI"), Col.Status("cpiStatus", "رنگ CPI"), Col.Date("forecastFinish", "پیش‌بینی پایان"), Col.Num("lateActivities", "فعالیت دیرکرد"),
                Col.Num("openChanges", "تغییر باز"), Col.Num("guaranteeAlerts", "هشدار تضمین"), Col.Num("overdueMilestones", "مایل‌استون معوق"), Col.Rial("penaltyExposure", "جریمه‌ی محاسبه‌شده"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProjectControlRoles.Readers);
                string status = c.Args.Str("status");
                PagedList<PcPortfolioRowDto> portfolio = Take(await c.Get<IPcReportService>().GetPortfolioAsync(new PaginationParams { PageNumber = 1, PageSize = 0 }, ct));
                List<Row> rows = portfolio.Where(r => InStatus(r, status)).OrderBy(r => r.Spi ?? 9m).ThenBy(r => r.ProjectCode, StringComparer.Ordinal).Select(r => new Row(ProjectLink(r.ContractId))
                {
                    ["id"] = r.ContractId, ["projectCode"] = r.ProjectCode, ["projectTitle"] = r.ContractTitle, ["status"] = Traffic(r.SpiStatus, r.CpiStatus), ["physicalPercent"] = r.PhysicalPercent,
                    ["plannedPercent"] = r.PlannedPercent, ["spi"] = r.Spi, ["spiStatus"] = Traffic(r.SpiStatus), ["cpi"] = r.Cpi, ["cpiStatus"] = Traffic(r.CpiStatus), ["forecastFinish"] = DrillDates.Text(r.ForecastFinish),
                    ["lateActivities"] = r.LateActivities, ["openChanges"] = r.OpenChanges, ["guaranteeAlerts"] = r.GuaranteeAlerts, ["overdueMilestones"] = r.OverdueMilestones, ["penaltyExposure"] = r.PenaltyExposure,
                }).ToList();
                return DrillResult.Slice(rows, c, "SPI و CPI کمتر از یک یعنی عقب از برنامه / بیش از بودجه؛ رنگ از آستانه‌های تنظیمات پروژه می‌آید. شمارنده‌های خلاصه همیشه برای همه‌ی پروژه‌های فعال است، نه فقط وضعیت انتخاب‌شده.", new Dictionary<string, object>
                {
                    ["activeProjects"] = portfolio.Count, ["red"] = portfolio.Count(PcReportService.IsRed), ["amber"] = portfolio.Count(PcReportService.IsAmber),
                    ["lateActivities"] = portfolio.Sum(r => r.LateActivities), ["penaltyExposure"] = portfolio.Sum(r => r.PenaltyExposure),
                });
            },
        };

        // ------------------------------------------------------------------------------------------ schedule

        private static string ActivityStatus(PcActivityStatus status) => status switch
        {
            PcActivityStatus.NotStarted => "شروع‌نشده",
            PcActivityStatus.InProgress => "در حال انجام",
            PcActivityStatus.Done => "انجام‌شده",
            _ => "متوقف",
        };

        private static DrillDefinition LateActivities() => new DrillDefinition
        {
            Key = "pc.late_activities", Module = M, Title = "فعالیت‌های دارای تاخیر",
            Description = "فعالیت‌های پروژه‌هایی که پیش‌بینی پایانشان از پایان برنامه‌ریزی‌شده دیرتر است (فلگ تاخیر برنامه‌ریزی)؛ بیشترین روز تاخیر اول. با onlyCritical=true فقط فعالیت‌های مسیر بحرانی.",
            Link = "/project-control/activities", RecordType = "Activity", RecordLabel = "فعالیت",
            Parameters = new[] { Prm.Bool("onlyCritical", "فقط فعالیت‌های مسیر بحرانی؛ پیش‌فرض false") },
            Columns = new[]
            {
                Col.Text("projectCode", "کد پروژه"), Col.Text("code", "کد فعالیت"), Col.Text("title", "عنوان"), Col.Status("status", "وضعیت"), Col.Pct("percent", "پیشرفت"),
                Col.Date("plannedFinish", "پایان برنامه"), Col.Date("forecastFinish", "پیش‌بینی پایان"), Col.Num("lateDays", "روز تاخیر"), Col.Status("critical", "بحرانی"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProjectControlRoles.Readers);
                PagedList<PcActivityDto> late = Take(await c.Get<IPcScheduleService>().GetActivitiesAsync(
                    new PcActivityQuery { OnlyLate = true, OnlyCritical = c.Args.Bool("onlyCritical"), PageNumber = 1, PageSize = 0 }, ct));
                Dictionary<int, ProjectInfo> projects = await ProjectsAsync(c.Get<IUnitOfWork>(), late.Select(a => a.ContractId), ct);
                var items = late.Select(a => new { Activity = a, Days = a.ForecastFinish.HasValue && a.EarlyFinish.HasValue ? Math.Max(0, (int)(a.ForecastFinish.Value.Date - a.EarlyFinish.Value.Date).TotalDays) : 0 })
                    .OrderByDescending(x => x.Days).ThenByDescending(x => x.Activity.IsCritical).ThenBy(x => x.Activity.Code, StringComparer.Ordinal).ToList();
                List<Row> rows = items.Select(x => new Row("/project-control/activities")
                {
                    ["id"] = x.Activity.Id, ["projectCode"] = InfoOf(projects, x.Activity.ContractId).Label, ["code"] = x.Activity.Code, ["title"] = Short(x.Activity.Title), ["status"] = ActivityStatus(x.Activity.Status),
                    ["percent"] = x.Activity.PercentCache, ["plannedFinish"] = DrillDates.Text(x.Activity.EarlyFinish), ["forecastFinish"] = DrillDates.Text(x.Activity.ForecastFinish), ["lateDays"] = x.Days,
                    ["critical"] = x.Activity.IsCritical ? "بحرانی" : "غیر بحرانی",
                }).ToList();
                return DrillResult.Slice(rows, c, "روز تاخیر = پیش‌بینی پایان منهای پایان برنامه‌ریزی‌شده (روز تقویمی).", new Dictionary<string, object>
                {
                    ["critical"] = items.Count(x => x.Activity.IsCritical), ["projects"] = items.Select(x => x.Activity.ContractId).Distinct().Count(),
                });
            },
        };

        // ------------------------------------------------------------------------------------------ commercial

        private static string MilestoneKind(PcMilestoneKind kind) => kind switch
        {
            PcMilestoneKind.Advance => "پیش‌پرداخت",
            PcMilestoneKind.Progress => "پیشرفت",
            PcMilestoneKind.Delivery => "تحویل",
            PcMilestoneKind.Retention => "آزادسازی سپرده",
            _ => "سایر",
        };

        private static string MilestoneStatus(PcMilestoneStatus status) => status switch
        {
            PcMilestoneStatus.Planned => "برنامه‌ریزی‌شده",
            PcMilestoneStatus.Due => "سررسیده",
            PcMilestoneStatus.StatementIssued => "صورت‌وضعیت صادر شد",
            PcMilestoneStatus.Invoiced => "فاکتور شد",
            _ => "پرداخت شد",
        };

        private static DrillDefinition MilestonesNext() => new DrillDefinition
        {
            Key = "pc.milestones_next", Module = M, Title = "مایل‌استون‌های پرداخت پیش‌رو",
            Description = "مایل‌استون‌های پرداخت قراردادها که هنوز صورت‌وضعیت نشده‌اند (برنامه‌ریزی‌شده یا سررسیده) و تاریخ برنامه‌شان تا چند روز آینده است یا گذشته است؛ نزدیک‌ترین تاریخ اول.",
            Link = "/project-control/milestones", RecordType = "Milestone", RecordLabel = "مایل‌استون",
            Parameters = new[] { Prm.Days(30, 365, "مایل‌استون‌هایی که تاریخ برنامه‌شان تا این تعداد روز آینده است (معوق‌ها هم می‌آید)") },
            Columns = new[]
            {
                Col.Text("projectCode", "کد پروژه"), Col.Num("seq", "ردیف"), Col.Text("title", "عنوان"), Col.Text("kind", "نوع"), Col.Status("status", "وضعیت"), Col.Date("plannedDate", "تاریخ برنامه"),
                Col.Num("daysTo", "روز تا تاریخ (منفی: معوق)"), Col.Rial("amount", "مبلغ"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProjectControlRoles.Readers);
                DateTime limit = c.Today.AddDays(c.Args.Int("days"));
                PagedList<PcMilestoneDto> all = Take(await c.Get<IPcCommercialService>().GetMilestonesAsync(new PcContractQuery { PageNumber = 1, PageSize = 0 }, ct));
                List<PcMilestoneDto> next = all.Where(m => (m.Status == PcMilestoneStatus.Planned || m.Status == PcMilestoneStatus.Due) && (m.PlannedDate.HasValue ? m.PlannedDate.Value.Date <= limit : m.Status == PcMilestoneStatus.Due))
                    .OrderBy(m => m.PlannedDate.HasValue ? 0 : 1).ThenBy(m => m.PlannedDate).ThenBy(m => m.ContractId).ThenBy(m => m.Seq).ToList();
                Dictionary<int, ProjectInfo> projects = await ProjectsAsync(c.Get<IUnitOfWork>(), next.Select(m => m.ContractId), ct);
                List<Row> rows = next.Select(m => new Row("/project-control/milestones")
                {
                    ["id"] = m.Id, ["projectCode"] = InfoOf(projects, m.ContractId).Label, ["seq"] = m.Seq, ["title"] = Short(m.Title), ["kind"] = MilestoneKind(m.Kind), ["status"] = MilestoneStatus(m.Status),
                    ["plannedDate"] = DrillDates.Text(m.PlannedDate), ["daysTo"] = m.PlannedDate.HasValue ? (int)(m.PlannedDate.Value.Date - c.Today).TotalDays : (int?)null, ["amount"] = m.Amount,
                }).ToList();
                return DrillResult.Slice(rows, c, "معوق: مایل‌استون برنامه‌ریزی‌شده‌ای که تاریخ برنامه‌اش گذشته است (هم‌پایه با «مایل‌استون معوق» داشبورد).", new Dictionary<string, object>
                {
                    ["amount"] = next.Sum(m => m.Amount), ["overdue"] = next.Count(m => m.Status == PcMilestoneStatus.Planned && m.PlannedDate.HasValue && m.PlannedDate.Value.Date < c.Today),
                });
            },
        };

        private static string ChangeType(PcChangeType type) => type switch
        {
            PcChangeType.Scope => "دامنه",
            PcChangeType.Schedule => "زمان‌بندی",
            PcChangeType.Assumption => "فرض",
            PcChangeType.Constraint => "محدودیت",
            PcChangeType.Team => "تیم",
            PcChangeType.Method => "روش",
            PcChangeType.Standard => "استاندارد",
            PcChangeType.ClientApprovalCondition => "شرط تأیید کارفرما",
            PcChangeType.CommunicationPlan => "برنامه ارتباطات",
            _ => "سیستم اندازه‌گیری",
        };

        private static DrillDefinition ChangeRequestsOpen() => new DrillDefinition
        {
            Key = "pc.change_requests_open", Module = M, Title = "درخواست‌های تغییر باز",
            Description = "درخواست‌های تغییر ثبت‌شده و در حال تحلیل (هم‌پایه با «تغییر باز» داشبورد) با مهلت تحلیل و اثر درخواستی بر مبلغ و زمان؛ نزدیک‌ترین مهلت اول.",
            Link = "/project-control/changes", RecordType = "ChangeRequest", RecordLabel = "درخواست تغییر",
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("projectCode", "کد پروژه"), Col.Text("type", "نوع"), Col.Text("description", "شرح"), Col.Status("status", "وضعیت"), Col.Date("dueDate", "مهلت تحلیل"),
                Col.Num("daysToDue", "روز تا مهلت (منفی: معوق)"), Col.Rial("priceDelta", "اثر مبلغ"), Col.Num("daysDelta", "اثر زمان (روز)"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProjectControlRoles.Readers);
                var baseline = c.Get<IPcBaselineService>();
                var open = new List<PcChangeDto>();
                foreach (PcChangeStatus status in new[] { PcChangeStatus.Registered, PcChangeStatus.InAnalysis })
                    open.AddRange(Take(await baseline.GetChangesAsync(new PcChangeQuery { Status = status, PageNumber = 1, PageSize = 0 }, ct)));
                open = open.OrderBy(x => x.DueDate.HasValue ? 0 : 1).ThenBy(x => x.DueDate).ThenBy(x => x.Number, StringComparer.Ordinal).ToList();
                Dictionary<int, ProjectInfo> projects = await ProjectsAsync(c.Get<IUnitOfWork>(), open.Select(x => x.ContractId), ct);
                List<Row> rows = open.Select(x => new Row("/project-control/changes" + (x.Id.HasValue ? "?id=" + Id(x.Id.Value) : string.Empty))
                {
                    ["id"] = x.Id, ["number"] = x.Number, ["projectCode"] = InfoOf(projects, x.ContractId).Label, ["type"] = ChangeType(x.Type), ["description"] = Short(x.Description),
                    ["status"] = x.Status == PcChangeStatus.Registered ? "ثبت‌شده" : "در تحلیل", ["dueDate"] = DrillDates.Text(x.DueDate),
                    ["daysToDue"] = x.DueDate.HasValue ? (int)(x.DueDate.Value.Date - c.Today).TotalDays : (int?)null, ["priceDelta"] = x.RequestedPriceDelta, ["daysDelta"] = x.RequestedDaysDelta,
                }).ToList();
                var overdue = open.Where(x => x.DueDate.HasValue && x.DueDate.Value.Date < c.Today).ToList();
                return DrillResult.Slice(rows, c, "معوق: مهلت تحلیل گذشته است؛ شمارنده‌ی داشبورد تعداد پروژه‌های دارای درخواست معوق است.", new Dictionary<string, object>
                {
                    ["overdue"] = overdue.Count, ["overdueProjects"] = overdue.Select(x => x.ContractId).Distinct().Count(), ["priceDelta"] = open.Sum(x => x.RequestedPriceDelta),
                });
            },
        };

        private static string GuaranteeType(PcGuaranteeType type) => type switch
        {
            PcGuaranteeType.Bid => "شرکت در مناقصه",
            PcGuaranteeType.Advance => "پیش‌پرداخت",
            PcGuaranteeType.Performance => "حسن انجام کار",
            PcGuaranteeType.Warranty => "ضمانت کیفیت",
            _ => "سایر",
        };

        private static DrillDefinition GuaranteesExpiring() => new DrillDefinition
        {
            Key = "pc.guarantees_expiring", Module = M, Title = "ضمانت‌نامه‌های در آستانه‌ی انقضا",
            Description = "ضمانت‌نامه‌های صادرشده/فعال/در انتظار تمدید که تا آخرین آستانه‌ی هشدار تنظیمات پروژه (پیش‌فرض ۳۰ روز) منقضی می‌شوند یا منقضی شده‌اند؛ نزدیک‌ترین انقضا اول.",
            Link = "/project-control/guarantees", RecordType = "Guarantee", RecordLabel = "ضمانت‌نامه",
            Columns = new[]
            {
                Col.Text("no", "شماره"), Col.Text("title", "عنوان"), Col.Text("projectCode", "کد پروژه"), Col.Text("type", "نوع"), Col.Text("bank", "بانک / صادرکننده"), Col.Rial("amount", "مبلغ"),
                Col.Date("expiryDate", "تاریخ انقضا"), Col.Num("daysToExpiry", "روز تا انقضا (منفی: منقضی)"), Col.Status("alert", "هشدار"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProjectControlRoles.Readers);
                var commercial = c.Get<IPcCommercialService>();
                List<PcGuaranteeAlertDto> alerts = await commercial.GuaranteeAlertsAsync(null, ct);
                var details = new Dictionary<int, PcGuaranteeDto>();
                foreach (PcGuaranteeStatus status in new[] { PcGuaranteeStatus.Issued, PcGuaranteeStatus.Active, PcGuaranteeStatus.ExtensionPending })
                    foreach (PcGuaranteeDto g in Take(await commercial.GetGuaranteesAsync(new PcGuaranteeQuery { Status = status, PageNumber = 1, PageSize = 0 }, ct)))
                        if (g.Id.HasValue) details[g.Id.Value] = g;
                Dictionary<int, ProjectInfo> projects = await ProjectsAsync(c.Get<IUnitOfWork>(), alerts.Where(a => a.ContractId.HasValue).Select(a => a.ContractId.Value), ct);
                List<Row> rows = alerts.OrderBy(a => a.ExpiryDate).ThenBy(a => a.GuaranteeId).Select(a =>
                {
                    details.TryGetValue(a.GuaranteeId, out PcGuaranteeDto d);
                    return new Row("/project-control/guarantees")
                    {
                        ["id"] = a.GuaranteeId, ["no"] = a.No, ["title"] = Short(a.Title), ["projectCode"] = a.ContractId.HasValue ? InfoOf(projects, a.ContractId.Value).Label : null,
                        ["type"] = d != null ? GuaranteeType(d.GuaranteeType) : null, ["bank"] = d?.Bank, ["amount"] = d?.AmountDecimal, ["expiryDate"] = DrillDates.Text(a.ExpiryDate),
                        ["daysToExpiry"] = a.DaysToExpiry, ["alert"] = a.AlertLevel == 0 ? "منقضی‌شده" : "هشدار " + Id(a.AlertLevel) + " روزه",
                    };
                }).ToList();
                return DrillResult.Slice(rows, c, "شمارنده‌ی داشبورد فقط ضمانت‌نامه‌های وابسته به قرارداد پروژه‌های فعال را می‌شمارد.", new Dictionary<string, object>
                {
                    ["expired"] = alerts.Count(a => a.AlertLevel == 0), ["amount"] = alerts.Sum(a => details.TryGetValue(a.GuaranteeId, out PcGuaranteeDto d) ? d.AmountDecimal ?? 0m : 0m),
                });
            },
        };

        private static DrillDefinition PenaltyExposure() => new DrillDefinition
        {
            Key = "pc.penalty_exposure", Module = M, Title = "ریسک جریمه‌ی تاخیر",
            Description = "جریمه‌های تاخیر محاسبه‌شده‌ای که هنوز بخشوده یا توافق نشده‌اند (مجموع = «ریسک جریمه» داشبورد)؛ بیشترین مبلغ اول.",
            Link = "/project-control/penalties", RecordType = "Penalty", RecordLabel = "جریمه",
            Columns = new[]
            {
                Col.Text("projectCode", "کد پروژه"), Col.Text("subject", "موضوع"), Col.Date("dueDate", "سررسید تعهد"), Col.Date("actualDate", "تاریخ تحقق"), Col.Num("lateDays", "روز تاخیر"),
                Col.Num("ratePercentPerWeek", "نرخ هفتگی (درصد)"), Col.Rial("baseAmount", "مبنای محاسبه"), Col.Rial("amount", "مبلغ جریمه"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProjectControlRoles.Readers);
                PagedList<PcPenaltyDto> all = Take(await c.Get<IPcCommercialService>().GetPenaltiesAsync(new PcContractQuery { PageNumber = 1, PageSize = 0 }, ct));
                List<PcPenaltyDto> computed = all.Where(p => p.Status == PcPenaltyStatus.Computed).ToList();
                List<PcPenaltyDto> shown = computed.Where(p => p.Amount > 0m).OrderByDescending(p => p.Amount).ThenBy(p => p.Id).ToList();
                Dictionary<int, ProjectInfo> projects = await ProjectsAsync(c.Get<IUnitOfWork>(), shown.Select(p => p.ContractId), ct);
                List<Row> rows = shown.Select(p => new Row("/project-control/penalties")
                {
                    ["id"] = p.Id, ["projectCode"] = InfoOf(projects, p.ContractId).Label, ["subject"] = Short(p.Subject), ["dueDate"] = DrillDates.Text(p.DueDate), ["actualDate"] = DrillDates.Text(p.ActualDate),
                    ["lateDays"] = p.LateDays, ["ratePercentPerWeek"] = p.RatePercentPerWeek, ["baseAmount"] = p.BaseAmount, ["amount"] = p.Amount,
                }).ToList();
                return DrillResult.Slice(rows, c, "فقط جریمه‌های محاسبه‌شده با مبلغ بیشتر از صفر؛ بخشوده‌شده و توافقی شمرده نمی‌شود.",
                    new Dictionary<string, object> { ["penaltyExposure"] = computed.Sum(p => p.Amount), ["projects"] = shown.Select(p => p.ContractId).Distinct().Count() });
            },
        };

        // ------------------------------------------------------------------------------------------ set-up and documents

        private static DrillDefinition InitiationOverdue() => new DrillDefinition
        {
            Key = "pc.initiation_overdue", Module = M, Title = "گام‌های راه‌اندازی پروژه‌ی عقب‌افتاده",
            Description = "گام‌های راه‌اندازی پروژه (کدگذاری، انتخاب مدیر پروژه، منشور، PEP، جلسه‌ی KOM و ...) که مهلت سطح خدمت‌شان گذشته و انجام نشده‌اند؛ بیشترین تاخیر اول.",
            Link = "/project-control/project", RecordType = "Project", RecordLabel = "پروژه",
            Columns = new[]
            {
                Col.Text("projectCode", "کد پروژه"), Col.Text("step", "گام"), Col.Text("gateRole", "نقش مسئول"), Col.Num("slaDays", "مهلت (روز)"), Col.Date("dueDate", "مهلت انجام"), Col.Num("daysOverdue", "روز تاخیر"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProjectControlRoles.Readers);
                List<PcInitiationStepDto> steps = await c.Get<IPcProjectService>().OverdueStepsAsync(ct);
                // the service carries the contract id in the note of the step
                var items = steps.Select(s => new { Step = s, ContractId = int.TryParse(s.Note, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : 0 }).ToList();
                Dictionary<int, ProjectInfo> projects = await ProjectsAsync(c.Get<IUnitOfWork>(), items.Select(x => x.ContractId).Where(i => i > 0), ct);
                List<Row> rows = items.OrderBy(x => x.Step.DueDate).ThenBy(x => x.ContractId).ThenBy(x => (int)x.Step.Step).Select(x => new Row(x.ContractId > 0 ? ProjectLink(x.ContractId) : null)
                {
                    ["id"] = x.ContractId > 0 ? x.ContractId : (int?)null, ["projectCode"] = InfoOf(projects, x.ContractId).Label, ["step"] = x.Step.Title, ["gateRole"] = x.Step.GateRole, ["slaDays"] = x.Step.SlaDays,
                    ["dueDate"] = DrillDates.Text(x.Step.DueDate), ["daysOverdue"] = x.Step.DueDate.HasValue ? Math.Max(0, (int)(c.Today - x.Step.DueDate.Value.Date).TotalDays) : 0,
                }).ToList();
                return DrillResult.Slice(rows, c, "شمارنده‌ی داشبورد تعداد پروژه‌هایی است که دست‌کم یک گام عقب‌افتاده دارند.",
                    new Dictionary<string, object> { ["steps"] = items.Count, ["projects"] = items.Where(x => x.ContractId > 0).Select(x => x.ContractId).Distinct().Count() });
            },
        };

        private static DrillDefinition ClientResponsesOverdue() => new DrillDefinition
        {
            Key = "pc.client_responses_overdue", Module = M, Title = "پاسخ‌های معوق کارفرما",
            Description = "ترنسمیتال‌های ارسالی برای تأیید که مهلت پاسخ کارفرما گذشته و دست‌کم یک سندشان پاسخ نگرفته است؛ بیشترین تاخیر اول (مجموع اسناد بدون پاسخ = «پاسخ معوق کارفرما» داشبورد).",
            Link = "/project-control/transmittals", RecordType = "Transmittal", RecordLabel = "ترنسمیتال",
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("projectCode", "کد پروژه"), Col.Date("transmittalDate", "تاریخ ارسال"), Col.Date("responseDue", "مهلت پاسخ"), Col.Num("daysOverdue", "روز تاخیر"),
                Col.Num("overdueDocuments", "سند بدون پاسخ"), Col.Text("reference", "ارجاع"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProjectControlRoles.Readers);
                PagedList<PcTransmittalDto> overdue = Take(await c.Get<IPcMdrService>().GetTransmittalsAsync(new PcTransmittalQuery { OnlyOverdue = true, PageNumber = 1, PageSize = 0 }, ct));
                var items = overdue.Select(t => new { Transmittal = t, Open = t.Lines.Count(l => l.Purpose == PcTransmittalPurpose.ForApproval && l.ResponseCode == null) })
                    .Where(x => x.Open > 0 && x.Transmittal.ResponseDue.HasValue).OrderBy(x => x.Transmittal.ResponseDue).ThenBy(x => x.Transmittal.Number, StringComparer.Ordinal).ToList();
                Dictionary<int, ProjectInfo> projects = await ProjectsAsync(c.Get<IUnitOfWork>(), items.Select(x => x.Transmittal.ContractId), ct);
                List<Row> rows = items.Select(x => new Row("/project-control/transmittals")
                {
                    ["id"] = x.Transmittal.Id, ["number"] = x.Transmittal.Number, ["projectCode"] = InfoOf(projects, x.Transmittal.ContractId).Label, ["transmittalDate"] = DrillDates.Text(x.Transmittal.TransmittalDate),
                    ["responseDue"] = DrillDates.Text(x.Transmittal.ResponseDue), ["daysOverdue"] = Math.Max(0, (int)(c.Today - x.Transmittal.ResponseDue.Value.Date).TotalDays), ["overdueDocuments"] = x.Open,
                    ["reference"] = Short(x.Transmittal.Reference, 60),
                }).ToList();
                return DrillResult.Slice(rows, c, "هر ردیف یک ترنسمیتال است؛ شمارنده‌ی داشبورد مجموع اسناد بدون پاسخ آن‌هاست.",
                    new Dictionary<string, object> { ["overdueDocuments"] = items.Sum(x => x.Open), ["transmittals"] = items.Count });
            },
        };
    }
}
