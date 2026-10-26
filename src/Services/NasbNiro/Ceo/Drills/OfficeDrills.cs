using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DataLayer.Context;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.OfficeDtos;
using static Services.NasbNiro.Ceo.Drills.DrillText;

namespace Services.NasbNiro.Ceo.Drills
{
    /// <summary>
    /// The office drills: overdue decisions of the minutes, letters waiting for a step, the attendance of the month (counts only; people only for the roles the attendance service lets read people) and the leave requests waiting.
    /// The letter, meeting and leave services decide what the caller sees; the attendance counts hold no names and no amounts.
    /// </summary>
    internal static class OfficeDrills
    {
        private const string M = CeoModules.Office;

        /// <summary>A unit with fewer people than this is not shown on its own to a role that may not read people (the same threshold as the payroll mask).</summary>
        private const int SmallGroup = 3;

        private const int MaxDays = 366;

        public static IEnumerable<DrillDefinition> Build()
        {
            yield return DecisionsOverdue();
            yield return LettersWaiting();
            yield return AttendanceMonth();
            yield return LeaveRequestsPending();
        }

        /// <summary>Whether personal rows may be shown: the role reads attendance of everybody and the call is not a seat or evaluation run (its text is read by other people).</summary>
        private static bool SeesPeople(DrillContext c) => c.User.IsInRole(OfficeRoles.AttendanceReaders) && !AiActing.IsActing;

        private static string LetterKind(OfLetterKind kind) => kind switch
        {
            OfLetterKind.Incoming => "وارده",
            OfLetterKind.Outgoing => "صادره",
            _ => "داخلی",
        };

        private static string LetterStatus(OfLetterStatus status) => status switch
        {
            OfLetterStatus.Draft => "پیش‌نویس",
            OfLetterStatus.InApproval => "در جریان تایید یا امضا",
            OfLetterStatus.Signed => "امضاشده، منتظر ثبت دبیرخانه",
            OfLetterStatus.Registered => "ثبت‌شده، منتظر ارسال",
            OfLetterStatus.Sent => "ارسال‌شده",
            OfLetterStatus.Received => "دریافت‌شده",
            OfLetterStatus.Archived => "بایگانی",
            _ => "لغوشده",
        };

        private static string Classification(OfClassification value) => value switch
        {
            OfClassification.Normal => "عادی",
            OfClassification.Confidential => "محرمانه",
            _ => "سری",
        };

        private static string Urgency(OfUrgency value) => value switch
        {
            OfUrgency.Normal => "عادی",
            OfUrgency.Urgent => "فوری",
            _ => "آنی",
        };

        private static string LeaveKind(OfLeaveKind kind) => kind switch
        {
            OfLeaveKind.DailyLeave => "مرخصی روزانه",
            OfLeaveKind.HourlyLeave => "مرخصی ساعتی",
            OfLeaveKind.SickLeave => "مرخصی استعلاجی",
            OfLeaveKind.UnpaidLeave => "مرخصی بدون حقوق",
            OfLeaveKind.Mission => "ماموریت",
            OfLeaveKind.Overtime => "اضافه‌کار",
            _ => "فراموشی تردد",
        };

        private static string LeaveStatus(OfLeaveStatus status) => status switch
        {
            OfLeaveStatus.Draft => "پیش‌نویس",
            OfLeaveStatus.Submitted => "منتظر تایید مدیر",
            OfLeaveStatus.Approved => "تاییدشده، منتظر ثبت منابع انسانی",
            OfLeaveStatus.Recorded => "ثبت‌شده",
            OfLeaveStatus.Rejected => "ردشده",
            _ => "لغوشده",
        };

        // ------------------------------------------------------------------------------------------ decisions

