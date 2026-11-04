using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>Role names of the engineering document control (upper case, as in the JWT).</summary>
    public static class EdmsRoles
    {
        /// <summary>Prepare a revision, add its files, submit it, check documents out.</summary>
        public const string Preparers = ProjectControlRoles.Dcc;

        /// <summary>Review / approve / release when the discipline has no approver matrix of its own (the same people as the MDR rules before P-B7b).</summary>
        public const string FallbackReviewers = "ENGINEERING,ENGINEER,DCC,PMO";
        public const string FallbackApprovers = "ENGINEERING,PMO";
        public const string FallbackReleasers = "DCC,ENGINEERING,PMO";

        /// <summary>Cancel the check-out of somebody else, edit the distribution lists.</summary>
        public const string Managers = "DCC,PMO";

        /// <summary>The approver matrix is edited by the PMO only (it decides who may approve).</summary>
        public const string MatrixEditors = "PMO";

        /// <summary>Read the latest released revision of the documents of an order (the shop floor and the roles that work with it).</summary>
        public const string ShopFloor = "PRODUCTIONMANAGER,SHOPSUPERVISOR,OPERATOR,MAINTENANCE,ASSEMBLER,CONSTRUCTION,QC,QA,PLANNING,PROJECTMANAGER,WAREHOUSE,WHSUPERVISOR,SECURITY";

        public const string Readers = ProjectControlRoles.Readers + ",PRODUCTIONMANAGER,SHOPSUPERVISOR,OPERATOR,MAINTENANCE,AUDITOR";

        /// <summary>
        /// H3: who reads the whole history of a document (drafts, superseded revisions, reject reasons, every file), the links and the register export: the engineering and document-control side.
        /// The floor roles (ShopFloor) are left out on purpose; they read only the released revision through the shop-floor endpoints.
        /// </summary>
        public const string DocReaders = "PMO,CEO,DCC,COMMERCIAL,FINANCIAL,ENGINEERING,ENGINEER,AUDITOR";

        /// <summary>Tender documents: commercial and engineering prepare them, the PMO and the CEO read them.</summary>
        public const string TenderWriters = "COMMERCIAL,ENGINEERING,ENGINEER,DCC,PMO";
        public const string TenderReaders = "COMMERCIAL,ENGINEERING,ENGINEER,DCC,PMO,CEO,PROJECTMANAGER,PLANNING";
    }

    /// <summary>Pure rules of the engineering document control (no database).</summary>
    public static class EdmsRules
    {
        public const int SchemeNumeric = 1;
        public const int SchemeAlpha = 2;

        /// <summary>The revision code of the n-th revision (n starts at 1): 00, 01, 02 ... or A, B ... Z, AA, AB ...</summary>
        public static string NextCode(int scheme, int sequence)
        {
            if (sequence < 1) sequence = 1;
            if (scheme == SchemeAlpha)
            {
                string code = "";
                int n = sequence;
                while (n > 0)
                {
                    int rem = (n - 1) % 26;
                    code = (char)('A' + rem) + code;
                    n = (n - 1) / 26;
                }
                return code;
            }
            return (sequence - 1).ToString("00", CultureInfo.InvariantCulture);
        }

        public static bool IsOpen(PcDocStatus s) => s == PcDocStatus.Draft || s == PcDocStatus.UnderReview || s == PcDocStatus.Approved;

        /// <summary>Only a Draft can change (its files, its reason). Every later status is read only.</summary>
        public static bool IsEditable(PcDocStatus s) => s == PcDocStatus.Draft;

        /// <summary>E7: which revisions a transmittal may send. A released revision for every purpose; a revision still under review or approved only when it is sent for the client's review (purpose ForApproval). A superseded, obsolete or draft revision never.</summary>
        public static bool CanSend(PcDocStatus s, PcTransmittalPurpose purpose)
        {
            if (s == PcDocStatus.Released) return true;
            if (purpose == PcTransmittalPurpose.AsBuilt && s == PcDocStatus.AsBuilt) return true;
            return purpose == PcTransmittalPurpose.ForApproval && (s == PcDocStatus.UnderReview || s == PcDocStatus.Approved);
        }

        /// <summary>M3: a revision is in force when it is Released or As-Built (an as-built drawing is the drawing of what was made).</summary>
        public static bool InForce(PcDocStatus s) => s == PcDocStatus.Released || s == PcDocStatus.AsBuilt;

        /// <summary>The revision in force of a document (the highest sequence among the Released and As-Built ones), or null.</summary>
        public static T LatestReleased<T>(IEnumerable<T> revisions, Func<T, PcDocStatus> status, Func<T, int> sequence) where T : class =>
            revisions.Where(r => InForce(status(r))).OrderByDescending(sequence).FirstOrDefault();

        public static string StatusText(PcDocStatus s)
        {
            switch (s)
            {
                case PcDocStatus.Draft: return "پیش‌نویس";
                case PcDocStatus.UnderReview: return "در حال بررسی";
                case PcDocStatus.Approved: return "تاییدشده";
                case PcDocStatus.Released: return "منتشرشده";
                case PcDocStatus.Superseded: return "جایگزین‌شده";
                case PcDocStatus.Obsolete: return "منسوخ";
                case PcDocStatus.AsBuilt: return "اجرا‌شده (As-Built)";
                default: return s.ToString();
            }
        }

        /// <summary>The line of the stamp of a controlled copy: revision, date and the user who took the copy.</summary>
        public static string StampText(string documentNumber, string code, string dateText, string userName) =>
            $"نسخه کنترل‌شده - سند {documentNumber} - ویرایش {code} - تاریخ {dateText} - دریافت‌کننده {userName}";

        /// <summary>Metadata difference of two revisions (no binary comparison): field by field, only what differs.</summary>
        public static List<PcRevisionDiffDto> Diff(PcRevisionDto a, PcRevisionDto b)
        {
            var list = new List<PcRevisionDiffDto>();
            void Add(string field, string x, string y) { if (!string.Equals(x ?? "", y ?? "", StringComparison.Ordinal)) list.Add(new PcRevisionDiffDto { Field = field, A = x, B = y }); }
            string D(DateTimeOffset? d) => d?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            Add("code", a.Code, b.Code);
            Add("status", StatusText(a.Status), StatusText(b.Status));
            Add("reason", a.Reason, b.Reason);
            Add("preparedBy", a.PreparedByName, b.PreparedByName);
            Add("reviewer", a.ReviewerName, b.ReviewerName);
            Add("approver", a.ApproverName, b.ApproverName);
            Add("releasedBy", a.ReleasedByName, b.ReleasedByName);
            Add("submittedAt", D(a.SubmittedAt), D(b.SubmittedAt));
            Add("releasedAt", D(a.ReleasedAt), D(b.ReleasedAt));
            Add("changeRequest", a.ChangeRequestNumber, b.ChangeRequestNumber);
            Add("changeImpact", a.ChangeImpact ? "1" : "0", b.ChangeImpact ? "1" : "0");
            Add("fileCount", a.Files.Count.ToString(CultureInfo.InvariantCulture), b.Files.Count.ToString(CultureInfo.InvariantCulture));
            Add("fileNames", string.Join("; ", a.Files.Select(f => f.FileName).OrderBy(x => x, StringComparer.Ordinal)), string.Join("; ", b.Files.Select(f => f.FileName).OrderBy(x => x, StringComparer.Ordinal)));
            return list;
        }
    }

    /// <summary>One document found for an order, a part or a tag.</summary>
    public sealed class EdmsFoundDocument
    {
        public PcEngineeringDeliverable Deliverable;
        public int? ControlledDocumentId;
        public string Number;
        public string Title;
        public string DocumentType;
        public string Discipline;
        public PcDocLinkKind Via;
        public string ViaText;
        public PcDocumentRevision Released;
        public DocumentRevisionRecord ControlledRevision;
        public PcDocStatus? LatestStatus;
        public bool NoRevisions;
        public List<PcDocumentRevisionFile> Files = new List<PcDocumentRevisionFile>();
    }

    /// <summary>What an order is known by: the keys a document can be linked to.</summary>
    public sealed class EdmsKeys
    {
        public int? ItemId;
        public string PartNumber;
        public string StageCode2;
        public int? ActivityId;
        public int? WbsNodeId;
        public int? WorkOrderId;
        public HashSet<int> TagIds = new HashSet<int>();
    }

    /// <summary>
    /// The one place that answers "which documents belong to this order / part / tag and which revision of each is in force". The shop floor viewer, the production release gate and the part view
    /// all use it, so the three can never disagree.
    /// </summary>
    public static class EdmsResolver
    {
        public static async Task<EdmsKeys> KeysOfOrderAsync(IUnitOfWork uow, MfProductionOrder order, CancellationToken ct)
        {
            var keys = new EdmsKeys { ItemId = order.ItemId, ActivityId = order.ActivityId, WorkOrderId = order.WorkOrderId };
            if (order.ItemId.HasValue)
            {
                var item = await uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == order.ItemId.Value).Select(i => new { i.PartNumber, i.StageCode2 }).FirstOrDefaultAsync(ct);
                keys.PartNumber = item?.PartNumber; keys.StageCode2 = item?.StageCode2;
                List<int> tags = await uow.Set<PcEquipmentTag>().AsNoTracking().Where(t => !t.Deleted && t.IsActive && t.ItemId == order.ItemId.Value).Select(t => t.Id).ToListAsync(ct);
                foreach (int t in tags) keys.TagIds.Add(t);
            }
            if (order.ActivityId.HasValue)
                keys.WbsNodeId = await uow.Set<PcActivity>().AsNoTracking().Where(a => a.Id == order.ActivityId.Value).Select(a => (int?)a.WbsNodeId).FirstOrDefaultAsync(ct);
            return keys;
        }

        public const string FrozenText = "سند پیش‌تر منتشر شده است؛ شماره، عنوان، رشته و نوع سند دیگر تغییر نمی‌کند (تغییر با بازنگری تازه انجام می‌شود).";

        /// <summary>M2: any revision of the document has been released (it is released now, or was superseded, retired or made as-built).</summary>
        public static Task<bool> HasBeenReleasedAsync(IUnitOfWork uow, int deliverableId, CancellationToken ct) =>
            uow.Set<PcDocumentRevision>().AsNoTracking().AnyAsync(r => !r.Deleted && r.DeliverableId == deliverableId
                && (r.Status == PcDocStatus.Released || r.Status == PcDocStatus.Superseded || r.Status == PcDocStatus.Obsolete || r.Status == PcDocStatus.AsBuilt), ct);

        private static string ViaText(PcDocumentLink l)
        {
            switch (l.Kind)
            {
                case PcDocLinkKind.Item: return "کالا";
                case PcDocLinkKind.BomLine: return "ردیف BOM " + l.PartNumber;
                case PcDocLinkKind.PartNumber: return "قطعه " + l.PartNumber + (string.IsNullOrEmpty(l.StageCode2) ? "" : "-" + l.StageCode2);
                case PcDocLinkKind.WbsNode: return "گره WBS";
                case PcDocLinkKind.Activity: return "فعالیت";
                case PcDocLinkKind.EquipmentTag: return "تگ تجهیز";
                default: return l.Kind.ToString();
            }
        }

        /// <summary>The live links that match the keys. A link bound to an operation template matches when no operation is asked for, or the same template.</summary>
        public static async Task<List<PcDocumentLink>> MatchingLinksAsync(IUnitOfWork uow, EdmsKeys k, int? operationTemplateId, bool withOperationScope, CancellationToken ct)
        {
            string part = k.PartNumber;
            string stage = k.StageCode2;
            List<int> tags = k.TagIds.ToList();
            IQueryable<PcDocumentLink> q = uow.Set<PcDocumentLink>().AsNoTracking().Where(l => !l.Deleted && (
                (k.ItemId != null && l.ItemId == k.ItemId)
                || (part != null && l.PartNumber == part && (l.StageCode2 == null || l.StageCode2 == stage))
                || (k.WbsNodeId != null && l.WbsNodeId == k.WbsNodeId)
                || (k.ActivityId != null && l.ActivityId == k.ActivityId)
                || (l.TagId != null && tags.Contains(l.TagId.Value))));
            List<PcDocumentLink> links = await q.ToListAsync(ct);
            if (withOperationScope) links = links.Where(l => l.OperationTemplateId == null || l.OperationTemplateId == operationTemplateId).ToList();
            return links;
        }

        /// <summary>
        /// Documents of an order: reached through the links of its item, part number (and stage), WBS node, activity and tags, plus the register lines of its work order that have no link at all
        /// (documents of the whole work order). Each with its latest released revision (or none).
        /// </summary>
        public static async Task<List<EdmsFoundDocument>> ForOrderAsync(IUnitOfWork uow, MfProductionOrder order, int? operationTemplateId, bool withOperationScope, bool includeWorkOrderLevel, CancellationToken ct)
        {
            EdmsKeys keys = await KeysOfOrderAsync(uow, order, ct);
            List<PcDocumentLink> links = await MatchingLinksAsync(uow, keys, operationTemplateId, withOperationScope, ct);
            var found = new Dictionary<string, EdmsFoundDocument>();
            foreach (PcDocumentLink l in links)
            {
                string key = l.DeliverableId.HasValue ? "d" + l.DeliverableId.Value : "c" + l.ControlledDocumentId.Value;
                if (found.ContainsKey(key)) continue;
                found[key] = new EdmsFoundDocument { Via = l.Kind, ViaText = ViaText(l), Deliverable = l.DeliverableId.HasValue ? new PcEngineeringDeliverable { Id = l.DeliverableId.Value } : null, ControlledDocumentId = l.ControlledDocumentId };
            }
            if (includeWorkOrderLevel)
            {
                List<int> woAll = await uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(d => !d.Deleted && d.WorkOrderId == order.WorkOrderId).Select(d => d.Id).ToListAsync(ct);
                HashSet<int> linkedAny = woAll.Count == 0 ? new HashSet<int>()
                    : (await uow.Set<PcDocumentLink>().AsNoTracking().Where(l => !l.Deleted && l.DeliverableId != null && woAll.Contains(l.DeliverableId.Value)).Select(l => l.DeliverableId.Value).ToListAsync(ct)).ToHashSet();
                List<int> wo = woAll.Where(id => !linkedAny.Contains(id)).ToList();
                foreach (int id in wo)
                    if (!found.ContainsKey("d" + id)) found["d" + id] = new EdmsFoundDocument { Via = PcDocLinkKind.Item, ViaText = "دستور کار", Deliverable = new PcEngineeringDeliverable { Id = id } };
            }
            await FillAsync(uow, found.Values.ToList(), ct);
            // M4 (★): a part drawing is company-wide once it is released, so a released document of another project still counts; a document that is NOT released yet counts only for its own project
            // (a draft of project A linked to part X must not block the orders of project B)
            int? orderContract = await uow.Set<Entities.Model.NasbNiro.Contracts.WorkOrder>().AsNoTracking().Where(w => w.Id == order.WorkOrderId).Select(w => (int?)w.ContractId).FirstOrDefaultAsync(ct);
            return found.Values.Where(f => f.Number != null && (f.Deliverable == null || f.Released != null || f.Deliverable.ContractId == orderContract)).OrderBy(f => f.Number, StringComparer.Ordinal).ToList();
        }

        /// <summary>Fills number, title, revisions and files of the found documents with a fixed number of queries.</summary>
        public static async Task FillAsync(IUnitOfWork uow, List<EdmsFoundDocument> docs, CancellationToken ct)
        {
            List<int> dIds = docs.Where(d => d.Deliverable != null).Select(d => d.Deliverable.Id).Distinct().ToList();
            List<int> cIds = docs.Where(d => d.ControlledDocumentId.HasValue).Select(d => d.ControlledDocumentId.Value).Distinct().ToList();
            Dictionary<int, PcEngineeringDeliverable> deliverables = dIds.Count == 0 ? new Dictionary<int, PcEngineeringDeliverable>()
                : (await uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(d => !d.Deleted && dIds.Contains(d.Id)).ToListAsync(ct)).ToDictionary(d => d.Id);
            List<PcDocumentRevision> revisions = dIds.Count == 0 ? new List<PcDocumentRevision>()
                : await uow.Set<PcDocumentRevision>().AsNoTracking().Where(r => !r.Deleted && dIds.Contains(r.DeliverableId)).ToListAsync(ct);
            List<int> releasedIds = revisions.Where(r => EdmsRules.InForce(r.Status)).Select(r => r.Id).ToList();
            List<PcDocumentRevisionFile> files = releasedIds.Count == 0 ? new List<PcDocumentRevisionFile>()
                : await uow.Set<PcDocumentRevisionFile>().AsNoTracking().Where(f => !f.Deleted && releasedIds.Contains(f.RevisionId)).OrderBy(f => f.Id).ToListAsync(ct);
            Dictionary<int, ControlledDocument> controlled = cIds.Count == 0 ? new Dictionary<int, ControlledDocument>()
                : (await uow.Set<ControlledDocument>().AsNoTracking().Where(c => !c.Deleted && cIds.Contains(c.Id)).ToListAsync(ct)).ToDictionary(c => c.Id);
            List<DocumentRevisionRecord> effective = cIds.Count == 0 ? new List<DocumentRevisionRecord>()
                : await uow.Set<DocumentRevisionRecord>().AsNoTracking().Where(r => !r.Deleted && cIds.Contains(r.DocumentId) && r.Status == ControlledDocumentStatus.Effective).ToListAsync(ct);

            foreach (EdmsFoundDocument f in docs)
            {
                if (f.Deliverable != null)
                {
                    if (!deliverables.TryGetValue(f.Deliverable.Id, out PcEngineeringDeliverable d)) continue;
                    f.Deliverable = d; f.Number = d.DocumentNumber; f.Title = d.Title; f.DocumentType = d.DocumentType; f.Discipline = d.Discipline;
                    List<PcDocumentRevision> mine = revisions.Where(r => r.DeliverableId == d.Id).ToList();
                    f.NoRevisions = mine.Count == 0;
                    f.Released = EdmsRules.LatestReleased(mine, r => r.Status, r => r.Sequence);
                    f.LatestStatus = mine.OrderByDescending(r => r.Sequence).Select(r => (PcDocStatus?)r.Status).FirstOrDefault();
                    if (f.Released != null) f.Files = files.Where(x => x.RevisionId == f.Released.Id).ToList();
                }
                else if (f.ControlledDocumentId.HasValue && controlled.TryGetValue(f.ControlledDocumentId.Value, out ControlledDocument c))
                {
                    f.Number = c.Code; f.Title = c.Title; f.DocumentType = c.Type.ToString(); f.Discipline = c.Department;
                    f.ControlledRevision = effective.Where(r => r.DocumentId == c.Id).OrderByDescending(r => r.Id).FirstOrDefault();
                    f.LatestStatus = f.ControlledRevision != null ? PcDocStatus.Released : (PcDocStatus?)null;
                }
            }
        }
    }
}
