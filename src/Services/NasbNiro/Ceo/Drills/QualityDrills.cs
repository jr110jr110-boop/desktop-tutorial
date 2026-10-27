using System;
using System.Collections.Generic;
using System.Linq;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.QualityDtos;
using static Services.NasbNiro.Ceo.Drills.DrillText;

namespace Services.NasbNiro.Ceo.Drills
{
    /// <summary>The quality drills: open NCRs, overdue CAPAs, calibration, open complaints, hold points and the documents that wait for a decision.</summary>
    internal static class QualityDrills
    {
        private const string M = CeoModules.Quality;

        /// <summary>An NCR is open until it is closed or cancelled (the quality dashboard and the release rules use the same definition).</summary>
        private static readonly NcrStatus[] OpenNcrStatuses = { NcrStatus.Open, NcrStatus.Approved, NcrStatus.Executed };

        /// <summary>The stock documents that need an approval before they are confirmed (the same set as the document counter of the CEO dashboard).</summary>
        private static readonly InventoryDocumentType[] ApprovalTypes =
        {
            InventoryDocumentType.AdjustmentIncrease, InventoryDocumentType.AdjustmentDecrease, InventoryDocumentType.OpeningBalance, InventoryDocumentType.ScrapSale,
        };

        public static IEnumerable<DrillDefinition> Build()
        {
            yield return NcrOpen();
            yield return CapaOverdue();
            yield return CalibrationDue();
            yield return ComplaintsOpen();
            yield return HPointsPending();
            yield return DocumentsWaiting();
        }

        // ------------------------------------------------------------------------------------------ labels

        private static string Severity(NcrSeverity severity) => severity switch
        {
            NcrSeverity.Major => "عمده",
            NcrSeverity.Minor => "جزئی",
            _ => "طبقه‌بندی‌نشده",
        };

        private static string NcrState(NcrStatus status) => status switch
        {
            NcrStatus.Open => "باز",
            NcrStatus.Approved => "تصمیم تأییدشده",
            NcrStatus.Executed => "تصمیم اجراشده",
            NcrStatus.Closed => "بسته‌شده",
            _ => "لغوشده",
        };

        private static string NcrSourceText(NcrSource source) => source switch
        {
            NcrSource.Receiving => "بازرسی ورودی",
            NcrSource.InProcess => "حین ساخت",
            NcrSource.Final => "بازرسی نهایی",
            NcrSource.Customer => "مشتری",
            _ => "پیمانکار",
        };

        private static string Disposition(NcrDisposition disposition) => disposition switch
        {
            NcrDisposition.Concession => "استفاده با مجوز ارفاقی",
            NcrDisposition.Rework => "دوباره‌کاری",
            NcrDisposition.Repair => "تعمیر",
            NcrDisposition.Scrap => "ضایعات",
            NcrDisposition.ReturnToSupplier => "مرجوعی به تأمین‌کننده",
            _ => "تعیین‌نشده",
        };

        private static string CapaSourceText(CapaSource source) => source switch
        {
            CapaSource.Ncr => "عدم انطباق",
            CapaSource.AuditFinding => "یافته‌ی ممیزی",
            CapaSource.ManagementReview => "بازنگری مدیریت",
            CapaSource.CustomerComplaint => "شکایت مشتری",
            CapaSource.DataAnalysis => "تحلیل داده",
            CapaSource.Risk => "ریسک",
            _ => "سایر",
        };

        private static string CapaState(CapaStatus status) => status switch
        {
            CapaStatus.Open => "باز",
            CapaStatus.ActionsDefined => "اقدام‌ها تعریف‌شده",
            CapaStatus.Implemented => "اجراشده، منتظر بررسی اثربخشی",
            CapaStatus.EffectivenessCheck => "در بررسی اثربخشی",
            CapaStatus.Closed => "بسته‌شده",
            _ => "لغوشده",
        };

        private static string ComplaintState(ComplaintStatus status) => status switch
        {
            ComplaintStatus.Received => "دریافت‌شده",
            ComplaintStatus.UnderReview => "در بررسی",
            ComplaintStatus.Responded => "پاسخ‌داده‌شده",
            _ => "بسته‌شده",
        };

        private static string EquipmentState(EquipmentStatus status) => status switch
        {
            EquipmentStatus.Calibrated => "کالیبره",
            EquipmentStatus.DueSoon => "نزدیک سررسید",
            EquipmentStatus.Overdue => "سررسیدگذشته",
            EquipmentStatus.OutOfService => "از رده خارج",
            _ => "بدون نیاز به کالیبراسیون",
        };

