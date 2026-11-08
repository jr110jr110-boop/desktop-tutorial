using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.AuditSeat;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.AuditSeatDtos;

namespace Services.NasbNiro.AuditSeat
{
    /// <summary>The state changes of a finding, in one place (used by the run and the service). Every change adds an event; nothing is deleted.</summary>
    internal static class AusFindingFlow
    {
        public static void Event(IUnitOfWork uow, AusFinding f, DateTimeOffset at, string kind, int userId, AusFindingStatus from, AusFindingStatus to, string note)
        {
            uow.Set<AusFindingEvent>().Add(new AusFindingEvent { FindingId = f.Id, At = at, Kind = kind, UserId = userId, FromStatus = from, ToStatus = to, Note = note, CreateDate = at, ModifyDate = at });
        }

        public static void ClearClosure(AusFinding f)
        {
            f.RecheckAt = null; f.RecheckPassed = null; f.RecheckSha256 = null;
            f.QaUserId = null; f.QaAt = null; f.QaNote = null; f.PmoUserId = null; f.PmoAt = null; f.PmoNote = null; f.CeoUserId = null; f.CeoAt = null; f.CeoNote = null;
        }

        /// <summary>Back to Open with the reason; the closure fields are cleared (the events keep who signed what).</summary>
        public static void Reopen(AusFinding f, DateTimeOffset at, int userId, string note, IUnitOfWork uow, string kind = "reopened")
        {
            AusFindingStatus from = f.Status;
            ClearClosure(f);
            f.Status = AusFindingStatus.Open; f.ModifyDate = at;
            Event(uow, f, at, kind, userId, from, AusFindingStatus.Open, note);
        }

        public static void ApplyRecheck(AusFinding f, bool passed, DateTimeOffset at, int runId, int userId, string note, IUnitOfWork uow)
        {
            AusFindingStatus from = f.Status;
            f.RecheckAt = at; f.RecheckPassed = passed;
            f.RecheckSha256 = AusEvidence.Sha256Hex(f.Number + "|" + f.RuleCode + "|" + f.ObjectKey + "|" + (passed ? "pass" : "fail") + "|" + at.ToString("o", System.Globalization.CultureInfo.InvariantCulture) + "|" + runId);
            f.Status = passed ? AusFindingStatus.Rechecked : AusFindingStatus.Open; f.ModifyDate = at;
            if (!passed) { f.RecheckPassed = false; }
            Event(uow, f, at, passed ? "recheck-pass" : "recheck-fail", userId, from, f.Status, note);
        }
    }

    public interface IAusFindingService
    {
        Task<ResponseDto<List<AusFindingListDto>>> GetAsync(AusFindingQuery query, CancellationToken ct);
        Task<ResponseDto<AusFindingDto>> GetOneAsync(int id, CancellationToken ct);
        Task<ResponseDto<AusFindingDto>> ProposeActionAsync(AusActionDto dto, CancellationToken ct);
        Task<ResponseDto<AusFindingDto>> StepAsync(AusStepDto dto, CancellationToken ct);
        Task<ResponseDto<AusFindingDto>> CreateManualAsync(AusManualFindingDto dto, CancellationToken ct);
        Task<ResponseDto<AusFindingDto>> ManualRecheckAsync(AusManualRecheckDto dto, CancellationToken ct);
        Task<ResponseDto<AusStatusDto>> GetStatusAsync(CancellationToken ct);
    }

    /// <summary>
    /// The closure chain of a finding (CEO decisions D11 and D12): the owner proposes root cause, correction and corrective action; the rule re-checks itself; the QA manager proposes closure, then the PMO manager (another person),
    /// then the CEO approves (a third person). The owner, the person who proposed the action and an AI seat can sign none of the steps. A refusal at any step returns the finding to Open with the reason.
    /// A void takes the same three persons and a reason. There is no automatic closing, no deletion and no dropping by severity.
    /// </summary>
    public class AusFindingService : IAusFindingService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IAusRunService _runs;
        private readonly INotificationService _notify;

