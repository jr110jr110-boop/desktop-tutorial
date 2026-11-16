using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Quality;
using Services.NasbNiro.ProjectControl;

using ViewModels.NasbNiro.EngineeringDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Engineering
{
    /// <summary>One document the handover should carry, read from its source (the platform dossier, or the project MDR for a new design).</summary>
    internal sealed class EnSourceLine
    {
        public int Seq;
        public string Type, Discipline, Title, Revision;
        public bool Required;
        public EnLineState State;
        public Guid? DocumentId;
        public int? PlatformDocumentId, DeliverableId;
        public string LinkPath, Units;
        public string Key => EnReferenceRules.Key(Type) + "|" + EnHash.Norm(Discipline).ToUpperInvariant() + "|" + EnHash.Norm(Title).ToUpperInvariant();
    }

    internal static class EnHandoverRules
    {
        /// <summary>"Approved for construction / issued" of a project MDR row: the issue state Approved with a file.</summary>
        public static bool DeliverableReady(PcEngineeringDeliverable d) => d.IssueState == PcIssueState.Approved && d.DocumentId.HasValue;

        public static string LineHash(IEnumerable<EnHandoverLine> lines) =>
            EnHash.Of(string.Join(";", lines.OrderBy(l => l.Seq).Select(l => string.Join("|", EnText.Num(l.Seq), EnHash.Norm(l.DocumentType), EnHash.Norm(l.Title), l.Required ? "1" : "0", EnText.Num((int)l.State), l.DocumentId?.ToString("N") ?? "-", EnHash.Norm(l.Revision)) + (string.IsNullOrEmpty(l.Units) ? string.Empty : "|U:" + l.Units))));

        /// <summary>The Code of the Approved list the handover lines come from: spare parts and service may use the one chosen at the confirm; else the standard of the project.</summary>
        public static string ListCode(EnCase c) => c.Kind != EnCaseKind.Turbine && !string.IsNullOrWhiteSpace(c.HandoverListCode) ? c.HandoverListCode : c.ReferenceCode;

        public static async Task<List<EnSourceLine>> SourcesAsync(IUnitOfWork uow, EnCase c, CancellationToken ct)
        {
            List<EnSourceLine> result = await SourceListAsync(uow, c, ct);
            // the product's own BOM and manufacturing route are proven by the system when the reference list has no line of that type; they carry a link to the bank page, not a file
            if (c.ProductId.HasValue && c.Kind == EnCaseKind.Turbine)
            {
                int pid = c.ProductId.Value;
                int seq = result.Count == 0 ? 1000 : result.Max(x => x.Seq) + 1;
                if (!result.Any(x => EnReferenceRules.Key(x.Type) == EnDocTypes.Bom))
                {
                    int? rev = await uow.Set<Entities.Model.NasbNiro.ProductBank.PbBomHeader>().AsNoTracking().Where(b => !b.Deleted && b.ProductId == pid && b.Status == Entities.Model.NasbNiro.ProductBank.PbBomStatus.Released).OrderByDescending(b => b.Revision).Select(b => (int?)b.Revision).FirstOrDefaultAsync(ct);
                    result.Add(new EnSourceLine { Seq = seq++, Type = EnDocTypes.Bom, Discipline = "تولید", Title = "BOM منتشرشده", Required = true, State = rev.HasValue ? EnLineState.Available : EnLineState.Missing, Revision = rev.HasValue ? rev.Value.ToString(CultureInfo.InvariantCulture) : null, LinkPath = "/product-bank/products/" + pid.ToString(CultureInfo.InvariantCulture) + "/bom", Units = EnUnits.Bom });
                }
                if (!result.Any(x => EnReferenceRules.Key(x.Type) == EnDocTypes.Opc))
                {
                    bool ok = await uow.Set<Entities.Model.NasbNiro.ProductBank.PbOpcRouting>().AsNoTracking().AnyAsync(o => !o.Deleted && o.ProductId == pid && o.Status == Entities.Model.NasbNiro.ProductBank.PbRoutingStatus.Approved, ct)
                        && !await uow.Set<Entities.Model.NasbNiro.ProductBank.PbOpcRouting>().AsNoTracking().AnyAsync(o => !o.Deleted && o.ProductId == pid && (o.Status == Entities.Model.NasbNiro.ProductBank.PbRoutingStatus.Draft || o.Status == Entities.Model.NasbNiro.ProductBank.PbRoutingStatus.UnderReview), ct);
                    result.Add(new EnSourceLine { Seq = seq++, Type = EnDocTypes.Opc, Discipline = "تولید", Title = "چارت مسیر ساخت (OPC) تاییدشده", Required = true, State = ok ? EnLineState.Available : EnLineState.Missing, LinkPath = "/product-bank/products/" + pid.ToString(CultureInfo.InvariantCulture) + "/routing", Units = EnUnits.Opc });
                }
            }
            return result;
        }

        private static async Task<List<EnSourceLine>> SourceListAsync(IUnitOfWork uow, EnCase c, CancellationToken ct)
        {
            List<EnSourceLine> result = new List<EnSourceLine>();
            if (c.Classification == EnClassification.Platform && c.ProductId.HasValue)
            {
                EnPlatformDossier d = await uow.Set<EnPlatformDossier>().AsNoTracking().Include(x => x.Documents).FirstOrDefaultAsync(x => x.ProductId == c.ProductId.Value && x.ReferenceCode == c.ReferenceCode && !x.Deleted, ct);
                if (d == null) return result;
                foreach (EnPlatformDocument x in d.Documents.OrderBy(x => x.Seq).ThenBy(x => x.Id))
                    result.Add(new EnSourceLine { Seq = x.Seq, Type = x.DocumentType, Discipline = x.Discipline, Title = x.Title, Required = x.Required, State = x.State, DocumentId = x.DocumentId, Revision = x.Revision, PlatformDocumentId = x.Id, Units = x.Units });
                return result;
            }
            EnReferenceList list = await EnReferenceRules.CurrentAsync(uow, false, ct, ListCode(c));
            if (list == null || !c.ContractId.HasValue) return result;
            List<PcEngineeringDeliverable> mdr = await uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(x => x.ContractId == c.ContractId.Value && !x.Deleted).OrderBy(x => x.Id).ToListAsync(ct);
            foreach (EnReferenceLine l in list.Lines.OrderBy(l => l.Seq))
            {
                string type = EnReferenceRules.Key(l.DocumentType), title = EnHash.Norm(l.Title).ToUpperInvariant();
                PcEngineeringDeliverable m = mdr.FirstOrDefault(x => EnReferenceRules.Key(x.DocumentType) == type && EnHash.Norm(x.Title).ToUpperInvariant() == title && DeliverableReady(x))
                    ?? mdr.FirstOrDefault(x => EnReferenceRules.Key(x.DocumentType) == type && EnHash.Norm(x.Title).ToUpperInvariant() == title);
                bool ready = m != null && DeliverableReady(m);
                result.Add(new EnSourceLine { Seq = l.Seq, Type = l.DocumentType, Discipline = l.Discipline, Title = l.Title, Required = l.Required, State = ready ? EnLineState.Available : EnLineState.Missing, DocumentId = ready ? m.DocumentId : null, Revision = ready ? m.Revision : null, DeliverableId = m?.Id, Units = l.Units });
            }
            return result;
        }

        public static void Apply(EnHandoverLine line, EnSourceLine s)
        {
            line.State = s.State; line.DocumentId = s.DocumentId; line.Revision = s.Revision; line.PlatformDocumentId = s.PlatformDocumentId; line.DeliverableId = s.DeliverableId; line.LinkPath = s.LinkPath; line.Units = s.Units;
        }

        /// <summary>The notice text of one unit: what that unit needs from the handover (deterministic: contract, case, standard, count and titles of its own documents), cut with an ellipsis at the column length.</summary>
        public static string ScopeNote(string contractCode, string caseNumber, string standard, EnUnit unit, IEnumerable<EnHandoverLine> lines, out int count)
        {
            List<EnHandoverLine> mine = lines.Where(l => EnUnits.Includes(l.Units, unit)).OrderBy(l => l.Seq).ThenBy(l => l.Id).ToList();
            count = mine.Count;
            string head = "مدارک مهندسی قرارداد " + (contractCode ?? "—") + " (پرونده " + caseNumber + "، استاندارد " + (string.IsNullOrWhiteSpace(standard) ? "—" : standard) + ") برای واحد " + EnText.Unit(unit) + ": ";
            string body = count == 0 ? "برای این واحد مدرکی در این تحویل نیست." : EnText.Num(count) + " مدرک: " + string.Join("؛ ", mine.Select(l => l.Title));
            string text = head + body;
            return text.Length > 2000 ? text.Substring(0, 1999) + "…" : text;
        }

        /// <summary>The keys of the required lines that are missing.</summary>
        public static List<string> MissingKeys(IEnumerable<EnHandoverLine> lines) => lines.Where(l => l.Required && l.State == EnLineState.Missing).Select(l => EnReferenceRules.Key(l.DocumentType) + "|" + EnHash.Norm(l.Title).ToUpperInvariant()).ToList();

        public sealed class Snapshot
        {
            public List<string> Lines { get; set; } = new List<string>();
            public bool DossierUnconfirmed { get; set; }
        }

        public static Snapshot Parse(string json)
        {
            try { return string.IsNullOrWhiteSpace(json) ? new Snapshot() : JsonSerializer.Deserialize<Snapshot>(json) ?? new Snapshot(); }
            catch (JsonException) { return new Snapshot(); }
        }

        /// <summary>Whether the waiver still covers the handover: every missing line now was missing at the request, and an unconfirmed dossier was unconfirmed then too.</summary>
        public static string Coverage(Snapshot s, List<string> missingNow, bool dossierUnconfirmedNow)
        {
            List<string> extra = missingNow.Where(k => !s.Lines.Contains(k)).ToList();
            if (extra.Count > 0) return "معافیت این تحویل را پوشش نمی‌دهد: " + EnText.Num(extra.Count) + " مدرک الزامی تازه کم شده یا پس از درخواست از دست رفته است.";
            if (dossierUnconfirmedNow && !s.DossierUnconfirmed) return "معافیت این تحویل را پوشش نمی‌دهد: تایید پروندهٔ پلتفرم پس از درخواست باطل شده است.";
            return null;
        }

        public static string LockMessage(int missing, bool dossierUnconfirmed)
        {
            if (missing <= 0 && !dossierUnconfirmed) return null;
            string body = missing > 0 ? EnText.Num(missing) + " مدرک الزامی هنوز موجود نیست" + (dossierUnconfirmed ? " و پروندهٔ پلتفرم هنوز توسط مدیر مهندسی تایید نشده است" : string.Empty)
                : "پروندهٔ پلتفرم هنوز کامل و تایید نشده است";
            return "تحویل مدارک به PMO قفل است: " + body + ". فقط معافیت اضطراری تاییدشده توسط مدیرعامل این قفل را باز می‌کند.";
        }

        public static async Task<bool> DebtSourceReadyAsync(IUnitOfWork uow, EnDocumentDebt debt, EnHandoverLine line, CancellationToken ct)
        {
            if (line.PlatformDocumentId.HasValue)
            {
                EnPlatformDocument d = await uow.Set<EnPlatformDocument>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == line.PlatformDocumentId.Value, ct);
                return d != null && d.State == EnLineState.Available && (d.DocumentId.HasValue || d.SystemProven);
            }
            string type = EnReferenceRules.Key(line.DocumentType), title = EnHash.Norm(line.Title).ToUpperInvariant();
            List<PcEngineeringDeliverable> mdr = await uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(x => x.ContractId == debt.ContractId && !x.Deleted).ToListAsync(ct);
            return mdr.Any(x => EnReferenceRules.Key(x.DocumentType) == type && EnHash.Norm(x.Title).ToUpperInvariant() == title && DeliverableReady(x));
        }
    }

    public interface IEnHandoverService
    {
        Task<ResponseDto<PagedList<EnHandoverDto>>> GetHandoversAsync(EnHandoverQuery query, CancellationToken ct);
        Task<ResponseDto<EnHandoverDto>> GetHandoverAsync(int id, CancellationToken ct);
        Task<ResponseDto<EnHandoverDto>> HandoverActionAsync(EnHandoverActionDto dto, CancellationToken ct);
        Task<ResponseDto<PagedList<EnWaiverDto>>> GetWaiversAsync(EnWaiverQuery query, CancellationToken ct);
        Task<ResponseDto<EnWaiverDto>> GetWaiverAsync(int id, CancellationToken ct);
        Task<ResponseDto<EnWaiverDto>> RequestWaiverAsync(EnWaiverRequestDto dto, CancellationToken ct);
        Task<ResponseDto<EnWaiverDto>> DecideWaiverAsync(EnWaiverDecisionDto dto, CancellationToken ct);
        Task<ResponseDto<PagedList<EnDebtDto>>> GetDebtsAsync(EnDebtQuery query, CancellationToken ct);
        Task<ResponseDto<int>> SettleDebtsAsync(CancellationToken ct);
        Task<ResponseDto<List<EnDistributionDto>>> GetDistributionsAsync(int? handoverId, bool onlyOpen, CancellationToken ct);
    }

    /// <summary>The handover of the engineering documents to the PMO: the lock at point B, the CEO emergency waiver and its document debts, the round trips with the PMO, the release to execution and the distribution to the units.</summary>
    public class EnHandoverService : IEnHandoverService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public EnHandoverService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;

        private sealed class Who { public int Me; public bool Writer, Manager, Ceo, Pmo, PmoSide, Seat; }

        private async Task<Who> WhoAsync(CancellationToken ct) => new Who
        {
            Me = Me, Writer = _user.IsInRole(EnRoles.Writers), Manager = _user.IsInRole(EnRoles.Manager), Ceo = _user.IsInRole(EnRoles.Ceo), Pmo = _user.IsInRole(EnRoles.Pmo), PmoSide = _user.IsInRole(EnRoles.PmoSide), Seat = await EnActors.IsSeatAsync(_uow, Me, ct),
        };

        private static readonly (EnUnit Unit, string Roles)[] Units =
        {
            (EnUnit.Qa, "QA"), (EnUnit.Qc, "QC"), (EnUnit.Production, "PRODUCTIONMANAGER,SHOPSUPERVISOR,CONSTRUCTION"), (EnUnit.Procurement, "COMMERCIAL"), (EnUnit.Warehouse, "WAREHOUSE,WHSUPERVISOR"), (EnUnit.Planning, "PLANNING"),
        };

        // ---------------------------------------------------------------- state

        private sealed class LockState
        {
            public int Missing;
            public bool DossierUnconfirmed;
            public bool Locked => Missing > 0 || DossierUnconfirmed;
            public List<string> Keys = new List<string>();
        }

        private async Task<EnGapSummaryDto> GapSummaryAsync(int caseId, CancellationToken ct)
        {
            EnStandardGap g = await EnGaps.LiveAsync(_uow, caseId, ct);
            return g == null ? null : new EnGapSummaryDto { Id = g.Id, Number = g.Number, Status = (int)g.Status, StatusText = EnStandardGapService.StatusText(g.Status), Summary = g.Summary };
        }

        private async Task<LockState> LockAsync(EnHandover h, EnCase c, CancellationToken ct)
        {
            LockState s = new LockState { Keys = EnHandoverRules.MissingKeys(h.Lines) };
            s.Missing = h.Lines.Count(l => l.Required && l.State == EnLineState.Missing);
            s.DossierUnconfirmed = c.Classification == EnClassification.Platform && !(c.ProductId.HasValue && await EnPlatformRules.IsCompleteAsync(_uow, c.ProductId.Value, c.ReferenceCode, ct));
            return s;
        }

        private async Task<EnWaiver> LiveWaiverAsync(int handoverId, CancellationToken ct) =>
            await _uow.Set<EnWaiver>().FirstOrDefaultAsync(w => w.HandoverId == handoverId && !w.Deleted && (w.Status == EnWaiverStatus.Requested || w.Status == EnWaiverStatus.Approved) && w.UsedAt == null, ct);

        private static string SubmitReason(EnHandover h, Who w, LockState ls, EnWaiver waiver, DateTimeOffset now)
        {
            if (!w.Manager) return EnText.NotAllowed;
            if (w.Seat) return EnText.AiNever;
            if (h.Status != EnHandoverStatus.Draft) return "فقط پیش‌نویس تحویل به PMO ارسال می‌شود.";
            if (h.PreparedByUserId == w.Me) return EnText.SameUser + " مدیری که پیش‌نویس را آماده کرده آن را ارسال نمی‌کند.";
            if (!ls.Locked) return null;
            string message = EnHandoverRules.LockMessage(ls.Missing, ls.DossierUnconfirmed);
            if (waiver == null || waiver.Status != EnWaiverStatus.Approved) return message;
            string coverage = EnHandoverRules.Coverage(EnHandoverRules.Parse(waiver.MissingSnapshot), ls.Keys, ls.DossierUnconfirmed);
            return coverage == null ? null : message + " " + coverage;
        }

        private static string WaiverRequestReason(EnHandover h, Who w, LockState ls, EnWaiver live) =>
            !w.Manager ? EnText.NotAllowed : w.Seat ? EnText.AiNever : h.Status != EnHandoverStatus.Draft ? "معافیت فقط برای پیش‌نویس تحویل درخواست می‌شود." : !ls.Locked ? "قفل تحویل باز است؛ معافیت لازم نیست."
            : live != null ? "برای این تحویل معافیت باز (درخواست‌شده یا تاییدشده) وجود دارد." : null;

        private async Task<string> PmoReasonAsync(EnHandover h, Who w, bool needPmoOnly, CancellationToken ct)
        {
            if (needPmoOnly) return w.Pmo ? null : EnText.NotAllowed;
            if (w.Pmo) return null;
            if (!w.PmoSide) return EnText.NotAllowed;
            PcContractTerms terms = await PcContext.TermsAsync(_uow, h.ContractId, false, ct);
            return terms?.ProjectManagerUserId == w.Me ? null : EnText.NotAllowed + " (پذیرش با PMO یا مدیر پروژهٔ همین قرارداد است)";
        }

        private async Task<string> AcceptReasonAsync(EnHandover h, Who w, CancellationToken ct) =>
            await PmoReasonAsync(h, w, false, ct) ?? (w.Seat ? EnText.AiNever : h.Status != EnHandoverStatus.Submitted ? "فقط تحویل ارسال‌شده پذیرفته می‌شود." : h.SubmittedByUserId == w.Me ? EnText.SameUser : null);

        private async Task<string> ReturnReasonAsync(EnHandover h, Who w, CancellationToken ct) =>
            await PmoReasonAsync(h, w, false, ct) ?? (w.Seat ? EnText.AiNever : h.Status != EnHandoverStatus.Submitted ? "فقط تحویل ارسال‌شده برگشت می‌خورد." : null);

        private async Task<string> ReleaseReasonAsync(EnHandover h, Who w, CancellationToken ct) => await ReleaseBaseReasonAsync(h, w, ct) ?? await DistributionNotReadyAsync(h, ct);

        /// <summary>The release is refused while the distribution rows are missing or any unit's notice text is empty.</summary>
        private async Task<string> DistributionNotReadyAsync(EnHandover h, CancellationToken ct)
        {
            List<string> notes = await _uow.Set<EnDistribution>().AsNoTracking().Where(d => d.HandoverId == h.Id && !d.Deleted && d.Status == EnDistStatus.Draft).Select(d => d.ScopeNote).ToListAsync(ct);
            return notes.Count < Units.Length || notes.Any(string.IsNullOrWhiteSpace) ? "ابلاغ ممکن نیست: ردیف توزیع واحدها و یادداشت هر واحد هنوز آماده نیست (ایجنت مهندسی آن‌ها را می‌سازد و یادداشت هیچ واحدی خالی نمی‌ماند)." : null;
        }

        private async Task<string> ReleaseBaseReasonAsync(EnHandover h, Who w, CancellationToken ct) =>
            await PmoReasonAsync(h, w, true, ct) ?? (w.Seat ? EnText.AiNever : h.Status != EnHandoverStatus.Accepted ? "فقط تحویل پذیرفته‌شده ابلاغ می‌شود."
                : h.SubmittedByUserId == w.Me || EnEditors.Has(h.EditorUserIds, w.Me) || h.PreparedByUserId == w.Me ? EnText.SameUser : null);

        private static string VoidReasonOf(EnHandover h, Who w) =>
            !w.Manager ? EnText.NotAllowed : w.Seat ? EnText.AiNever : h.Status != EnHandoverStatus.Draft && h.Status != EnHandoverStatus.Returned ? "فقط پیش‌نویس یا تحویل برگشت‌خورده ابطال می‌شود." : null;

        private static string EditScopeReason(EnDistribution d, EnHandoverStatus handover, Who w) =>
            !w.Pmo ? EnText.NotAllowed + " (یادداشت ابلاغ واحد را PMO ویرایش می‌کند)" : w.Seat ? EnText.AiNever : handover != EnHandoverStatus.Accepted || d.Status != EnDistStatus.Draft ? "یادداشت ابلاغ فقط تا پیش از ابلاغ تحویل (وضعیت پذیرفته‌شده) ویرایش می‌شود." : null;

        private static string DecideReason(EnWaiver w, Who who, DateTimeOffset now) =>
            !who.Ceo ? EnText.NotAllowed + " (تایید معافیت اضطراری فقط با مدیرعامل است)" : who.Seat ? EnText.AiNever : w.Status != EnWaiverStatus.Requested ? "فقط درخواست معافیت در انتظار تصمیم تصمیم‌گیری می‌شود."
            : w.RequestedByUserId == who.Me ? EnText.SameUser : null;

        // ---------------------------------------------------------------- read

        private static EnDebtDto DebtDto(EnDocumentDebt d, Dictionary<int, string> names, Dictionary<int, string> waivers, Dictionary<int, string> cases, DateTimeOffset today) => new EnDebtDto
        {
            Id = d.Id, WaiverId = d.WaiverId, WaiverNumber = waivers.TryGetValue(d.WaiverId, out string wn) ? wn : null, HandoverLineId = d.HandoverLineId, CaseId = d.CaseId, CaseNumber = cases.TryGetValue(d.CaseId, out string cn) ? cn : null,
            ContractId = d.ContractId, ProductId = d.ProductId, DocumentType = d.DocumentType, Title = d.Title, DueAt = d.DueAt, DueAtText = EnText.Stamp(d.DueAt), Status = (int)d.Status, StatusText = d.Status == EnDebtStatus.Open ? "باز" : d.Status == EnDebtStatus.Settled ? "تسویه‌شده" : "باطل",
            Overdue = d.Status == EnDebtStatus.Open && d.DueAt < today, OverdueDays = d.Status == EnDebtStatus.Open && d.DueAt < today ? (int)(today - d.DueAt).TotalDays : 0,
            SettledAtText = EnText.Stamp(d.SettledAt), SettledByName = names.Name(d.SettledByUserId), SettledDocumentId = d.SettledDocumentId,
        };

        private async Task<List<EnWaiverDto>> WaiverDtosAsync(List<EnWaiver> rows, CancellationToken ct)
        {
            Who who = await WhoAsync(ct);
            DateTimeOffset now = DateTimeOffset.Now;
            Dictionary<int, string> names = await EnUsers.NamesAsync(_uow, rows.SelectMany(r => new int?[] { r.RequestedByUserId, r.DecidedByUserId }), ct);
            List<int> hids = rows.Select(r => r.HandoverId).Distinct().ToList();
            Dictionary<int, string> hn = await _uow.Set<EnHandover>().AsNoTracking().Where(h => hids.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => h.Number, ct);
            List<int> cids = rows.Select(r => r.CaseId).Distinct().ToList();
            Dictionary<int, string> cn = await _uow.Set<EnCase>().AsNoTracking().Where(c => cids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Number, ct);
            List<EnWaiverDto> result = new List<EnWaiverDto>();
            foreach (EnWaiver w in rows)
            {
                EnHandoverRules.Snapshot snap = EnHandoverRules.Parse(w.MissingSnapshot);
                string decide = DecideReason(w, who, now), coverage = null;
                if (w.Status == EnWaiverStatus.Approved && w.UsedAt == null)
                {
                    EnHandover h = await _uow.Set<EnHandover>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == w.HandoverId, ct);
                    EnCase c = h == null ? null : await _uow.Set<EnCase>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == h.CaseId, ct);
                    if (h != null && c != null) { LockState ls = await LockAsync(h, c, ct); coverage = EnHandoverRules.Coverage(snap, ls.Keys, ls.DossierUnconfirmed); }
                }
                result.Add(new EnWaiverDto
                {
                    Id = w.Id, Number = w.Number, HandoverId = w.HandoverId, HandoverNumber = hn.TryGetValue(w.HandoverId, out string hnum) ? hnum : null, CaseId = w.CaseId, CaseNumber = cn.TryGetValue(w.CaseId, out string cnum) ? cnum : null,
                    Reason = w.Reason, MissingLines = snap.Lines, DossierUnconfirmed = snap.DossierUnconfirmed, DebtDueAt = w.DebtDueAt, DebtDueAtText = EnText.Stamp(w.DebtDueAt), Status = (int)w.Status, StatusText = EnText.Waiver(w.Status),
                    RequestedByName = names.Name(w.RequestedByUserId), RequestedAtText = EnText.Stamp(w.RequestedAt), WaitingHours = w.Status == EnWaiverStatus.Requested ? (int)Math.Floor((now - w.RequestedAt).TotalHours) : 0,
                    DecidedByName = names.Name(w.DecidedByUserId), DecidedAtText = EnText.Stamp(w.DecidedAt), DecisionNote = w.DecisionNote, Used = w.UsedAt.HasValue, Covers = w.Status == EnWaiverStatus.Approved && coverage == null, CoverageNote = coverage,
                    RowVersion = EnConcurrency.Text(w.RowVersion), CanDecide = decide == null, DecideBlockedReason = decide,
                });
            }
            return result;
        }

        private async Task<List<EnHandoverDto>> DtosAsync(List<EnHandover> rows, CancellationToken ct)
        {
            Who w = await WhoAsync(ct);
            DateTimeOffset now = DateTimeOffset.Now;
            DateTimeOffset today = DateTimeOffset.Now;
            Dictionary<int, string> names = await EnUsers.NamesAsync(_uow, rows.SelectMany(r => new int?[] { r.PreparedByUserId, r.SubmittedByUserId, r.AcceptedByUserId, r.ReturnedByUserId, r.ReleasedByUserId }), ct);
            int[] seats = await EnActors.SeatIdsAsync(_uow, ct);
            List<int> caseIds = rows.Select(r => r.CaseId).Distinct().ToList(), hids = rows.Select(r => r.Id).ToList(), contractIds = rows.Select(r => r.ContractId).Distinct().ToList();
            Dictionary<int, EnCase> cases = await _uow.Set<EnCase>().AsNoTracking().Where(c => caseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
            Dictionary<int, string> contracts = await _uow.Set<Entities.Model.NasbNiro.Contracts.ContractEntity>().AsNoTracking().Where(c => contractIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            List<EnWaiver> waivers = await _uow.Set<EnWaiver>().AsNoTracking().Where(x => !x.Deleted && hids.Contains(x.HandoverId)).OrderBy(x => x.Id).ToListAsync(ct);
            List<EnDistribution> dist = await _uow.Set<EnDistribution>().AsNoTracking().Where(d => !d.Deleted && hids.Contains(d.HandoverId)).OrderBy(d => d.Unit).ToListAsync(ct);
            List<EnDocumentDebt> debts = await _uow.Set<EnDocumentDebt>().AsNoTracking().Where(d => !d.Deleted && waivers.Select(x => x.Id).Contains(d.WaiverId)).OrderBy(d => d.Id).ToListAsync(ct);
            Dictionary<int, string> debtUsers = await EnUsers.NamesAsync(_uow, debts.Select(d => d.SettledByUserId).Concat(dist.Select(d => d.AcknowledgedByUserId)), ct);
            Dictionary<int, string> caseNames = cases.ToDictionary(k => k.Key, k => k.Value.Number);
            Dictionary<int, string> waiverNames = waivers.ToDictionary(x => x.Id, x => x.Number);
            List<EnHandoverDto> result = new List<EnHandoverDto>();
            List<EnUnit> myUnits = Units.Where(u => _user.IsInRole(u.Roles)).Select(u => u.Unit).ToList();   // the units of the reader (none for the PMO or the CEO)
            foreach (EnHandover h in rows)
            {
                EnCase c = cases[h.CaseId];
                LockState ls = await LockAsync(h, c, ct);
                EnWaiver live = waivers.Where(x => x.HandoverId == h.Id && x.Status != EnWaiverStatus.Rejected && x.Status != EnWaiverStatus.Void).OrderByDescending(x => x.Id).FirstOrDefault();
                string submit = SubmitReason(h, w, ls, live, now), request = WaiverRequestReason(h, w, ls, live != null && live.UsedAt == null ? live : null);
                string accept = await AcceptReasonAsync(h, w, ct), ret = await ReturnReasonAsync(h, w, ct), release = await ReleaseReasonAsync(h, w, ct), voidR = VoidReasonOf(h, w);
                EnWaiver shown = waivers.Where(x => x.HandoverId == h.Id || x.Id == h.WaiverId).OrderByDescending(x => x.Id).FirstOrDefault();
                EnWaiverDto waiverDto = shown == null ? null : (await WaiverDtosAsync(new List<EnWaiver> { shown }, ct))[0];
                result.Add(new EnHandoverDto
                {
                    Id = h.Id, Number = h.Number, CaseId = h.CaseId, CaseNumber = c.Number, ContractId = h.ContractId, ContractCode = contracts.TryGetValue(h.ContractId, out string cc) ? cc : null, Classification = (int)c.Classification, ClassificationText = EnText.Classification(c.Classification),
                    Round = h.Round, Status = (int)h.Status, StatusText = EnText.Handover(h.Status), PreparedByName = names.Name(h.PreparedByUserId), PreparedByAgent = seats.Contains(h.PreparedByUserId),
                    Complete = h.Status == EnHandoverStatus.Draft ? !ls.Locked : h.Complete, MissingRequiredCount = h.Status == EnHandoverStatus.Draft ? ls.Missing : h.MissingRequiredCount, RequiredCount = h.Lines.Count(l => l.Required),
                    PlatformDossierConfirmed = c.Classification == EnClassification.Platform && !ls.DossierUnconfirmed, LockMessage = h.Status == EnHandoverStatus.Draft ? EnHandoverRules.LockMessage(ls.Missing, ls.DossierUnconfirmed) : null,
                    WaiverId = h.WaiverId, WaiverNumber = h.WaiverId.HasValue && waiverNames.TryGetValue(h.WaiverId.Value, out string wn) ? wn : null,
                    SubmittedByName = names.Name(h.SubmittedByUserId), SubmittedAtText = EnText.Stamp(h.SubmittedAt), AcceptedByName = names.Name(h.AcceptedByUserId), AcceptedAtText = EnText.Stamp(h.AcceptedAt),
                    ReturnedByName = names.Name(h.ReturnedByUserId), ReturnedAtText = EnText.Stamp(h.ReturnedAt), ReturnReason = h.ReturnReason, ReleasedByName = names.Name(h.ReleasedByUserId), ReleasedAtText = EnText.Stamp(h.ReleasedAt),
                    StandardGap = await GapSummaryAsync(c.Id, ct),
                    Note = h.Note, VoidReason = h.VoidReason, TouchedByMe = EnEditors.Has(h.EditorUserIds, Me), RowVersion = EnConcurrency.Text(h.RowVersion), Waiver = waiverDto,
                    Lines = h.Lines.OrderBy(l => l.Seq).ThenBy(l => l.Id).Select(l => new EnHandoverLineDto { Id = l.Id, Seq = l.Seq, DocumentType = l.DocumentType, Discipline = l.Discipline, Title = l.Title, Required = l.Required, State = (int)l.State, StateText = EnText.LineState(l.State), DocumentId = l.DocumentId, Revision = l.Revision, PlatformDocumentId = l.PlatformDocumentId, DeliverableId = l.DeliverableId, LinkPath = l.LinkPath, Note = l.Note, Units = l.Units, ForMyUnit = myUnits.Any(u => EnUnits.Includes(l.Units, u)) }).ToList(),
                    Distribution = dist.Where(d => d.HandoverId == h.Id).Select(d => DistDto(d, h.Number, h.Status, debtUsers, w)).ToList(),
                    Debts = debts.Where(d => waivers.Any(x => x.Id == d.WaiverId && x.HandoverId == h.Id)).Select(d => DebtDto(d, debtUsers, waiverNames, caseNames, today)).ToList(),
                    CanSubmit = submit == null, SubmitBlockedReason = submit, CanRequestWaiver = request == null, RequestWaiverBlockedReason = request, CanAccept = accept == null, AcceptBlockedReason = accept,
                    CanReturn = ret == null, ReturnBlockedReason = ret, CanRelease = release == null, ReleaseBlockedReason = release, CanVoid = voidR == null, VoidBlockedReason = voidR,
                });
            }
            return result;
        }

        private EnDistributionDto DistDto(EnDistribution d, string handoverNumber, EnHandoverStatus handover, Dictionary<int, string> names, Who w)
        {
            string why = AckReason(d, w), edit = EditScopeReason(d, handover, w);
            return new EnDistributionDto
            {
                Id = d.Id, HandoverId = d.HandoverId, HandoverNumber = handoverNumber, ContractId = d.ContractId, Unit = (int)d.Unit, UnitText = EnText.Unit(d.Unit), Roles = d.Roles, Status = (int)d.Status, StatusText = d.Status == EnDistStatus.Draft ? "پیش‌نویس (منتظر ابلاغ PMO)" : "ابلاغ‌شده", ScopeNote = d.ScopeNote, DocumentCount = d.DocumentCount, CanEditScope = edit == null, EditScopeBlockedReason = edit, NotifiedAtText = EnText.Stamp(d.NotifiedAt),
                AcknowledgedByName = names.Name(d.AcknowledgedByUserId), AcknowledgedAtText = EnText.Stamp(d.AcknowledgedAt), Acknowledged = d.AcknowledgedAt.HasValue, RowVersion = EnConcurrency.Text(d.RowVersion), CanAcknowledge = why == null, AcknowledgeBlockedReason = why,
            };
        }

        private string AckReason(EnDistribution d, Who w) =>
            d.AcknowledgedAt.HasValue ? "این واحد قبلاً دریافت را تایید کرده است." : d.Status != EnDistStatus.Notified || !d.NoticeSentAt.HasValue ? "ابلاغ این واحد هنوز ارسال نشده است." : w.Seat ? EnText.AiNever : !_user.IsInRole(d.Roles) ? EnText.NotAllowed + " (تایید دریافت با نقش‌های واحد " + EnText.Unit(d.Unit) + " است)" : null;

        public async Task<ResponseDto<PagedList<EnHandoverDto>>> GetHandoversAsync(EnHandoverQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<PagedList<EnHandoverDto>>(EnText.NotAllowed);
            query ??= new EnHandoverQuery();
            IQueryable<EnHandover> q = _uow.Set<EnHandover>().AsNoTracking().Include(h => h.Lines).Where(h => !h.Deleted);
            if (query.CaseId.HasValue) q = q.Where(h => h.CaseId == query.CaseId.Value);
            if (query.ContractId.HasValue) q = q.Where(h => h.ContractId == query.ContractId.Value);
            if (query.Status.HasValue) { EnHandoverStatus s = (EnHandoverStatus)query.Status.Value; q = q.Where(h => h.Status == s); }
            if (query.OnlyOpen) q = q.Where(h => h.Status == EnHandoverStatus.Draft || h.Status == EnHandoverStatus.Submitted || h.Status == EnHandoverStatus.Accepted);
            return ResponseDtoHelper.Ok(await EnPaging.CutAsync(q.OrderByDescending(h => h.Id), query.PageNumber, query.PageSize, rows => DtosAsync(rows, ct), ct));
        }

        public async Task<ResponseDto<EnHandoverDto>> GetHandoverAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<EnHandoverDto>(EnText.NotAllowed);
            EnHandover h = await _uow.Set<EnHandover>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (h == null) return EnText.NotFound<EnHandoverDto>(EnText.NotFoundHandover);
            return ResponseDtoHelper.Ok((await DtosAsync(new List<EnHandover> { h }, ct))[0]);
        }

        public async Task<ResponseDto<PagedList<EnWaiverDto>>> GetWaiversAsync(EnWaiverQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<PagedList<EnWaiverDto>>(EnText.NotAllowed);
            query ??= new EnWaiverQuery();
            IQueryable<EnWaiver> q = _uow.Set<EnWaiver>().AsNoTracking().Where(x => !x.Deleted);
            if (query.HandoverId.HasValue) q = q.Where(x => x.HandoverId == query.HandoverId.Value);
            if (query.Status.HasValue) { EnWaiverStatus s = (EnWaiverStatus)query.Status.Value; q = q.Where(x => x.Status == s); }
            return ResponseDtoHelper.Ok(await EnPaging.CutAsync(q.OrderByDescending(x => x.Id), query.PageNumber, query.PageSize, rows => WaiverDtosAsync(rows, ct), ct));
        }

        public async Task<ResponseDto<EnWaiverDto>> GetWaiverAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<EnWaiverDto>(EnText.NotAllowed);
            EnWaiver x = await _uow.Set<EnWaiver>().AsNoTracking().FirstOrDefaultAsync(w => w.Id == id && !w.Deleted, ct);
            if (x == null) return EnText.NotFound<EnWaiverDto>("معافیت پیدا نشد.");
            return ResponseDtoHelper.Ok((await WaiverDtosAsync(new List<EnWaiver> { x }, ct))[0]);
        }

        public async Task<ResponseDto<PagedList<EnDebtDto>>> GetDebtsAsync(EnDebtQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<PagedList<EnDebtDto>>(EnText.NotAllowed);
            query ??= new EnDebtQuery();
            DateTimeOffset today = DateTimeOffset.Now;
            IQueryable<EnDocumentDebt> q = _uow.Set<EnDocumentDebt>().AsNoTracking().Where(d => !d.Deleted);
            if (query.CaseId.HasValue) q = q.Where(d => d.CaseId == query.CaseId.Value);
            if (query.ContractId.HasValue) q = q.Where(d => d.ContractId == query.ContractId.Value);
            if (query.Status.HasValue) { EnDebtStatus s = (EnDebtStatus)query.Status.Value; q = q.Where(d => d.Status == s); }
            if (query.OnlyOverdue) q = q.Where(d => d.Status == EnDebtStatus.Open && d.DueAt < today);
            return ResponseDtoHelper.Ok(await EnPaging.CutAsync(q.OrderBy(d => d.DueAt).ThenBy(d => d.Id), query.PageNumber, query.PageSize, async rows =>
            {
                Dictionary<int, string> names = await EnUsers.NamesAsync(_uow, rows.Select(r => r.SettledByUserId), ct);
                List<int> wids = rows.Select(r => r.WaiverId).Distinct().ToList(), cids = rows.Select(r => r.CaseId).Distinct().ToList();
                Dictionary<int, string> waivers = await _uow.Set<EnWaiver>().AsNoTracking().Where(x => wids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Number, ct);
                Dictionary<int, string> cases = await _uow.Set<EnCase>().AsNoTracking().Where(x => cids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Number, ct);
                return rows.Select(d => DebtDto(d, names, waivers, cases, today)).ToList();
            }, ct));
        }

        public async Task<ResponseDto<List<EnDistributionDto>>> GetDistributionsAsync(int? handoverId, bool onlyOpen, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<List<EnDistributionDto>>(EnText.NotAllowed);
            Who w = await WhoAsync(ct);
            IQueryable<EnDistribution> q = _uow.Set<EnDistribution>().AsNoTracking().Where(d => !d.Deleted);
            if (handoverId.HasValue) q = q.Where(d => d.HandoverId == handoverId.Value);
            if (onlyOpen) q = q.Where(d => d.AcknowledgedAt == null);
            List<EnDistribution> rows = await q.OrderBy(d => d.HandoverId).ThenBy(d => d.Unit).Take(500).ToListAsync(ct);
            List<int> hids = rows.Select(r => r.HandoverId).Distinct().ToList();
            var heads = await _uow.Set<EnHandover>().AsNoTracking().Where(h => hids.Contains(h.Id)).Select(h => new { h.Id, h.Number, h.Status }).ToListAsync(ct);
            Dictionary<int, string> names = await EnUsers.NamesAsync(_uow, rows.Select(r => r.AcknowledgedByUserId), ct);
            return ResponseDtoHelper.Ok(rows.Select(d => { var head = heads.FirstOrDefault(x => x.Id == d.HandoverId); return DistDto(d, head?.Number, head == null ? EnHandoverStatus.Void : head.Status, names, w); }).ToList());
        }

        // ---------------------------------------------------------------- the waiver

        public async Task<ResponseDto<EnWaiverDto>> RequestWaiverAsync(EnWaiverRequestDto dto, CancellationToken ct)
        {
            if (dto == null) return EnText.Bad<EnWaiverDto>(EnText.InvalidData);
            Who w = await WhoAsync(ct);
            EnHandover h = await _uow.Set<EnHandover>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.HandoverId && !x.Deleted, ct);
            if (h == null) return EnText.NotFound<EnWaiverDto>(EnText.NotFoundHandover);
            EnCase c = await _uow.Set<EnCase>().AsNoTracking().FirstAsync(x => x.Id == h.CaseId, ct);
            await RefreshLinesAsync(h, c, ct);
            LockState ls = await LockAsync(h, c, ct);
            EnWaiver live = await LiveWaiverAsync(h.Id, ct);
            string why = WaiverRequestReason(h, w, ls, live);
            string reason = EnText.Clean(dto.Reason);
            why ??= EnText.ReasonShort(reason, EnRules.WaiverReasonMin, "دلیل معافیت") ?? EnText.Over(reason, 2000, "دلیل معافیت");
            if (why != null) return EnText.Bad<EnWaiverDto>(why);
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(_uow, ct);
            int maxHours = Math.Min(72, (int)settings[EnSettingKeys.WaiverMaxDebtHours]);   // the ceiling of the employer is 72 hours, whatever the setting says
            DateTimeOffset now = DateTimeOffset.Now;
            DateTimeOffset due = dto.DebtDueAt;
            if (due <= now) return EnText.Bad<EnWaiverDto>("مهلت تسویهٔ بدهی مدارک (تاریخ و ساعت) باید در آینده باشد.");
            if (due > now.AddHours(maxHours)) return EnText.Bad<EnWaiverDto>("مهلت تسویهٔ بدهی مدارک بیش از " + EnText.Num(maxHours) + " ساعت آینده نمی‌شود (سقف ۷۲ ساعت).");
            EnWaiver waiver = new EnWaiver
            {
                HandoverId = h.Id, CaseId = h.CaseId, Reason = reason, DebtDueAt = due, Status = EnWaiverStatus.Requested, RequestedByUserId = Me, RequestedAt = now,
                MissingSnapshot = JsonSerializer.Serialize(new EnHandoverRules.Snapshot { Lines = ls.Keys, DossierUnconfirmed = ls.DossierUnconfirmed }), CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
            };
            waiver.Number = await EnNumbers.NextAsync(_uow, EnNumbers.Waiver, "EWV", now.DateTime, ct);
            _uow.Set<EnWaiver>().Add(waiver);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return EnText.Bad<EnWaiverDto>("برای این تحویل هم‌زمان معافیت دیگری درخواست شد."); }
            await OfficeGates.SyncAsync(_notify, "EN-WAIVER-" + waiver.Id, true, "Engineering", nameof(EnWaiver), waiver.Id, "تصمیم دربارهٔ معافیت اضطراری " + waiver.Number, "/engineering/waivers/" + waiver.Id, EnRoles.Ceo, ct, null, OfInboxKind.Approval);
            return await GetWaiverAsync(waiver.Id, ct);
        }

        public async Task<ResponseDto<EnWaiverDto>> DecideWaiverAsync(EnWaiverDecisionDto dto, CancellationToken ct)
        {
            if (dto == null) return EnText.Bad<EnWaiverDto>(EnText.InvalidData);
            string decision = (dto.Decision ?? string.Empty).Trim().ToLowerInvariant();
            if (decision != "approve" && decision != "reject") return EnText.Bad<EnWaiverDto>("تصمیم نامعتبر است (approve یا reject).");
            Who who = await WhoAsync(ct);
            EnWaiver waiver = await _uow.Set<EnWaiver>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (waiver == null) return EnText.NotFound<EnWaiverDto>("معافیت پیدا نشد.");
            if (!EnConcurrency.Apply(_uow, waiver, dto.RowVersion)) return EnText.Bad<EnWaiverDto>(EnText.InvalidData);
            DateTimeOffset now = DateTimeOffset.Now;
            string why = DecideReason(waiver, who, now);
            string note = EnText.Clean(dto.Note);
            if (why == null && decision == "reject") why = EnText.ReasonShort(note, EnRules.ReasonMin, "دلیل رد");
            why ??= EnText.Over(note, 1000, "توضیح");
            if (why != null) return EnText.Bad<EnWaiverDto>(why);
            waiver.Status = decision == "approve" ? EnWaiverStatus.Approved : EnWaiverStatus.Rejected; waiver.DecidedByUserId = Me; waiver.DecidedAt = now; waiver.DecisionNote = note;
            if (decision == "approve" && dto.DebtDueAt.HasValue)
            {
                if (dto.DebtDueAt.Value >= waiver.DebtDueAt) return EnText.Bad<EnWaiverDto>("مدیرعامل فقط می‌تواند مهلت را کوتاه‌تر کند، نه بیشتر از مهلت درخواست‌شده.");
                if (dto.DebtDueAt.Value <= now) return EnText.Bad<EnWaiverDto>("مهلت تسویهٔ بدهی مدارک باید در آینده باشد.");
                waiver.DebtDueAt = dto.DebtDueAt.Value;
            }
            waiver.ModifyDate = now; waiver.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnWaiverDto>(EnText.Concurrency); }
            await OfficeGates.SyncAsync(_notify, "EN-WAIVER-" + waiver.Id, false, "Engineering", nameof(EnWaiver), waiver.Id, "تصمیم دربارهٔ معافیت اضطراری " + waiver.Number, "/engineering/waivers/" + waiver.Id, EnRoles.Ceo, ct, null, OfInboxKind.Approval);
            return await GetWaiverAsync(waiver.Id, ct);
        }

        // ---------------------------------------------------------------- the handover

        private async Task RefreshLinesAsync(EnHandover h, EnCase c, CancellationToken ct)
        {
            if (h.Status != EnHandoverStatus.Draft) return;
            List<EnSourceLine> sources = await EnHandoverRules.SourcesAsync(_uow, c, ct);
            HashSet<EnHandoverLine> used = new HashSet<EnHandoverLine>();
            int seq = h.Lines.Count == 0 ? 0 : h.Lines.Max(l => l.Seq);
            foreach (EnSourceLine s in sources)
            {
                EnHandoverLine line = h.Lines.FirstOrDefault(l => !used.Contains(l) && ((s.PlatformDocumentId.HasValue && l.PlatformDocumentId == s.PlatformDocumentId) || (!s.PlatformDocumentId.HasValue && !l.PlatformDocumentId.HasValue && EnReferenceRules.Key(l.DocumentType) + "|" + EnHash.Norm(l.Discipline).ToUpperInvariant() + "|" + EnHash.Norm(l.Title).ToUpperInvariant() == s.Key)));
                if (line == null)
                {
                    line = new EnHandoverLine { Handover = h, Seq = ++seq, DocumentType = s.Type, Discipline = s.Discipline, Title = s.Title, Required = s.Required };
                    h.Lines.Add(line);
                }
                used.Add(line);
                line.Required = s.Required;
                EnHandoverRules.Apply(line, s);
            }
        }

        private async Task<ResponseDto<EnHandoverDto>> DraftAsync(EnHandoverActionDto dto, Who w, DateTimeOffset now, CancellationToken ct)
        {
            if (!w.Writer) return EnText.Bad<EnHandoverDto>(EnText.NotAllowed);
            if (!dto.CaseId.HasValue) return EnText.Bad<EnHandoverDto>(EnText.InvalidData);
            EnCase c = await _uow.Set<EnCase>().FirstOrDefaultAsync(x => x.Id == dto.CaseId.Value && !x.Deleted, ct);
            if (c == null) return EnText.NotFound<EnHandoverDto>(EnText.NotFoundCase);
            if (!c.ContractId.HasValue || (c.Status != EnCaseStatus.Awarded && c.Status != EnCaseStatus.InEngineering)) return EnText.Bad<EnHandoverDto>("تحویل مدارک فقط برای پروندهٔ برنده‌شده و در مهندسی ساخته می‌شود.");
            if (c.Kind == EnCaseKind.Turbine && c.Classification == EnClassification.None) return EnText.Bad<EnHandoverDto>("نوع پرونده هنوز تصویب نشده است.");
            if (await _uow.Set<EnHandover>().AnyAsync(x => x.CaseId == c.Id && !x.Deleted && (x.Status == EnHandoverStatus.Draft || x.Status == EnHandoverStatus.Submitted || x.Status == EnHandoverStatus.Accepted || x.Status == EnHandoverStatus.Released), ct))
                return EnText.Bad<EnHandoverDto>("برای این پرونده تحویل باز یا ابلاغ‌شده وجود دارد.");
            Entities.Model.NasbNiro.ProjectControl.PcProjectCharter charter = await EnCaseRules.CharterAsync(_uow, c.ContractId.Value, ct);
            if (charter != null && !(c.CharterReviewOutcome == EnCharterOutcome.Confirmed && c.CharterHash == EnCaseRules.CharterHash(charter))) return EnText.Bad<EnHandoverDto>("منشور پروژه هنوز توسط مهندسی تایید نشده است.");
            if (string.IsNullOrWhiteSpace(c.ReferenceCode)) return EnText.Bad<EnHandoverDto>("استاندارد پروژه از دیتاشیت کارفرما ثبت نشده است؛ تحویل مدارک بدون آن ساخته نمی‌شود.");
            if ((c.Classification == EnClassification.New || c.Kind != EnCaseKind.Turbine) && await EnReferenceRules.CurrentAsync(_uow, false, ct, EnHandoverRules.ListCode(c)) == null) return EnText.Bad<EnHandoverDto>("فهرست مدارک مرجع تاییدشده‌ای برای این پرونده (" + (EnHandoverRules.ListCode(c) ?? "بدون استاندارد مرجع") + ") وجود ندارد.");
            int round = (await _uow.Set<EnHandover>().Where(x => x.CaseId == c.Id).Select(x => (int?)x.Round).MaxAsync(ct) ?? 0) + 1;
            EnHandover h = new EnHandover { CaseId = c.Id, ContractId = c.ContractId.Value, Round = round, Status = EnHandoverStatus.Draft, PreparedByUserId = Me, EditorUserIds = w.Seat ? null : EnEditors.With(null, Me), CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me };
            h.Number = await EnNumbers.NextAsync(_uow, EnNumbers.Handover, "EHO", now.DateTime, ct);
            _uow.Set<EnHandover>().Add(h);
            await RefreshLinesAsync(h, c, ct);
            c.Status = EnCaseStatus.InEngineering; c.ModifyDate = now; c.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return EnText.Bad<EnHandoverDto>("برای این پرونده هم‌زمان تحویل دیگری ساخته شد."); }
            EnEvents.Add(_uow, c, "handover-drafted", Me, h.Number + " (دور " + EnText.Num(round) + ")");
            await _uow.SaveChangesAsync(ct);
            return await GetHandoverAsync(h.Id, ct);
        }

        public async Task<ResponseDto<EnHandoverDto>> HandoverActionAsync(EnHandoverActionDto dto, CancellationToken ct)
        {
            if (dto == null) return EnText.Bad<EnHandoverDto>(EnText.InvalidData);
            string action = (dto.Action ?? string.Empty).Trim().ToLowerInvariant();
            string[] all = { "draft", "refresh", "submit", "accept", "return", "release", "void", "draft-distribution", "edit-scope", "distribute", "acknowledge" };
            if (!all.Contains(action)) return EnText.Bad<EnHandoverDto>("عمل نامعتبر است (" + string.Join("، ", all) + ").");
            Who w = await WhoAsync(ct);
            DateTimeOffset now = DateTimeOffset.Now;
            if (action == "draft") return await DraftAsync(dto, w, now, ct);
            int? id = dto.Id;
            EnDistribution dist = null;
            if (action == "acknowledge" || action == "edit-scope")
            {
                if (!dto.DistributionId.HasValue) return EnText.Bad<EnHandoverDto>(EnText.InvalidData);
                dist = await _uow.Set<EnDistribution>().FirstOrDefaultAsync(d => d.Id == dto.DistributionId.Value && !d.Deleted, ct);
                if (dist == null) return EnText.NotFound<EnHandoverDto>("ردیف توزیع پیدا نشد.");
                id = dist.HandoverId;
            }
            if (!id.HasValue) return EnText.Bad<EnHandoverDto>(EnText.InvalidData);
            EnHandover h = await _uow.Set<EnHandover>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id.Value && !x.Deleted, ct);
            if (h == null) return EnText.NotFound<EnHandoverDto>(EnText.NotFoundHandover);
            if (action != "acknowledge" && action != "edit-scope" && !EnConcurrency.Apply(_uow, h, dto.RowVersion)) return EnText.Bad<EnHandoverDto>(EnText.InvalidData);
            EnCase c = await _uow.Set<EnCase>().FirstAsync(x => x.Id == h.CaseId, ct);
            string note = EnText.Clean(dto.Note);
            string why;
            switch (action)
            {
                case "refresh":
                    if (!w.Writer) return EnText.Bad<EnHandoverDto>(EnText.NotAllowed);
                    if (h.Status != EnHandoverStatus.Draft) return EnText.Bad<EnHandoverDto>("فقط پیش‌نویس از منابع تازه می‌شود.");
                    await RefreshLinesAsync(h, c, ct);
                    break;
                case "submit":
                {
                    await RefreshLinesAsync(h, c, ct);
                    LockState ls = await LockAsync(h, c, ct);
                    EnWaiver waiver = await _uow.Set<EnWaiver>().FirstOrDefaultAsync(x => x.HandoverId == h.Id && !x.Deleted && x.Status == EnWaiverStatus.Approved && x.UsedAt == null, ct);
                    why = SubmitReason(h, w, ls, waiver, now);
                    if (why != null) return EnText.Bad<EnHandoverDto>(why);
                    h.Status = EnHandoverStatus.Submitted; h.SubmittedByUserId = Me; h.SubmittedAt = now; h.MissingRequiredCount = ls.Missing; h.Complete = !ls.Locked; h.ContentHash = EnHandoverRules.LineHash(h.Lines);
                    if (ls.Locked)
                    {
                        h.WaiverId = waiver.Id; waiver.UsedAt = now; waiver.ModifyDate = now; waiver.ModifierUserId = Me;
                        foreach (EnHandoverLine l in h.Lines.Where(l => l.Required && l.State == EnLineState.Missing))
                            _uow.Set<EnDocumentDebt>().Add(new EnDocumentDebt { WaiverId = waiver.Id, HandoverLineId = l.Id, CaseId = c.Id, ContractId = h.ContractId, ProductId = c.ProductId, DocumentType = l.DocumentType, Title = l.Title, DueAt = waiver.DebtDueAt, Status = EnDebtStatus.Open, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me });
                    }
                    c.Status = EnCaseStatus.HandedOver;
                    EnEvents.Add(_uow, c, "handover-submitted", Me, h.Number + (ls.Locked ? " با معافیت " + waiver.Number : string.Empty));
                    break;
                }
                case "accept":
                    why = await AcceptReasonAsync(h, w, ct);
                    if (why != null) return EnText.Bad<EnHandoverDto>(why);
                    h.Status = EnHandoverStatus.Accepted; h.AcceptedByUserId = Me; h.AcceptedAt = now; c.Status = EnCaseStatus.Accepted;
                    EnEvents.Add(_uow, c, "handover-accepted", Me, h.Number);
                    break;
                case "return":
                    why = await ReturnReasonAsync(h, w, ct) ?? EnText.ReasonShort(note, EnRules.ReasonMin, "دلیل برگشت");
                    if (why != null) return EnText.Bad<EnHandoverDto>(why);
                    await VoidDebtsAsync(h, now, ct);
                    h.Status = EnHandoverStatus.Returned; h.ReturnedByUserId = Me; h.ReturnedAt = now; h.ReturnReason = EnText.Cut(note, 1000); c.Status = EnCaseStatus.InEngineering;
                    EnEvents.Add(_uow, c, "handover-returned", Me, note);
                    break;
                case "release":
                    why = await ReleaseReasonAsync(h, w, ct);
                    if (why != null) return EnText.Bad<EnHandoverDto>(why);
                    h.Status = EnHandoverStatus.Released; h.ReleasedByUserId = Me; h.ReleasedAt = now; c.Status = EnCaseStatus.Released;
                    foreach (EnDistribution row in await _uow.Set<EnDistribution>().Where(d => d.HandoverId == h.Id && !d.Deleted).ToListAsync(ct)) { row.Status = EnDistStatus.Notified; row.NotifiedAt = now; row.ModifyDate = now; row.ModifierUserId = Me; }   // the notice texts are frozen; ENG.DIST.NOTIFY sends them
                    EnEvents.Add(_uow, c, "handover-released", Me, h.Number);
                    break;
                case "void":
                    why = VoidReasonOf(h, w) ?? EnText.ReasonShort(note, EnRules.ReasonMin, "دلیل ابطال");
                    if (why != null) return EnText.Bad<EnHandoverDto>(why);
                    await VoidDebtsAsync(h, now, ct);
                    h.Status = EnHandoverStatus.Void; h.VoidReason = EnText.Cut(note, 1000);
                    EnEvents.Add(_uow, c, "handover-void", Me, note);
                    break;
                case "draft-distribution":
                {
                    if (!w.Writer) return EnText.Bad<EnHandoverDto>(EnText.NotAllowed);
                    if (h.Status != EnHandoverStatus.Accepted) return EnText.Bad<EnHandoverDto>("ردیف‌های توزیع فقط برای تحویل پذیرفته‌شده ساخته می‌شود.");
                    if (await _uow.Set<EnDistribution>().AnyAsync(d => d.HandoverId == h.Id && !d.Deleted, ct)) return EnText.Bad<EnHandoverDto>("ردیف‌های توزیع این تحویل قبلاً ساخته شده است.");
                    string contractCode = await _uow.Set<Entities.Model.NasbNiro.Contracts.ContractEntity>().AsNoTracking().Where(k => k.Id == h.ContractId).Select(k => k.Code).FirstOrDefaultAsync(ct);
                    foreach ((EnUnit unit, string roles) in Units)
                    {
                        string text = EnHandoverRules.ScopeNote(contractCode, c.Number, c.ReferenceCode, unit, h.Lines, out int count);
                        _uow.Set<EnDistribution>().Add(new EnDistribution { HandoverId = h.Id, CaseId = h.CaseId, ContractId = h.ContractId, Unit = unit, Roles = roles, Status = EnDistStatus.Draft, ScopeNote = text, DocumentCount = count, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me });
                    }
                    EnEvents.Add(_uow, c, "distribution-drafted", Me, h.Number);
                    break;
                }
                case "edit-scope":
                {
                    string text = EnText.Clean(dto.ScopeNote);
                    why = EditScopeReason(dist, h.Status, w) ?? (text == null ? "یادداشت ابلاغ واحد نباید خالی باشد." : EnText.Over(text, 2000, "یادداشت ابلاغ"));
                    if (why != null) return EnText.Bad<EnHandoverDto>(why);
                    if (!EnConcurrency.Apply(_uow, dist, dto.RowVersion)) return EnText.Bad<EnHandoverDto>(EnText.InvalidData);
                    dist.ScopeNote = text; dist.ModifyDate = now; dist.ModifierUserId = Me;
                    EnEvents.Add(_uow, c, "distribution-note-edited", Me, EnText.Unit(dist.Unit));
                    break;
                }
                case "distribute":
                {
                    if (!w.Writer) return EnText.Bad<EnHandoverDto>(EnText.NotAllowed);
                    if (h.Status != EnHandoverStatus.Released) return EnText.Bad<EnHandoverDto>("فقط تحویل ابلاغ‌شده توزیع می‌شود.");
                    List<EnDistribution> rows = await _uow.Set<EnDistribution>().Where(d => d.HandoverId == h.Id && !d.Deleted).ToListAsync(ct);
                    if (rows.Any(d => d.NoticeSentAt.HasValue)) return EnText.Bad<EnHandoverDto>("توزیع این تحویل قبلاً انجام شده است.");
                    if (rows.Count == 0)
                    {
                        // a handover released before the per-unit notices existed: the rows are made now, already notified
                        string contractCode = await _uow.Set<Entities.Model.NasbNiro.Contracts.ContractEntity>().AsNoTracking().Where(k => k.Id == h.ContractId).Select(k => k.Code).FirstOrDefaultAsync(ct);
                        foreach ((EnUnit unit, string roles) in Units)
                        {
                            string text = EnHandoverRules.ScopeNote(contractCode, c.Number, c.ReferenceCode, unit, h.Lines, out int count);
                            _uow.Set<EnDistribution>().Add(new EnDistribution { HandoverId = h.Id, CaseId = h.CaseId, ContractId = h.ContractId, Unit = unit, Roles = roles, Status = EnDistStatus.Notified, ScopeNote = text, DocumentCount = count, NotifiedAt = now, NoticeSentAt = now, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me });
                        }
                    }
                    else foreach (EnDistribution d in rows) { d.NoticeSentAt = now; d.ModifyDate = now; d.ModifierUserId = Me; }
                    break;
                }
                default:   // acknowledge
                    why = AckReason(dist, w);
                    if (why != null) return EnText.Bad<EnHandoverDto>(why);
                    if (!EnConcurrency.Apply(_uow, dist, dto.RowVersion)) return EnText.Bad<EnHandoverDto>(EnText.InvalidData);
                    dist.AcknowledgedByUserId = Me; dist.AcknowledgedAt = now; dist.ModifyDate = now; dist.ModifierUserId = Me;
                    break;
            }
            h.ModifyDate = now; h.ModifierUserId = Me; c.ModifyDate = now; c.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnHandoverDto>(EnText.Concurrency); }
            await SyncAsync(h, ct);
            return await GetHandoverAsync(h.Id, ct);
        }

        /// <summary>A returned or voided handover no longer owes its waived documents: the next round has its own waiver and debts.</summary>
        private async Task VoidDebtsAsync(EnHandover h, DateTimeOffset now, CancellationToken ct)
        {
            if (!h.WaiverId.HasValue) return;
            int waiverId = h.WaiverId.Value;
            List<EnDocumentDebt> open = await _uow.Set<EnDocumentDebt>().Where(d => d.WaiverId == waiverId && !d.Deleted && d.Status == EnDebtStatus.Open).ToListAsync(ct);
            foreach (EnDocumentDebt d in open) { d.Status = EnDebtStatus.Void; d.ModifyDate = now; d.ModifierUserId = Me; }
        }

        private async Task SyncAsync(EnHandover h, CancellationToken ct)
        {
            string link = "/engineering/handovers/" + h.Id;
            await OfficeGates.SyncAsync(_notify, "EN-HO-ACCEPT-" + h.Id, h.Status == EnHandoverStatus.Submitted, "Engineering", nameof(EnHandover), h.Id, "پذیرش تحویل مدارک مهندسی " + h.Number, link, EnRoles.PmoSide, ct, null, OfInboxKind.Approval);
            await OfficeGates.SyncAsync(_notify, "EN-HO-RELEASE-" + h.Id, h.Status == EnHandoverStatus.Accepted, "Engineering", nameof(EnHandover), h.Id, "ابلاغ مدارک مهندسی " + h.Number + " به اجرا", link, EnRoles.Pmo, ct, null, OfInboxKind.Approval);
            List<EnDistribution> rows = await _uow.Set<EnDistribution>().AsNoTracking().Where(d => d.HandoverId == h.Id && !d.Deleted).ToListAsync(ct);
            foreach (EnDistribution d in rows.Where(d => d.Status == EnDistStatus.Notified && d.NoticeSentAt.HasValue))
                await OfficeGates.SyncAsync(_notify, "EN-DIST-" + d.Id, !d.AcknowledgedAt.HasValue, "Engineering", nameof(EnDistribution), d.Id, EnText.Cut("دریافت مدارک مهندسی " + h.Number + " (" + EnText.Unit(d.Unit) + "): " + d.ScopeNote, 280), link, d.Roles, ct, null, OfInboxKind.Task);
        }

        // ---------------------------------------------------------------- debts

        public async Task<ResponseDto<int>> SettleDebtsAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Writers)) return EnText.Bad<int>(EnText.NotAllowed);
            DateTimeOffset now = DateTimeOffset.Now;
            List<EnDocumentDebt> open = await _uow.Set<EnDocumentDebt>().Where(d => !d.Deleted && d.Status == EnDebtStatus.Open).OrderBy(d => d.Id).Take(200).ToListAsync(ct);
            int settled = 0;
            foreach (EnDocumentDebt d in open)
            {
                EnHandoverLine line = await _uow.Set<EnHandoverLine>().AsNoTracking().FirstOrDefaultAsync(l => l.Id == d.HandoverLineId, ct);
                if (line == null || !await EnHandoverRules.DebtSourceReadyAsync(_uow, d, line, ct)) continue;
                Guid? file = null;
                if (line.PlatformDocumentId.HasValue) file = await _uow.Set<EnPlatformDocument>().AsNoTracking().Where(x => x.Id == line.PlatformDocumentId.Value).Select(x => x.DocumentId).FirstOrDefaultAsync(ct);
                else
                {
                    string type = EnReferenceRules.Key(line.DocumentType), title = EnHash.Norm(line.Title).ToUpperInvariant();
                    List<PcEngineeringDeliverable> mdr = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(x => x.ContractId == d.ContractId && !x.Deleted).ToListAsync(ct);
                    file = mdr.FirstOrDefault(x => EnReferenceRules.Key(x.DocumentType) == type && EnHash.Norm(x.Title).ToUpperInvariant() == title && EnHandoverRules.DeliverableReady(x))?.DocumentId;
                }
                d.Status = EnDebtStatus.Settled; d.SettledAt = now; d.SettledByUserId = Me; d.SettledDocumentId = file; d.ModifyDate = now; d.ModifierUserId = Me;
                settled++;
            }
            if (settled > 0) await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(settled);
        }
    }
}