        private static string EquipmentTypeText(EquipmentType type) => type switch
        {
            EquipmentType.DimensionalHandTool => "ابزار ابعادی",
            EquipmentType.Gauge => "گیج",
            EquipmentType.PressureGauge => "فشارسنج",
            EquipmentType.TemperatureInstrument => "دماسنج",
            EquipmentType.ElectricalMeter => "ابزار الکتریکی",
            EquipmentType.NdtEquipment => "تجهیز NDT",
            EquipmentType.TestStandInstrument => "ابزار پایه‌ی تست",
            _ => "سایر",
        };

        private static string ActivityState(ItpActivityStatus status) => status switch
        {
            ItpActivityStatus.Planned => "برنامه‌ریزی‌شده",
            ItpActivityStatus.Notified => "اعلام‌شده",
            ItpActivityStatus.Done => "انجام‌شده",
            ItpActivityStatus.Waived => "معاف‌شده",
            _ => "غیرقابل‌اعمال",
        };

        private static string InventoryDocumentTypeText(InventoryDocumentType type) => type switch
        {
            InventoryDocumentType.AdjustmentIncrease => "تعدیل افزایشی",
            InventoryDocumentType.AdjustmentDecrease => "تعدیل کاهشی",
            InventoryDocumentType.OpeningBalance => "موجودی اول دوره",
            InventoryDocumentType.ScrapSale => "فروش ضایعات",
            _ => "سند انبار",
        };

        /// <summary>The parties of a hold or witness point as the ITP service writes them ("V:H T:None C:W"), in Persian.</summary>
        private static string PointsText(string points)
        {
            if (string.IsNullOrWhiteSpace(points)) return null;
            var parts = new List<string>();
            foreach (string part in points.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = part.IndexOf(':');
                if (colon < 1) continue;
                string party = part.Substring(0, colon) switch { "V" => "شرکت", "T" => "بازرس ثالث", "C" => "کارفرما", var other => other };
                string point = part.Substring(colon + 1);
                parts.Add(party + " " + (point == "None" ? "-" : point));
            }
            return string.Join("، ", parts);
        }

        /// <summary>True when one of the parties holds the activity with an H point.</summary>
        private static bool HasHoldPoint(string points) =>
            !string.IsNullOrWhiteSpace(points) && points.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(p => p.EndsWith(":H", StringComparison.Ordinal));

        private static string YesNo(bool value) => value ? "بله" : "خیر";

        // ------------------------------------------------------------------------------------------ NCR

