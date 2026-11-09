using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Entities.Model;
using Services.NasbNiro.Quality;

namespace Services.NasbNiro.ProductBank
{
    /// <summary>What a closed design review says about one output (a BOM revision, a drawing revision).</summary>
    public sealed class DesignCoverage
    {
        public bool Required { get; set; }
        public bool Passed { get; set; }

        /// <summary>Persian: what is missing (empty when passed).</summary>
        public List<string> Missing { get; } = new List<string>();

        /// <summary>The closed Accepted review of the wanted type that covers the output, if any.</summary>
        public PbDesignReview Covering { get; set; }

        /// <summary>Every review (not void) of the wanted type that mentions the output.</summary>
        public List<PbDesignReview> Related { get; } = new List<PbDesignReview>();

        /// <summary>Every closed Accepted review whose cells together cover the output (a supplementary verification restores a stale one).</summary>
        public List<PbDesignReview> Coverers { get; } = new List<PbDesignReview>();

        /// <summary>The accepted adoption of an existing (legacy) design that stands in for the review.</summary>
        public PbDesignAdoption Adoption { get; set; }

        /// <summary>A closed review exists but the inputs, the files or the content changed after it.</summary>
        public bool Stale { get; set; }
    }

    /// <summary>What the readiness item RD-DESIGN says about the released BOM of a product.</summary>
    public sealed class DesignReadinessResult
    {
        public List<string> Problems { get; } = new List<string>();

        /// <summary>The BOM was released before the design control started and has no adoption: the item is Soft with the note.</summary>
        public bool Legacy { get; set; }
    }

