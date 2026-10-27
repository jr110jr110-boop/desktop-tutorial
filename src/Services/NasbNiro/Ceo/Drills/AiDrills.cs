using System;
using System.Collections.Generic;
using System.Linq;
using Entities.Model.NasbNiro.Ai;
using Services.NasbNiro.Ai;
using ViewModels.NasbNiro.AiDtos;
using ViewModels.NasbNiro.Pagination;
using static Services.NasbNiro.Ceo.Drills.DrillText;

namespace Services.NasbNiro.Ceo.Drills
{
    /// <summary>The drills of the AI seats: the notes nobody decided on yet and the runs of the seats. Both go through the AI module's own services (the notes follow the module's role and record rules).</summary>
    internal static class AiDrills
    {
        private const string M = CeoModules.Ai;

        /// <summary>The page size the AI module accepts at most.</summary>
        private const int AiPageSize = 200;

        public static IEnumerable<DrillDefinition> Build()
        {
            yield return NotesNew();
            yield return SeatRuns();
        }

        private static string NoteType(string type) => type switch
        {
            AiNoteTypes.QualityNcr => "عدم انطباق کیفیت",
            AiNoteTypes.FinanceJournal => "سند مالی",
            AiNoteTypes.BankLine => "ردیف صورت‌حساب بانک",
            AiNoteTypes.SupplierIbanChange => "تغییر حساب تأمین‌کننده",
            AiNoteTypes.PurchaseRfq => "استعلام خرید",
            AiNoteTypes.OfficeLetter => "نامه",
            AiNoteTypes.OfficeMeeting => "جلسه",
            AiNoteTypes.PcWeeklyReport => "گزارش هفتگی پروژه",
            AiNoteTypes.CeoBrief => "گزارش روزانه‌ی مدیرعامل",
            _ => type,
        };

        private static string RunStatus(AiSeatRunStatus status) => status switch
        {
            AiSeatRunStatus.Running => "در حال اجرا",
            AiSeatRunStatus.Succeeded => "موفق",
            AiSeatRunStatus.Failed => "ناموفق",
            _ => "ردشده (نیازی به اجرا نبود)",
        };