        public AusFindingService(IUnitOfWork uow, IInventoryUserContext user, IAusRunService runs, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static bool IsAi => AiActing.IsActing;
        private bool InRole(string r) => _user.IsInRole(r);
        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private const int MinText = 10;

        // ---------- reading

        private static IQueryable<AusFinding> Filter(IQueryable<AusFinding> q, AusFindingQuery f, int me)
        {
            q = q.Where(x => !x.Deleted);
            if (f == null) return q;
            if (!string.IsNullOrWhiteSpace(f.Status) && Enum.TryParse(f.Status, true, out AusFindingStatus s)) q = q.Where(x => x.Status == s);
            if (!string.IsNullOrWhiteSpace(f.Severity) && Enum.TryParse(f.Severity, true, out AusSeverity v)) q = q.Where(x => x.Severity == v);
            if (!string.IsNullOrWhiteSpace(f.ProcessCode)) { string p = f.ProcessCode.Trim().ToUpperInvariant(); q = q.Where(x => x.ProcessCode == p); }
            if (!string.IsNullOrWhiteSpace(f.RuleCode)) { string r = f.RuleCode.Trim().ToUpperInvariant(); q = q.Where(x => x.RuleCode == r); }
            if (!string.IsNullOrWhiteSpace(f.Text)) { string t = f.Text.Trim(); q = q.Where(x => x.Number.Contains(t) || x.Title.Contains(t) || (x.ObjectNumber != null && x.ObjectNumber.Contains(t)) || (x.Description != null && x.Description.Contains(t))); }
            if (f.OpenOnly == true) q = q.Where(x => x.Status != AusFindingStatus.Closed && x.Status != AusFindingStatus.Voided);
            if (f.Overdue == true) { DateTime today = DateTime.Now.Date; q = q.Where(x => x.Status != AusFindingStatus.Closed && x.Status != AusFindingStatus.Voided && x.DueDate < today); }
            if (f.Mine == true) q = q.Where(x => x.OwnerUserId == me);
            return q;
        }

        private static AusFindingListDto ToList(AusFinding x, Dictionary<int, string> names, DateTime today)
        {
            return Fill(new AusFindingListDto(), x, names, today);
        }

        private static T Fill<T>(T d, AusFinding x, Dictionary<int, string> names, DateTime today) where T : AusFindingListDto
        {
            d.Id = x.Id; d.Number = x.Number; d.RuleCode = x.RuleCode; d.Title = x.Title; d.Standard = x.Standard; d.Clause = x.Clause; d.ProcessCode = x.ProcessCode; d.ProcessTitle = AusProcesses.TitleOf(x.ProcessCode);
            d.Severity = AusRules.SeverityText(x.Severity); d.SeverityValue = (int)x.Severity; d.Status = AusRules.StatusText(x.Status); d.StatusValue = (int)x.Status; d.ObjectNumber = x.ObjectNumber;
            d.OwnerUserId = x.OwnerUserId; d.OwnerName = AusNames.Of(names, x.OwnerUserId); d.OwnerRole = x.OwnerRole; d.DueDate = x.DueDate; d.DueDateText = AusEvidence.Day(x.DueDate);
            d.Overdue = !AusRules.IsTerminal(x.Status) && x.DueDate < today; d.AgeDays = Math.Max(0, (int)(today - x.DetectedAt.LocalDateTime.Date).TotalDays);
            d.DetectedAtText = AusRunService.Stamp(x.DetectedAt); d.RecurrenceCount = x.RecurrenceCount; d.RecurrenceOfId = x.RecurrenceOfId; d.Source = x.Source == AusFindingSource.Rule ? "قاعده" : "ممیزی فرایند";
            return d;
        }

        public async Task<ResponseDto<List<AusFindingListDto>>> GetAsync(AusFindingQuery query, CancellationToken ct)
        {
            if (!InRole(AusRoles.Readers) && !InRole(AusRoles.Actioners)) return Bad<List<AusFindingListDto>>(QualityText.NotAllowed);
            bool reader = InRole(AusRoles.Readers);
            int n = Math.Min(Math.Max(query?.Limit ?? 200, 1), 1000);
            IQueryable<AusFinding> q = Filter(_uow.Set<AusFinding>().AsNoTracking(), query, Me);
            if (!reader) q = q.Where(x => x.OwnerUserId == Me);
            List<AusFinding> rows = await q.OrderByDescending(x => x.Severity).ThenBy(x => x.DueDate).ThenByDescending(x => x.Id).Take(n).ToListAsync(ct);
            Dictionary<int, string> names = await AusNames.LoadAsync(_uow, rows.Select(r => r.OwnerUserId), ct);
            DateTime today = DateTime.Now.Date;
            return ResponseDtoHelper.Ok(rows.Select(r => ToList(r, names, today)).ToList());
        }

        public async Task<ResponseDto<AusFindingDto>> GetOneAsync(int id, CancellationToken ct)
        {
            AusFinding f = await _uow.Set<AusFinding>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (f == null) return QualityText.NotFound<AusFindingDto>("یافته پیدا نشد.");
            if (!InRole(AusRoles.Readers) && f.OwnerUserId != Me) return Bad<AusFindingDto>(QualityText.NotAllowed);
            return ResponseDtoHelper.Ok(await DetailAsync(f, ct));
        }

        private async Task<AusFindingDto> DetailAsync(AusFinding f, CancellationToken ct)
        {
            List<AusFindingEvent> events = await _uow.Set<AusFindingEvent>().AsNoTracking().Where(e => e.FindingId == f.Id && !e.Deleted).OrderBy(e => e.Id).ToListAsync(ct);
            var related = await _uow.Set<AusFinding>().AsNoTracking().Where(x => !x.Deleted && x.Id != f.Id && ((x.RecurrenceOfId == f.Id) || (f.RecurrenceOfId != null && x.Id == f.RecurrenceOfId))).OrderBy(x => x.Id).Take(20).ToListAsync(ct);
            var ids = new List<int?> { f.OwnerUserId, f.ActionByUserId, f.QaUserId, f.PmoUserId, f.CeoUserId };
            ids.AddRange(events.Select(e => (int?)e.UserId)); ids.AddRange(related.Select(r => r.OwnerUserId));
            Dictionary<int, string> names = await AusNames.LoadAsync(_uow, ids, ct);
            DateTime today = DateTime.Now.Date;
            AusFindingDto d = Fill(new AusFindingDto(), f, names, today);
            d.Description = f.Description; d.Procedure = f.Procedure; d.ObjectType = f.ObjectType; d.ObjectId = f.ObjectId; d.Link = f.Link; d.EvidenceJson = f.EvidenceJson; d.EvidenceSha256 = f.EvidenceSha256; d.OwnerUnit = f.OwnerUnit;
            d.SeenCount = f.SeenCount; d.LastSeenAtText = AusRunService.Stamp(f.LastSeenAt); d.RootCause = f.RootCause; d.Correction = f.Correction; d.CorrectiveAction = f.CorrectiveAction; d.CapaId = f.CapaId;
            d.ActionByName = AusNames.Of(names, f.ActionByUserId); d.ActionAtText = f.ActionAt.HasValue ? AusRunService.Stamp(f.ActionAt.Value) : null; d.RecheckAtText = f.RecheckAt.HasValue ? AusRunService.Stamp(f.RecheckAt.Value) : null;
            d.RecheckPassed = f.RecheckPassed; d.RecheckSha256 = f.RecheckSha256;
            d.QaName = AusNames.Of(names, f.QaUserId); d.QaAtText = f.QaAt.HasValue ? AusRunService.Stamp(f.QaAt.Value) : null; d.QaNote = f.QaNote;
            d.PmoName = AusNames.Of(names, f.PmoUserId); d.PmoAtText = f.PmoAt.HasValue ? AusRunService.Stamp(f.PmoAt.Value) : null; d.PmoNote = f.PmoNote;
            d.CeoName = AusNames.Of(names, f.CeoUserId); d.CeoAtText = f.CeoAt.HasValue ? AusRunService.Stamp(f.CeoAt.Value) : null; d.CeoNote = f.CeoNote;
            d.ClosedAtText = f.ClosedAt.HasValue ? AusRunService.Stamp(f.ClosedAt.Value) : null; d.VoidReason = f.VoidReason; d.VoidedAtText = f.VoidedAt.HasValue ? AusRunService.Stamp(f.VoidedAt.Value) : null;
            d.NextStep = NextStep(f);
            d.Allowed = AllowedFor(f, events);
            d.Events = events.Select(e => new AusFindingEventDto { AtText = AusRunService.Stamp(e.At), Kind = e.Kind, KindTitle = KindTitle(e.Kind), UserName = e.UserId > 0 ? AusNames.Of(names, e.UserId) : "ممیز هوشمند", From = AusRules.StatusText(e.FromStatus), To = AusRules.StatusText(e.ToStatus), Note = e.Note }).ToList();
            d.Related = related.Select(r => ToList(r, names, today)).ToList();
            return d;
        }

        private static string KindTitle(string kind)
        {
            switch (kind)
            {
                case "detected": return "شناسایی";
                case "manual": return "ثبت توسط ممیز";
                case "action": return "پیشنهاد اقدام مسئول";
                case "recheck-pass": return "بازبینی: قبول";
                case "recheck-fail": return "بازبینی: رد";
                case "qa": return "پیشنهاد بستن QA";
                case "pmo": return "پیشنهاد بستن PMO";
                case "ceo": return "تایید مدیرعامل (بسته شد)";
                case "refused": return "رد و بازگشت به باز";
                case "reopened": return "بازگشایی";
                case "void-qa": return "درخواست ابطال QA";
                case "void-pmo": return "ابطال: PMO";
                case "void-ceo": return "ابطال: مدیرعامل";
                case "void-refused": return "ابطال رد شد";
                default: return kind;
            }
        }

        private static string NextStep(AusFinding f)
        {
            switch (f.Status)
            {
                case AusFindingStatus.Open: return "مسئول ریشه، اصلاح و اقدام اصلاحی را ثبت کند.";
                case AusFindingStatus.ActionProposed: return f.Source == AusFindingSource.Manual ? "ممیز اقدام را راستی‌آزمایی کند." : "بازبینی خودکار قاعده.";
                case AusFindingStatus.Rechecked: return "مدیر QA بستن را پیشنهاد دهد.";
                case AusFindingStatus.QaProposed: return "مدیر PMO بستن را پیشنهاد دهد (نفر دوم).";
                case AusFindingStatus.PmoProposed: return "مدیرعامل تایید نهایی کند (نفر سوم).";
                default: return null;
            }
        }

        private List<string> AllowedFor(AusFinding f, List<AusFindingEvent> events)
        {
            var a = new List<string>();
            if (IsAi || AusRules.IsTerminal(f.Status)) return a;
            HashSet<int> actioners = Actioners(f, events);
            bool blockedBase = f.OwnerUserId == Me || actioners.Contains(Me);
            if (f.Status == AusFindingStatus.Open && (f.OwnerUserId == Me || (f.OwnerUserId == null && f.OwnerRole != null && InRole(f.OwnerRole)))) a.Add("action");
            if (f.Status == AusFindingStatus.ActionProposed && f.Source == AusFindingSource.Manual && InRole(AusRoles.Auditor) && !blockedBase) a.Add("recheck");
            if (!blockedBase)
            {
                if (f.Status == AusFindingStatus.Rechecked && InRole(AusRoles.Qa)) { a.Add("qa"); a.Add("refuse"); }
                if (f.Status == AusFindingStatus.QaProposed && InRole(AusRoles.Pmo) && f.QaUserId != Me) { a.Add("pmo"); a.Add("refuse"); }
                if (f.Status == AusFindingStatus.PmoProposed && InRole(AusRoles.Ceo) && f.QaUserId != Me && f.PmoUserId != Me) { a.Add("ceo"); a.Add("refuse"); }
                if (f.VoidQaUserId == null && f.VoidReason == null && InRole(AusRoles.Qa)) a.Add("void-qa");
                if (f.VoidQaUserId != null && f.VoidPmoUserId == null && InRole(AusRoles.Pmo) && f.VoidQaUserId != Me) { a.Add("void-pmo"); a.Add("void-refuse"); }
                if (f.VoidPmoUserId != null && InRole(AusRoles.Ceo) && f.VoidQaUserId != Me && f.VoidPmoUserId != Me) { a.Add("void-ceo"); a.Add("void-refuse"); }
            }
            return a;
        }

        private static HashSet<int> Actioners(AusFinding f, IEnumerable<AusFindingEvent> events)
        {
            var set = new HashSet<int>(events.Where(e => e.Kind == "action" && e.UserId > 0).Select(e => e.UserId));
            if (f.ActionByUserId.HasValue) set.Add(f.ActionByUserId.Value);
            return set;
        }

        // ---------- owner's action

        public async Task<ResponseDto<AusFindingDto>> ProposeActionAsync(AusActionDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<AusFindingDto>(QualityText.InvalidData);
            if (IsAi) return Bad<AusFindingDto>("هوش مصنوعی اقدام اصلاحی را به جای مسئول ثبت نمی‌کند.");
            AusFinding f = await _uow.Set<AusFinding>().FirstOrDefaultAsync(x => x.Id == dto.FindingId && !x.Deleted, ct);
            if (f == null) return QualityText.NotFound<AusFindingDto>("یافته پیدا نشد.");
            bool owner = f.OwnerUserId == Me || (f.OwnerUserId == null && f.OwnerRole != null && InRole(f.OwnerRole));
            if (!owner) return Bad<AusFindingDto>("فقط مسئول یافته می‌تواند اقدام را پیشنهاد دهد.");
            if (f.Status != AusFindingStatus.Open) return Bad<AusFindingDto>("اقدام فقط برای یافته‌ی باز ثبت می‌شود؛ وضعیت فعلی: " + AusRules.StatusText(f.Status));
            string root = QualityText.Clean(dto.RootCause), corr = QualityText.Clean(dto.Correction), act = QualityText.Clean(dto.CorrectiveAction);
            if (root == null || root.Length < MinText) return Bad<AusFindingDto>("ریشه‌ی علت را (دست‌کم ۱۰ نویسه) بنویسید.");
            if (corr == null || corr.Length < MinText) return Bad<AusFindingDto>("اصلاح انجام‌شده را (دست‌کم ۱۰ نویسه) بنویسید.");
            if (act == null || act.Length < MinText) return Bad<AusFindingDto>("اقدام اصلاحی برای جلوگیری از تکرار را (دست‌کم ۱۰ نویسه) بنویسید.");
            if (dto.CapaId.HasValue && !await _uow.Set<Capa>().AnyAsync(c => c.Id == dto.CapaId.Value && !c.Deleted, ct)) return Bad<AusFindingDto>("CAPA انتخاب‌شده پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            AusFindingStatus from = f.Status;
            f.RootCause = root; f.Correction = corr; f.CorrectiveAction = act; f.CapaId = dto.CapaId; f.ActionByUserId = Me; f.ActionAt = now; f.Status = AusFindingStatus.ActionProposed; f.ModifyDate = now;
            AusFindingFlow.Event(_uow, f, now, "action", Me, from, f.Status, "ریشه: " + root);
            if (!await Save(ct)) return Bad<AusFindingDto>(QualityText.Concurrency);
            // the automatic re-check: the rule runs again and must no longer see the deviation. A manual finding is verified by a human auditor.
            if (f.Source == AusFindingSource.Rule)
            {
                bool still;
                try { still = await _runs.StillDeviatesAsync(f, ct); }
                catch (Exception ex) when (!(ex is OperationCanceledException)) { return ResponseDtoHelper.Ok(await DetailAsync(f, ct), "اقدام ثبت شد؛ بازبینی خودکار اجرا نشد و در اجرای بعدی انجام می‌شود."); }
                AusFindingFlow.ApplyRecheck(f, !still, DateTimeOffset.Now, 0, 0, still ? "بازبینی خودکار: انحراف هنوز دیده می‌شود؛ یافته به باز برگشت." : "بازبینی خودکار: انحراف دیده نشد.", _uow);
                if (!await Save(ct)) return Bad<AusFindingDto>(QualityText.Concurrency);
                return ResponseDtoHelper.Ok(await DetailAsync(f, ct), still ? "بازبینی خودکار رد شد: انحراف هنوز هست." : "بازبینی خودکار قبول شد؛ منتظر پیشنهاد QA.");
            }
            return ResponseDtoHelper.Ok(await DetailAsync(f, ct), "اقدام ثبت شد؛ ممیز باید آن را راستی‌آزمایی کند.");
        }

        public async Task<ResponseDto<AusFindingDto>> ManualRecheckAsync(AusManualRecheckDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<AusFindingDto>(QualityText.InvalidData);
            if (IsAi) return Bad<AusFindingDto>("راستی‌آزمایی اقدام ممیزی فرایند با ممیز انسانی است.");
            if (!InRole(AusRoles.Auditor)) return Bad<AusFindingDto>(QualityText.NotAllowed);
            AusFinding f = await _uow.Set<AusFinding>().FirstOrDefaultAsync(x => x.Id == dto.FindingId && !x.Deleted, ct);
            if (f == null) return QualityText.NotFound<AusFindingDto>("یافته پیدا نشد.");
            if (f.Source != AusFindingSource.Manual) return Bad<AusFindingDto>("بازبینی یافته‌ی قاعده‌ای خودکار است.");
            if (f.Status != AusFindingStatus.ActionProposed) return Bad<AusFindingDto>("یافته در مرحله‌ی راستی‌آزمایی نیست.");
            List<AusFindingEvent> ev = await _uow.Set<AusFindingEvent>().AsNoTracking().Where(e => e.FindingId == f.Id).ToListAsync(ct);
            if (f.OwnerUserId == Me || Actioners(f, ev).Contains(Me)) return Bad<AusFindingDto>("مسئول یا پیشنهاددهنده‌ی اقدام نمی‌تواند خودش آن را راستی‌آزمایی کند.");
            string note = QualityText.Clean(dto.Note);
            if (note == null || note.Length < MinText) return Bad<AusFindingDto>("نتیجه‌ی راستی‌آزمایی را (دست‌کم ۱۰ نویسه) بنویسید.");
            AusFindingFlow.ApplyRecheck(f, dto.Passed, DateTimeOffset.Now, 0, Me, note, _uow);
            if (!await Save(ct)) return Bad<AusFindingDto>(QualityText.Concurrency);
            return ResponseDtoHelper.Ok(await DetailAsync(f, ct));
        }

        // ---------- closure chain and void

        public async Task<ResponseDto<AusFindingDto>> StepAsync(AusStepDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Step)) return Bad<AusFindingDto>(QualityText.InvalidData);
            string step = dto.Step.Trim().ToLowerInvariant();
            if (!new[] { "qa", "pmo", "ceo", "void-qa", "void-pmo", "void-ceo" }.Contains(step)) return Bad<AusFindingDto>("مرحله نامعتبر است.");
            AusFinding f = await _uow.Set<AusFinding>().FirstOrDefaultAsync(x => x.Id == dto.FindingId && !x.Deleted, ct);
            if (f == null) return QualityText.NotFound<AusFindingDto>("یافته پیدا نشد.");
            string role = step.EndsWith("qa") ? AusRoles.Qa : step.EndsWith("pmo") ? AusRoles.Pmo : AusRoles.Ceo;
            if (IsAi) return Bad<AusFindingDto>(AusRules.ClosureRefusal(Me, true, f, step));
            if (!InRole(role)) return Bad<AusFindingDto>(QualityText.NotAllowed);
            if (AusRules.IsTerminal(f.Status)) return Bad<AusFindingDto>("این یافته بسته یا ابطال شده است.");
            List<AusFindingEvent> ev = await _uow.Set<AusFindingEvent>().AsNoTracking().Where(e => e.FindingId == f.Id).ToListAsync(ct);
            string refusal = AusRules.ClosureRefusal(Me, false, f, step);
            if (refusal == null && Actioners(f, ev).Contains(Me)) refusal = "کسی که اقدام اصلاحی را پیشنهاد داده است نمی‌تواند آن را ببندد یا ابطال کند.";
            if (refusal != null) return Bad<AusFindingDto>(refusal);
            string note = QualityText.Clean(dto.Note);
            DateTimeOffset now = DateTimeOffset.Now;
            AusFindingStatus from = f.Status;

            if (step.StartsWith("void-")) return await VoidStepAsync(f, step, dto.Refuse, note, now, ct);

            if (dto.Refuse)
            {
                if (note == null || note.Length < MinText) return Bad<AusFindingDto>("دلیل رد را (دست‌کم ۱۰ نویسه) بنویسید.");
                string expected = step == "qa" ? "Rechecked" : step == "pmo" ? "QaProposed" : "PmoProposed";
                if (f.Status.ToString() != expected) return Bad<AusFindingDto>("یافته در مرحله‌ی " + step.ToUpperInvariant() + " نیست: " + AusRules.StatusText(f.Status));
                AusFindingFlow.Reopen(f, now, Me, step.ToUpperInvariant() + " رد کرد: " + note, _uow, "refused");
            }
            else if (step == "qa")
            {
                if (f.Status != AusFindingStatus.Rechecked || f.RecheckPassed != true) return Bad<AusFindingDto>("بازبینی خودکار انجام نشده یا قبول نشده است؛ پیش از پیشنهاد بستن باید بازبینی قبول شود.");
                f.QaUserId = Me; f.QaAt = now; f.QaNote = note; f.Status = AusFindingStatus.QaProposed; f.ModifyDate = now;
                AusFindingFlow.Event(_uow, f, now, "qa", Me, from, f.Status, note);
            }
            else if (step == "pmo")
            {
                if (f.Status != AusFindingStatus.QaProposed) return Bad<AusFindingDto>("ابتدا باید QA بستن را پیشنهاد دهد: " + AusRules.StatusText(f.Status));
                f.PmoUserId = Me; f.PmoAt = now; f.PmoNote = note; f.Status = AusFindingStatus.PmoProposed; f.ModifyDate = now;
                AusFindingFlow.Event(_uow, f, now, "pmo", Me, from, f.Status, note);
            }
            else
            {
                if (f.Status != AusFindingStatus.PmoProposed) return Bad<AusFindingDto>("ابتدا باید QA و PMO بستن را پیشنهاد دهند: " + AusRules.StatusText(f.Status));
                f.CeoUserId = Me; f.CeoAt = now; f.CeoNote = note; f.Status = AusFindingStatus.Closed; f.ClosedAt = now; f.ModifyDate = now;
                AusFindingFlow.Event(_uow, f, now, "ceo", Me, from, f.Status, note);
            }
            if (!await Save(ct)) return Bad<AusFindingDto>(QualityText.Concurrency);
            await NotifyNextAsync(f, ct);
            return ResponseDtoHelper.Ok(await DetailAsync(f, ct));
        }

