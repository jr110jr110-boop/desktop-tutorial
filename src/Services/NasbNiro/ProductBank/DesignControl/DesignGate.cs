using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
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

        // ------------------------------------------------------------------------------------------ design output classification

        /// <summary>
        /// A deliverable is a design output when it is the drawing or document of a part: it is linked (not unlinked) to an item, a BOM line or a part number. Documents received from the client and documents
        /// that are linked to nothing (project correspondence, procedures of the contract) are controlled by the EDMS review and approval only.
        /// </summary>
        public static async Task<bool> IsDesignOutputAsync(IUnitOfWork uow, PcEngineeringDeliverable d, CancellationToken ct)
        {
            if (d == null || d.ReceivedFromClient) return false;
            return await uow.Set<PcDocumentLink>().AnyAsync(k => !k.Deleted && k.UnlinkReason == null && k.DeliverableId == d.Id
                && (k.Kind == PcDocLinkKind.Item || k.Kind == PcDocLinkKind.BomLine || k.Kind == PcDocLinkKind.PartNumber), ct);
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

        private static async Task<bool> ApprovedPlanExistsAsync(IUnitOfWork uow, int productId, int revision, CancellationToken ct) =>
            await uow.Set<PbDesignPlan>().AsNoTracking().AnyAsync(p => !p.Deleted && p.ProductId == productId && p.ProductRevision == revision && p.Status == DesignPlanStatus.Approved, ct);

        /// <summary>Whether a BOM revision is covered by a closed Accepted review of the type (Verification or Validation), and what is missing.</summary>
        public static async Task<DesignCoverage> BomCoverageAsync(IUnitOfWork uow, PbBomHeader h, DesignReviewType type, CancellationToken ct)
        {
            var result = new DesignCoverage { Required = true };
            List<PbDesignReview> reviews = await uow.Set<PbDesignReview>().AsNoTracking().Include(r => r.Outputs)
                .Where(r => !r.Deleted && r.Type == type && r.Status != DesignReviewStatus.Void && r.Outputs.Any(o => !o.Deleted && o.Kind == DesignOutputKind.Bom && o.BomHeaderId == h.Id))
                .OrderBy(r => r.Id).ToListAsync(ct);
            result.Related.AddRange(reviews);
            string what = type == DesignReviewType.Verification ? "تایید طراحی (Verification)" : "صحه‌گذاری طراحی (Validation)";
            foreach (PbDesignReview r in reviews.Where(r => r.Status == DesignReviewStatus.Closed && r.Outcome == DesignReviewOutcome.Accepted))
            {
                PbDesignReviewOutput o = r.Outputs.First(x => !x.Deleted && x.Kind == DesignOutputKind.Bom && x.BomHeaderId == h.Id);
                if (!SameHash(o.ContentHash, h.ContentHash)) { result.Missing.Add($"محتوای BOM بعد از بازبینی {r.Number} تغییر کرده است؛ بازبینی تازه لازم است"); continue; }
                result.Covering = r;
                result.Passed = true;
                result.Missing.Clear();
                return result;
            }
            if (reviews.Count == 0)
                result.Missing.Add($"هیچ بازبینی «{what}» که BOM {h.Code} بازنگری {DesignRules.Rev(h.Revision)} را دربرگیرد ثبت نشده است");
            else
                foreach (PbDesignReview r in reviews.Where(r => !(r.Status == DesignReviewStatus.Closed && r.Outcome == DesignReviewOutcome.Accepted)))
                    result.Missing.Add(StateText(r));
            if (h.ProductId.HasValue && !await ApprovedPlanExistsAsync(uow, h.ProductId.Value, h.Revision, ct))
                result.Missing.Add($"برنامه طراحی تاییدشده برای این محصول و بازنگری {DesignRules.Rev(h.Revision)} وجود ندارد");
            if (!h.ProductId.HasValue)
                result.Missing.Add("این BOM به هیچ محصولی متصل نیست؛ برنامه طراحی برای محصول ساخته می‌شود");
            return result;
        }

        /// <summary>Whether a drawing revision is covered by a closed Accepted verification.</summary>
        public static async Task<DesignCoverage> DrawingCoverageAsync(IUnitOfWork uow, int revisionId, string label, CancellationToken ct)
        {
            var result = new DesignCoverage { Required = true };
            List<PbDesignReview> reviews = await uow.Set<PbDesignReview>().AsNoTracking().Include(r => r.Outputs)
                .Where(r => !r.Deleted && r.Type == DesignReviewType.Verification && r.Status != DesignReviewStatus.Void && r.Outputs.Any(o => !o.Deleted && o.Kind == DesignOutputKind.Drawing && o.RevisionId == revisionId))
                .OrderBy(r => r.Id).ToListAsync(ct);
            result.Related.AddRange(reviews);
            PbDesignReview covering = reviews.FirstOrDefault(r => r.Status == DesignReviewStatus.Closed && r.Outcome == DesignReviewOutcome.Accepted);
            if (covering != null) { result.Covering = covering; result.Passed = true; return result; }
            if (reviews.Count == 0) result.Missing.Add($"هیچ بازبینی «تایید طراحی (Verification)» که {label} را دربرگیرد ثبت نشده است");
            else foreach (PbDesignReview r in reviews) result.Missing.Add(StateText(r));
            return result;
        }

        // ------------------------------------------------------------------------------------------ the gates

        /// <summary>The refusal (Persian) of releasing a product BOM revision, or null when it may be released.</summary>
        public static async Task<string> BomReleaseProblemAsync(IUnitOfWork uow, PbBomHeader h, CancellationToken ct)
        {
            if (!await VerificationRequiredAsync(uow, ct)) return null;
            DesignCoverage c = await BomCoverageAsync(uow, h, DesignReviewType.Verification, ct);
            if (c.Passed) return null;
            return $"BOM {h.Code} بازنگری {DesignRules.Rev(h.Revision)} منتشر نمی‌شود: طبق کنترل طراحی (ISO 9001 بند 8.3.4) پیش از انتشار، بازبینی «تایید طراحی (Verification)» بسته‌شده با نتیجه «پذیرفته» لازم است که این بازنگری BOM را دربرگیرد. کمبود: " + string.Join("؛ ", c.Missing) + ".";
        }

        /// <summary>What is missing of the design control for a released BOM (readiness item RD-DESIGN): the approved plan, the declaration that the inputs are complete, the accepted verification.</summary>
        public static async Task<List<string>> ReadinessProblemsAsync(IUnitOfWork uow, PbBomHeader h, CancellationToken ct)
        {
            var problems = new List<string>();
            string cfg = $"BOM {h.Code} بازنگری {DesignRules.Rev(h.Revision)}";
            if (!h.ProductId.HasValue) { problems.Add(cfg + ": به محصول متصل نیست."); return problems; }
            PbDesignPlan plan = await uow.Set<PbDesignPlan>().AsNoTracking().FirstOrDefaultAsync(p => !p.Deleted && p.ProductId == h.ProductId.Value && p.ProductRevision == h.Revision && p.Status == DesignPlanStatus.Approved, ct);
            if (plan == null) problems.Add($"برنامه طراحی تاییدشده برای این محصول و بازنگری {DesignRules.Rev(h.Revision)} وجود ندارد.");
            else
            {
                string incomplete = await DesignQueries.InputsIncompleteReasonAsync(uow, plan, ct);
                if (incomplete != null) problems.Add("ورودی‌های طراحی: " + incomplete);
            }
            string conflicts = await DesignQueries.ConflictTextAsync(uow, h.ProductId.Value, h.Revision, ct);
            if (conflicts != null) problems.Add(conflicts);
            DesignCoverage c = await BomCoverageAsync(uow, h, DesignReviewType.Verification, ct);
            if (!c.Passed) problems.AddRange(c.Missing.Where(m => !m.StartsWith("برنامه طراحی تاییدشده")).Select(m => cfg + ": " + m));
            return problems;
        }

        /// <summary>The refusal (Persian) of releasing a revision of a design-output drawing, or null (not a design output, or covered).</summary>
        public static async Task<string> DrawingReleaseProblemAsync(IUnitOfWork uow, PcEngineeringDeliverable d, PcDocumentRevision r, CancellationToken ct)
        {
            if (!await VerificationRequiredAsync(uow, ct)) return null;
            if (!await IsDesignOutputAsync(uow, d, ct)) return null;
            string label = $"نقشه {d.DocumentNumber} بازنگری {r.Code}";
            DesignCoverage c = await DrawingCoverageAsync(uow, r.Id, label, ct);
            if (c.Passed) return null;
            return $"{label} منتشر نمی‌شود: خروجی طراحی (نقشه قطعه) پیش از انتشار به بازبینی «تایید طراحی (Verification)» بسته‌شده با نتیجه «پذیرفته» نیاز دارد که این بازنگری را دربرگیرد. کمبود: " + string.Join("؛ ", c.Missing) + ".";
        }

        // ------------------------------------------------------------------------------------------ functional configuration audit

        public static async Task<DesignFcaResult> FcaAsync(IUnitOfWork uow, int workOrderId, CancellationToken ct)
        {
            var result = new DesignFcaResult();
            Dictionary<string, decimal> st = await SettingsAsync(uow, ct);
            if (st[DesignSettingKeys.VerificationRequired] < 1) { result.NotApplicableReason = "کنترل طراحی در تنظیمات غیرفعال است."; return result; }
            PbProjectBaseline baseline = await uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => b.WorkOrderId == workOrderId && b.IsCurrent && !b.Deleted).OrderByDescending(b => b.Id).FirstOrDefaultAsync(ct);
            if (baseline == null) { result.NotApplicableReason = "این دستور کار BOM پروژه (baseline) ندارد؛ نبود آن را ممیزی پیکربندی فیزیکی می‌گیرد."; return result; }
            PbBomHeader bom = await uow.Set<PbBomHeader>().AsNoTracking().FirstOrDefaultAsync(h => h.Id == baseline.BomHeaderId, ct);
            if (bom == null) { result.NotApplicableReason = "BOM مبنای پروژه پیدا نشد."; return result; }
            result.Applicable = true;
            result.Bom = bom;
            string cfg = $"BOM {bom.Code} بازنگری {DesignRules.Rev(bom.Revision)}";
            DesignCoverage ver = await BomCoverageAsync(uow, bom, DesignReviewType.Verification, ct);
            if (ver.Passed) result.Reviews.Add(ver.Covering);
            else result.Problems.Add($"{cfg}: تایید طراحی (Verification) بسته‌شده و پذیرفته ندارد: " + string.Join("؛ ", ver.Missing));
            bool needValidation = st[DesignSettingKeys.ValidationRequired] >= 1;
            if (needValidation && ver.Passed)
            {
                PbDesignPlan plan = await uow.Set<PbDesignPlan>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == ver.Covering.PlanId, ct);
                if (plan != null && !plan.ValidationRequired) needValidation = false;
            }
            if (needValidation)
            {
                DesignCoverage val = await BomCoverageAsync(uow, bom, DesignReviewType.Validation, ct);
                if (val.Passed) result.Reviews.Add(val.Covering);
                else result.Problems.Add($"{cfg}: صحه‌گذاری طراحی (Validation) بسته‌شده و پذیرفته ندارد: " + string.Join("؛ ", val.Missing));
            }
            return result;
        }
    }
}