        private static DrillDefinition NcrOpen() => new DrillDefinition
        {
            Key = "qc.ncr_open", Module = M, Title = "گزارش‌های عدم انطباق باز",
            Description = "گزارش‌های عدم انطباق (NCR) که هنوز بسته یا لغو نشده‌اند (باز، تصمیم‌تأییدشده، تصمیم‌اجراشده)؛ شدیدترین اول و در هر شدت قدیمی‌ترین اول، با سن گزارش، مهلت تصمیم و روز تأخیر. می‌شود به یک شدت (عمده، جزئی، طبقه‌بندی‌نشده) یا فقط به گزارش‌های گذشته از مهلت تصمیم (هم‌پایه با شاخص «عدم انطباق‌های گذشته از مهلت تصمیم») محدود کرد؛ پیش‌فرض همه‌ی گزارش‌های باز.",
            Link = "/inventory/ncr", RecordType = "Ncr", RecordLabel = "گزارش عدم انطباق",
            Parameters = new[]
            {
                Prm.Str("severity", "فقط یک شدت: Major (عمده)، Minor (جزئی) یا NotClassified (طبقه‌بندی‌نشده)؛ پیش‌فرض همه", null, new[] { "Major", "Minor", "NotClassified" }),
                Prm.Bool("onlyOverdue", "فقط گزارش‌هایی که مهلت تصمیمشان گذشته است؛ پیش‌فرض نه"),
            },
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Date("reportDate", "تاریخ گزارش"), Col.Num("ageDays", "سن (روز)"), Col.Status("severity", "شدت"), Col.Status("status", "وضعیت"), Col.Text("source", "منبع"),
                Col.Text("disposition", "تصمیم"), Col.Text("itemCode", "کد کالا"), Col.Text("itemTitle", "کالا"), Col.Num("quantity", "تعداد"), Col.Text("providerName", "تأمین‌کننده"),
                Col.Text("workOrderCode", "دستور کار"), Col.Date("dueDate", "مهلت تصمیم"), Col.Num("daysOverdue", "روز تأخیر"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                var quality = c.Get<IInventoryQualityService>();
                string severity = c.Args.Str("severity");
                var open = new List<NcrListItemDto>();
                foreach (NcrStatus status in OpenNcrStatuses)
                    open.AddRange(Take(await quality.GetNcrsAsync(new NcrQuery { Status = status, PageSize = 0 }, ct)));
                if (severity != null) open = open.Where(n => n.Severity.ToString() == severity).ToList();

                // the decision deadline is not in the list of the module: one read for the open reports
                Dictionary<int, DateTime?> due = (await c.Get<IUnitOfWork>().Set<NonConformance>().AsNoTracking()
                        .Where(n => !n.Deleted && n.Status != NcrStatus.Closed && n.Status != NcrStatus.Cancelled).Select(n => new { n.Id, n.DueDate }).ToListAsync(ct))
                    .ToDictionary(x => x.Id, x => x.DueDate);
                DateTime? DueOf(NcrListItemDto n) => due.TryGetValue(n.Id, out DateTime? d) ? d : null;
                int Late(NcrListItemDto n) => n.Status == NcrStatus.Open && DueOf(n).HasValue && DueOf(n).Value.Date < c.Today ? DaysBetween(DueOf(n).Value, c.Today) : 0;
                if (c.Args.Bool("onlyOverdue")) open = open.Where(n => Late(n) > 0).ToList();

                List<NcrListItemDto> ordered = open.OrderByDescending(n => (int)n.Severity).ThenBy(n => n.ReportDate).ThenBy(n => n.Number, StringComparer.Ordinal).ToList();
                List<Row> rows = ordered.Select(n => new Row("/inventory/ncr/" + Id(n.Id))
                {
                    ["id"] = n.Id, ["number"] = n.Number, ["reportDate"] = DrillDates.Text(n.ReportDate), ["ageDays"] = Math.Max(0, DaysBetween(n.ReportDate, c.Today)),
                    ["severity"] = Severity(n.Severity), ["status"] = NcrState(n.Status), ["source"] = NcrSourceText(n.Source), ["disposition"] = Disposition(n.Disposition),
                    ["itemCode"] = n.ItemCode, ["itemTitle"] = n.ItemTitle, ["quantity"] = n.Quantity, ["providerName"] = n.ProviderName, ["workOrderCode"] = n.WorkOrderCode,
                    ["dueDate"] = DrillDates.Text(DueOf(n)), ["daysOverdue"] = Late(n),
                }).ToList();
                return DrillResult.Slice(rows, c, "باز یعنی بسته یا لغو نشده؛ مهلت تصمیم و روز تأخیر فقط برای گزارش‌هایی است که هنوز تصمیم نگرفته‌اند (وضعیت «باز»).", new Dictionary<string, object>
                {
                    ["overdue"] = ordered.Count(n => Late(n) > 0), ["major"] = ordered.Count(n => n.Severity == NcrSeverity.Major),
                    ["minor"] = ordered.Count(n => n.Severity == NcrSeverity.Minor), ["notClassified"] = ordered.Count(n => n.Severity == NcrSeverity.NotClassified),
                });
            },
        };

        // ------------------------------------------------------------------------------------------ CAPA