        private static DrillDefinition NotesNew() => new DrillDefinition
        {
            Key = "ai.notes_new", Module = M, Title = "یادداشت‌های جدید هوش مصنوعی",
            Description = "یادداشت‌هایی که صندلی‌های هوش مصنوعی گذاشته‌اند و هنوز کسی درباره‌شان تصمیم نگرفته است (پذیرش یا رد)؛ تازه‌ترین اول. هر نقش فقط یادداشت‌های ماژول‌های خودش را می‌بیند. می‌شود فقط یک نوع یادداشت یا فقط یادداشت‌های یک صندلی را خواست.",
            Link = "/ai/notes", RecordType = "AiNote", RecordLabel = "یادداشت هوش مصنوعی",
            Parameters = new[]
            {
                Prm.Str("type", "فقط یک نوع یادداشت (مثلا OfficeLetter، QualityNcr، CeoBrief)؛ پیش‌فرض همه‌ی نوع‌ها", null, AiNoteTypes.All),
                Prm.Str("seat", "فقط یادداشت‌های یک صندلی (کد صندلی)؛ پیش‌فرض همه‌ی صندلی‌ها"),
            },
            Columns = new[]
            {
                Col.Text("title", "عنوان"), Col.Text("seat", "صندلی"), Col.Text("type", "درباره‌ی"), Col.Text("text", "متن"), Col.Date("createdAt", "زمان ثبت"), Col.Num("ageDays", "روز از ثبت"),
                Col.Status("verified", "راستی‌آزمایی"), Col.Text("rule", "قاعده"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                var notes = c.Get<IAiNoteService>();
                string type = c.Args.Str("type"), seat = c.Args.Str("seat");
                List<AiNoteDto> news = await DrillRead.AllAsync(n => notes.GetNotesAsync(new AiNoteQuery { Status = AiNoteStatus.New, TargetType = type, Seat = seat, PageNumber = n, PageSize = AiPageSize }, ct));
                List<Row> rows = news.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Select(x => new Row(string.IsNullOrWhiteSpace(x.Link) ? "/ai/notes" : x.Link)
                {
                    ["id"] = x.Id, ["title"] = Short(x.Title, 120), ["seat"] = x.SeatCode, ["type"] = NoteType(x.TargetType), ["text"] = Short(x.Text, 160), ["createdAt"] = x.CreatedAtText,
                    ["ageDays"] = Math.Max(0, DaysBetween(x.CreatedAt.LocalDateTime, c.Today)), ["verified"] = x.Verified ? "راستی‌آزمایی‌شده" : "راستی‌آزمایی‌نشده", ["rule"] = x.RuleCode,
                }).ToList();
                return DrillResult.Slice(rows, c, "پذیرش یا رد یادداشت فقط آن را می‌بندد و رکورد اصلی را تغییر نمی‌دهد؛ راستی‌آزمایی‌شده یعنی عددهای متن با داده‌ی سامانه تطبیق داده شده است.", new Dictionary<string, object>
                {
                    ["new"] = news.Count, ["verified"] = news.Count(x => x.Verified), ["unverified"] = news.Count(x => !x.Verified),
                });
            },
        };

        private static DrillDefinition SeatRuns() => new DrillDefinition
        {
            Key = "ai.seat_runs", Module = M, Title = "اجراهای صندلی‌های هوش مصنوعی",
            Description = "اجراهای اخیر صندلی‌های هوش مصنوعی با وضعیت، زمان و خطا؛ تازه‌ترین اول. پیش‌فرض ۱۴ روز اخیر؛ می‌شود فقط اجراهای یک صندلی یا فقط یک وضعیت (در حال اجرا، موفق، ناموفق، ردشده) را خواست. در جمع‌ها شمار صندلی‌های فعالی که آخرین اجرایشان ناموفق بوده هم می‌آید (هم‌پایه با هشدار داشبورد مدیرعامل).",
            Link = "/ai/admin/seats", RecordType = "AiSeatRun", RecordLabel = "اجرای صندلی",
            Parameters = new[]
            {
                Prm.Days(14, 365, "تعداد روز اخیر"),
                Prm.Str("seat", "فقط اجراهای یک صندلی (کد صندلی)؛ پیش‌فرض همه‌ی صندلی‌ها"),
                Prm.Str("status", "فقط یک وضعیت (Running در حال اجرا، Succeeded موفق، Failed ناموفق، Skipped ردشده)؛ پیش‌فرض همه", null, new[] { "Running", "Succeeded", "Failed", "Skipped" }),
            },
            Columns = new[]
            {
                Col.Text("seat", "صندلی"), Col.Status("status", "وضعیت"), Col.Date("startedAt", "شروع"), Col.Date("finishedAt", "پایان"), Col.Num("durationSeconds", "مدت (ثانیه)"), Col.Date("asOf", "روز داده"),
                Col.Text("error", "خطا"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                var seats = c.Get<IAiSeatAdminService>();
                int days = c.Args.Int("days");
                string seat = c.Args.Str("seat"), statusText = c.Args.Str("status");
                AiSeatRunStatus? status = statusText != null && Enum.TryParse(statusText, true, out AiSeatRunStatus parsed) ? parsed : (AiSeatRunStatus?)null;
                DateTimeOffset cutoff = new DateTimeOffset(c.Today.AddDays(-days), TimeZoneInfo.Local.GetUtcOffset(c.Today.AddDays(-days)));

                // the module lists newest first and has no date filter: read pages until a run is older than the window
                var runs = new List<AiSeatRunDto>();
                bool complete = false;
                for (int number = 1; number <= DrillRead.MaxPages && !complete; number++)
                {
                    PagedList<AiSeatRunDto> page = Take(await seats.GetRunsAsync(new AiSeatRunQuery { Seat = seat, Status = status, PageNumber = number, PageSize = AiPageSize }, ct));
                    runs.AddRange(page.Where(r => r.StartedAt >= cutoff));
                    complete = page.Count == 0 || page.Any(r => r.StartedAt < cutoff) || number * AiPageSize >= page.TotalCount;
                }
                // a window with more runs than the pages hold is refused, not cut silently
                if (!complete) throw new DrillFailureException(CeoDrillText.TooManyRows);
                int failing = Take(await seats.GetSeatsAsync(ct)).Count(s => s.Enabled && s.LastRun != null && s.LastRun.Status == AiSeatRunStatus.Failed);
                List<Row> rows = runs.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).Select(r => new Row("/ai/admin/seats")
                {
                    ["id"] = r.Id, ["seat"] = r.SeatCode, ["status"] = RunStatus(r.Status), ["startedAt"] = r.StartedAtText, ["finishedAt"] = DrillDates.Stamp(r.FinishedAt),
                    ["durationSeconds"] = r.FinishedAt.HasValue ? (int?)Math.Max(0, (int)(r.FinishedAt.Value - r.StartedAt).TotalSeconds) : null, ["asOf"] = DrillDates.Text(r.AsOf), ["error"] = Short(r.Error, 160),
                }).ToList();
                return DrillResult.Slice(rows, c, "بازه از شروع اجرا شمرده می‌شود؛ اجرای «ردشده» یعنی صندلی بررسی کرد و کاری برای انجام نبود.", new Dictionary<string, object>
                {
                    ["runs"] = runs.Count, ["succeeded"] = runs.Count(r => r.Status == AiSeatRunStatus.Succeeded), ["failed"] = runs.Count(r => r.Status == AiSeatRunStatus.Failed),
                    ["skipped"] = runs.Count(r => r.Status == AiSeatRunStatus.Skipped), ["running"] = runs.Count(r => r.Status == AiSeatRunStatus.Running), ["seatsFailing"] = failing,
                });
            },
        };
    }
}