    /// <summary>The parts, items, lines and BOM revisions of the BOM tree of one product revision.</summary>
    public sealed class DesignStructure
    {
        public HashSet<int> HeaderIds { get; } = new HashSet<int>();
        public HashSet<int> ItemIds { get; } = new HashSet<int>();
        public HashSet<int> LineIds { get; } = new HashSet<int>();
        public HashSet<string> PartNumbers { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The functional configuration audit of one work order (ISO 10007 6.5).</summary>
    public sealed class DesignFcaResult
    {
        public bool Applicable { get; set; }
        public string NotApplicableReason { get; set; }
        public List<string> Problems { get; } = new List<string>();
        public bool Passed => !Applicable || Problems.Count == 0;
        public PbBomHeader Bom { get; set; }
        public List<PbDesignReview> Reviews { get; } = new List<PbDesignReview>();

        /// <summary>The project BOM was released before the design control started: shown as not applicable WITH this note, never silently.</summary>
        public bool Legacy { get; set; }
        public string LegacyNote { get; set; }
    }

    /// <summary>
    /// Package A1: the gates that rest on a closed design verification. One place decides, so that the BOM release, the drawing release, the engineering readiness, the functional configuration audit and the
    /// dossier page say the same thing. Only a review that is Closed with outcome Accepted counts; a review that was Accepted with conditions, Rejected, Held or Planned never does.
    /// </summary>
    public static class DesignGate
    {
        public static async Task<Dictionary<string, decimal>> SettingsAsync(IUnitOfWork uow, CancellationToken ct) => await QualitySettingKeys.LoadAsync(uow, ct);

        public static async Task<bool> VerificationRequiredAsync(IUnitOfWork uow, CancellationToken ct) => (await SettingsAsync(uow, ct))[DesignSettingKeys.VerificationRequired] >= 1;

        private static bool InForce(PcDocStatus s) => s == PcDocStatus.Released || s == PcDocStatus.AsBuilt;

        // ------------------------------------------------------------------------------------------ the cut-over (legacy revisions)

        /// <summary>The Shamsi number yyyyMMdd of the setting as a day, or null when it is 0 or not a valid day.</summary>
        public static DateTime? ShamsiDay(decimal value)
        {
            int v = (int)decimal.Truncate(value);
            if (v <= 0) return null;
            int y = v / 10000, m = v / 100 % 100, d = v % 100;
            try { return new PersianCalendar().ToDateTime(y, m, d, 0, 0, 0, 0).Date; } catch (ArgumentOutOfRangeException) { return null; }
        }

        public static string ShamsiText(DateTime day)
        {
            var pc = new PersianCalendar();
            return pc.GetYear(day).ToString("0000", CultureInfo.InvariantCulture) + "/" + pc.GetMonth(day).ToString("00", CultureInfo.InvariantCulture) + "/" + pc.GetDayOfMonth(day).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>The day the design control started: the setting when it is set, else the day recorded by the first gate decision, else today (nothing has been recorded yet).</summary>
        public static async Task<DateTime> ControlStartAsync(IUnitOfWork uow, CancellationToken ct)
        {
            DateTime? set = ShamsiDay((await SettingsAsync(uow, ct))[DesignSettingKeys.ControlStartDate]);
            if (set.HasValue) return set.Value;
            DateTime? recorded = await uow.Set<PbDesignActivation>().AsNoTracking().Where(a => !a.Deleted).OrderBy(a => a.Id).Select(a => (DateTime?)a.StartDate).FirstOrDefaultAsync(ct);
            return (recorded ?? DateTime.Today).Date;
        }

        /// <summary>Records the start day once (in the transaction of the decision that first needs it). Nothing is saved here.</summary>
        public static async Task EnsureActivationAsync(IUnitOfWork uow, CancellationToken ct)
        {
            if (await uow.Set<PbDesignActivation>().AnyAsync(a => !a.Deleted, ct)) return;
            DateTimeOffset now = DateTimeOffset.Now;
            uow.Set<PbDesignActivation>().Add(new PbDesignActivation { StartDate = now.Date, Note = "ثبت خودکار در نخستین تصمیم گیت طراحی", CreateDate = now, ModifyDate = now });
        }

        public static bool IsLegacy(PbBomHeader h, DateTime start) => h.ReleasedAt.HasValue && h.ReleasedAt.Value.Date < start.Date;

        /// <summary>The accepted adoption of this exact BOM content, if any.</summary>
        public static async Task<PbDesignAdoption> AcceptedAdoptionAsync(IUnitOfWork uow, PbBomHeader h, CancellationToken ct)
        {
            PbDesignAdoption a = await uow.Set<PbDesignAdoption>().AsNoTracking().FirstOrDefaultAsync(x => !x.Deleted && x.BomHeaderId == h.Id && x.Status == DesignAdoptionStatus.Accepted, ct);
            return a != null && SameHash(a.ContentHash, h.ContentHash) ? a : null;
        }

        // ------------------------------------------------------------------------------------------ design output classification

        /// <summary>
        /// A deliverable is a design output when it is, or ever was, the drawing or document of a part: a link to an item, a BOM line or a part number exists in its history. The status is STICKY (fix round 1, H2):
        /// removing the link (a second person, with a reason) does not clear it, so unlink - release - relink is no way round the verification. Documents received from the client and documents that never had such a link
        /// (project correspondence, procedures of the contract) are controlled by the EDMS review and approval only.
        /// </summary>
        public static async Task<bool> IsDesignOutputAsync(IUnitOfWork uow, PcEngineeringDeliverable d, CancellationToken ct)
        {
            if (d == null || d.ReceivedFromClient) return false;
            return await uow.Set<PcDocumentLink>().IgnoreQueryFilters().AnyAsync(k => k.DeliverableId == d.Id
                && (k.Kind == PcDocLinkKind.Item || k.Kind == PcDocLinkKind.BomLine || k.Kind == PcDocLinkKind.PartNumber), ct);
        }

        // ------------------------------------------------------------------------------------------ product structure and fingerprints

        /// <summary>The BOM tree of a product revision (the revision in preparation counts too): its BOM revisions, lines, items and part numbers.</summary>
        public static async Task<DesignStructure> StructureAsync(IUnitOfWork uow, int productId, int revision, CancellationToken ct)
        {
            var s = new DesignStructure();
            List<PbBomHeader> roots = await uow.Set<PbBomHeader>().AsNoTracking().Where(h => !h.Deleted && h.ProductId == productId && h.Revision == revision && h.Status != PbBomStatus.Obsolete && h.Status != PbBomStatus.Superseded).ToListAsync(ct);
            var level = new List<int>();
            void Add(PbBomHeader h)
            {
                if (!s.HeaderIds.Add(h.Id)) return;
                level.Add(h.Id);
                if (!string.IsNullOrWhiteSpace(h.AssemblyPartNumber)) s.PartNumbers.Add(h.AssemblyPartNumber.Trim());
                if (h.ProductItemId.HasValue) s.ItemIds.Add(h.ProductItemId.Value);
            }
            foreach (PbBomHeader h in roots) Add(h);
            int guard = 0;
            while (level.Count > 0 && guard++ < 40)
            {
                List<int> ids = level; level = new List<int>();
                List<PbBomLine> lines = await uow.Set<PbBomLine>().AsNoTracking().Where(l => ids.Contains(l.BomHeaderId) && !l.Deleted && !l.IsHeading).ToListAsync(ct);
                var children = new List<int>();
                foreach (PbBomLine l in lines)
                {
                    s.LineIds.Add(l.Id);
                    if (!string.IsNullOrWhiteSpace(l.PartNumber)) s.PartNumbers.Add(l.PartNumber.Trim());
                    if (l.ItemId.HasValue) s.ItemIds.Add(l.ItemId.Value);
                    if (l.ChildBomId.HasValue && !s.HeaderIds.Contains(l.ChildBomId.Value)) children.Add(l.ChildBomId.Value);
                }
                // a sub-assembly is found by its link (ChildBomId) or, while it is still a draft, by the part number of the line that names it (so a sub-assembly can be verified BEFORE it is released)
                List<string> lineParts = lines.Where(l => !string.IsNullOrWhiteSpace(l.PartNumber)).Select(l => l.PartNumber.Trim()).Distinct().ToList();
                foreach (PbBomHeader h in await uow.Set<PbBomHeader>().AsNoTracking().Where(h => !h.Deleted && h.Status != PbBomStatus.Obsolete && h.Status != PbBomStatus.Superseded && (children.Contains(h.Id) || lineParts.Contains(h.AssemblyPartNumber))).ToListAsync(ct)) Add(h);
            }
            return s;
        }

        /// <summary>Whether the drawing belongs to the structure: it is linked (an active link) to a part of the BOM tree.</summary>
        public static async Task<bool> DrawingInStructureAsync(IUnitOfWork uow, int deliverableId, DesignStructure s, CancellationToken ct)
        {
            List<PcDocumentLink> links = await uow.Set<PcDocumentLink>().AsNoTracking().Where(k => !k.Deleted && k.UnlinkReason == null && k.DeliverableId == deliverableId
                && (k.Kind == PcDocLinkKind.Item || k.Kind == PcDocLinkKind.BomLine || k.Kind == PcDocLinkKind.PartNumber)).ToListAsync(ct);
            foreach (PcDocumentLink k in links)
            {
                if (k.Kind == PcDocLinkKind.Item && k.ItemId.HasValue)
                {
                    if (s.ItemIds.Contains(k.ItemId.Value)) return true;
                    // a draft BOM has no item ids on its lines yet (they are set when it is submitted): the item is then found by its code or part number
                    var item = await uow.Set<global::Entities.Model.NasbNiro.Inventory.InventoryItem>().AsNoTracking().Where(i => i.Id == k.ItemId.Value).Select(i => new { i.Code, i.PartNumber }).FirstOrDefaultAsync(ct);
                    if (item != null && ((!string.IsNullOrWhiteSpace(item.Code) && s.PartNumbers.Contains(item.Code.Trim())) || (!string.IsNullOrWhiteSpace(item.PartNumber) && s.PartNumbers.Contains(item.PartNumber.Trim())))) return true;
                }
                if (k.Kind == PcDocLinkKind.BomLine && k.BomLineId.HasValue && s.LineIds.Contains(k.BomLineId.Value)) return true;
                if (k.Kind == PcDocLinkKind.PartNumber && !string.IsNullOrWhiteSpace(k.PartNumber) && s.PartNumbers.Contains(k.PartNumber.Trim())) return true;
            }
            return false;
        }

        /// <summary>The fingerprint of a drawing revision (fix round 1, H3): SHA-256 over the sorted file names, roles and content hashes (the stored SHA-256 of the file, else its identity) of the files it holds now.</summary>
        public static async Task<string> DrawingFingerprintAsync(IUnitOfWork uow, int revisionId, CancellationToken ct)
        {
            var files = await uow.Set<PcDocumentRevisionFile>().AsNoTracking().Where(f => !f.Deleted && f.RevisionId == revisionId).Select(f => new { f.FileName, f.Role, f.DocumentId }).ToListAsync(ct);
            List<Guid> ids = files.Select(f => f.DocumentId).Distinct().ToList();
            Dictionary<Guid, string> hashes = (await uow.Set<Document>().AsNoTracking().Where(d => ids.Contains(d.Id)).Select(d => new { d.Id, d.Sha256 }).ToListAsync(ct)).ToDictionary(x => x.Id, x => x.Sha256);
            IEnumerable<string> lines = files.Select(f => $"{f.FileName}|{(int)f.Role}|{(hashes.TryGetValue(f.DocumentId, out string h) && !string.IsNullOrEmpty(h) ? h : f.DocumentId.ToString("N"))}").OrderBy(x => x, StringComparer.Ordinal);
            return PbText.Sha256(string.Join("\n", lines));
        }

        // ------------------------------------------------------------------------------------------ authors

        private static readonly PcDocEventKind[] MakerKinds = { PcDocEventKind.RevisionCreated, PcDocEventKind.FileAdded, PcDocEventKind.FileRemoved, PcDocEventKind.Submitted, PcDocEventKind.Received };

        /// <summary>The users who made an output: the preparer and creator of a BOM or OPC revision; for a drawing revision also everyone who added or removed a file, sent it for review or received it.</summary>
        public static async Task<HashSet<int>> AuthorsAsync(IUnitOfWork uow, DesignOutputKind kind, int? revisionId, int? bomHeaderId, int? opcRoutingId, CancellationToken ct)
        {
            var set = new HashSet<int>();
            if (kind == DesignOutputKind.Bom && bomHeaderId.HasValue)
            {
                var h = await uow.Set<PbBomHeader>().AsNoTracking().Where(x => x.Id == bomHeaderId.Value).Select(x => new { x.PreparedByUserId, x.CreatorUserId }).FirstOrDefaultAsync(ct);
                if (h != null) { set.Add(h.PreparedByUserId); set.Add(h.CreatorUserId); }
            }
            else if (kind == DesignOutputKind.Opc && opcRoutingId.HasValue)
            {
                var h = await uow.Set<PbOpcRouting>().AsNoTracking().Where(x => x.Id == opcRoutingId.Value).Select(x => new { x.PreparedByUserId, x.CreatorUserId }).FirstOrDefaultAsync(ct);
                if (h != null) { set.Add(h.PreparedByUserId); set.Add(h.CreatorUserId); }
            }
            else if (kind == DesignOutputKind.Drawing && revisionId.HasValue)
            {
                var r = await uow.Set<PcDocumentRevision>().AsNoTracking().Where(x => x.Id == revisionId.Value).Select(x => new { x.PreparedByUserId, x.CreatorUserId }).FirstOrDefaultAsync(ct);
                if (r != null) { set.Add(r.PreparedByUserId); set.Add(r.CreatorUserId); }
                foreach (int u in await uow.Set<PcDocumentEvent>().AsNoTracking().Where(e => e.RevisionId == revisionId.Value && MakerKinds.Contains(e.Kind)).Select(e => e.UserId).Distinct().ToListAsync(ct)) set.Add(u);
            }
            set.Remove(0);
            return set;
        }

        // ------------------------------------------------------------------------------------------ coverage of a BOM revision / a drawing revision

        private static bool SameHash(string a, string b) => string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b) || string.Equals(a, b, StringComparison.Ordinal);

        private static string StateText(PbDesignReview r)
        {
            string head = $"{r.Number} ({DesignRules.TypeText(r.Type)})";
            switch (r.Status)
            {
                case DesignReviewStatus.Planned: return $"بازبینی {head} هنوز برگزار نشده است";
                case DesignReviewStatus.Held: return $"بازبینی {head} برگزار شده ولی هنوز بسته نشده است (یافته باز، ورودی بدون مدرک یا بدون تایید شرکت‌کننده مستقل)";
                case DesignReviewStatus.Closed:
                    return r.Outcome == DesignReviewOutcome.Accepted ? $"بازبینی {head} پذیرفته شده است"
                        : r.Outcome == DesignReviewOutcome.AcceptedWithConditions ? $"بازبینی {head} فقط «پذیرفته با شرط» بسته شده است؛ این نتیجه گیت را باز نمی‌کند، بازبینی تازه با نتیجه «پذیرفته» لازم است"
                        : $"بازبینی {head} با نتیجه «رد» بسته شده است؛ بازبینی تازه لازم است";
                default: return $"بازبینی {head} باطل است";
            }
        }

        public const string StaleText = "تصدیق طراحی پس از تغییر ورودی‌ها باید تکرار/تکمیل شود";

        private static async Task<bool> ApprovedPlanExistsAsync(IUnitOfWork uow, int productId, int revision, CancellationToken ct) =>
            await uow.Set<PbDesignPlan>().AsNoTracking().AnyAsync(p => !p.Deleted && p.ProductId == productId && p.ProductRevision == revision && p.Status == DesignPlanStatus.Approved, ct);

        private sealed class Qualified
        {
            public PbDesignReview R;
            public PbDesignReviewOutput O;
        }

        /// <summary>
        /// Whether a BOM revision is covered by closed Accepted reviews of the type (Verification or Validation), and what is missing. The coverage is decided now, not when the review closed: every live input of the scope
        /// of the plan must have a PASSED criterion against this very BOM in a review under an approved plan (a supplementary review for the new inputs restores a stale one); an input added, replaced or in conflict
        /// since, or a BOM that changed, leaves it uncovered. A BOM released before the design control started is covered by its accepted adoption.
        /// </summary>
        public static async Task<DesignCoverage> BomCoverageAsync(IUnitOfWork uow, PbBomHeader h, DesignReviewType type, CancellationToken ct)
        {
            var result = new DesignCoverage { Required = true };
            DateTime start = await ControlStartAsync(uow, ct);
            if (IsLegacy(h, start))
            {
                PbDesignAdoption adoption = await AcceptedAdoptionAsync(uow, h, ct);
                if (adoption != null) { result.Adoption = adoption; result.Passed = true; return result; }
            }
            List<PbDesignReview> reviews = await uow.Set<PbDesignReview>().AsNoTracking().Include(r => r.Outputs).Include(r => r.Inputs).Include(r => r.Traces).Include(r => r.Plan)
                .Where(r => !r.Deleted && r.Type == type && r.Status != DesignReviewStatus.Void && r.Outputs.Any(o => !o.Deleted && o.Kind == DesignOutputKind.Bom && o.BomHeaderId == h.Id))
                .OrderBy(r => r.Id).ToListAsync(ct);
            result.Related.AddRange(reviews);
            string what = type == DesignReviewType.Verification ? "تایید طراحی (Verification)" : "صحه‌گذاری طراحی (Validation)";
            string cfg = $"BOM {h.Code} بازنگری {DesignRules.Rev(h.Revision)}";

            var qualified = new List<Qualified>();
            foreach (PbDesignReview r in reviews.Where(r => r.Status == DesignReviewStatus.Closed && r.Outcome == DesignReviewOutcome.Accepted))
            {
                PbDesignReviewOutput o = r.Outputs.First(x => !x.Deleted && x.Kind == DesignOutputKind.Bom && x.BomHeaderId == h.Id);
                if (!SameHash(o.ContentHash, h.ContentHash)) { result.Stale = true; result.Missing.Add($"محتوای {cfg} بعد از بازبینی {r.Number} تغییر کرده است؛ بازبینی تازه لازم است"); continue; }
                if (r.Plan == null || r.Plan.Status != DesignPlanStatus.Approved) { result.Missing.Add($"برنامه طراحی بازبینی {r.Number} دیگر معتبر نیست (جایگزین یا باطل شده)؛ بازبینی تازه زیر برنامه معتبر لازم است"); continue; }
                qualified.Add(new Qualified { R = r, O = o });
            }
            if (qualified.Count == 0)
            {
                if (reviews.Count == 0) result.Missing.Add($"هیچ بازبینی «{what}» که {cfg} را دربرگیرد ثبت نشده است");
                else foreach (PbDesignReview r in reviews.Where(r => !(r.Status == DesignReviewStatus.Closed && r.Outcome == DesignReviewOutcome.Accepted))) result.Missing.Add(StateText(r));
                if (h.ProductId.HasValue && !await ApprovedPlanExistsAsync(uow, h.ProductId.Value, h.Revision, ct))
                    result.Missing.Add($"برنامه طراحی تاییدشده برای این محصول و بازنگری {DesignRules.Rev(h.Revision)} وجود ندارد");
                return result;
            }

            IEnumerable<IGrouping<(int ProductId, int Revision), Qualified>> groups = qualified.GroupBy(q => (q.R.ProductId, q.R.ProductRevision));
            if (h.ProductId.HasValue)
            {
                groups = groups.Where(g => g.Key.ProductId == h.ProductId.Value && g.Key.Revision == h.Revision);
                if (!groups.Any()) { result.Missing.Add("بازبینی‌های بسته‌شده زیر برنامه طراحی محصول یا بازنگری دیگری است، نه این BOM"); return result; }
            }
            var allMissing = new List<string>();
            foreach (var g in groups)
            {
                var missing = new List<string>();
                List<Qualified> active = g.ToList();
                // an input voided after a review closed: that review is stale unless a later accepted review of the group came after the change
                foreach (Qualified q in g)
                {
                    DateTimeOffset closed = q.R.ClosedAt ?? q.R.ModifyDate;
                    List<int> ids = q.R.Inputs.Where(x => !x.Deleted).Select(x => x.InputId).ToList();
                    DateTimeOffset? change = (await uow.Set<PbDesignInput>().AsNoTracking().Where(i => ids.Contains(i.Id) && i.Status == DesignInputStatus.Void && i.ModifyDate > closed).Select(i => (DateTimeOffset?)i.ModifyDate).MaxAsync(ct));
                    if (change.HasValue && !g.Any(x => x.R.Id != q.R.Id && (x.R.ClosedAt ?? x.R.ModifyDate) > change.Value))
                    {
                        active.Remove(q);
                        result.Stale = true;
                        missing.Add($"بازبینی {q.R.Number}: ورودی‌ای که در آن بود پس از بستن باطل شده است؛ {StaleText}");
                    }
                }
                List<PbDesignInput> live = (await DesignQueries.LiveInputsAsync(uow, g.Key.ProductId, g.Key.Revision, false, ct)).Where(i => DesignRules.InScope(type, i.Kind)).ToList();
                var uncovered = new List<string>();
                if (live.Count == 0 && !active.Any(q => q.R.Traces.Any(t => !t.Deleted && t.OutputId == q.O.Id && t.Result == DesignTraceResult.Passed))) uncovered.Add("هیچ ورودی طراحی معتبری برای این دامنه ثبت نشده است");
                foreach (PbDesignInput input in live)
                {
                    if (input.Status == DesignInputStatus.Conflict) { uncovered.Add($"{input.Code} (در تعارض)"); continue; }
                    bool covered = active.Any(q => q.R.Inputs.Any(x => !x.Deleted && x.InputId == input.Id)
                        && q.R.Traces.Any(t => !t.Deleted && t.InputId == input.Id && t.OutputId == q.O.Id && t.Result == DesignTraceResult.Passed)
                        && !q.R.Traces.Any(t => !t.Deleted && t.InputId == input.Id && t.OutputId == q.O.Id && t.Result == DesignTraceResult.Failed));
                    if (!covered) uncovered.Add(input.Code);
                }
                if (uncovered.Count > 0) { result.Stale = true; missing.Add($"{StaleText}: ورودی‌های بدون معیار قبول‌شده برای این BOM: " + string.Join("، ", uncovered)); }
                if (missing.Count == 0)
                {
                    result.Passed = true;
                    result.Missing.Clear();
                    foreach (Qualified q in active.OrderBy(x => x.R.Id)) { result.Coverers.Add(q.R); result.Covering = q.R; }
                    return result;
                }
                allMissing.AddRange(missing);
            }
            result.Missing.AddRange(allMissing);
            return result;
        }

        /// <summary>
        /// Whether a drawing revision is covered by a closed Accepted verification: its files must be the files that were verified (fingerprint), the revision must not have been rejected to draft since, the plan must
        /// still be approved, and the review must hold at least one PASSED criterion for this drawing and no failed one (a drawing listed with no checks is not verified).
        /// </summary>
        public static async Task<DesignCoverage> DrawingCoverageAsync(IUnitOfWork uow, int revisionId, string label, CancellationToken ct)
        {
            var result = new DesignCoverage { Required = true };
            string current = await DrawingFingerprintAsync(uow, revisionId, ct);
            List<PbDesignReview> reviews = await uow.Set<PbDesignReview>().AsNoTracking().Include(r => r.Outputs).Include(r => r.Traces).Include(r => r.Plan)
                .Where(r => !r.Deleted && r.Type == DesignReviewType.Verification && r.Status != DesignReviewStatus.Void && r.Outputs.Any(o => !o.Deleted && o.Kind == DesignOutputKind.Drawing && o.RevisionId == revisionId))
                .OrderBy(r => r.Id).ToListAsync(ct);
            result.Related.AddRange(reviews);
            foreach (PbDesignReview r in reviews)
            {
                if (!(r.Status == DesignReviewStatus.Closed && r.Outcome == DesignReviewOutcome.Accepted)) { result.Missing.Add(StateText(r)); continue; }
                PbDesignReviewOutput o = r.Outputs.First(x => !x.Deleted && x.Kind == DesignOutputKind.Drawing && x.RevisionId == revisionId);
                if (!SameHash(o.ContentHash, current)) { result.Stale = true; result.Missing.Add($"{label} پس از بازبینی {r.Number} تغییر کرده است (فایل‌های بازنگری با فایل‌های بازبینی‌شده یکی نیست)؛ تایید طراحی باید تکرار شود"); continue; }
                if (r.Plan == null || r.Plan.Status != DesignPlanStatus.Approved) { result.Missing.Add($"برنامه طراحی بازبینی {r.Number} دیگر معتبر نیست (جایگزین یا باطل شده)؛ بازبینی تازه زیر برنامه معتبر لازم است"); continue; }
                DateTimeOffset closed = r.ClosedAt ?? r.ModifyDate;
                if (await uow.Set<PcDocumentEvent>().AsNoTracking().AnyAsync(e => e.RevisionId == revisionId && e.Kind == PcDocEventKind.Rejected && e.At > closed, ct))
                { result.Stale = true; result.Missing.Add($"{label} پس از بازبینی {r.Number} رد و به پیش‌نویس برگردانده شده است؛ تایید طراحی باید تکرار شود"); continue; }
                List<PbDesignTrace> cells = r.Traces.Where(t => !t.Deleted && t.OutputId == o.Id).ToList();
                if (cells.Any(t => t.Result == DesignTraceResult.Failed)) { result.Missing.Add($"در بازبینی {r.Number} دست‌کم یک معیار {label} مردود شده است"); continue; }
                if (!cells.Any(t => t.Result == DesignTraceResult.Passed)) { result.Missing.Add($"بازبینی {r.Number} برای {label} هیچ معیار قبول‌شده‌ای ندارد؛ نقشه‌ی بدون بررسی «تایید‌شده» شمرده نمی‌شود"); continue; }
                result.Covering = r; result.Coverers.Add(r); result.Passed = true; result.Missing.Clear();
                return result;
            }
            if (reviews.Count == 0) result.Missing.Add($"هیچ بازبینی «تایید طراحی (Verification)» که {label} را دربرگیرد ثبت نشده است");
            return result;
        }

        // ------------------------------------------------------------------------------------------ the gates

        /// <summary>
        /// THE release check of the design control: exactly one of the two targets is given, a product BOM revision about to be released, or a drawing revision (with its deliverable) about to be released.
        /// Returns the refusal (Persian) or null. The BOM service and the EDMS service call this one method at one line each, so another gate (the engineering change order) combines with it by calling both.
        /// </summary>
        public static async Task<string> CheckReleaseAsync(IUnitOfWork uow, PbBomHeader bom, PcEngineeringDeliverable deliverable, PcDocumentRevision revision, CancellationToken ct)
        {
            await EnsureActivationAsync(uow, ct);
            if (bom != null) return await BomReleaseProblemAsync(uow, bom, ct);
            if (deliverable != null && revision != null) return await DrawingReleaseProblemAsync(uow, deliverable, revision, ct);
            return null;
        }

        private static async Task<string> BomReleaseProblemAsync(IUnitOfWork uow, PbBomHeader h, CancellationToken ct)
        {
            if (!await VerificationRequiredAsync(uow, ct)) return null;
            DesignCoverage c = await BomCoverageAsync(uow, h, DesignReviewType.Verification, ct);
            if (c.Passed) return null;
            return $"BOM {h.Code} بازنگری {DesignRules.Rev(h.Revision)} منتشر نمی‌شود: طبق کنترل طراحی (ISO 9001 بند 8.3.4) پیش از انتشار، بازبینی «تایید طراحی (Verification)» بسته‌شده با نتیجه «پذیرفته» لازم است که این بازنگری BOM را دربرگیرد. کمبود: " + string.Join("؛ ", c.Missing) + ".";
        }

        private static async Task<string> DrawingReleaseProblemAsync(IUnitOfWork uow, PcEngineeringDeliverable d, PcDocumentRevision r, CancellationToken ct)
        {
            if (!await VerificationRequiredAsync(uow, ct)) return null;
            if (!await IsDesignOutputAsync(uow, d, ct)) return null;
            string label = $"نقشه {d.DocumentNumber} بازنگری {r.Code}";
            DesignCoverage c = await DrawingCoverageAsync(uow, r.Id, label, ct);
            if (c.Passed) return null;
            return $"{label} منتشر نمی‌شود: خروجی طراحی (نقشه قطعه) پیش از انتشار به بازبینی «تایید طراحی (Verification)» بسته‌شده با نتیجه «پذیرفته» نیاز دارد که این بازنگری را دربرگیرد. کمبود: " + string.Join("؛ ", c.Missing) + ".";
        }

        /// <summary>
        /// Linking a drawing that is already in force to a part makes it a design output after the fact: the same verification the release asks for must exist now. Null when it does (or when verification is not required).
        /// A client document is not a design output. Used by the link gate of the go-live cut-over; the release path above keeps its own check (it needs the link to exist).
        /// </summary>
        public static async Task<string> LinkProblemAsync(IUnitOfWork uow, PcEngineeringDeliverable d, PcDocumentRevision r, CancellationToken ct)
        {
            if (d == null || r == null || d.ReceivedFromClient) return null;
            if (!await VerificationRequiredAsync(uow, ct)) return null;
            DesignCoverage c = await DrawingCoverageAsync(uow, r.Id, $"نقشه {d.DocumentNumber} بازنگری {r.Code}", ct);
            return c.Passed ? null : "بازبینی «تایید طراحی (Verification)» بسته‌شده با نتیجه «پذیرفته» که این بازنگری را دربرگیرد وجود ندارد (" + string.Join("؛ ", c.Missing) + ")";
        }

        /// <summary>What is missing of the design control for a released BOM (readiness item RD-DESIGN): the approved plan, the declaration that the inputs are complete, the accepted verification. A legacy BOM without an adoption is reported as one Soft note.</summary>
        public static async Task<DesignReadinessResult> ReadinessAsync(IUnitOfWork uow, PbBomHeader h, CancellationToken ct)
        {
            var result = new DesignReadinessResult();
            DateTime start = await ControlStartAsync(uow, ct);
            bool legacy = IsLegacy(h, start);
            string cfg = $"BOM {h.Code} بازنگری {DesignRules.Rev(h.Revision)}";
            if (legacy && await AcceptedAdoptionAsync(uow, h, ct) != null) return result;
            var problems = new List<string>();
            if (!h.ProductId.HasValue) { problems.Add(cfg + ": به محصول متصل نیست."); }
            else
            {
                PbDesignPlan plan = await uow.Set<PbDesignPlan>().AsNoTracking().FirstOrDefaultAsync(p => !p.Deleted && p.ProductId == h.ProductId.Value && p.ProductRevision == h.Revision && p.Status == DesignPlanStatus.Approved, ct);
                if (plan == null) problems.Add($"برنامه طراحی تاییدشده برای این محصول و بازنگری {DesignRules.Rev(h.Revision)} وجود ندارد.");
                else
                {
                    string incomplete = await DesignQueries.InputsIncompleteReasonAsync(uow, plan, ct);
                    if (incomplete != null) problems.Add("ورودی‌های طراحی: " + incomplete);
                }
                string conflicts = await DesignQueries.ConflictTextAsync(uow, h.ProductId.Value, h.Revision, ct);
                if (conflicts != null) problems.Add(conflicts);
            }
            DesignCoverage c = await BomCoverageAsync(uow, h, DesignReviewType.Verification, ct);
            if (!c.Passed) problems.AddRange(c.Missing.Where(m => !m.StartsWith("برنامه طراحی تاییدشده")).Select(m => cfg + ": " + m));
            if (problems.Count == 0) return result;
            if (legacy)
            {
                result.Legacy = true;
                result.Problems.Add($"{cfg}: پیش از آغاز کنترل طراحی ({ShamsiText(start)}) منتشر شده است؛ «قدیمی، بدون تایید طراحی». پذیرش طراحی موجود (QA و مهندسی) ثبت نشده است؛ بازنگری تازه تایید کامل طراحی می‌خواهد.");
                return result;
            }
            result.Problems.AddRange(problems);
            return result;
        }

        // ------------------------------------------------------------------------------------------ functional configuration audit

        public static async Task<DesignFcaResult> FcaAsync(IUnitOfWork uow, int workOrderId, CancellationToken ct)
        {
            var result = new DesignFcaResult();
            Dictionary<string, decimal> st = await SettingsAsync(uow, ct);
            bool needVerification = st[DesignSettingKeys.VerificationRequired] >= 1;
            bool needValidation = st[DesignSettingKeys.ValidationRequired] >= 1;
            if (!needVerification && !needValidation) { result.NotApplicableReason = "کنترل طراحی در تنظیمات غیرفعال است."; return result; }
            PbProjectBaseline baseline = await uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => b.WorkOrderId == workOrderId && b.IsCurrent && !b.Deleted).OrderByDescending(b => b.Id).FirstOrDefaultAsync(ct);
            if (baseline == null) { result.NotApplicableReason = "این دستور کار BOM پروژه (baseline) ندارد؛ نبود آن را ممیزی پیکربندی فیزیکی می‌گیرد."; return result; }
            PbBomHeader bom = await uow.Set<PbBomHeader>().AsNoTracking().FirstOrDefaultAsync(h => h.Id == baseline.BomHeaderId, ct);
            if (bom == null) { result.NotApplicableReason = "BOM مبنای پروژه پیدا نشد."; return result; }
            string cfg = $"BOM {bom.Code} بازنگری {DesignRules.Rev(bom.Revision)}";
            DateTime start = await ControlStartAsync(uow, ct);
            if (IsLegacy(bom, start))
            {
                PbDesignAdoption adoption = await AcceptedAdoptionAsync(uow, bom, ct);
                result.Bom = bom;
                result.Legacy = true;
                if (adoption != null) { result.Applicable = true; result.LegacyNote = $"{cfg} پیش از آغاز کنترل طراحی ({ShamsiText(start)}) منتشر شده و طراحی موجود آن پذیرفته شده است (مدرک: {adoption.EvidenceRef})."; return result; }
                result.NotApplicableReason = $"قدیمی، بدون تایید طراحی: {cfg} پیش از آغاز کنترل طراحی ({ShamsiText(start)}) منتشر شده و «پذیرش طراحی موجود» ثبت نشده است؛ این دستور کار مسدود نمی‌شود ولی ممیزی عملکردی آن انجام نشده است.";
                result.LegacyNote = result.NotApplicableReason;
                return result;
            }
            result.Applicable = true;
            result.Bom = bom;
            if (needVerification)
            {
                DesignCoverage ver = await BomCoverageAsync(uow, bom, DesignReviewType.Verification, ct);
                if (ver.Passed) foreach (PbDesignReview r in ver.Coverers) result.Reviews.Add(r);
                else result.Problems.Add($"{cfg}: تایید طراحی (Verification) بسته‌شده و پذیرفته ندارد: " + string.Join("؛ ", ver.Missing));
            }
            if (needValidation)
            {
                // the waiver is read from the plan in force now, not from the plan of an old review
                PbDesignPlan inForce = bom.ProductId.HasValue ? await uow.Set<PbDesignPlan>().AsNoTracking().FirstOrDefaultAsync(p => !p.Deleted && p.ProductId == bom.ProductId.Value && p.ProductRevision == bom.Revision && p.Status == DesignPlanStatus.Approved, ct) : null;
                bool waived = inForce != null && !inForce.ValidationRequired;
                if (!waived)
                {
                    DesignCoverage val = await BomCoverageAsync(uow, bom, DesignReviewType.Validation, ct);
                    if (val.Passed) foreach (PbDesignReview r in val.Coverers) result.Reviews.Add(r);
                    else result.Problems.Add($"{cfg}: صحه‌گذاری طراحی (Validation) بسته‌شده و پذیرفته ندارد: " + string.Join("؛ ", val.Missing));
                }
            }
            return result;
        }
    }
}