        private static DrillDefinition CapaOverdue() => new DrillDefinition
        {
            Key = "qc.capa_overdue", Module = M, Title = "اقدام‌های اصلاحی و پیشگیرانه‌ی عقب‌افتاده",
            Description = "اقدام‌های اصلاحی و پیشگیرانه (CAPA) که مهلتشان گذشته است: باز یا اقدام‌تعریف‌شده با مهلت اجرای گذشته، و اجراشده با مهلت بررسی اثربخشی گذشته (هم‌پایه با داشبورد کیفیت)؛ بیشترین روز تأخیر اول.",
            Link = "/quality/capa", RecordType = "Capa", RecordLabel = "اقدام اصلاحی",
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("source", "منبع"), Col.Text("problem", "مسئله"), Col.Status("status", "وضعیت"), Col.Text("ownerName", "مسئول"), Col.Date("dueDate", "مهلت اجرا"),
                Col.Date("effectivenessDueDate", "مهلت بررسی اثربخشی"), Col.Num("daysOverdue", "روز تأخیر"), Col.Text("autoDrafted", "پیش‌نویس خودکار"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, QualityRoles.Readers);
                List<CapaListDto> overdue = Take(await c.Get<ICapaService>().GetOverdueAsync(new CapaQuery { PageSize = 0 }, ct)).ToList();
                // an implemented action is late against its effectiveness deadline, the others against the due date
                DateTime? Deadline(CapaListDto x) => x.Status == CapaStatus.Implemented ? x.EffectivenessDueDate : x.DueDate;
                int Late(CapaListDto x) => Deadline(x).HasValue ? Math.Max(0, DaysBetween(Deadline(x).Value, c.Today)) : 0;
                List<CapaListDto> ordered = overdue.OrderByDescending(Late).ThenBy(x => x.Number, StringComparer.Ordinal).ToList();
                List<Row> rows = ordered.Select(x => new Row("/quality/capa/" + Id(x.Id))
                {
                    ["id"] = x.Id, ["number"] = x.Number, ["source"] = CapaSourceText(x.Source), ["problem"] = Short(x.Problem), ["status"] = CapaState(x.Status), ["ownerName"] = x.OwnerName,
                    ["dueDate"] = DrillDates.Text(x.DueDate), ["effectivenessDueDate"] = DrillDates.Text(x.EffectivenessDueDate), ["daysOverdue"] = Late(x), ["autoDrafted"] = YesNo(x.AutoDrafted),
                }).ToList();
                return DrillResult.Slice(rows, c, "روز تأخیر برای اقدام‌های اجراشده از مهلت بررسی اثربخشی و برای بقیه از مهلت اجرا شمرده می‌شود.", new Dictionary<string, object>
                {
                    ["effectivenessLate"] = ordered.Count(x => x.Status == CapaStatus.Implemented), ["withoutOwner"] = ordered.Count(x => !x.OwnerUserId.HasValue),
                });
            },
        };

        // ------------------------------------------------------------------------------------------ calibration

        private static DrillDefinition CalibrationDue() => new DrillDefinition
        {
            Key = "qc.calibration_due", Module = M, Title = "کالیبراسیون سررسیدشده و نزدیک به سررسید",
            Description = "تجهیزهای اندازه‌گیری که کالیبراسیونشان سررسیدگذشته یا بدون تاریخ است، یا تا چند روز آینده سررسید می‌شود (تجهیز از رده خارج و بدون نیاز به کالیبراسیون نمی‌آید)؛ قدیمی‌ترین سررسید اول. پیش‌فرض ۳۰ روز؛ صفر روز فقط سررسیدگذشته‌ها و بدون تاریخ‌ها را می‌دهد (هم‌پایه با شاخص «تجهیزات اندازه‌گیری با کالیبراسیون سررسیدگذشته»).",
            Link = "/quality/calibration", RecordType = "Equipment", RecordLabel = "تجهیز اندازه‌گیری",
            Parameters = new[] { Prm.Int("days", "تجهیزهایی که تا این تعداد روز آینده سررسید کالیبراسیون دارند؛ 0 فقط سررسیدگذشته‌ها و بدون تاریخ‌ها؛ پیش‌فرض 30 روز", 30, 0, 365) },
            Columns = new[]
            {
                Col.Text("code", "کد"), Col.Text("name", "نام"), Col.Text("type", "نوع"), Col.Text("location", "محل"), Col.Status("status", "وضعیت"), Col.Date("lastCalibrated", "آخرین کالیبراسیون"),
                Col.Date("nextDue", "سررسید بعدی"), Col.Num("daysToDue", "روز تا سررسید"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, QualityRoles.Readers);
                int days = c.Args.Int("days");
                // the module reads a limit of zero or less as its default of thirty days, so overdue only (days = 0) is taken from the equipment due within a day
                List<EquipmentListDto> due = Take(await c.Get<ICalibrationService>().GetDueAsync(new DueQuery { Days = Math.Max(1, days), PageSize = 0 }, ct)).ToList();
                if (days == 0) due = due.Where(e => !e.NextDue.HasValue || e.DaysToDue < 0).ToList();
                List<Row> rows = due.Select(e => new Row("/quality/calibration/" + Id(e.Id))
                {
                    ["id"] = e.Id, ["code"] = e.Code, ["name"] = e.Name, ["type"] = EquipmentTypeText(e.Type), ["location"] = e.Location, ["status"] = EquipmentState(e.Status),
                    ["lastCalibrated"] = DrillDates.Text(e.LastCalibrated), ["nextDue"] = DrillDates.Text(e.NextDue), ["daysToDue"] = e.DaysToDue,
                }).ToList();
                return DrillResult.Slice(rows, c, "روز تا سررسید منفی یعنی سررسید گذشته؛ تجهیز بدون تاریخ سررسید هرگز کالیبره نشده است.", new Dictionary<string, object>
                {
                    ["overdue"] = due.Count(e => !e.NextDue.HasValue || e.DaysToDue < 0), ["neverCalibrated"] = due.Count(e => !e.NextDue.HasValue),
                    ["dueSoon"] = due.Count(e => e.NextDue.HasValue && e.DaysToDue >= 0),
                });
            },
        };