        private async Task<ResponseDto<AusFindingDto>> VoidStepAsync(AusFinding f, string step, bool refuse, string note, DateTimeOffset now, CancellationToken ct)
        {
            if (refuse)
            {
                if (note == null || note.Length < MinText) return Bad<AusFindingDto>("دلیل رد ابطال را (دست‌کم ۱۰ نویسه) بنویسید.");
                if (f.VoidQaUserId == null) return Bad<AusFindingDto>("درخواست ابطالی در جریان نیست.");
                f.VoidReason = null; f.VoidQaUserId = null; f.VoidPmoUserId = null; f.ModifyDate = now;
                AusFindingFlow.Event(_uow, f, now, "void-refused", Me, f.Status, f.Status, step + " رد کرد: " + note);
            }
            else if (step == "void-qa")
            {
                if (f.VoidQaUserId != null) return Bad<AusFindingDto>("درخواست ابطال از پیش ثبت شده است.");
                if (note == null || note.Length < 15) return Bad<AusFindingDto>("دلیل ابطال را (دست‌کم ۱۵ نویسه) بنویسید.");
                f.VoidReason = note; f.VoidQaUserId = Me; f.ModifyDate = now;
                AusFindingFlow.Event(_uow, f, now, "void-qa", Me, f.Status, f.Status, note);
            }
            else if (step == "void-pmo")
            {
                if (f.VoidQaUserId == null || f.VoidPmoUserId != null) return Bad<AusFindingDto>("ابتدا QA باید ابطال را درخواست کند.");
                f.VoidPmoUserId = Me; f.ModifyDate = now;
                AusFindingFlow.Event(_uow, f, now, "void-pmo", Me, f.Status, f.Status, note);
            }
            else
            {
                if (f.VoidPmoUserId == null) return Bad<AusFindingDto>("ابتدا QA و PMO باید ابطال را تایید کنند.");
                AusFindingStatus from = f.Status;
                f.VoidCeoUserId = Me; f.VoidedAt = now; f.Status = AusFindingStatus.Voided; f.ModifyDate = now;
                AusFindingFlow.Event(_uow, f, now, "void-ceo", Me, from, f.Status, note ?? f.VoidReason);
            }
            if (!await Save(ct)) return Bad<AusFindingDto>(QualityText.Concurrency);
            return ResponseDtoHelper.Ok(await DetailAsync(f, ct));
        }