        private static DrillDefinition DecisionsOverdue() => new DrillDefinition
        {
            Key = "of.decisions_overdue", Module = M, Title = "مصوبه‌های جلسه‌ی معوق",
            Description = "مصوبه‌های باز صورتجلسه‌های تاییدشده که موعدشان گذشته است (هم‌پایه با «مصوبه‌ی معوق» داشبورد مدیرعامل)؛ قدیمی‌ترین موعد اول. مدیرعامل همه را می‌بیند؛ نقش‌های دیگر فقط مصوبه‌ی جلسه‌هایی که در آن‌ها نقش دارند.",
            Link = "/office/decisions", RecordType = "MeetingDecision", RecordLabel = "مصوبه",
            Columns = new[]
            {
                Col.Text("title", "مصوبه"), Col.Text("meetingNumber", "شماره‌ی جلسه"), Col.Text("meetingTitle", "جلسه"), Col.Text("ownerPosition", "مسئول"), Col.Date("dueDate", "موعد"), Col.Num("daysOverdue", "روز تاخیر"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                var meetings = c.Get<IMeetingService>();
                List<DecisionDto> overdue = await DrillRead.AllAsync(n => meetings.GetDecisionsAsync(new DecisionQuery { OverdueOnly = true, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct));
                List<Row> rows = overdue.Select(d => new Row("/office/meetings/" + Id(d.MeetingId))
                {
                    ["id"] = d.Id, ["title"] = Short(d.Text, 120), ["meetingNumber"] = d.MeetingNumber, ["meetingTitle"] = Short(d.MeetingTitle, 80), ["ownerPosition"] = d.OwnerPositionTitle,
                    ["dueDate"] = d.DueDateText, ["daysOverdue"] = d.DueDate.HasValue ? Math.Max(0, DaysBetween(d.DueDate.Value, c.Today)) : (int?)null,
                }).ToList();
                return DrillResult.Slice(rows, c, "فقط مصوبه‌ی باز از صورتجلسه‌ی تاییدشده؛ موعد گذشته یعنی قبل از امروز.", new Dictionary<string, object>
                {
                    ["overdue"] = overdue.Count, ["owners"] = overdue.Select(d => d.OwnerPositionId).Distinct().Count(),
                    ["oldestDaysOverdue"] = overdue.Where(d => d.DueDate.HasValue).Select(d => Math.Max(0, DaysBetween(d.DueDate.Value, c.Today))).DefaultIfEmpty(0).Max(),
                });
            },
        };

        // ------------------------------------------------------------------------------------------ letters

        private static DrillDefinition LettersWaiting() => new DrillDefinition
        {
            Key = "of.letters_waiting", Module = M, Title = "نامه‌های منتظر اقدام",
            Description = "نامه‌هایی که در یک گام مانده‌اند: در جریان تایید یا امضا، امضاشده و منتظر ثبت دبیرخانه، ثبت‌شده و منتظر ارسال؛ فوری‌ترین و قدیمی‌ترین اول. با mineOnly فقط نامه‌های منتظر تایید یا امضای خود شما. محرمانگی نامه‌ها را سرویس مکاتبات اعمال می‌کند (موضوع نامه‌ی سری یا محرمانه‌ی غیرقابل‌خواندن نمایش داده نمی‌شود).",
            Link = "/office/letters", RecordType = "Letter", RecordLabel = "نامه",
            Parameters = new[] { Prm.Bool("mineOnly", "فقط نامه‌هایی که منتظر تایید یا امضای خود شما هستند؛ پیش‌فرض false") },
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("title", "موضوع"), Col.Text("kind", "نوع"), Col.Status("status", "وضعیت"), Col.Status("classification", "طبقه‌بندی"), Col.Status("urgency", "فوریت"),
                Col.Text("senderName", "فرستنده"), Col.Text("recipientNames", "گیرنده"), Col.Date("date", "تاریخ"), Col.Num("ageDays", "روز از تاریخ نامه"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                var letters = c.Get<ILetterService>();
                var waiting = new List<LetterListDto>();
                if (c.Args.Bool("mineOnly"))
                    waiting.AddRange(await DrillRead.AllAsync(n => letters.GetLettersAsync(new LetterQuery { WaitingForMe = true, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct)));
                else
                    foreach (OfLetterStatus status in new[] { OfLetterStatus.InApproval, OfLetterStatus.Signed, OfLetterStatus.Registered })
                    {
                        OfLetterStatus current = status;
                        waiting.AddRange(await DrillRead.AllAsync(n => letters.GetLettersAsync(new LetterQuery { Status = current, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct)));
                    }
                int waitingForMe = Take(await letters.GetLettersAsync(new LetterQuery { WaitingForMe = true, PageNumber = 1, PageSize = 1 }, ct)).TotalCount;
                List<Row> rows = waiting.OrderByDescending(l => (int)l.Urgency).ThenBy(l => l.Date).ThenBy(l => l.Id).Select(l => new Row("/office/letters/" + Id(l.Id))
                {
                    ["id"] = l.Id, ["number"] = l.Number, ["title"] = Short(l.Subject, 100), ["kind"] = LetterKind(l.Kind), ["status"] = LetterStatus(l.Status), ["classification"] = Classification(l.Classification),
                    ["urgency"] = Urgency(l.Urgency), ["senderName"] = l.SenderName, ["recipientNames"] = Short(l.RecipientNames, 100), ["date"] = l.DateText, ["ageDays"] = Math.Max(0, DaysBetween(l.Date, c.Today)),
                }).ToList();
                return DrillResult.Slice(rows, c, "نامه‌ی در جریان تایید را فقط نویسنده و اعضای زنجیره‌ی تایید و امضا می‌بینند؛ گام‌های بعدی را دبیرخانه انجام می‌دهد.", new Dictionary<string, object>
                {
                    ["waiting"] = waiting.Count, ["inApproval"] = waiting.Count(l => l.Status == OfLetterStatus.InApproval), ["awaitingRegistration"] = waiting.Count(l => l.Status == OfLetterStatus.Signed),
                    ["awaitingDispatch"] = waiting.Count(l => l.Status == OfLetterStatus.Registered), ["waitingForMe"] = waitingForMe, ["urgent"] = waiting.Count(l => l.Urgency != OfUrgency.Normal),
                });
            },
        };

        // ------------------------------------------------------------------------------------------ attendance

        /// <summary>One day of one employee: the only facts the attendance counts use.</summary>
        private sealed class DayFact
        {
            public int EmployeeId { get; init; }
            public DateTime Date { get; init; }
            public OfDayStatus Status { get; init; }
            public int DelayMinutes { get; init; }
            public int WorkedMinutes { get; init; }
        }

        private static Row Summary(string label, IReadOnlyCollection<DayFact> facts, string link = "/office/attendance")
        {
            int present = facts.Count(f => f.Status == OfDayStatus.Present), absent = facts.Count(f => f.Status == OfDayStatus.Absent), leave = facts.Count(f => f.Status == OfDayStatus.Leave);
            int mission = facts.Count(f => f.Status == OfDayStatus.Mission), rest = facts.Count(f => f.Status == OfDayStatus.Rest), incomplete = facts.Count(f => f.Status == OfDayStatus.Incomplete);
            int workDays = present + absent + leave + mission + incomplete;
            return new Row(link)
            {
                ["label"] = label, ["employees"] = facts.Select(f => f.EmployeeId).Distinct().Count(), ["present"] = present, ["absent"] = absent, ["leave"] = leave, ["mission"] = mission, ["rest"] = rest,
                ["incomplete"] = incomplete, ["late"] = facts.Count(f => f.DelayMinutes > 0), ["lateMinutes"] = facts.Sum(f => f.DelayMinutes), ["workedHours"] = Round(facts.Sum(f => f.WorkedMinutes) / 60m),
                ["presentPercent"] = workDays == 0 ? (decimal?)null : Percent(present, workDays),
            };
        }

        private static DrillDefinition AttendanceMonth() => new DrillDefinition
        {
            Key = "of.attendance_month", Module = M, Title = "حضور و غیاب ماه",
            Description = "شمار روزهای حاضر، غایب، مرخصی، ماموریت، استراحت و تردد ناقص و روزهای دارای تاخیر در بازه، فقط به‌صورت شمارش (بدون مبلغ و حقوق). by=day (پیش‌فرض) به تفکیک روز، by=unit به تفکیک واحد سازمانی (برای نقشی که پرسنل را نمی‌بیند، واحد کمتر از ۳ نفر در «سایر واحدها» جمع می‌شود)، by=person به تفکیک پرسنل و فقط برای نقش‌هایی که سرویس حضور و غیاب نمایش پرسنل را به آن‌ها می‌دهد (منابع انسانی و مدیرعامل). بازه‌ی پیش‌فرض: از اول ماه شمسی جاری تا امروز.",
            Link = "/office/attendance", RecordType = "Attendance", RecordLabel = "حضور و غیاب",
            Parameters = Prm.MonthToDate().Concat(new[] { Prm.Str("by", "تفکیک: day روز، unit واحد سازمانی، person پرسنل (فقط منابع انسانی و مدیرعامل)؛ پیش‌فرض day", "day", new[] { "day", "unit", "person" }) }).ToArray(),
            Columns = new[]
            {
                Col.Text("label", "روز / واحد / پرسنل"), Col.Num("employees", "پرسنل"), Col.Num("present", "حاضر"), Col.Num("absent", "غایب"), Col.Num("leave", "مرخصی"), Col.Num("mission", "ماموریت"),
                Col.Num("rest", "استراحت"), Col.Num("incomplete", "تردد ناقص"), Col.Num("late", "روز دارای تاخیر"), Col.Num("lateMinutes", "دقیقه‌ی تاخیر"), Col.Num("workedHours", "ساعت کارکرد"),
                Col.Pct("presentPercent", "درصد حضور"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                DateTime from = c.Args.Date("from").Date, to = c.Args.Date("to").Date;
                if ((to - from).TotalDays > MaxDays) throw new DrillFailureException("بازه‌ی حضور و غیاب نباید از یک سال بیشتر باشد.");
                string by = c.Args.Str("by") ?? "day";
                bool people = SeesPeople(c);
                List<DayFact> facts;
                var labels = new Dictionary<int, string>();
                if (by == "person")
                {
                    if (!people) throw new DrillFailureException("نمایش حضور و غیاب به تفکیک پرسنل فقط برای منابع انسانی و مدیرعامل مجاز است؛ به تفکیک روز یا واحد بخواهید.");
                    var attendance = c.Get<IAttendanceService>();
                    List<AttendanceDayDto> days = await DrillRead.AllAsync(n => attendance.GetDaysAsync(new DayQuery { From = from, To = to, PageNumber = n, PageSize = 1000 }, ct));
                    facts = days.Select(d => new DayFact { EmployeeId = d.EmployeeId, Date = d.Date.Date, Status = d.Status, DelayMinutes = d.DelayMinutes, WorkedMinutes = d.WorkedMinutes }).ToList();
                    foreach (AttendanceDayDto d in days) if (!labels.ContainsKey(d.EmployeeId)) labels[d.EmployeeId] = string.IsNullOrWhiteSpace(d.EmployeeName) ? d.PersonnelCode : d.EmployeeName.Trim();
                }
                else
                {
                    // counts of the whole company: read here, so a role that may not read people still gets the totals (nothing in them names a person)
                    var uow = c.Get<IUnitOfWork>();
                    facts = (await uow.Set<OfAttendanceDay>().AsNoTracking().Where(d => d.Date >= from && d.Date <= to)
                        .Select(d => new { d.EmployeeId, d.Date, d.Status, d.DelayMinutes, d.WorkedMinutes }).ToListAsync(ct))
                        .Select(d => new DayFact { EmployeeId = d.EmployeeId, Date = d.Date.Date, Status = d.Status, DelayMinutes = d.DelayMinutes, WorkedMinutes = d.WorkedMinutes }).ToList();
                }

                var rows = new List<Row>();
                if (by == "day")
                {
                    foreach (IGrouping<DateTime, DayFact> g in facts.GroupBy(f => f.Date).OrderBy(g => g.Key)) rows.Add(Summary(DrillDates.Text(g.Key), g.ToList()));
                }
                else if (by == "unit")
                {
                    var uow = c.Get<IUnitOfWork>();
                    Dictionary<int, int> unitOf = (await uow.Set<OfPosition>().AsNoTracking().Where(p => !p.Deleted && p.IsActive && p.EmployeeId != null).Select(p => new { EmployeeId = p.EmployeeId.Value, p.UnitId }).ToListAsync(ct))
                        .GroupBy(p => p.EmployeeId).ToDictionary(g => g.Key, g => g.Min(p => p.UnitId));
                    Dictionary<int, string> titles = await uow.Set<OfUnit>().AsNoTracking().Where(u => !u.Deleted).ToDictionaryAsync(u => u.Id, u => u.Title, ct);
                    var groups = facts.GroupBy(f => unitOf.TryGetValue(f.EmployeeId, out int unitId) && titles.TryGetValue(unitId, out string title) ? title : "بدون واحد سازمانی")
                        .Select(g => new { Label = g.Key, Facts = g.ToList() }).ToList();
                    var shown = new List<(string Label, List<DayFact> Facts)>();
                    var small = new List<DayFact>();
                    foreach (var g in groups)
                        if (!people && g.Facts.Select(f => f.EmployeeId).Distinct().Count() < SmallGroup) small.AddRange(g.Facts);
                        else shown.Add((g.Label, g.Facts));
                    if (small.Count > 0) shown.Add(("سایر واحدها (کمتر از ۳ نفر)", small));
                    rows.AddRange(shown.Select(s => Summary(s.Label, s.Facts)).OrderByDescending(r => (int)r["absent"] + (int)r["late"]).ThenBy(r => (string)r["label"], StringComparer.Ordinal));
                }
                else
                {
                    rows.AddRange(facts.GroupBy(f => f.EmployeeId).Select(g => Summary(labels.TryGetValue(g.Key, out string name) ? name : "—", g.ToList()))
                        .OrderByDescending(r => (int)r["absent"] + (int)r["late"]).ThenBy(r => (string)r["label"], StringComparer.Ordinal));
                }
                Row total = Summary("جمع", facts);
                total["days"] = facts.Select(f => f.Date).Distinct().Count();
                total.Remove("label");
                total.Remove("link");
                return DrillResult.Slice(rows, c, "شمارش از روزهای محاسبه‌شده‌ی حضور و غیاب است؛ روز دارای تاخیر یعنی تاخیر بیش از مهلت مجاز. درصد حضور = حاضر / (حاضر + غایب + مرخصی + ماموریت + تردد ناقص).", total.ToDictionary(kv => kv.Key, kv => kv.Value));
            },
        };

        // ------------------------------------------------------------------------------------------ leave

        private static DrillDefinition LeaveRequestsPending() => new DrillDefinition
        {
            Key = "of.leave_requests_pending", Module = M, Title = "درخواست‌های مرخصی و ماموریت منتظر تصمیم",
            Description = "درخواست‌های مرخصی، ماموریت، اضافه‌کار و فراموشی تردد که یا منتظر تایید مدیر واحدند یا تاییدشده و منتظر ثبت منابع انسانی؛ زودترین شروع اول. علت درخواست نمایش داده نمی‌شود. مدیرعامل و منابع انسانی همه را می‌بینند؛ نقش‌های دیگر فقط درخواست‌های خود یا درخواست‌هایی که تاییدکننده‌ی آن‌اند.",
            Link = "/office/leaves", RecordType = "LeaveRequest", RecordLabel = "درخواست مرخصی",
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("employeeName", "پرسنل"), Col.Text("kind", "نوع"), Col.Status("status", "وضعیت"), Col.Date("fromDate", "از تاریخ"), Col.Date("toDate", "تا تاریخ"), Col.Num("days", "روز"),
                Col.Text("approver", "تاییدکننده"), Col.Num("daysToStart", "روز تا شروع"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                var leaves = c.Get<ILeaveService>();
                var pending = new List<LeaveDto>();
                foreach (OfLeaveStatus status in new[] { OfLeaveStatus.Submitted, OfLeaveStatus.Approved })
                {
                    OfLeaveStatus current = status;
                    pending.AddRange(await DrillRead.AllAsync(n => leaves.GetLeavesAsync(new LeaveQuery { Status = current, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct)));
                }
                bool names = !AiActing.IsActing;
                List<Row> rows = pending.OrderBy(l => l.FromDate).ThenBy(l => l.Number, StringComparer.Ordinal).Select(l => new Row("/office/leaves/" + Id(l.Id))
                {
                    ["id"] = l.Id, ["number"] = l.Number, ["employeeName"] = names ? l.EmployeeName : null, ["kind"] = LeaveKind(l.Kind), ["status"] = LeaveStatus(l.Status), ["fromDate"] = l.FromDateText, ["toDate"] = l.ToDateText,
                    ["days"] = l.Days, ["approver"] = l.ApproverPositionTitle, ["daysToStart"] = DaysBetween(c.Today, l.FromDate),
                }).ToList();
                return DrillResult.Slice(rows, c, "روز تا شروع منفی یعنی بازه‌ی درخواست شروع شده ولی هنوز تصمیم یا ثبت نهایی نشده است.", new Dictionary<string, object>
                {
                    ["pending"] = pending.Count, ["waitingManager"] = pending.Count(l => l.Status == OfLeaveStatus.Submitted), ["waitingHr"] = pending.Count(l => l.Status == OfLeaveStatus.Approved),
                    ["days"] = pending.Sum(l => l.Days), ["alreadyStarted"] = pending.Count(l => l.FromDate.Date < c.Today),
                });
            },
        };
    }
}
