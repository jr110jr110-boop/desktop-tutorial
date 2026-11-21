using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Engineering;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Services.Contracts;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.EngineeringDtos;

namespace Services.NasbNiro.Engineering
{
    /// <summary>The shared rules of the project MDL (employer rule 20): the hash, the CSV, the lookup of the live revision and the ONE check of the tender release lock (used by the case action and by the case flag, so they never disagree).</summary>
    internal static class EnProjectMdlRules
    {
        public const string LockText = "انتشار بستهٔ فنی مناقصه بسته است: فهرست مدارک پروژه (MDL) هنوز توسط مدیر مهندسی تایید نشده است. در پروژهٔ خاص، مدیر مهندسی «مناقصه بدون MDL» را درخواست می‌کند و فقط مدیرعامل تصمیم می‌گیرد.";
        public const string NoticeText = "بستهٔ فنی مناقصه تا تایید فهرست مدارک پروژه (MDL) منتشر نمی‌شود.";
        public const string NoticeFromFileText = "فهرست مدارک پروژه (MDL) را از فایل بسازید یا برای مناقصه بدون MDL از مدیرعامل مجوز بگیرید.";
        public const string StaleText = "فهرست مدارک پروژه (MDL) یا مجوز پیش از آخرین تصمیم پرونده تایید شده است؛ آن را باطل و دوباره تهیه کنید.";
        public const string CsvHeader = "docType,title,discipline,required,tender,units,clientSubmittal,clause,note";

        /// <summary>The lines that exist (never deleted).</summary>
        public static IEnumerable<EnProjectMdlLine> Active(EnProjectMdl m) => m.Lines.Where(l => !l.Deleted);

        /// <summary>The lines that go to the client: not removed, in order.</summary>
        public static List<EnProjectMdlLine> Kept(EnProjectMdl m) => Active(m).Where(l => !l.Removed).OrderBy(l => l.Seq).ThenBy(l => l.Id).ToList();

        /// <summary>SHA-256 over the ordered not-removed lines (type, discipline, title, required, tender stage, client submittal, clause, units) + case id + revision.</summary>
        public static string Hash(int caseId, int revision, IEnumerable<EnProjectMdlLine> keptLines) =>
            EnHash.Of("PMDL#" + EnText.Num(caseId) + "#" + EnText.Num(revision) + "#" + string.Join(";", keptLines.Select(l =>
                string.Join("|", EnHash.Norm(l.DocumentType), EnHash.Norm(l.Discipline), EnHash.Norm(l.Title), l.Required ? "1" : "0", l.TenderStage ? "1" : "0", l.ClientSubmittal ? "1" : "0", EnHash.Norm(l.StandardClause), l.Units ?? string.Empty))));

        public static string HashOf(EnProjectMdl m) => Hash(m.CaseId, m.Revision, Kept(m));