        private async Task NotifyNextAsync(AusFinding f, CancellationToken ct)
        {
            if (_notify == null) return;
            string next = f.Status == AusFindingStatus.QaProposed ? AusRoles.Pmo : f.Status == AusFindingStatus.PmoProposed ? AusRoles.Ceo : f.Status == AusFindingStatus.Open ? null : null;
            if (f.Status == AusFindingStatus.Open && f.OwnerUserId.HasValue)
            {
                var back = new NotifyRequest { SourceKey = "aus:" + f.Id, Module = "AUDIT", SourceType = "AusFinding", SourceId = f.Id, Title = $"یافته‌ی {f.Number} رد شد و به باز برگشت", Link = "/audit/findings/" + f.Id, Kind = Entities.Model.NasbNiro.Office.OfInboxKind.Task };
                back.UserIds.Add(f.OwnerUserId.Value);
                await _notify.NotifyAsync(back, ct);
                return;
            }
            if (next == null) return;
            await _notify.NotifyAsync(new NotifyRequest { SourceKey = "aus-close:" + f.Id + ":" + (int)f.Status, Module = "AUDIT", SourceType = "AusFinding", SourceId = f.Id, Title = $"بستن یافته‌ی {f.Number} منتظر شماست", Link = "/audit/findings/" + f.Id, Kind = Entities.Model.NasbNiro.Office.OfInboxKind.Approval, Roles = next }, ct);
        }

