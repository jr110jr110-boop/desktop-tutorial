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

        /// <summary>The values of the stage parameter of of.letters_waiting: in approval, signed (waiting for the secretariat), registered (waiting to be sent), or the three together.</summary>
        public static readonly string[] LetterStages = { "approval", "signature", "registered", "all" };

        /// <summary>The values of the status parameter of of.leave_requests_pending: waiting for the manager, approved (waiting for human resources), or both.</summary>
        public static readonly string[] LeaveFilters = { "submitted", "approved", "all" };

        /// <summary>The letter statuses of a stage of of.letters_waiting.</summary>
        internal static OfLetterStatus[] StageStatuses(string stage) => stage switch
        {
            "approval" => new[] { OfLetterStatus.InApproval },
            "signature" => new[] { OfLetterStatus.Signed },
            "registered" => new[] { OfLetterStatus.Registered },
            _ => new[] { OfLetterStatus.InApproval, OfLetterStatus.Signed, OfLetterStatus.Registered },
        };

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
            OfLetterStatus.InApproval => "در جریان تأیید یا امضا",
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
            OfLeaveStatus.Submitted => "منتظر تأیید مدیر",
            OfLeaveStatus.Approved => "تأییدشده، منتظر ثبت منابع انسانی",
            OfLeaveStatus.Recorded => "ثبت‌شده",
            OfLeaveStatus.Rejected => "ردشده",
            _ => "لغوشده",
        };

        // ------------------------------------------------------------------------------------------ decisions

        private static DrillDefinition DecisionsOverdue() => new DrillDefinition
        {
            Key = "of.decisions_overdue", Module = M, Title = "مصوبات جلسه‌ی معوق",
            Description = "مصوبات باز صورت‌جلسه‌های تأییدشده که موعدشان گذشته است (هم‌پایه با «مصوبات جلسه‌ی معوق» داشبورد مدیرعامل)؛ قدیمی‌ترین موعد اول. مدیرعامل همه را می‌بیند؛ نقش‌های دیگر فقط مصوبات جلسه‌هایی که در آن‌ها نقش دارند.",
            Link = "/office/decisions", RecordType = "MeetingDecision", RecordLabel = "مصوبه",
            Columns = new[]
            {
                Col.Text("title", "مصوبه"), Col.Text("meetingNumber", "شماره‌ی جلسه"), Col.Text("meetingTitle", "جلسه"), Col.Text("ownerPosition", "مسئول"), Col.Date("dueDate", "موعد"), Col.Num("daysOverdue", "روز تأخیر"), Col.Link(),
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
                return DrillResult.Slice(rows, c, "فقط مصوبات باز از صورت‌جلسه‌های تأییدشده؛ موعد گذشته یعنی قبل از امروز.", new Dictionary<string, object>
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
            Description = "نامه‌هایی که در یک گام مانده‌اند: در جریان تأیید یا امضا (هم‌پایه با شاخص «نامه‌های منتظر تأیید و امضا»)، امضاشده و منتظر ثبت دبیرخانه، ثبت‌شده و منتظر ارسال؛ بدون محدودیت گام هر سه گام می‌آید؛ فوری‌ترین و قدیمی‌ترین اول. می‌شود فقط نامه‌های منتظر تأیید یا امضای خود کاربر را خواست. محرمانگی نامه‌ها را سرویس مکاتبات اعمال می‌کند: نامه‌ی در جریان تأیید را فقط نویسنده و زنجیره‌ی تأیید و امضا می‌بینند و موضوع نامه‌ی سری یا محرمانه‌ی غیرقابل‌خواندن نمایش داده نمی‌شود؛ شمار نامه‌های در جریان تأییدی که برای این کاربر پنهان مانده در جمع‌ها می‌آید.",
            Link = "/office/letters", RecordType = "Letter", RecordLabel = "نامه",
            Parameters = new[]
            {
                Prm.Str("stage", "approval (در جریان تأیید یا امضا)، signature (امضاشده، منتظر ثبت دبیرخانه)، registered (ثبت‌شده، منتظر ارسال) یا all (هر سه)؛ پیش‌فرض all", "all", LetterStages),
                Prm.Bool("mineOnly", "فقط نامه‌هایی که منتظر تأیید یا امضای خود شما هستند (گام نادیده گرفته می‌شود)؛ پیش‌فرض false"),
            },
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("title", "موضوع"), Col.Text("kind", "نوع"), Col.Status("status", "وضعیت"), Col.Status("classification", "طبقه‌بندی"), Col.Status("urgency", "فوریت"),
                Col.Text("senderName", "فرستنده"), Col.Text("recipientNames", "گیرنده"), Col.Date("date", "تاریخ"), Col.Num("ageDays", "روز از تاریخ نامه"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                var letters = c.Get<ILetterService>();
                string stage = c.Args.Str("stage") ?? "all";
                bool mineOnly = c.Args.Bool("mineOnly");
                var waiting = new List<LetterListDto>();
                if (mineOnly)
                    waiting.AddRange(await DrillRead.AllAsync(n => letters.GetLettersAsync(new LetterQuery { WaitingForMe = true, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct)));
                else
                    foreach (OfLetterStatus status in StageStatuses(stage))
                    {
                        OfLetterStatus current = status;
                        waiting.AddRange(await DrillRead.AllAsync(n => letters.GetLettersAsync(new LetterQuery { Status = current, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct)));
                    }
                int waitingForMe = Take(await letters.GetLettersAsync(new LetterQuery { WaitingForMe = true, PageNumber = 1, PageSize = 1 }, ct)).TotalCount;

                // the letters in approval that the letter service does not show to this user (only the writer and the approval chain read them): only their number is counted, as the figure of the dashboard counts them
                int notVisible = 0;
                if (!mineOnly && (stage == "approval" || stage == "all"))
                {
                    int inApproval = await c.Get<IUnitOfWork>().Set<OfLetter>().AsNoTracking().CountAsync(l => !l.Deleted && l.Status == OfLetterStatus.InApproval, ct);
                    notVisible = Math.Max(0, inApproval - waiting.Count(l => l.Status == OfLetterStatus.InApproval));
                }

                List<Row> rows = waiting.OrderByDescending(l => (int)l.Urgency).ThenBy(l => l.Date).ThenBy(l => l.Id).Select(l => new Row("/office/letters/" + Id(l.Id))
                {
                    ["id"] = l.Id, ["number"] = l.Number, ["title"] = Short(l.Subject, 100), ["kind"] = LetterKind(l.Kind), ["status"] = LetterStatus(l.Status), ["classification"] = Classification(l.Classification),
                    ["urgency"] = Urgency(l.Urgency), ["senderName"] = l.SenderName, ["recipientNames"] = Short(l.RecipientNames, 100), ["date"] = l.DateText, ["ageDays"] = Math.Max(0, DaysBetween(l.Date, c.Today)),
                }).ToList();
                return DrillResult.Slice(rows, c, "نامه‌ی در جریان تأیید را فقط نویسنده و اعضای زنجیره‌ی تأیید و امضا می‌بینند؛ گام‌های بعدی را دبیرخانه انجام می‌دهد.", new Dictionary<string, object>
                {
                    ["waiting"] = waiting.Count, ["inApproval"] = waiting.Count(l => l.Status == OfLetterStatus.InApproval), ["awaitingRegistration"] = waiting.Count(l => l.Status == OfLetterStatus.Signed),
                    ["awaitingDispatch"] = waiting.Count(l => l.Status == OfLetterStatus.Registered), ["waitingForMe"] = waitingForMe, ["urgent"] = waiting.Count(l => l.Urgency != OfUrgency.Normal),
                    ["notVisible"] = notVisible,
                });
            },
        };

        // ------------------------------------------------------------------------------------------ attendance

        /// <summary>One day of one employee (the person view only: it is read through the attendance service, which decides who may read people).</summary>
        internal sealed class DayFact
        {
            public int EmployeeId { get; init; }
            public DateTime Date { get; init; }
            public OfDayStatus Status { get; init; }
            public int DelayMinutes { get; init; }
            public int WorkedMinutes { get; init; }
        }

        /// <summary>
        /// The counts of a group of attendance days (one day, one unit, one person or everybody). They are summed in the database, so no row of a person is read to build them;
        /// nothing in them names a person.
        /// </summary>
        internal sealed class AttendanceCounts
        {
            public DateTime Date { get; set; }
            public int EmployeeId { get; set; }

            /// <summary>The number of different employees in the group.</summary>
            public int Employees { get; set; }
            public int Present { get; set; }
            public int Absent { get; set; }
            public int Leave { get; set; }
            public int Mission { get; set; }
            public int Rest { get; set; }
            public int Incomplete { get; set; }
            public int Late { get; set; }
            public int LateMinutes { get; set; }
            public int WorkedMinutes { get; set; }

            /// <summary>The counts of two groups of different employees added together.</summary>
            public AttendanceCounts Plus(AttendanceCounts other) => new AttendanceCounts
            {
                Employees = Employees + other.Employees, Present = Present + other.Present, Absent = Absent + other.Absent, Leave = Leave + other.Leave, Mission = Mission + other.Mission,
                Rest = Rest + other.Rest, Incomplete = Incomplete + other.Incomplete, Late = Late + other.Late, LateMinutes = LateMinutes + other.LateMinutes, WorkedMinutes = WorkedMinutes + other.WorkedMinutes,
            };

            public static AttendanceCounts Of(IEnumerable<DayFact> facts)
            {
                List<DayFact> list = facts.ToList();
                return new AttendanceCounts
                {
                    Employees = list.Select(f => f.EmployeeId).Distinct().Count(), Present = list.Count(f => f.Status == OfDayStatus.Present), Absent = list.Count(f => f.Status == OfDayStatus.Absent),
                    Leave = list.Count(f => f.Status == OfDayStatus.Leave), Mission = list.Count(f => f.Status == OfDayStatus.Mission), Rest = list.Count(f => f.Status == OfDayStatus.Rest),
                    Incomplete = list.Count(f => f.Status == OfDayStatus.Incomplete), Late = list.Count(f => f.DelayMinutes > 0), LateMinutes = list.Sum(f => f.DelayMinutes), WorkedMinutes = list.Sum(f => f.WorkedMinutes),
                };
            }
        }

        /// <summary>The groups of the attendance days of the range in the database: one row per day (employees counted as different people) or one per employee.</summary>
        private static IQueryable<AttendanceCounts> ByDay(IQueryable<OfAttendanceDay> days) => days.GroupBy(d => d.Date).Select(g => new AttendanceCounts
        {
            Date = g.Key, Employees = g.Select(d => d.EmployeeId).Distinct().Count(),
            Present = g.Count(d => d.Status == OfDayStatus.Present), Absent = g.Count(d => d.Status == OfDayStatus.Absent), Leave = g.Count(d => d.Status == OfDayStatus.Leave),
            Mission = g.Count(d => d.Status == OfDayStatus.Mission), Rest = g.Count(d => d.Status == OfDayStatus.Rest), Incomplete = g.Count(d => d.Status == OfDayStatus.Incomplete),
            Late = g.Count(d => d.DelayMinutes > 0), LateMinutes = g.Sum(d => d.DelayMinutes), WorkedMinutes = g.Sum(d => d.WorkedMinutes),
        });

        private static IQueryable<AttendanceCounts> ByEmployee(IQueryable<OfAttendanceDay> days) => days.GroupBy(d => d.EmployeeId).Select(g => new AttendanceCounts
        {
            EmployeeId = g.Key, Employees = 1,
            Present = g.Count(d => d.Status == OfDayStatus.Present), Absent = g.Count(d => d.Status == OfDayStatus.Absent), Leave = g.Count(d => d.Status == OfDayStatus.Leave),
            Mission = g.Count(d => d.Status == OfDayStatus.Mission), Rest = g.Count(d => d.Status == OfDayStatus.Rest), Incomplete = g.Count(d => d.Status == OfDayStatus.Incomplete),
            Late = g.Count(d => d.DelayMinutes > 0), LateMinutes = g.Sum(d => d.DelayMinutes), WorkedMinutes = g.Sum(d => d.WorkedMinutes),
        });

        /// <summary>
        /// One row of counts. <paramref name="detail"/> false gives the counts of days only: the minutes of delay and the hours worked of a very small group are the figures of one or two people.
        /// </summary>
        internal static Row Summary(string label, AttendanceCounts a, bool detail = true, string link = "/office/attendance")
        {
            int workDays = a.Present + a.Absent + a.Leave + a.Mission + a.Incomplete;
            return new Row(link)
            {
                ["label"] = label, ["employees"] = a.Employees, ["present"] = a.Present, ["absent"] = a.Absent, ["leave"] = a.Leave, ["mission"] = a.Mission, ["rest"] = a.Rest,
                ["incomplete"] = a.Incomplete, ["late"] = a.Late, ["lateMinutes"] = detail ? a.LateMinutes : (int?)null, ["workedHours"] = detail ? Round(a.WorkedMinutes / 60m) : (decimal?)null,
                ["presentPercent"] = workDays == 0 ? (decimal?)null : Percent(a.Present, workDays),
            };
        }

        /// <summary>Whether the minutes and hours of a group may be shown: a role that may read people sees everything, any other role only the groups of at least <see cref="SmallGroup"/> people.</summary>
        internal static bool ShowDetail(AttendanceCounts counts, bool people) => people || counts.Employees >= SmallGroup;

        /// <summary>The rows of the day view, oldest day first; a day with fewer than <see cref="SmallGroup"/> people shows counts only to a role that may not read people.</summary>
        internal static List<Row> DayRows(IEnumerable<AttendanceCounts> days, bool people) =>
            days.OrderBy(d => d.Date).Select(d => Summary(DrillDates.Text(d.Date), d, ShowDetail(d, people))).ToList();

        /// <summary>The label of the group that holds the units too small to be shown on their own.</summary>
        internal const string OtherUnits = "سایر واحدها";

        /// <summary>
        /// The rows of the unit view. A role that may read people sees every unit. Any other role never sees a unit of fewer than <see cref="SmallGroup"/> people on its own: the small units are added
        /// together, and while that group still holds fewer than <see cref="SmallGroup"/> people the smallest remaining unit joins it. A group that cannot reach <see cref="SmallGroup"/> people even then
        /// (the whole company is that small) is not shown; it is only part of the totals.
        /// </summary>
        internal static List<Row> UnitRows(IEnumerable<(string Label, AttendanceCounts Counts)> units, bool people)
        {
            var shown = new List<(string Label, AttendanceCounts Counts)>();
            if (people) shown.AddRange(units);
            else
            {
                List<(string Label, AttendanceCounts Counts)> big = units.Where(u => u.Counts.Employees >= SmallGroup).OrderBy(u => u.Counts.Employees).ThenBy(u => u.Label, StringComparer.Ordinal).ToList();
                List<(string Label, AttendanceCounts Counts)> small = units.Where(u => u.Counts.Employees < SmallGroup).ToList();
                if (small.Count > 0)
                {
                    AttendanceCounts merged = small.Aggregate(new AttendanceCounts(), (sum, u) => sum.Plus(u.Counts));
                    while (merged.Employees < SmallGroup && big.Count > 0)
                    {
                        merged = merged.Plus(big[0].Counts);
                        big.RemoveAt(0);
                    }
                    if (merged.Employees >= SmallGroup) shown.Add((OtherUnits, merged));
                }
                shown.AddRange(big);
            }
            return shown.Select(u => Summary(u.Label, u.Counts, ShowDetail(u.Counts, people))).OrderByDescending(r => (int)r["absent"] + (int)r["late"]).ThenBy(r => (string)r["label"], StringComparer.Ordinal).ToList();
        }

        private static DrillDefinition AttendanceMonth() => new DrillDefinition
        {
            Key = "of.attendance_month", Module = M, Title = "حضور و غیاب ماه",
            Description = "شمار روزهای حاضر، غایب، مرخصی، ماموریت، استراحت و تردد ناقص و روزهای دارای تأخیر در بازه، فقط به‌صورت شمارش (بدون مبلغ و حقوق). تفکیک پیش‌فرض روز است؛ تفکیک واحد سازمانی هم هست. برای نقشی که پرسنل را نمی‌بیند، واحد یا روزی که کمتر از ۳ نفر دارد فقط شمارش روز نشان می‌دهد (دقیقه‌ی تأخیر و ساعت کارکرد نمی‌آید) و واحدهای کمتر از ۳ نفر در «سایر واحدها» جمع می‌شوند. تفکیک پرسنل فقط برای نقش‌هایی است که سرویس حضور و غیاب نمایش پرسنل را به آن‌ها می‌دهد (منابع انسانی و مدیرعامل). بازه‌ی پیش‌فرض: از اول ماه شمسی جاری تا امروز.",
            Link = "/office/attendance", RecordType = "Attendance", RecordLabel = "حضور و غیاب",
            Parameters = Prm.MonthToDate().Concat(new[] { Prm.Str("by", "تفکیک: day روز، unit واحد سازمانی، person پرسنل (فقط منابع انسانی و مدیرعامل)؛ پیش‌فرض day", "day", new[] { "day", "unit", "person" }) }).ToArray(),
            Columns = new[]
            {
                Col.Text("label", "روز / واحد / پرسنل"), Col.Num("employees", "پرسنل"), Col.Num("present", "حاضر"), Col.Num("absent", "غایب"), Col.Num("leave", "مرخصی"), Col.Num("mission", "ماموریت"),
                Col.Num("rest", "استراحت"), Col.Num("incomplete", "تردد ناقص"), Col.Num("late", "روز دارای تأخیر"), Col.Num("lateMinutes", "دقیقه‌ی تأخیر"), Col.Num("workedHours", "ساعت کارکرد"),
                Col.Pct("presentPercent", "درصد حضور"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                DateTime from = c.Args.Date("from").Date, to = c.Args.Date("to").Date;
                if ((to - from).TotalDays > MaxDays) throw new DrillFailureException("بازه‌ی حضور و غیاب نباید از یک سال بیشتر باشد.");
                string by = c.Args.Str("by") ?? "day";
                bool people = SeesPeople(c);
                var uow = c.Get<IUnitOfWork>();
                var rows = new List<Row>();
                AttendanceCounts total;
                int days;
                if (by == "person")
                {
                    if (!people) throw new DrillFailureException("نمایش حضور و غیاب به تفکیک پرسنل فقط برای منابع انسانی و مدیرعامل مجاز است؛ به تفکیک روز یا واحد بخواهید.");
                    var attendance = c.Get<IAttendanceService>();
                    List<AttendanceDayDto> list = await DrillRead.AllAsync(n => attendance.GetDaysAsync(new DayQuery { From = from, To = to, PageNumber = n, PageSize = 1000 }, ct));
                    List<DayFact> facts = list.Select(d => new DayFact { EmployeeId = d.EmployeeId, Date = d.Date.Date, Status = d.Status, DelayMinutes = d.DelayMinutes, WorkedMinutes = d.WorkedMinutes }).ToList();
                    var labels = new Dictionary<int, string>();
                    foreach (AttendanceDayDto d in list) if (!labels.ContainsKey(d.EmployeeId)) labels[d.EmployeeId] = string.IsNullOrWhiteSpace(d.EmployeeName) ? d.PersonnelCode : d.EmployeeName.Trim();
                    rows.AddRange(facts.GroupBy(f => f.EmployeeId).Select(g => Summary(labels.TryGetValue(g.Key, out string name) ? name : "—", AttendanceCounts.Of(g)))
                        .OrderByDescending(r => (int)r["absent"] + (int)r["late"]).ThenBy(r => (string)r["label"], StringComparer.Ordinal));
                    total = AttendanceCounts.Of(facts);
                    days = facts.Select(f => f.Date).Distinct().Count();
                }
                else
                {
                    // counts of the whole company, summed in the database: a role that may not read people still gets them (nothing in them names a person)
                    IQueryable<OfAttendanceDay> range = uow.Set<OfAttendanceDay>().AsNoTracking().Where(d => d.Date >= from && d.Date <= to);
                    List<AttendanceCounts> perDay = await ByDay(range).ToListAsync(ct);
                    total = perDay.Aggregate(new AttendanceCounts(), (sum, d) => sum.Plus(d));
                    total.Employees = await range.Select(d => d.EmployeeId).Distinct().CountAsync(ct);
                    days = perDay.Count;
                    if (by == "day") rows.AddRange(DayRows(perDay, people));
                    else
                    {
                        List<AttendanceCounts> perEmployee = await ByEmployee(range).ToListAsync(ct);
                        Dictionary<int, int> unitOf = (await uow.Set<OfPosition>().AsNoTracking().Where(p => !p.Deleted && p.IsActive && p.EmployeeId != null).Select(p => new { EmployeeId = p.EmployeeId.Value, p.UnitId }).ToListAsync(ct))
                            .GroupBy(p => p.EmployeeId).ToDictionary(g => g.Key, g => g.Min(p => p.UnitId));
                        Dictionary<int, string> titles = await uow.Set<OfUnit>().AsNoTracking().Where(u => !u.Deleted).ToDictionaryAsync(u => u.Id, u => u.Title, ct);
                        var units = perEmployee.GroupBy(e => unitOf.TryGetValue(e.EmployeeId, out int unitId) && titles.TryGetValue(unitId, out string title) ? title : "بدون واحد سازمانی")
                            .Select(g => (Label: g.Key, Counts: g.Aggregate(new AttendanceCounts(), (sum, e) => sum.Plus(e)))).ToList();
                        rows.AddRange(UnitRows(units, people));
                    }
                }
                Row totals = Summary("جمع", total, ShowDetail(total, people));
                totals["days"] = days;
                totals.Remove("label");
                totals.Remove("link");
                return DrillResult.Slice(rows, c, "شمارش از روزهای محاسبه‌شده‌ی حضور و غیاب است؛ روز دارای تأخیر یعنی تأخیر بیش از مهلت مجاز. درصد حضور = حاضر / (حاضر + غایب + مرخصی + ماموریت + تردد ناقص).", totals.ToDictionary(kv => kv.Key, kv => kv.Value));
            },
        };

        // ------------------------------------------------------------------------------------------ leave

        private static DrillDefinition LeaveRequestsPending() => new DrillDefinition
        {
            Key = "of.leave_requests_pending", Module = M, Title = "درخواست‌های مرخصی و ماموریت منتظر تصمیم",
            Description = "درخواست‌های مرخصی، ماموریت، اضافه‌کار و فراموشی تردد که منتظر تأیید مدیر واحدند (پیش‌فرض، هم‌پایه با شاخص «درخواست‌های مرخصی و ماموریت منتظر تصمیم»)، یا تأییدشده و منتظر ثبت منابع انسانی، یا هر دو؛ زودترین شروع اول. علت درخواست نمایش داده نمی‌شود. مدیرعامل و منابع انسانی همه را می‌بینند؛ نقش‌های دیگر فقط درخواست‌های خود یا درخواست‌هایی که تأییدکننده‌ی آن‌اند.",
            Link = "/office/leaves", RecordType = "LeaveRequest", RecordLabel = "درخواست مرخصی",
            Parameters = new[] { Prm.Str("status", "submitted (منتظر تأیید مدیر)، approved (تأییدشده، منتظر ثبت منابع انسانی) یا all (هر دو)؛ پیش‌فرض submitted", "submitted", LeaveFilters) },
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("employeeName", "پرسنل"), Col.Text("kind", "نوع"), Col.Status("status", "وضعیت"), Col.Date("fromDate", "از تاریخ"), Col.Date("toDate", "تا تاریخ"), Col.Num("days", "روز"),
                Col.Text("approver", "تأییدکننده"), Col.Num("daysToStart", "روز تا شروع"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                var leaves = c.Get<ILeaveService>();
                string filter = c.Args.Str("status") ?? "submitted";
                OfLeaveStatus[] wanted = filter == "all" ? new[] { OfLeaveStatus.Submitted, OfLeaveStatus.Approved }
                    : filter == "approved" ? new[] { OfLeaveStatus.Approved } : new[] { OfLeaveStatus.Submitted };
                var pending = new List<LeaveDto>();
                foreach (OfLeaveStatus status in wanted)
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