        private static string Cell(string s)
        {
            s = EnReferenceCsv.Neutralise(s ?? string.Empty);
            return s.IndexOfAny(new[] { ',', ';', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        /// <summary>The CSV text in exactly the format the reference upload reads (same header, optional columns included); removed lines are absent.</summary>
        public static string Csv(IEnumerable<EnProjectMdlLine> keptLines)
        {
            StringBuilder b = new StringBuilder(CsvHeader).Append("\r\n");
            foreach (EnProjectMdlLine l in keptLines)
                b.Append(string.Join(",", Cell(l.DocumentType), Cell(l.Title), Cell(l.Discipline), l.Required ? "1" : "0", l.TenderStage ? "1" : "0", Cell(l.Units == null ? string.Empty : l.Units.Replace(',', ';')), l.ClientSubmittal ? "1" : "0", Cell(l.StandardClause), Cell(l.Note))).Append("\r\n");
            return b.ToString();
        }

        /// <summary>The CSV bytes (UTF-8 with a byte-order mark, so a spreadsheet opens the Persian text right; the reader strips it).</summary>
        public static byte[] CsvBytes(IEnumerable<EnProjectMdlLine> keptLines) => new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Csv(keptLines))).ToArray();

        public static string IssuedFileName(string caseNumber, int revision) => "MDL-" + caseNumber + "-R" + EnText.Num(revision) + ".csv";

        /// <summary>The live (draft, checked or approved) project MDL of a case with its lines; null when none.</summary>
        public static Task<EnProjectMdl> LiveAsync(IUnitOfWork uow, int caseId, bool track, CancellationToken ct)
        {
            IQueryable<EnProjectMdl> q = uow.Set<EnProjectMdl>();
            if (!track) q = q.AsNoTracking();
            return q.Include(m => m.Lines).Where(m => !m.Deleted && m.CaseId == caseId && m.Status != EnProjectMdlStatus.Void).OrderByDescending(m => m.Revision).FirstOrDefaultAsync(ct);
        }

        /// <summary>The permit that matters for a case: the live one (requested or approved), else the latest of any status; null when none.</summary>
        public static async Task<EnMdlPermit> PermitAsync(IUnitOfWork uow, int caseId, bool track, CancellationToken ct)
        {
            IQueryable<EnMdlPermit> q = uow.Set<EnMdlPermit>();
            if (!track) q = q.AsNoTracking();
            List<EnMdlPermit> all = await q.Where(p => !p.Deleted && p.CaseId == caseId).OrderByDescending(p => p.Id).ToListAsync(ct);
            return all.FirstOrDefault(p => p.Status == EnMdlPermitStatus.Approved || p.Status == EnMdlPermitStatus.Requested) ?? all.FirstOrDefault();
        }

        /// <summary>The scope (employer, every kind of tender): a case that has, or had, a tender package (turbine, spare parts, service). A direct-contract case never has one and needs nothing.</summary>
        public static bool Applies(EnCase c) => c.Status == EnCaseStatus.TenderPreparation || (EnCaseRules.TenderPassed(c.Status) && (c.InquiryId.HasValue || c.TenderPackageId.HasValue));

        public static string AppliesText(EnCase c) =>
            Applies(c) ? null
            : EnCaseRules.TenderPassed(c.Status) ? "این پرونده بستهٔ فنی مناقصه ندارد (قرارداد مستقیم)؛ فهرست مدارک پروژه برای آن لازم نیست."
            : "فهرست مدارک پروژه از مرحلهٔ تهیهٔ بستهٔ فنی مناقصه لازم است.";

        /// <summary>The approved standard list of the case's standard (the start of an agent draft); null when the case has no standard code or its standard has no approved list.</summary>
        public static Task<EnReferenceList> StandardAsync(IUnitOfWork uow, EnCase c, bool track, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(c.ReferenceCode) ? Task.FromResult<EnReferenceList>(null) : EnReferenceRules.CurrentAsync(uow, track, ct, EnReferenceRules.NormCode(c.ReferenceCode));

        /// <summary>The instant of the latest decision of the case (the CEO's approval of the current round; for a case without a CEO gate, the engineering manager's confirmation); null when the case holds none right now.</summary>
        public static DateTimeOffset? DecisionAt(EnCase c)
        {
            DateTimeOffset? a = c.CeoApprovedAt, b = c.EngConfirmedAt;
            return a.HasValue && b.HasValue ? (a.Value > b.Value ? a : b) : a ?? b;
        }

        /// <summary>An approval that does not belong to the case's current decision: the list was made from another standard than the case's current one, or it was approved before the latest decision (a changed standard or classification sends the case through the approvals again).</summary>
        public static bool Outdated(EnProjectMdl m, EnCase c)
        {
            if (m.BasedOnCode != null && EnReferenceRules.NormCode(m.BasedOnCode) != EnReferenceRules.NormCode(c.ReferenceCode)) return true;
            DateTimeOffset? d = DecisionAt(c);
            return d.HasValue && !(m.ApprovedAt.HasValue && m.ApprovedAt.Value > d.Value);
        }

        public static bool PermitOutdated(EnMdlPermit p, EnCase c)
        {
            DateTimeOffset? d = DecisionAt(c);
            return d.HasValue && !(p.DecidedAt.HasValue && p.DecidedAt.Value > d.Value);
        }

        /// <summary>The outcome of the release lock: <see cref="Refusal"/> is null when the tender package may go; with an approved list <see cref="Mdl"/> is the list whose file joins the package, with a permit <see cref="Permit"/> is the CEO's permit. <see cref="Notice"/> is what the case page tells the engineer while it is locked.</summary>
        public sealed class LockResult
        {
            public string Refusal;
            public string Notice;
            public EnProjectMdl Mdl;
            public EnMdlPermit Permit;
        }

        /// <summary>
        /// The lock of the tender package release (EVERY kind of case while it is still in the tender preparation; later statuses are not locked): open when the case has an APPROVED project MDL whose approved hash
        /// still equals the hash of its lines, or an APPROVED "without MDL" permit.
        /// </summary>
        public static async Task<LockResult> ReleaseLockAsync(IUnitOfWork uow, EnCase c, CancellationToken ct)
        {
            if (c.Status != EnCaseStatus.TenderPreparation) return new LockResult();
            EnProjectMdl m = await LiveAsync(uow, c.Id, false, ct);
            bool staleSeen = false;
            if (m != null && m.Status == EnProjectMdlStatus.Approved && !string.IsNullOrEmpty(m.ApprovedHash) && m.ApprovedHash == HashOf(m))
            {
                if (!Outdated(m, c)) return new LockResult { Mdl = m };
                staleSeen = true;
            }
            EnMdlPermit p = await uow.Set<EnMdlPermit>().AsNoTracking().Where(x => !x.Deleted && x.CaseId == c.Id && x.Status == EnMdlPermitStatus.Approved).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (p != null)
            {
                if (!PermitOutdated(p, c)) return new LockResult { Permit = p };
                staleSeen = true;
            }
            if (staleSeen) return new LockResult { Refusal = StaleText, Notice = StaleText };
            // nothing to start from (no live list, no approved standard list): a person creates it from a file
            string notice = m == null && await StandardAsync(uow, c, false, ct) == null ? NoticeFromFileText : NoticeText;
            return new LockResult { Refusal = LockText, Notice = notice };
        }
    }

    public interface IEnProjectMdlService
    {
        /// <summary>The project MDL of a case: the live revision with its lines, the history, the permit, and the flags with their reasons.</summary>
        Task<ResponseDto<EnProjectMdlCaseDto>> GetByCaseAsync(int caseId, CancellationToken ct);

        /// <summary>The CSV of the current not-removed lines of a revision (the format the reference upload reads).</summary>
        Task<ResponseDto<EnProjectMdlCsvDto>> GetCsvAsync(int id, CancellationToken ct);

        /// <summary>draft, check, return, approve, void, record-client-approval, request-permit, decide-permit, withdraw-permit (see <see cref="EnProjectMdlActionDto"/>); the answer is the case view after the action.</summary>
        Task<ResponseDto<EnProjectMdlCaseDto>> ActionAsync(EnProjectMdlActionDto dto, CancellationToken ct);

        /// <summary>A person creates the project MDL of a case that has no approved standard list to start from: every line of the file is a project line.</summary>
        Task<ResponseDto<EnProjectMdlCaseDto>> DraftFromFileAsync(int caseId, string note, byte[] file, string fileName, CancellationToken ct);

        /// <summary>Replaces the lines of a Draft or Checked revision with the lines of a CSV file (a person only); the file is kept as evidence.</summary>
        Task<ResponseDto<EnProjectMdlCaseDto>> UploadLinesAsync(int id, string rowVersion, string note, byte[] file, string fileName, CancellationToken ct);
    }

    /// <summary>
    /// The MDL of a PROJECT in the tender (employer rule 20). The engineering agent drafts it from the approved standard list of the case's standard, an engineer replaces the lines from a file (remove / add for the project),
    /// an engineer checks, the engineering manager approves (bound to the hash of the lines; the CSV of the approved list is generated and joins the tender package at its release), commercial records the client's approval.
    /// A special project may go without it by the permit of the CEO. Releasing the tender package of a turbine case is locked until one of the two exists (see <see cref="EnProjectMdlRules.ReleaseLockAsync"/>).
    /// </summary>
    public class EnProjectMdlService : IEnProjectMdlService
    {
        private const string Readers = EnRoles.MdlReaders;

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IDocumentService _documents;

        public EnProjectMdlService(IUnitOfWork uow, IInventoryUserContext user, IDocumentService documents = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _documents = documents;
        }

        private int Me => _user.UserId;

        private sealed class Who
        {
            public int Me;
            public bool Writer, Manager, Ceo, Commercial, Seat;
        }

        private async Task<Who> WhoAsync(CancellationToken ct) => new Who
        {
            Me = Me, Writer = _user.IsInRole(EnRoles.Writers), Manager = _user.IsInRole(EnRoles.Manager), Ceo = _user.IsInRole(EnRoles.Ceo), Commercial = _user.IsInRole(EnRoles.Commercial), Seat = await EnActors.IsSeatAsync(_uow, Me, ct),
        };

        /// <summary>Everything the rules need about one case.</summary>
        private sealed class Ctx
        {
            public EnCase Case;
            public EnProjectMdl Live;
            public EnMdlPermit Permit;
            public EnReferenceList Standard;
        }

        private async Task<Ctx> LoadAsync(EnCase c, bool track, CancellationToken ct) => new Ctx
        {
            Case = c, Live = await EnProjectMdlRules.LiveAsync(_uow, c.Id, track, ct), Permit = await EnProjectMdlRules.PermitAsync(_uow, c.Id, track, ct),
            Standard = string.IsNullOrWhiteSpace(c.ReferenceCode) ? null : await EnReferenceRules.CurrentAsync(_uow, false, ct, EnReferenceRules.NormCode(c.ReferenceCode)),
        };

        // ---------------------------------------------------------------- the rules (one predicate for the button and the action)

        private static bool Applies(EnCase c) => EnProjectMdlRules.Applies(c);

        private static string AppliesText(EnCase c) => EnProjectMdlRules.AppliesText(c);

        private const string TenderPreparationOnly = "فهرست مدارک پروژه فقط در مرحلهٔ تهیهٔ بستهٔ فنی مناقصه (پیش از ابلاغ بسته) تغییر می‌کند.";

        private static string DraftReason(Ctx x, Who w) =>
            !w.Writer ? EnText.NotAllowed
            : x.Case.Status != EnCaseStatus.TenderPreparation ? "فهرست مدارک پروژه فقط در مرحلهٔ تهیهٔ بستهٔ فنی مناقصه ساخته می‌شود."
            : string.IsNullOrWhiteSpace(x.Case.ReferenceCode) ? "استاندارد پروژه ثبت نشده است؛ فهرست پروژه از فهرست MDL تاییدشدهٔ استاندارد ساخته می‌شود. بدون استاندارد، کارشناس فهرست را از فایل بسازد."
            : x.Standard == null ? "فهرست MDL تاییدشده‌ای برای استاندارد «" + x.Case.ReferenceCode + "» نیست؛ ایجنت پیش‌نویس نمی‌سازد و کارشناس فهرست را از فایل بسازد."
            : x.Live != null ? "برای این پرونده فهرست مدارک پروژهٔ در جریان (" + x.Live.Number + "، " + EnText.Num(x.Live.Revision) + ") وجود دارد؛ همان را ادامه دهید یا ابطال کنید."
            : FreshPermit(x) ? "مدیرعامل مجوز «مناقصه بدون MDL» داده است (" + x.Permit.Number + ")؛ فهرست پروژه ساخته نمی‌شود." : null;

        /// <summary>The CEO's permit that still belongs to the case's current decision.</summary>
        private static bool FreshPermit(Ctx x) => x.Permit != null && x.Permit.Status == EnMdlPermitStatus.Approved && !EnProjectMdlRules.PermitOutdated(x.Permit, x.Case);

        private static bool StalePermit(Ctx x) => x.Permit != null && x.Permit.Status == EnMdlPermitStatus.Approved && EnProjectMdlRules.PermitOutdated(x.Permit, x.Case);

        /// <summary>A person creates the project MDL from a file only when there is no approved standard list to draft from (otherwise: draft from the standard, then change the lines by a file).</summary>
        private static string DraftFromFileReason(Ctx x, Who w) =>
            !w.Writer ? EnText.NotAllowed : w.Seat ? EnText.AiNever
            : x.Case.Status != EnCaseStatus.TenderPreparation ? "فهرست مدارک پروژه فقط در مرحلهٔ تهیهٔ بستهٔ فنی مناقصه ساخته می‌شود."
            : x.Live != null ? "برای این پرونده فهرست مدارک پروژهٔ در جریان (" + x.Live.Number + "، " + EnText.Num(x.Live.Revision) + ") وجود دارد؛ همان را با فایل تغییر دهید یا ابطال کنید."
            : FreshPermit(x) ? "مدیرعامل مجوز «مناقصه بدون MDL» داده است (" + x.Permit.Number + ")؛ فهرست پروژه ساخته نمی‌شود."
            : x.Standard != null ? "برای استاندارد «" + x.Case.ReferenceCode + "» فهرست MDL تاییدشده هست؛ پیش‌نویس را از فهرست استاندارد بسازید (ایجنت یا کارشناس) و سپس ردیف‌ها را با فایل تغییر دهید." : null;

        private static string UploadReason(Ctx x, Who w) =>
            !w.Writer ? EnText.NotAllowed : w.Seat ? EnText.AiNever
            : x.Live == null ? "فهرست مدارک پروژه‌ای برای تغییر نیست؛ ابتدا پیش‌نویس ساخته شود (ایجنت از فهرست استاندارد، یا کارشناس از فایل)."
            : x.Live.Status != EnProjectMdlStatus.Draft && x.Live.Status != EnProjectMdlStatus.Checked ? "فقط فهرست پیش‌نویس یا بررسی‌شده با فایل جایگزین می‌شود؛ فهرست تاییدشده را باید مدیر مهندسی ابطال کند."
            : x.Case.Status != EnCaseStatus.TenderPreparation ? TenderPreparationOnly : null;

        private static string CheckReason(Ctx x, Who w) =>
            !w.Writer ? EnText.NotAllowed : w.Seat ? EnText.AiNever
            : x.Live == null ? "فهرست مدارک پروژه‌ای برای بررسی نیست."
            : x.Live.Status != EnProjectMdlStatus.Draft ? "فقط فهرست پیش‌نویس بررسی می‌شود."
            : x.Case.Status != EnCaseStatus.TenderPreparation ? TenderPreparationOnly
            : EnProjectMdlRules.Kept(x.Live).Count == 0 ? "فهرست بدون ردیف را نمی‌توان بررسی کرد." : null;

        private static string ReturnReasonOf(Ctx x, Who w) =>
            !w.Manager ? EnText.NotAllowed : w.Seat ? EnText.AiNever
            : x.Live == null || x.Live.Status != EnProjectMdlStatus.Checked ? "فقط فهرست بررسی‌شده برگشت می‌خورد."
            : x.Case.Status != EnCaseStatus.TenderPreparation ? TenderPreparationOnly : null;

        private static string ApproveReason(Ctx x, Who w) =>
            !w.Manager ? EnText.NotAllowed : w.Seat ? EnText.AiNever
            : x.Live == null || x.Live.Status != EnProjectMdlStatus.Checked ? "فقط فهرست بررسی‌شدهٔ کارشناس تایید می‌شود."
            : x.Case.Status != EnCaseStatus.TenderPreparation ? TenderPreparationOnly
            : x.Live.CheckedByUserId == w.Me ? EnText.SameUser + " کسی که فهرست را بررسی کرده آن را تایید نمی‌کند."
            : IsMaker(x.Live, w.Me) ? EnText.SameUser + " کسی که ردیف‌های فهرست را از فایل ساخته یا بارگذاری کرده آن را تایید نمی‌کند."
            : x.Live.CheckedAt == null || x.Live.Lines.Any(l => l.ModifyDate > x.Live.CheckedAt.Value) ? "ردیف‌های فهرست پس از بررسی کارشناس تغییر کرده است؛ فهرست باید دوباره بررسی شود."
            : EnProjectMdlRules.Kept(x.Live).Count == 0 ? "فهرست بدون ردیف را نمی‌توان تایید کرد." : null;

        /// <summary>The person who made the lines (created the list from a file or uploaded a file over it): every upload stamps all the lines with its author. An agent seat's draft carries the seat, who never approves.</summary>
        private static bool IsMaker(EnProjectMdl m, int me) => me > 0 && m.Lines.Any(l => l.CreatorUserId == me || l.ModifierUserId == me);

        private static string VoidReasonOf(Ctx x, Who w) =>
            !w.Manager ? EnText.NotAllowed : w.Seat ? EnText.AiNever
            : x.Live == null ? "فهرست مدارک پروژهٔ در جریانی برای ابطال نیست."
            : EnCaseRules.TenderPassed(x.Case.Status) ? "بستهٔ فنی مناقصه منتشر شده است؛ فهرست مدارک پروژه دیگر ابطال نمی‌شود (ابتدا ابلاغ بسته باید باطل شود)." : null;

        private static string RecordClientReason(Ctx x, Who w) =>
            !w.Commercial ? EnText.NotAllowed : w.Seat ? EnText.AiNever
            : x.Live == null || x.Live.Status != EnProjectMdlStatus.Approved ? "فقط برای فهرست تاییدشدهٔ مدیر مهندسی تایید کارفرما ثبت می‌شود."
            : !EnCaseRules.TenderPassed(x.Case.Status) || x.Case.TenderReleasedAt == null ? "بستهٔ فنی مناقصه هنوز منتشر نشده است؛ پس از انتشار، فهرست به کارفرما می‌رسد و تایید او ثبت می‌شود." : null;

        private static string RequestPermitReason(Ctx x, Who w) =>
            !w.Manager ? EnText.NotAllowed : w.Seat ? EnText.AiNever
            : x.Case.Status != EnCaseStatus.TenderPreparation ? "مجوز «مناقصه بدون MDL» فقط در مرحلهٔ تهیهٔ بستهٔ فنی مناقصه درخواست می‌شود."
            : x.Live != null && x.Live.Status == EnProjectMdlStatus.Approved && !EnProjectMdlRules.Outdated(x.Live, x.Case) ? "فهرست مدارک پروژه تایید شده است؛ نیازی به مجوز نیست."
            : StalePermit(x) ? "مجوز «مناقصه بدون MDL» (" + x.Permit.Number + ") پیش از آخرین تصمیم پرونده تایید شده است؛ ابتدا آن را پس بگیرید و دوباره درخواست کنید."
            : x.Permit != null && (x.Permit.Status == EnMdlPermitStatus.Requested || x.Permit.Status == EnMdlPermitStatus.Approved) ? "برای این پرونده مجوز «مناقصه بدون MDL» (" + x.Permit.Number + ") در جریان یا تاییدشده است." : null;

        private static string DecidePermitReason(Ctx x, Who w) =>
            !w.Ceo ? EnText.NotAllowed : w.Seat ? EnText.AiNever
            : x.Permit == null || x.Permit.Status != EnMdlPermitStatus.Requested ? "درخواست مجوزِ منتظر تصمیمی نیست."
            : x.Case.Status != EnCaseStatus.TenderPreparation ? "مجوز «مناقصه بدون MDL» فقط در مرحلهٔ تهیهٔ بستهٔ فنی مناقصه تصمیم می‌گیرد."
            : x.Permit.RequestedByUserId == w.Me ? EnText.SameUser + " درخواست‌کننده تصمیم نمی‌گیرد." : null;

        private static string WithdrawPermitReason(Ctx x, Who w) =>
            w.Seat ? EnText.AiNever
            : x.Permit == null || (x.Permit.Status != EnMdlPermitStatus.Requested && !StalePermit(x)) ? "درخواست مجوزِ منتظر تصمیمی (یا مجوز تاییدشده پیش از آخرین تصمیم پرونده) برای پس‌گرفتن نیست."
            : StalePermit(x) && !w.Manager ? EnText.NotAllowed
            : !w.Manager && x.Permit.RequestedByUserId != w.Me ? EnText.NotAllowed : null;

        // ---------------------------------------------------------------- read

        private static string Version(EnProjectMdl m) => m.ModifyDate.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>A person's action must carry the token the page was read with (an empty token is refused); the agent's calls (a seat) may omit it.</summary>
        private static string VersionRefusal(EnProjectMdl m, string version, Who w) =>
            string.IsNullOrWhiteSpace(version) ? (w.Seat ? null : "شناسهٔ نسخهٔ صفحه ارسال نشد؛ صفحه را دوباره باز کنید و دوباره تلاش کنید.")
            : version.Trim() == Version(m) ? null : EnText.Concurrency;

        private static string StatusText(EnProjectMdlStatus s) => s switch { EnProjectMdlStatus.Draft => "پیش‌نویس", EnProjectMdlStatus.Checked => "بررسی‌شدهٔ کارشناس", EnProjectMdlStatus.Approved => "تاییدشدهٔ مدیر مهندسی", _ => "ابطال‌شده" };

        private static string PermitText(EnMdlPermitStatus s) => s switch { EnMdlPermitStatus.Requested => "درخواست‌شده", EnMdlPermitStatus.Approved => "تاییدشده", EnMdlPermitStatus.Rejected => "ردشده", _ => "پس‌گرفته‌شده" };

        private async Task<EnProjectMdlDto> MdlDtoAsync(EnProjectMdl m, EnCase c, Dictionary<int, string> names, CancellationToken ct)
        {
            List<EnProjectMdlLine> all = EnProjectMdlRules.Active(m).OrderBy(l => l.Seq).ThenBy(l => l.Id).ToList();
            string issuedName = null;
            if (m.IssuedDocumentId.HasValue) issuedName = EnProjectMdlRules.IssuedFileName(await _uow.Set<EnCase>().AsNoTracking().Where(c => c.Id == m.CaseId).Select(c => c.Number).FirstOrDefaultAsync(ct), m.Revision);
            return new EnProjectMdlDto
            {
                Id = m.Id, Number = m.Number, CaseId = m.CaseId, Revision = m.Revision, Status = (int)m.Status, StatusText = StatusText(m.Status), BasedOnListId = m.BasedOnListId, BasedOnCode = m.BasedOnCode, BasedOnRevision = m.BasedOnRevision,
                SourceDocumentId = m.SourceDocumentId, SourceFileName = m.SourceFileName, ChangeNote = m.ChangeNote,
                CheckedByUserId = m.CheckedByUserId, CheckedByName = names.Name(m.CheckedByUserId), CheckedAtText = EnText.Stamp(m.CheckedAt), ApprovedByUserId = m.ApprovedByUserId, ApprovedByName = names.Name(m.ApprovedByUserId), ApprovedAtText = EnText.Stamp(m.ApprovedAt),
                ApprovalStale = m.Status == EnProjectMdlStatus.Approved && (m.ApprovedHash != EnProjectMdlRules.HashOf(m) || EnProjectMdlRules.Outdated(m, c)),
                IssuedDocumentId = m.IssuedDocumentId, IssuedFileName = issuedName,
                ClientApprovedAt = m.ClientApprovedAt, ClientApprovedAtText = EnText.Stamp(m.ClientApprovedAt), ClientApprovalRef = m.ClientApprovalRef, ClientApprovalDocumentId = m.ClientApprovalDocumentId, ClientApprovalRecordedByName = names.Name(m.ClientApprovalRecordedByUserId),
                VoidReason = m.VoidReason,
                StandardKeptCount = all.Count(l => l.Origin == EnProjectMdlLineOrigin.Standard && !l.Removed), RemovedCount = all.Count(l => l.Removed), AddedCount = all.Count(l => l.Origin == EnProjectMdlLineOrigin.Project && !l.Removed),
                LineCount = all.Count(l => !l.Removed),
                Lines = all.Select(l => new EnProjectMdlLineDto
                {
                    Id = l.Id, Seq = l.Seq, DocumentType = l.DocumentType, Discipline = l.Discipline, Title = l.Title, Required = l.Required, TenderStage = l.TenderStage, ClientSubmittal = l.ClientSubmittal, StandardClause = l.StandardClause, Units = l.Units, Note = l.Note,
                    Origin = (int)l.Origin, OriginText = l.Origin == EnProjectMdlLineOrigin.Project ? "افزوده برای پروژه" : "از فهرست استاندارد", Removed = l.Removed,
                }).ToList(),
                RowVersion = Version(m),
            };
        }

        private async Task<EnProjectMdlCaseDto> ViewAsync(EnCase c, CancellationToken ct)
        {
            Who w = await WhoAsync(ct);
            Ctx x = await LoadAsync(c, false, ct);
            string draft = DraftReason(x, w), draftFile = DraftFromFileReason(x, w), upload = UploadReason(x, w), check = CheckReason(x, w), ret = ReturnReasonOf(x, w), approve = ApproveReason(x, w), voidR = VoidReasonOf(x, w), client = RecordClientReason(x, w), reqPermit = RequestPermitReason(x, w), decide = DecidePermitReason(x, w), withdraw = WithdrawPermitReason(x, w);
            List<EnProjectMdl> others = await _uow.Set<EnProjectMdl>().AsNoTracking().Include(m => m.Lines).Where(m => !m.Deleted && m.CaseId == c.Id).OrderByDescending(m => m.Revision).ToListAsync(ct);
            Dictionary<int, string> names = await EnUsers.NamesAsync(_uow, others.SelectMany(m => new int?[] { m.CheckedByUserId, m.ApprovedByUserId, m.ClientApprovalRecordedByUserId }).Concat(new int?[] { x.Permit?.RequestedByUserId, x.Permit?.DecidedByUserId }), ct);
            EnProjectMdlCaseDto dto = new EnProjectMdlCaseDto
            {
                CaseId = c.Id, CaseNumber = c.Number, Applies = Applies(c), NotApplicableText = AppliesText(c),
                Mdl = x.Live == null ? null : await MdlDtoAsync(others.First(m => m.Id == x.Live.Id), c, names, ct),
                History = others.Select(m => new EnProjectMdlHistoryDto { Id = m.Id, Number = m.Number, Revision = m.Revision, Status = (int)m.Status, StatusText = StatusText(m.Status), BasedOnCode = m.BasedOnCode, BasedOnRevision = m.BasedOnRevision, ApprovedAtText = EnText.Stamp(m.ApprovedAt), VoidReason = m.VoidReason, LineCount = EnProjectMdlRules.Kept(m).Count }).ToList(),
                Permit = x.Permit == null ? null : new EnMdlPermitDto
                {
                    Id = x.Permit.Id, Number = x.Permit.Number, CaseId = x.Permit.CaseId, Reason = x.Permit.Reason, Status = (int)x.Permit.Status, StatusText = PermitText(x.Permit.Status), RequestedByUserId = x.Permit.RequestedByUserId, RequestedByName = names.Name(x.Permit.RequestedByUserId),
                    RequestedAtText = EnText.Stamp(x.Permit.RequestedAt), DecidedByName = names.Name(x.Permit.DecidedByUserId), DecidedAtText = EnText.Stamp(x.Permit.DecidedAt), DecisionNote = x.Permit.DecisionNote, ApprovalStale = x.Permit.Status == EnMdlPermitStatus.Approved && EnProjectMdlRules.PermitOutdated(x.Permit, c),
                },
                ReleaseLockText = (await EnProjectMdlRules.ReleaseLockAsync(_uow, c, ct)).Refusal,
                CanDraft = draft == null, DraftBlockedReason = draft, CanDraftFromFile = draftFile == null, DraftFromFileBlockedReason = draftFile, HasStandardList = x.Standard != null, CanUpload = upload == null, UploadBlockedReason = upload, CanCheck = check == null, CheckBlockedReason = check, CanReturn = ret == null, ReturnBlockedReason = ret,
                CanApprove = approve == null, ApproveBlockedReason = approve, CanVoid = voidR == null, VoidBlockedReason = voidR, CanRecordClientApproval = client == null, RecordClientApprovalBlockedReason = client,
                CanRequestPermit = reqPermit == null, RequestPermitBlockedReason = reqPermit, CanDecidePermit = decide == null, DecidePermitBlockedReason = decide, CanWithdrawPermit = withdraw == null, WithdrawPermitBlockedReason = withdraw,
            };
            return dto;
        }

        public async Task<ResponseDto<EnProjectMdlCaseDto>> GetByCaseAsync(int caseId, CancellationToken ct)
        {
            if (!_user.IsInRole(Readers)) return EnText.Bad<EnProjectMdlCaseDto>(EnText.NotAllowed);
            EnCase c = await _uow.Set<EnCase>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == caseId && !x.Deleted, ct);
            if (c == null) return EnText.NotFound<EnProjectMdlCaseDto>(EnText.NotFoundCase);
            return ResponseDtoHelper.Ok(await ViewAsync(c, ct));
        }

        public async Task<ResponseDto<EnProjectMdlCsvDto>> GetCsvAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(Readers)) return EnText.Bad<EnProjectMdlCsvDto>(EnText.NotAllowed);
            EnProjectMdl m = await _uow.Set<EnProjectMdl>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (m == null) return EnText.NotFound<EnProjectMdlCsvDto>("فهرست مدارک پروژه پیدا نشد.");
            string number = await _uow.Set<EnCase>().AsNoTracking().Where(c => c.Id == m.CaseId).Select(c => c.Number).FirstOrDefaultAsync(ct);
            return ResponseDtoHelper.Ok(new EnProjectMdlCsvDto { FileName = "MDL-" + number + "-R" + EnText.Num(m.Revision) + (m.Status == EnProjectMdlStatus.Approved ? ".csv" : "-draft.csv"), Content = EnProjectMdlRules.CsvBytes(EnProjectMdlRules.Kept(m)) });
        }