        // ------------------------------------------------------------------------------------------ complaints

        private static DrillDefinition ComplaintsOpen() => new DrillDefinition
        {
            Key = "qc.complaints_open", Module = M, Title = "شکایت‌های مشتری باز",
            Description = "شکایت‌های مشتری که هنوز بسته نشده‌اند (دریافت‌شده، در بررسی، پاسخ‌داده‌شده)؛ دارای تأخیر در پاسخ اول و سپس به ترتیب مهلت پاسخ. می‌شود فقط شکایت‌های گذشته از مهلت پاسخ را خواست؛ پیش‌فرض همه‌ی شکایت‌های باز.",
            Link = "/quality/complaints", RecordType = "Complaint", RecordLabel = "شکایت",
            Parameters = new[] { Prm.Bool("overdueOnly", "فقط شکایت‌هایی که از مهلت پاسخ گذشته‌اند؛ پیش‌فرض نه") },
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("workOrderCode", "دستور کار"), Col.Date("receivedDate", "تاریخ دریافت"), Col.Status("severity", "شدت"), Col.Status("status", "وضعیت"),
                Col.Date("responseDue", "مهلت پاسخ"), Col.Num("daysOverdue", "روز تأخیر"), Col.Text("ncr", "NCR مرتبط"), Col.Text("capa", "CAPA مرتبط"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, QualityRoles.Readers);
                var capa = c.Get<ICapaService>();
                var open = new List<ComplaintListDto>();
                foreach (ComplaintStatus status in new[] { ComplaintStatus.Received, ComplaintStatus.UnderReview, ComplaintStatus.Responded })
                    open.AddRange(Take(await capa.GetComplaintsAsync(new ComplaintQuery { Status = status, PageSize = 0 }, ct)));
                int lateAll = open.Count(x => x.Overdue);
                if (c.Args.Bool("overdueOnly")) open = open.Where(x => x.Overdue).ToList();
                List<ComplaintListDto> ordered = open.OrderByDescending(x => x.Overdue).ThenBy(x => x.ResponseDue).ThenBy(x => x.Number, StringComparer.Ordinal).ToList();
                List<Row> rows = ordered.Select(x => new Row("/quality/complaints")
                {
                    ["id"] = x.Id, ["number"] = x.Number, ["workOrderCode"] = x.WorkOrderCode, ["receivedDate"] = DrillDates.Text(x.ReceivedDate), ["severity"] = Severity(x.Severity),
                    ["status"] = ComplaintState(x.Status), ["responseDue"] = DrillDates.Text(x.ResponseDue), ["daysOverdue"] = x.Overdue ? Math.Max(0, DaysBetween(x.ResponseDue, c.Today)) : 0,
                    ["ncr"] = YesNo(x.NcrId.HasValue), ["capa"] = YesNo(x.CapaId.HasValue),
                }).ToList();
                return DrillResult.Slice(rows, c, "تأخیر یعنی هنوز پاسخ داده نشده و مهلت پاسخ گذشته است؛ شکایت پاسخ‌داده‌شده تا بسته شدن باز می‌ماند.", new Dictionary<string, object>
                {
                    ["overdue"] = lateAll, ["open"] = ordered.Count,
                });
            },
        };

        // ------------------------------------------------------------------------------------------ hold points

        private static DrillDefinition HPointsPending() => new DrillDefinition
        {
            Key = "qc.h_points_pending", Module = M, Title = "نقاط توقف (H) منتظر رهاسازی",
            Description = "فعالیت‌های برنامه‌ی بازرسی و آزمون (ITP) تأییدشده‌ی کارفرما یا در حال استفاده که نقطه‌ی توقف H دارند و هنوز انجام نشده‌اند (برنامه‌ریزی‌شده یا اعلام‌شده)؛ هم‌پایه با شمارنده‌ی «نقاط توقف» داشبورد؛ زودترین تاریخ برنامه اول.",
            Link = "/quality/itp", RecordType = "ItpActivity", RecordLabel = "فعالیت بازرسی",
            Columns = new[]
            {
                Col.Text("number", "شماره‌ی ITP"), Col.Text("workOrderCode", "دستور کار"), Col.Text("itemNo", "ردیف"), Col.Text("activity", "فعالیت"), Col.Text("points", "نقاط توقف و شاهد"),
                Col.Status("status", "وضعیت"), Col.Date("plannedDate", "تاریخ برنامه"), Col.Num("daysLate", "روز تأخیر از برنامه"), Col.Date("witnessDeadline", "مهلت اعلام به شاهد"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, QualityRoles.Readers);
                List<PendingPointDto> pending = Take(await c.Get<IItpService>().GetPendingPointsAsync(new PendingPointQuery { PageSize = 0 }, ct)).Where(p => HasHoldPoint(p.Points)).ToList();

                // the dashboard counts only the plans the client has approved or that are in use
                List<int> itpIds = pending.Select(p => p.ItpId).Distinct().ToList();
                HashSet<int> approved = (await c.Get<IUnitOfWork>().Set<Itp>().AsNoTracking()
                        .Where(i => !i.Deleted && itpIds.Contains(i.Id) && (i.Status == ItpStatus.ClientApproved || i.Status == ItpStatus.InUse)).Select(i => i.Id).ToListAsync(ct))
                    .ToHashSet();
                List<PendingPointDto> held = pending.Where(p => approved.Contains(p.ItpId))
                    .OrderBy(p => p.PlannedDate.HasValue ? 0 : 1).ThenBy(p => p.PlannedDate).ThenBy(p => p.ItpNumber, StringComparer.Ordinal).ThenBy(p => p.ItemNo, StringComparer.Ordinal).ToList();
                int Late(PendingPointDto p) => p.PlannedDate.HasValue && p.PlannedDate.Value.Date < c.Today ? DaysBetween(p.PlannedDate.Value, c.Today) : 0;
                List<Row> rows = held.Select(p => new Row("/quality/itp/" + Id(p.ItpId))
                {
                    ["id"] = p.ActivityId, ["number"] = p.ItpNumber, ["workOrderCode"] = p.WorkOrderCode, ["itemNo"] = p.ItemNo, ["activity"] = Short(p.Activity), ["points"] = PointsText(p.Points),
                    ["status"] = ActivityState(p.Status), ["plannedDate"] = DrillDates.Text(p.PlannedDate), ["daysLate"] = Late(p), ["witnessDeadline"] = DrillDates.Text(p.WitnessDeadline),
                }).ToList();
                return DrillResult.Slice(rows, c, "فقط برنامه‌های بازرسی تأییدشده‌ی کارفرما یا در حال استفاده شمرده می‌شوند؛ نقطه‌ی شاهد (W) بدون H در این جدول نمی‌آید.", new Dictionary<string, object>
                {
                    ["workOrders"] = held.Select(p => p.WorkOrderId).Distinct().Count(), ["late"] = held.Count(p => Late(p) > 0),
                });
            },
        };

        // ------------------------------------------------------------------------------------------ documents

        private static DrillDefinition DocumentsWaiting() => new DrillDefinition
        {
            Key = "qc.documents_waiting", Module = M, Title = "اسناد منتظر بررسی یا تأیید",
            Description = "اسنادی که منتظر تصمیم‌اند: اسناد انبار پیش‌نویس که تأیید می‌خواهند (تعدیل افزایشی و کاهشی، موجودی اول دوره، فروش ضایعات؛ همان عدد «اسناد منتظر تأیید» داشبورد) و بازنگری‌های اسناد کنترل‌شده‌ی کیفیت که پیش‌نویس یا در بررسی‌اند؛ قدیمی‌ترین اول. می‌شود فقط اسناد انبار یا فقط اسناد کنترل‌شده را خواست؛ پیش‌فرض هر دو.",
            Link = "/quality/documents", RecordType = "Document", RecordLabel = "سند",
            Parameters = new[] { Prm.Str("source", "منبع اسناد: all (هر دو)، inventory (اسناد انبار) یا controlled (اسناد کنترل‌شده‌ی کیفیت)؛ پیش‌فرض all", "all", new[] { "all", "inventory", "controlled" }) },
            Columns = new[]
            {
                Col.Text("kind", "نوع"), Col.Text("number", "شماره یا کد"), Col.Text("title", "عنوان"), Col.Status("status", "وضعیت"), Col.Date("since", "از تاریخ"), Col.Num("waitingDays", "روز انتظار"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                string source = c.Args.Str("source") ?? "all";
                var items = new List<(int WaitingDays, string Number, Row Row)>();
                int inventoryCount = 0, controlledCount = 0;

                if (source != "controlled")
                {
                    var documents = c.Get<IInventoryDocumentService>();
                    foreach (InventoryDocumentType type in ApprovalTypes)
                        foreach (DocumentListItemDto d in Take(await documents.GetDocumentsAsync(new DocumentQuery { Type = type, Status = InventoryDocumentStatus.Draft, PageSize = 0 }, ct)))
                        {
                            inventoryCount++;
                            int waiting = Math.Max(0, DaysBetween(d.DocumentDate, c.Today));
                            string title = InventoryDocumentTypeText(d.Type) + (string.IsNullOrWhiteSpace(d.WarehouseTitle) ? string.Empty : " — " + d.WarehouseTitle);
                            items.Add((waiting, d.Number, new Row("/inventory/documents/" + Id(d.Id))
                            {
                                ["id"] = "inv-" + Id(d.Id), ["kind"] = "سند انبار", ["number"] = d.Number, ["title"] = title, ["status"] = "پیش‌نویس، منتظر تأیید",
                                ["since"] = DrillDates.Text(d.DocumentDate), ["waitingDays"] = waiting,
                            }));
                        }
                }

                if (source != "inventory")
                {
                    Guard.Require(c, QualityRoles.Readers);
                    // the revision (not the document) carries the step: a new revision of an effective document waits while the document stays effective
                    var revisions = await c.Get<IUnitOfWork>().Set<DocumentRevisionRecord>().AsNoTracking()
                        .Where(r => !r.Deleted && !r.Document.Deleted && (r.Status == ControlledDocumentStatus.Draft || r.Status == ControlledDocumentStatus.InReview))
                        .Select(r => new
                        {
                            r.Id, r.DocumentId, r.Revision, r.Status, r.CreateDate, r.ReviewedAt, DocumentCode = r.Document.Code, DocumentTitle = r.Document.Title,
                        }).ToListAsync(ct);
                    foreach (var r in revisions)
                    {
                        controlledCount++;
                        DateTime since = (r.Status == ControlledDocumentStatus.InReview && r.ReviewedAt.HasValue ? r.ReviewedAt.Value : r.CreateDate).LocalDateTime.Date;
                        int waiting = Math.Max(0, DaysBetween(since, c.Today));
                        items.Add((waiting, r.DocumentCode, new Row("/quality/documents/" + Id(r.DocumentId))
                        {
                            ["id"] = "rev-" + Id(r.Id), ["kind"] = "سند کنترل‌شده", ["number"] = r.DocumentCode + " / " + r.Revision, ["title"] = r.DocumentTitle,
                            ["status"] = r.Status == ControlledDocumentStatus.InReview ? "بررسی‌شده، منتظر تأیید" : "پیش‌نویس، منتظر بررسی",
                            ["since"] = DrillDates.Text(since), ["waitingDays"] = waiting,
                        }));
                    }
                }

                List<Row> rows = items.OrderByDescending(x => x.WaitingDays).ThenBy(x => x.Number, StringComparer.Ordinal).Select(x => x.Row).ToList();
                return DrillResult.Slice(rows, c,
                    "اسناد انبار پیش‌نویس‌اند و تأیید می‌خواهند (source=inventory همان عدد داشبورد است)؛ اسناد کنترل‌شده بازنگری پیش‌نویس (منتظر بررسی) یا بررسی‌شده (منتظر تأیید) دارند (source=controlled).",
                    new Dictionary<string, object> { ["inventoryDocuments"] = inventoryCount, ["controlledRevisions"] = controlledCount });
            },
        };
    }
}
