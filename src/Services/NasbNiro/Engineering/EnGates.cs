using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.Engineering
{
    /// <summary>What a gate says about one inquiry or contract: not controlled, legacy (passes with a visible note), or controlled with a case and a verdict.</summary>
    public sealed class EnGateResult
    {
        public bool Controlled { get; set; }
        public bool Legacy { get; set; }
        public string LegacyNote { get; set; }
        public EnCase Case { get; set; }
        public bool Passed { get; set; }
        public string Reason { get; set; }
    }

    internal static class EnCaseRules
    {
        public static bool Open(EnCaseStatus s) => s != EnCaseStatus.ClosedLost && s != EnCaseStatus.Cancelled;

        public static string ClassHash(EnCase c) =>
            EnHash.Of(string.Join("|", EnText.Num((int)c.ProposedClassification), c.ProposedProductId?.ToString() ?? "-", c.ProposedWithChanges ? "1" : "0", EnHash.Norm(c.ProposedChangeNote), c.InquiryId?.ToString() ?? "-", c.ContractId?.ToString() ?? "-", EnHash.Norm(c.ReferenceCode), EnHash.Norm(c.StandardEvidence)));

        /// <summary>The documents of the tender package as they are now (id, file, revision, title): any addition, void or change gives another hash. A spare-parts or service case also binds the confirmed technical opinion into the hash.</summary>
        public static async Task<string> TenderHashAsync(IUnitOfWork uow, int packageId, EnCaseKind kind, string opinion, CancellationToken ct)
        {
            var docs = await uow.Set<PcTenderDocument>().AsNoTracking().Where(d => d.PackageId == packageId && !d.Deleted).OrderBy(d => d.Id).Select(d => new { d.Id, d.DocumentId, d.Revision, d.Title }).ToListAsync(ct);
            string text = string.Join(";", docs.Select(d => EnText.Num(d.Id) + "|" + (d.DocumentId?.ToString("N") ?? "-") + "|" + EnHash.Norm(d.Revision) + "|" + EnHash.Norm(d.Title)));
            return EnHash.Of(kind == EnCaseKind.Turbine ? text : text + "#OPINION#" + EnHash.Norm(opinion));
        }

        public static Task<string> TenderHashAsync(IUnitOfWork uow, EnCase c, CancellationToken ct) => TenderHashAsync(uow, c.TenderPackageId.Value, c.Kind, c.OpinionText, ct);

        public static async Task<PcProjectCharter> CharterAsync(IUnitOfWork uow, int contractId, CancellationToken ct) =>
            await uow.Set<PcProjectCharter>().AsNoTracking().Where(c => c.ContractId == contractId && !c.Deleted).OrderByDescending(c => c.Id).FirstOrDefaultAsync(ct);

        /// <summary>The price-free text sections of the charter, in a fixed order: this is what engineering sees and confirms. The cost estimate section is never part of it (the employer's rule: only managers see the project price), so a change of the cost section alone does not void the confirmation and any other change does.</summary>
        public static string[] PriceFreeSections(PcProjectCharter c) => new[]
        {
            c.Overview, c.ScopeDescription, c.Requirements, c.Deliverables, c.Boundaries, c.TimeBaselineEstimate, c.AlternativesAnalysis, c.Assumptions, c.Constraints, c.Risks, c.Stakeholders, c.PreAssignedResources, c.ManagerAuthority,
        };

        /// <summary>The hash engineering confirms: over exactly the price-free sections, so a change of the cost section alone does not void the confirmation and any other change does.</summary>
        public static string CharterHash(PcProjectCharter c) => c == null ? null : EnHash.Of(string.Join("#", PriceFreeSections(c).Select(EnHash.Norm)));

        public static bool TenderPassed(EnCaseStatus s) => s == EnCaseStatus.TenderIssued || s == EnCaseStatus.Awarded || s == EnCaseStatus.InEngineering || s == EnCaseStatus.HandedOver || s == EnCaseStatus.Accepted || s == EnCaseStatus.Released;
    }

    /// <summary>The chokepoints of the engineering control (each is merged into the refusal of the action it guards, so one message is answered) and the helpers the case service shares with them.</summary>
    public static class EnGates
    {
        private static string Name(EnCase c) => c.Number + "، وضعیت: " + EnText.Case(c.Status);

        public static async Task<EnGateResult> ForInquiryAsync(IUnitOfWork uow, int inquiryId, CancellationToken ct)
        {
            var inquiry = await uow.Set<SlInquiry>().AsNoTracking().Where(i => i.Id == inquiryId && !i.Deleted).Select(i => new { i.Id, i.CreateDate, i.ProductType, i.Status }).FirstOrDefaultAsync(ct);
            if (inquiry == null) return new EnGateResult { Passed = true };   // spare parts and service inquiries are controlled too (their case carries the technical opinion)
            DateTimeOffset? start = await EnControl.StartAsync(uow, ct);
            if (EnControl.IsLegacy(inquiry.CreateDate, start)) return new EnGateResult { Legacy = true, LegacyNote = EnControl.LegacyNote(start), Passed = true };
            EnCase c = await uow.Set<EnCase>().AsNoTracking().FirstOrDefaultAsync(x => x.InquiryId == inquiryId && !x.Deleted, ct);
            if (c == null) return new EnGateResult { Controlled = true, Reason = "بسته فنی مهندسی برای این استعلام هنوز به بازرگانی ابلاغ نشده است (پرونده مهندسی هنوز باز نشده است)." };
            EnGateResult r = new EnGateResult { Controlled = true, Case = c };
            if (!EnCaseRules.TenderPassed(c.Status) || !c.TenderReleasedAt.HasValue) { r.Reason = "بسته فنی مهندسی برای این استعلام هنوز به بازرگانی ابلاغ نشده است (پرونده مهندسی " + Name(c) + ")."; return r; }
            if (c.TenderPackageId.HasValue && c.TenderHash != await EnCaseRules.TenderHashAsync(uow, c, ct))
            { r.Reason = "بسته فنی مهندسی برای این استعلام هنوز به بازرگانی ابلاغ نشده است: مدارک بسته پس از ابلاغ تغییر کرده و مهندسی باید دوباره ابلاغ کند (پرونده مهندسی " + Name(c) + ")."; return r; }
            r.Passed = true;
            return r;
        }

        public static async Task<EnGateResult> ForContractAsync(IUnitOfWork uow, int contractId, CancellationToken ct)
        {
            DateTimeOffset? created = await uow.Set<ContractEntity>().AsNoTracking().Where(c => c.Id == contractId).Select(c => (DateTimeOffset?)c.CreateDate).FirstOrDefaultAsync(ct);
            if (!created.HasValue) return new EnGateResult { Passed = true };
            DateTimeOffset? start = await EnControl.StartAsync(uow, ct);
            if (EnControl.IsLegacy(created.Value, start)) return new EnGateResult { Legacy = true, LegacyNote = EnControl.LegacyNote(start), Passed = true };
            EnCase c = await uow.Set<EnCase>().AsNoTracking().FirstOrDefaultAsync(x => x.ContractId == contractId && !x.Deleted, ct);
            return new EnGateResult { Controlled = true, Case = c };
        }

        /// <summary>The proposal approval: null when it may pass, else the one Persian message.</summary>
        public static async Task<string> ProposalApproveRefusalAsync(IUnitOfWork uow, int inquiryId, CancellationToken ct) => (await ForInquiryAsync(uow, inquiryId, ct)).Reason;

        /// <summary>The charter approval of the CEO: engineering must have confirmed the CURRENT charter content.</summary>
        public static async Task<string> CharterApproveRefusalAsync(IUnitOfWork uow, int contractId, CancellationToken ct)
        {
            EnGateResult g = await ForContractAsync(uow, contractId, ct);
            if (!g.Controlled) return null;
            if (g.Case == null) return "منشور پروژه هنوز توسط مهندسی تایید نشده است (پرونده مهندسی برای این قرارداد هنوز باز نشده است).";
            PcProjectCharter charter = await EnCaseRules.CharterAsync(uow, contractId, ct);
            if (g.Case.CharterReviewOutcome == EnCharterOutcome.Confirmed && charter != null && g.Case.CharterHash == EnCaseRules.CharterHash(charter)) return null;
            return g.Case.CharterReviewOutcome == EnCharterOutcome.Returned
                ? "منشور پروژه هنوز توسط مهندسی تایید نشده است: مهندسی آن را برگردانده است (پرونده مهندسی " + Name(g.Case) + ")."
                : "منشور پروژه هنوز توسط مهندسی تایید نشده است یا پس از تایید مهندسی تغییر کرده است (پرونده مهندسی " + Name(g.Case) + ").";
        }

        /// <summary>The baseline of a work order from the product bank (and the first release steps) wait for the handover accepted by the PMO.</summary>
        public static async Task<string> InstantiateRefusalAsync(IUnitOfWork uow, int contractId, CancellationToken ct)
        {
            EnGateResult g = await ForContractAsync(uow, contractId, ct);
            if (!g.Controlled) return null;
            if (g.Case == null) return "مدارک مهندسی این قرارداد هنوز از سوی PMO پذیرفته نشده است (پرونده مهندسی هنوز باز نشده است).";
            int caseId = g.Case.Id;
            bool ok = await uow.Set<EnHandover>().AsNoTracking().AnyAsync(h => h.CaseId == caseId && !h.Deleted && (h.Status == EnHandoverStatus.Accepted || h.Status == EnHandoverStatus.Released), ct);
            return ok ? null : "مدارک مهندسی این قرارداد هنوز از سوی PMO پذیرفته نشده است (پرونده مهندسی " + Name(g.Case) + ").";
        }

        /// <summary>One more item of the release check of a production order: null when passed or legacy, else (Passed=false, message). Legacy adds no item.</summary>
        public static async Task<(bool Controlled, bool Passed, string Message)> ProductionReleaseAsync(IUnitOfWork uow, int contractId, CancellationToken ct)
        {
            EnGateResult g = await ForContractAsync(uow, contractId, ct);
            if (!g.Controlled) return (false, true, null);
            if (g.Case == null) return (true, false, "مدارک مهندسی این قرارداد هنوز از سوی PMO ابلاغ نشده است (پرونده مهندسی هنوز باز نشده است).");
            int caseId = g.Case.Id;
            bool ok = await uow.Set<EnHandover>().AsNoTracking().AnyAsync(h => h.CaseId == caseId && !h.Deleted && h.Status == EnHandoverStatus.Released, ct);
            return ok ? (true, true, "مدارک مهندسی ابلاغ‌شده است (" + g.Case.Number + ").") : (true, false, "مدارک مهندسی این قرارداد هنوز از سوی PMO پذیرفته/ابلاغ نشده است (پرونده مهندسی " + Name(g.Case) + ").");
        }
    }
}