        // ---------------------------------------------------------------- files

        /// <summary>Keeps bytes in the document store (the Documents service saves the unit of work: call it only after every refusal and before the record is changed, so a refusal leaves no orphan and nothing is saved half-done).</summary>
        private async Task<(Guid? Id, string Name)> StoreAsync(byte[] file, string fileName, string description, string fallbackName)
        {
            if (_documents == null || file == null || file.Length == 0) return (null, null);
            string name = EnText.Cut(EnText.Clean(System.IO.Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'))) ?? fallbackName, 260);
            IFormFile form = new FormFile(new System.IO.MemoryStream(file), 0, file.Length, "file", name);
            Guid id = await _documents.UploadDocumentAsync(new ViewModels.NasbNiro.DocumentDtos.UploadFileDto { File = form, Name = name, Description = EnText.Cut(description, 300), Code = "engineering" });
            return (id, name);
        }

        // ---------------------------------------------------------------- actions

        private static readonly string[] Actions = { "draft", "check", "return", "approve", "void", "record-client-approval", "request-permit", "decide-permit", "withdraw-permit" };

        private async Task<ResponseDto<EnProjectMdlCaseDto>> SaveAsync(EnCase c, CancellationToken ct)
        {
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnProjectMdlCaseDto>(EnText.Concurrency); }
            catch (DbUpdateException) { return EnText.Bad<EnProjectMdlCaseDto>(EnText.Concurrency); }
            return ResponseDtoHelper.Ok(await ViewAsync(c, ct));
        }

        public async Task<ResponseDto<EnProjectMdlCaseDto>> ActionAsync(EnProjectMdlActionDto dto, CancellationToken ct)
        {
            if (dto == null) return EnText.Bad<EnProjectMdlCaseDto>(EnText.InvalidData);
            string action = (dto.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (!Actions.Contains(action)) return EnText.Bad<EnProjectMdlCaseDto>("عمل نامعتبر است (" + string.Join("، ", Actions) + ").");
            // find the case: by case id (draft, request-permit), by revision id (the others) or by permit id
            int? caseId = dto.CaseId;
            if (!caseId.HasValue && dto.Id.HasValue) caseId = await _uow.Set<EnProjectMdl>().AsNoTracking().Where(m => m.Id == dto.Id.Value && !m.Deleted).Select(m => (int?)m.CaseId).FirstOrDefaultAsync(ct);
            if (!caseId.HasValue && dto.PermitId.HasValue) caseId = await _uow.Set<EnMdlPermit>().AsNoTracking().Where(p => p.Id == dto.PermitId.Value && !p.Deleted).Select(p => (int?)p.CaseId).FirstOrDefaultAsync(ct);
            if (!caseId.HasValue) return EnText.NotFound<EnProjectMdlCaseDto>(action == "decide-permit" || action == "withdraw-permit" ? "درخواست مجوز پیدا نشد." : action == "draft" || action == "request-permit" ? EnText.NotFoundCase : "فهرست مدارک پروژه پیدا نشد.");
            EnCase c = await _uow.Set<EnCase>().FirstOrDefaultAsync(k => k.Id == caseId.Value && !k.Deleted, ct);
            if (c == null) return EnText.NotFound<EnProjectMdlCaseDto>(EnText.NotFoundCase);
            Who w = await WhoAsync(ct);
            Ctx x = await LoadAsync(c, true, ct);
            if (dto.Id.HasValue && action != "draft" && action != "request-permit" && action != "decide-permit" && action != "withdraw-permit" && (x.Live == null || x.Live.Id != dto.Id.Value)) return EnText.Bad<EnProjectMdlCaseDto>("این ویرایش از فهرست مدارک پروژه دیگر جاری نیست؛ صفحه را دوباره باز کنید.");
            if ((action == "decide-permit" || action == "withdraw-permit") && x.Permit != null && dto.PermitId.HasValue && x.Permit.Id != dto.PermitId.Value) return EnText.Bad<EnProjectMdlCaseDto>("این درخواست مجوز دیگر جاری نیست؛ صفحه را دوباره باز کنید.");
            string tokenWhy = null;
            if (x.Live != null && action != "draft" && action != "request-permit" && action != "decide-permit" && action != "withdraw-permit")
            {
                string stale = VersionRefusal(x.Live, dto.RowVersion, w);
                // a token that is not the page's is refused at once; a MISSING token of a person is refused after the rules of the action (so a refusal for a role or a status keeps its own reason)
                if (stale != null && !string.IsNullOrWhiteSpace(dto.RowVersion)) return EnText.Bad<EnProjectMdlCaseDto>(stale);
                if (stale != null && action != "record-client-approval") tokenWhy = stale;
            }
            DateTimeOffset now = DateTimeOffset.Now;
            string why;
            switch (action)
            {
                case "draft":
                {
                    why = DraftReason(x, w);
                    if (why != null) return EnText.Bad<EnProjectMdlCaseDto>(why);
                    int revision = (await _uow.Set<EnProjectMdl>().Where(m => m.CaseId == c.Id).Select(m => (int?)m.Revision).MaxAsync(ct) ?? 0) + 1;
                    EnProjectMdl m = new EnProjectMdl
                    {
                        CaseId = c.Id, Revision = revision, Status = EnProjectMdlStatus.Draft, BasedOnListId = x.Standard.Id, BasedOnCode = x.Standard.Code, BasedOnRevision = x.Standard.Revision,
                        CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
                    };
                    m.Number = await EnNumbers.NextAsync(_uow, EnNumbers.ProjectMdl, "PMDL", now.DateTime, ct);
                    int seq = 0;
                    foreach (EnReferenceLine l in x.Standard.Lines.OrderBy(l => l.Seq).ThenBy(l => l.Id))
                        m.Lines.Add(new EnProjectMdlLine { Seq = ++seq, DocumentType = l.DocumentType, Discipline = l.Discipline, Title = l.Title, Required = l.Required, TenderStage = l.TenderStage, ClientSubmittal = l.ClientSubmittal, StandardClause = l.StandardClause, Units = l.Units, Note = l.Note, Origin = EnProjectMdlLineOrigin.Standard, Removed = false, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me });
                    _uow.Set<EnProjectMdl>().Add(m);
                    EnEvents.Add(_uow, c, "project-mdl-drafted", Me, "پیش‌نویس فهرست مدارک پروژه " + m.Number + " (ویرایش " + EnText.Num(revision) + ") از فهرست استاندارد " + m.BasedOnCode + " (بازنگری " + EnText.Num(m.BasedOnRevision) + ") با " + EnText.Num(seq) + " ردیف ساخته شد.");
                    break;
                }
                case "check":
                {
                    why = CheckReason(x, w) ?? tokenWhy;
                    if (why != null) return EnText.Bad<EnProjectMdlCaseDto>(why);
                    x.Live.Status = EnProjectMdlStatus.Checked; x.Live.CheckedByUserId = Me; x.Live.CheckedAt = now; Touch(x.Live, now);
                    EnEvents.Add(_uow, c, "project-mdl-checked", Me, "فهرست مدارک پروژه " + x.Live.Number + " توسط کارشناس بررسی شد.");
                    break;
                }
                case "return":
                {
                    string reason = EnText.Clean(dto.Reason);
                    why = ReturnReasonOf(x, w) ?? EnText.ReasonShort(reason, EnRules.ReasonMin, "دلیل برگشت") ?? EnText.Over(reason, 1000, "دلیل برگشت") ?? tokenWhy;
                    if (why != null) return EnText.Bad<EnProjectMdlCaseDto>(why);
                    x.Live.Status = EnProjectMdlStatus.Draft; x.Live.CheckedByUserId = null; x.Live.CheckedAt = null; Touch(x.Live, now);
                    EnEvents.Add(_uow, c, "project-mdl-returned", Me, "فهرست مدارک پروژه " + x.Live.Number + " برگشت خورد: " + reason);
                    break;
                }
                case "approve":
                {
                    why = ApproveReason(x, w) ?? tokenWhy;
                    if (why != null) return EnText.Bad<EnProjectMdlCaseDto>(why);
                    List<EnProjectMdlLine> kept = EnProjectMdlRules.Kept(x.Live);
                    string issuedName = EnProjectMdlRules.IssuedFileName(c.Number, x.Live.Revision);
                    (Guid? docId, _) = await StoreAsync(EnProjectMdlRules.CsvBytes(kept), issuedName, "فهرست مدارک پروژه (MDL) " + c.Number + " — ویرایش " + EnText.Num(x.Live.Revision), issuedName);   // saves the unit of work: nothing is changed yet
                    if (!docId.HasValue) return EnText.Bad<EnProjectMdlCaseDto>("ذخیرهٔ فایل فهرست تاییدشده ممکن نشد.");
                    x.Live.Status = EnProjectMdlStatus.Approved; x.Live.ApprovedByUserId = Me; x.Live.ApprovedAt = now; x.Live.ApprovedHash = EnProjectMdlRules.Hash(x.Live.CaseId, x.Live.Revision, kept); x.Live.IssuedDocumentId = docId; Touch(x.Live, now);
                    EnEvents.Add(_uow, c, "project-mdl-approved", Me, "فهرست مدارک پروژه " + x.Live.Number + " (ویرایش " + EnText.Num(x.Live.Revision) + "، " + EnText.Num(kept.Count) + " ردیف) توسط مدیر مهندسی تایید شد.");
                    break;
                }
                case "void":
                {
                    string reason = EnText.Clean(dto.Reason);
                    why = VoidReasonOf(x, w) ?? EnText.ReasonShort(reason, EnRules.ReasonMin, "دلیل ابطال") ?? EnText.Over(reason, 1000, "دلیل ابطال") ?? tokenWhy;
                    if (why != null) return EnText.Bad<EnProjectMdlCaseDto>(why);
                    x.Live.Status = EnProjectMdlStatus.Void; x.Live.VoidReason = reason; Touch(x.Live, now);
                    EnEvents.Add(_uow, c, "project-mdl-void", Me, "فهرست مدارک پروژه " + x.Live.Number + " باطل شد: " + reason);
                    break;
                }
                case "record-client-approval":
                {
                    string letter = EnText.Clean(dto.LetterRef), note = EnText.Clean(dto.Note);
                    why = RecordClientReason(x, w);
                    bool correction = x.Live != null && x.Live.ClientApprovedAt.HasValue;
                    if (why == null && letter == null) why = "شمارهٔ نامهٔ تایید کارفرما را بنویسید.";
                    if (why == null) why = EnText.Over(letter, 100, "شمارهٔ نامه");
                    if (why == null && !dto.DocumentId.HasValue) why = "نامهٔ تایید کارفرما را بارگذاری و فایل آن را مشخص کنید.";
                    if (why == null && correction) why = EnText.ReasonShort(note, EnRules.ReasonMin, "یادداشت اصلاح") ?? EnText.Over(note, 1000, "یادداشت اصلاح");
                    if (why == null) why = await EnFiles.CheckOwnAsync(_uow, dto.DocumentId.Value, Me, ct);
                    if (why != null) return EnText.Bad<EnProjectMdlCaseDto>(why);
                    string before = correction ? " (پیش از اصلاح: نامهٔ " + x.Live.ClientApprovalRef + ")" : string.Empty;
                    x.Live.ClientApprovedAt = now; x.Live.ClientApprovalRef = letter; x.Live.ClientApprovalDocumentId = dto.DocumentId; x.Live.ClientApprovalRecordedByUserId = Me; Touch(x.Live, now);
                    EnEvents.Add(_uow, c, correction ? "project-mdl-client-corrected" : "project-mdl-client-approved", Me, (correction ? "اصلاح ثبت تایید کارفرما: " + note + ". " : string.Empty) + "تایید کارفرما برای فهرست مدارک پروژه " + x.Live.Number + " با نامهٔ " + letter + " ثبت شد" + before + ".");
                    break;
                }
                case "request-permit":
                {
                    string reason = EnText.Clean(dto.Reason);
                    why = RequestPermitReason(x, w) ?? EnText.ReasonShort(reason, EnRules.WaiverReasonMin, "دلیل درخواست") ?? EnText.Over(reason, 2000, "دلیل درخواست");
                    if (why != null) return EnText.Bad<EnProjectMdlCaseDto>(why);
                    EnMdlPermit p = new EnMdlPermit { CaseId = c.Id, Reason = reason, Status = EnMdlPermitStatus.Requested, RequestedByUserId = Me, RequestedAt = now, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me };
                    p.Number = await EnNumbers.NextAsync(_uow, EnNumbers.MdlPermit, "EMP", now.DateTime, ct);
                    _uow.Set<EnMdlPermit>().Add(p);
                    EnEvents.Add(_uow, c, "mdl-permit-requested", Me, "درخواست مجوز «مناقصه بدون MDL» " + p.Number + " توسط مدیر مهندسی ثبت شد.");
                    break;
                }
                case "decide-permit":
                {
                    string note = EnText.Clean(dto.Note);
                    why = DecidePermitReason(x, w);
                    if (why == null && !dto.PermitId.HasValue) why = "شناسهٔ درخواست مجوزی که دیده‌اید ارسال نشد؛ صفحه را دوباره باز کنید و تصمیم بگیرید.";   // the decision is bound to the permit the CEO saw
                    if (why == null && !dto.Approve) why = EnText.ReasonShort(note, EnRules.ReasonMin, "دلیل رد");
                    if (why == null) why = EnText.Over(note, 1000, "توضیح");
                    if (why != null) return EnText.Bad<EnProjectMdlCaseDto>(why);
                    x.Permit.Status = dto.Approve ? EnMdlPermitStatus.Approved : EnMdlPermitStatus.Rejected; x.Permit.DecidedByUserId = Me; x.Permit.DecidedAt = now; x.Permit.DecisionNote = note; x.Permit.ModifyDate = now; x.Permit.ModifierUserId = Me;
                    EnEvents.Add(_uow, c, dto.Approve ? "mdl-permit-approved" : "mdl-permit-rejected", Me, "مجوز «مناقصه بدون MDL» " + x.Permit.Number + (dto.Approve ? " توسط مدیرعامل تایید شد." : " توسط مدیرعامل رد شد: " + note));
                    break;
                }
                default:   // withdraw-permit
                {
                    string note = EnText.Clean(dto.Note);
                    why = WithdrawPermitReason(x, w) ?? (x.Permit.Status == EnMdlPermitStatus.Approved ? EnText.ReasonShort(note, EnRules.ReasonMin, "دلیل پس‌گرفتن مجوز") : null) ?? EnText.Over(note, 1000, "توضیح");
                    if (why != null) return EnText.Bad<EnProjectMdlCaseDto>(why);
                    x.Permit.Status = EnMdlPermitStatus.Void; x.Permit.DecisionNote = note; x.Permit.ModifyDate = now; x.Permit.ModifierUserId = Me;
                    EnEvents.Add(_uow, c, "mdl-permit-withdrawn", Me, "درخواست مجوز «مناقصه بدون MDL» " + x.Permit.Number + " پس گرفته شد" + (note == null ? "." : ": " + EnText.Cut(note, 300)));
                    break;
                }
            }
            return await SaveAsync(c, ct);
        }

        private void Touch(EnProjectMdl m, DateTimeOffset now) { m.ModifyDate = now; m.ModifierUserId = Me; }

        public async Task<ResponseDto<EnProjectMdlCaseDto>> DraftFromFileAsync(int caseId, string note, byte[] file, string fileName, CancellationToken ct)
        {
            EnCase c = await _uow.Set<EnCase>().FirstOrDefaultAsync(k => k.Id == caseId && !k.Deleted, ct);
            if (c == null) return EnText.NotFound<EnProjectMdlCaseDto>(EnText.NotFoundCase);
            Who w = await WhoAsync(ct);
            Ctx x = await LoadAsync(c, true, ct);
            string why = DraftFromFileReason(x, w);
            if (why != null) return EnText.Bad<EnProjectMdlCaseDto>(why);
            string reason = EnText.Clean(note);
            why = EnText.ReasonShort(reason, EnRules.ReasonMin, "یادداشت (منبع و چرایی فهرست پروژه)") ?? EnText.Over(reason, 2000, "یادداشت");
            if (why != null) return EnText.Bad<EnProjectMdlCaseDto>(why);
            string problem = EnReferenceCsv.Parse(file, out List<EnReferenceLineDto> lines, out EnReferenceCsv.Columns _);
            if (problem != null) return EnText.Bad<EnProjectMdlCaseDto>(problem);
            if (lines.Count == 0) return EnText.Bad<EnProjectMdlCaseDto>("دست‌کم یک ردیف باید در فهرست بماند.");
            (Guid? docId, string docName) = await StoreAsync(file, fileName, "فایل فهرست مدارک پروژه (MDL) " + c.Number, "project-mdl.csv");   // saves the unit of work: nothing is changed yet
            DateTimeOffset now = DateTimeOffset.Now;
            int revision = (await _uow.Set<EnProjectMdl>().Where(m => m.CaseId == c.Id).Select(m => (int?)m.Revision).MaxAsync(ct) ?? 0) + 1;
            EnProjectMdl mdl = new EnProjectMdl
            {
                CaseId = c.Id, Revision = revision, Status = EnProjectMdlStatus.Draft, BasedOnListId = null, BasedOnCode = null, BasedOnRevision = 0, SourceDocumentId = docId, SourceFileName = docName, ChangeNote = reason,
                CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
            };
            mdl.Number = await EnNumbers.NextAsync(_uow, EnNumbers.ProjectMdl, "PMDL", now.DateTime, ct);
            int seq = 0;
            foreach (EnReferenceLineDto f in lines)
                mdl.Lines.Add(new EnProjectMdlLine
                {
                    Seq = ++seq, DocumentType = f.DocumentType.Trim(), Discipline = EnText.Clean(f.Discipline), Title = f.Title.Trim(), Required = f.Required, TenderStage = f.TenderStage, ClientSubmittal = f.ClientSubmittal, StandardClause = EnText.Clean(f.StandardClause), Note = EnText.Clean(f.Note),
                    Units = EnUnits.Normalize(f.Units, out _), Origin = EnProjectMdlLineOrigin.Project, Removed = false, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
                });
            _uow.Set<EnProjectMdl>().Add(mdl);
            EnEvents.Add(_uow, c, "project-mdl-drafted", Me, "پیش‌نویس فهرست مدارک پروژه " + mdl.Number + " (ویرایش " + EnText.Num(revision) + ") از فایل، بدون فهرست استاندارد، با " + EnText.Num(seq) + " ردیف ساخته شد: " + EnText.Cut(reason, 300));
            return await SaveAsync(c, ct);
        }

        public async Task<ResponseDto<EnProjectMdlCaseDto>> UploadLinesAsync(int id, string rowVersion, string note, byte[] file, string fileName, CancellationToken ct)
        {
            EnProjectMdl m = await _uow.Set<EnProjectMdl>().Include(l => l.Lines).FirstOrDefaultAsync(l => l.Id == id && !l.Deleted, ct);
            if (m == null) return EnText.NotFound<EnProjectMdlCaseDto>("فهرست مدارک پروژه پیدا نشد.");
            EnCase c = await _uow.Set<EnCase>().FirstOrDefaultAsync(k => k.Id == m.CaseId && !k.Deleted, ct);
            if (c == null) return EnText.NotFound<EnProjectMdlCaseDto>(EnText.NotFoundCase);
            Who w = await WhoAsync(ct);
            Ctx x = await LoadAsync(c, true, ct);
            string why = UploadReason(x, w);
            if (why == null && x.Live.Id != m.Id) why = "این ویرایش از فهرست مدارک پروژه دیگر جاری نیست؛ صفحه را دوباره باز کنید.";
            if (why != null) return EnText.Bad<EnProjectMdlCaseDto>(why);
            string versionWhy = VersionRefusal(m, rowVersion, w);
            if (versionWhy != null) return EnText.Bad<EnProjectMdlCaseDto>(versionWhy);
            string reason = EnText.Clean(note);
            why = EnText.ReasonShort(reason, EnRules.ReasonMin, "یادداشت تغییر (چرایی تفاوت با فهرست استاندارد)") ?? EnText.Over(reason, 2000, "یادداشت تغییر");
            if (why != null) return EnText.Bad<EnProjectMdlCaseDto>(why);
            string problem = EnReferenceCsv.Parse(file, out List<EnReferenceLineDto> lines, out EnReferenceCsv.Columns columns);
            if (problem != null) return EnText.Bad<EnProjectMdlCaseDto>(problem);
            if (lines.Count == 0) return EnText.Bad<EnProjectMdlCaseDto>("دست‌کم یک ردیف باید در فهرست بماند.");   // every row of the file is kept: a list with nothing left is refused
            Dictionary<string, EnReferenceLine> standard = new Dictionary<string, EnReferenceLine>();
            if (m.BasedOnListId.HasValue)
                foreach (EnReferenceLine l in (await _uow.Set<EnReferenceList>().AsNoTracking().Include(l => l.Lines).FirstOrDefaultAsync(l => l.Id == m.BasedOnListId.Value, ct))?.Lines ?? new List<EnReferenceLine>())
                    standard.TryAdd(EnDossierBuilder.TypeTitle(l.DocumentType, l.Title), l);
            (Guid? docId, string docName) = await StoreAsync(file, fileName, "فایل فهرست مدارک پروژه (MDL) " + c.Number, "project-mdl.csv");   // the file is parsed and nothing is changed yet
            DateTimeOffset now = DateTimeOffset.Now;
            List<EnProjectMdlLine> pool = EnProjectMdlRules.Active(m).ToList();
            HashSet<EnProjectMdlLine> kept = new HashSet<EnProjectMdlLine>();
            int seq = 0;
            foreach (EnReferenceLineDto f in lines)
            {
                string key = EnDossierBuilder.TypeTitle(f.DocumentType, f.Title);
                EnProjectMdlLine line = pool.FirstOrDefault(p => !kept.Contains(p) && EnDossierBuilder.TypeTitle(p.DocumentType, p.Title) == key);
                bool isNew = line == null;
                if (isNew)
                {
                    line = new EnProjectMdlLine { Origin = standard.ContainsKey(key) ? EnProjectMdlLineOrigin.Standard : EnProjectMdlLineOrigin.Project, CreateDate = now, CreatorUserId = Me };
                    m.Lines.Add(line);
                }
                kept.Add(line);
                line.Seq = ++seq; line.DocumentType = f.DocumentType.Trim(); line.Discipline = EnText.Clean(f.Discipline); line.Title = f.Title.Trim(); line.Required = f.Required; line.TenderStage = f.TenderStage; line.Removed = false;
                // a file without the optional columns never wipes what a matched line already holds
                if (isNew || columns.ClientSubmittal) line.ClientSubmittal = f.ClientSubmittal;
                if (isNew || columns.Clause) line.StandardClause = EnText.Clean(f.StandardClause);
                if (isNew || columns.Note) line.Note = EnText.Clean(f.Note);
                line.Units = EnUnits.Normalize(f.Units, out _);
                line.ModifyDate = now; line.ModifierUserId = Me;
            }
            foreach (EnProjectMdlLine gone in pool.Where(p => !kept.Contains(p)).OrderBy(p => p.Seq))
            {
                if (gone.Origin == EnProjectMdlLineOrigin.Standard) { gone.Removed = true; gone.Seq = ++seq; }   // a standard line stays as a marked row
                else gone.Delete();                                                                                // a project line that the file no longer has is hidden, never physically deleted
                gone.ModifyDate = now; gone.ModifierUserId = Me;
            }
            if (docId.HasValue) { m.SourceDocumentId = docId; m.SourceFileName = docName; }
            m.ChangeNote = reason; m.Status = EnProjectMdlStatus.Draft; m.CheckedByUserId = null; m.CheckedAt = null; Touch(m, now);
            int standardKept = kept.Count(l => l.Origin == EnProjectMdlLineOrigin.Standard), added = kept.Count(l => l.Origin == EnProjectMdlLineOrigin.Project), removed = pool.Count(p => !kept.Contains(p) && p.Origin == EnProjectMdlLineOrigin.Standard);
            EnEvents.Add(_uow, c, "project-mdl-uploaded", Me, "ردیف‌های فهرست مدارک پروژه " + m.Number + " از فایل جایگزین شد (از استاندارد " + EnText.Num(standardKept) + "، حذف‌شده " + EnText.Num(removed) + "، افزوده " + EnText.Num(added) + "): " + EnText.Cut(reason, 300));
            return await SaveAsync(c, ct);
        }
    }
}