        private async Task<bool> Save(CancellationToken ct)
        {
            try { await _uow.SaveChangesAsync(ct); return true; }
            catch (DbUpdateConcurrencyException) { return false; }
        }

        // ---------- manual finding (process audit)

        public async Task<ResponseDto<AusFindingDto>> CreateManualAsync(AusManualFindingDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<AusFindingDto>(QualityText.InvalidData);
            if (IsAi) return Bad<AusFindingDto>("یافته‌ی ممیزی فرایند را ممیز انسانی ثبت می‌کند.");
            if (!InRole(AusRoles.Auditor + "," + AusRoles.Qa)) return Bad<AusFindingDto>(QualityText.NotAllowed);
            AusAudit audit = await _uow.Set<AusAudit>().FirstOrDefaultAsync(a => a.Id == dto.AuditId && !a.Deleted, ct);
            if (audit == null) return QualityText.NotFound<AusFindingDto>("ممیزی فرایند پیدا نشد.");
            if (audit.Status != AusAuditStatus.InProgress) return Bad<AusFindingDto>("یافته فقط در ممیزی «در حال انجام» ثبت می‌شود.");
            if (audit.LeadAuditorUserId != Me) return Bad<AusFindingDto>("فقط ممیز ارشد این ممیزی یافته ثبت می‌کند.");
            string title = QualityText.Clean(dto.Title), desc = QualityText.Clean(dto.Description);
            if (title == null || title.Length < 5) return Bad<AusFindingDto>("عنوان یافته را بنویسید.");
            if (desc == null || desc.Length < MinText) return Bad<AusFindingDto>("شرح یافته را (دست‌کم ۱۰ نویسه) بنویسید.");
            if (dto.Severity < 1 || dto.Severity > 3) return Bad<AusFindingDto>("شدت یافته نامعتبر است.");
            int? owner = dto.OwnerUserId ?? audit.AuditeeUserId;
            if (owner == Me) return Bad<AusFindingDto>("ممیز و مسئول یافته نمی‌توانند یک نفر باشند.");
            AusAuditCheck check = null;
            if (dto.CheckId.HasValue)
            {
                check = await _uow.Set<AusAuditCheck>().FirstOrDefaultAsync(c => c.Id == dto.CheckId.Value && c.AuditId == audit.Id && !c.Deleted, ct);
                if (check == null) return Bad<AusFindingDto>("بند فهرست بازبینی پیدا نشد.");
            }
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(_uow, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            var sev = (AusSeverity)dto.Severity;
            int year = AusRules.PersianYear(now.LocalDateTime);
            await AusRunService.Gate.WaitAsync(ct);
            AusFinding f;
            try
            {
                int seq = await _uow.Set<AusFinding>().Where(x => x.Year == year).Select(x => (int?)x.Sequence).MaxAsync(ct) ?? 0;
                seq++;
                int n = await _uow.Set<AusFinding>().CountAsync(x => x.AuditId == audit.Id, ct) + 1;
                var snap = new SortedDictionary<string, string> { ["audit"] = audit.Number, ["title"] = title, ["description"] = desc, ["clause"] = dto.Clause ?? check?.Clause, ["auditor"] = Me.ToString(), ["at"] = now.ToString("o", System.Globalization.CultureInfo.InvariantCulture) };
                string evidence = System.Text.Json.JsonSerializer.Serialize(snap, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                f = new AusFinding
                {
                    Number = AusRules.Number(year, seq), Year = year, Sequence = seq, Source = AusFindingSource.Manual, RuleCode = "MANUAL", Standard = dto.Standard ?? check?.Standard ?? "ISO 19011:2018", Clause = dto.Clause ?? check?.Clause ?? "6.4",
                    Procedure = "ممیزی فرایند " + audit.Number, ProcessCode = audit.ProcessCode, Severity = sev, BaseSeverity = sev, Title = title, Description = desc, ObjectType = "AusAudit", ObjectId = audit.Id, ObjectKey = "AusAudit|" + audit.Id + "|" + n,
                    ObjectNumber = dto.ObjectNumber ?? audit.Number, Link = "/audit/process-audits", EvidenceJson = evidence, EvidenceSha256 = AusEvidence.Sha256Hex(evidence), OwnerUserId = owner, OwnerRole = AusProcesses.Find(audit.ProcessCode)?.OwnerRole,
                    OwnerUnit = audit.AuditeeUnit ?? AusProcesses.Find(audit.ProcessCode)?.OwnerUnit, DueDate = AusRules.DueDate(now.LocalDateTime, sev, st), DetectedAt = now, LastSeenAt = now, SeenCount = 1, Status = AusFindingStatus.Open, AuditId = audit.Id, CreateDate = now, ModifyDate = now,
                };
                _uow.Set<AusFinding>().Add(f);
                await _uow.SaveChangesAsync(ct);
                AusFindingFlow.Event(_uow, f, now, "manual", Me, AusFindingStatus.Open, AusFindingStatus.Open, title);
                if (check != null) { check.Result = AusCheckResult.NonConform; check.FindingId = f.Id; check.CheckedAt = now; check.CheckedByUserId = Me; check.ModifyDate = now; }
                await _uow.SaveChangesAsync(ct);
            }
            finally { AusRunService.Gate.Release(); }
            if (_notify != null && sev == AusSeverity.Major)
            {
                var req = new NotifyRequest { SourceKey = "aus:" + f.Id, Module = "AUDIT", SourceType = "AusFinding", SourceId = f.Id, Kind = Entities.Model.NasbNiro.Office.OfInboxKind.Alert, Priority = Entities.Model.NasbNiro.Office.OfUrgency.Immediate, DueDate = f.DueDate, Title = $"یافته‌ی عمده‌ی ممیزی {f.Number}: {f.Title}", Link = "/audit/findings/" + f.Id, Roles = AusRoles.Qa + "," + AusRoles.Pmo + "," + AusRoles.Ceo };
                if (owner.HasValue) req.UserIds.Add(owner.Value);
                await _notify.NotifyAsync(req, ct);
            }
            return ResponseDtoHelper.Ok(await DetailAsync(f, ct));
        }

        // ---------- status (widget and CEO-BRIEF)

        public async Task<ResponseDto<AusStatusDto>> GetStatusAsync(CancellationToken ct)
        {
            if (!InRole(AusRoles.Readers) && !InRole(AusRoles.Actioners)) return Bad<AusStatusDto>(QualityText.NotAllowed);
            return ResponseDtoHelper.Ok(await AusStatusBuilder.BuildAsync(_uow, Me, role => InRole(role), ct));
        }
    }

    public static class AusStatusBuilder
    {
        public static async Task<AusStatusDto> BuildAsync(IUnitOfWork uow, int me, Func<string, bool> inRole, CancellationToken ct)
        {
            DateTime today = DateTime.Now.Date;
            var open = await uow.Set<AusFinding>().AsNoTracking().Where(f => !f.Deleted && f.Status != AusFindingStatus.Closed && f.Status != AusFindingStatus.Voided)
                .Select(f => new { f.Severity, f.Status, f.DueDate, f.OwnerUserId }).ToListAsync(ct);
            AusRun last = await uow.Set<AusRun>().AsNoTracking().Where(r => !r.Deleted).OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(uow, ct);
            int enabled = AusCatalog.All.Count(r => !(st.TryGetValue(AusSettingKeys.RuleKey(r.Code), out decimal on) && on == 0m));
            bool master = st[AusSettingKeys.Master] != 0m;
            int my = open.Count(f => f.Status == AusFindingStatus.Open && f.OwnerUserId == me);
            if (inRole(AusRoles.Qa)) my += open.Count(f => f.Status == AusFindingStatus.Rechecked);
            if (inRole(AusRoles.Pmo)) my += open.Count(f => f.Status == AusFindingStatus.QaProposed);
            if (inRole(AusRoles.Ceo)) my += open.Count(f => f.Status == AusFindingStatus.PmoProposed);
            return new AusStatusDto
            {
                Enabled = master, Open = open.Count, OpenMajor = open.Count(f => f.Severity == AusSeverity.Major), OpenMinor = open.Count(f => f.Severity == AusSeverity.Minor), OpenObservation = open.Count(f => f.Severity == AusSeverity.Observation),
                Overdue = open.Count(f => f.DueDate < today), WaitingQa = open.Count(f => f.Status == AusFindingStatus.Rechecked), WaitingPmo = open.Count(f => f.Status == AusFindingStatus.QaProposed), WaitingCeo = open.Count(f => f.Status == AusFindingStatus.PmoProposed),
                MyTurn = my, LastRunAtText = last == null ? null : AusRunService.Stamp(last.StartedAt), LastRunStatus = last == null ? null : last.Status.ToString(), RulesEnabled = master ? enabled : 0, RulesTotal = AusCatalog.All.Count,
            };
        }
    }
}
