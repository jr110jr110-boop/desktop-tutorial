using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProductBankDtos;

namespace Services.NasbNiro.ProductBank
{
    public interface IDesignReviewService
    {
        Task<ResponseDto<PagedList<DesignReviewListDto>>> GetListAsync(DesignReviewQuery query, CancellationToken ct);
        Task<ResponseDto<DesignReviewDto>> GetAsync(int id, CancellationToken ct);
        Task<ResponseDto<DesignReviewDto>> SaveAsync(SaveDesignReviewDto dto, CancellationToken ct);
        Task<ResponseDto<DesignReviewDto>> ActionAsync(DesignReviewActionDto dto, CancellationToken ct);
        Task<ResponseDto<DesignReviewDto>> SaveValidationAsync(SaveDesignValidationDto dto, CancellationToken ct);
        Task<ResponseDto<DesignReviewDto>> SaveFindingAsync(SaveDesignFindingDto dto, CancellationToken ct);
        Task<ResponseDto<DesignReviewDto>> FindingActionAsync(DesignFindingActionDto dto, CancellationToken ct);
        Task<ResponseDto<DesignReviewDto>> SaveTraceAsync(SaveDesignTraceDto dto, CancellationToken ct);
        Task<ResponseDto<DesignMatrixDto>> MatrixAsync(int reviewId, CancellationToken ct);
    }

    /// <summary>
    /// Package A1 (ISO 9001 8.3.4): design review, verification and validation. A review covers inputs and outputs (drawing revisions, BOM and OPC revisions with their content hash), is attended by people
    /// of whom at least one is independent of the authors, and is closed by a human who is not an author. Only a review that is Closed with outcome Accepted opens the release gates.
    /// </summary>
    public class DesignReviewService : PbServiceBase, IDesignReviewService
    {
        private readonly INotificationService _notify;

        public DesignReviewService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify) : base(uow, user) { _notify = notify; }

        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private int Me => User.UserId;

        private sealed class State
        {
            public HashSet<int> Authors = new HashSet<int>();
            public List<PbDesignInput> Live = new List<PbDesignInput>();
            public List<DesignUncoveredDto> Uncovered = new List<DesignUncoveredDto>();
            public List<string> Blockers = new List<string>();
            public int Independent;
            public Dictionary<int, bool> Unchanged = new Dictionary<int, bool>();
        }

        private IQueryable<PbDesignReview> Full(bool tracked)
        {
            IQueryable<PbDesignReview> q = Uow.Set<PbDesignReview>().Include(r => r.Participants).Include(r => r.Inputs).Include(r => r.Outputs).Include(r => r.Findings).Include(r => r.Traces).Include(r => r.Plan).ThenInclude(p => p.Product);
            return tracked ? q : q.AsNoTracking();
        }

        // ------------------------------------------------------------------------------------------ state

        private async Task<bool> OutputUnchangedAsync(PbDesignReviewOutput o, CancellationToken ct)
        {
            if (o.Kind == DesignOutputKind.Bom)
            {
                PbBomHeader h = await Uow.Set<PbBomHeader>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == o.BomHeaderId, ct);
                return h != null && !h.Deleted && h.Status != PbBomStatus.Obsolete && string.Equals(h.ContentHash ?? string.Empty, o.ContentHash ?? string.Empty, StringComparison.Ordinal);
            }
            if (o.Kind == DesignOutputKind.Opc)
            {
                PbOpcRouting h = await Uow.Set<PbOpcRouting>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == o.OpcRoutingId, ct);
                return h != null && !h.Deleted && string.Equals(h.ContentHash ?? string.Empty, o.ContentHash ?? string.Empty, StringComparison.Ordinal);
            }
            // a drawing: the files it holds now must be the files that were verified, and the revision must still be in play
            PcDocumentRevision rev = await Uow.Set<PcDocumentRevision>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == o.RevisionId, ct);
            if (rev == null || rev.Deleted || rev.Status == PcDocStatus.Obsolete || rev.Status == PcDocStatus.Superseded) return false;
            return string.Equals(await DesignGate.DrawingFingerprintAsync(Uow, rev.Id, ct), o.ContentHash ?? string.Empty, StringComparison.Ordinal);
        }

        private async Task<State> StateAsync(PbDesignReview r, CancellationToken ct)
        {
            var s = new State();
            List<PbDesignReviewOutput> outputs = r.Outputs.Where(x => !x.Deleted).ToList();
            foreach (PbDesignReviewOutput o in outputs)
            {
                foreach (int u in await DesignGate.AuthorsAsync(Uow, o.Kind, o.RevisionId, o.BomHeaderId, o.OpcRoutingId, ct)) s.Authors.Add(u);
                s.Unchanged[o.Id] = await OutputUnchangedAsync(o, ct);
            }
            s.Live = await DesignQueries.LiveInputsAsync(Uow, r.ProductId, r.ProductRevision, false, ct);
            s.Independent = DesignRules.IndependentCount(r.Participants.Where(p => !p.Deleted).Select(p => p.UserId), s.Authors);
            HashSet<int> inputIds = r.Inputs.Where(x => !x.Deleted).Select(x => x.InputId).ToHashSet();
            var outTuples = outputs.Select(o => (o.Id, o.Kind, o.Label)).ToList();
            if (r.Type != DesignReviewType.Review)
                s.Uncovered = DesignRules.Uncovered(r.Type, s.Live, inputIds, r.Traces.Where(t => !t.Deleted), outTuples);
            Dictionary<string, decimal> st = await DesignGate.SettingsAsync(Uow, ct);
            int minIndependent = (int)st[DesignSettingKeys.MinIndependent];

            if (r.Status == DesignReviewStatus.Planned) s.Blockers.Add("بازبینی هنوز برگزار نشده است (ثبت برگزاری با صورتجلسه).");
            if (s.Independent < minIndependent) s.Blockers.Add($"دست‌کم {DesignRules.Num(minIndependent)} شرکت‌کننده مستقل (غیر از تهیه‌کنندگان خروجی‌ها) لازم است؛ فعلاً {DesignRules.Num(s.Independent)}.");
            if (outputs.Count == 0) s.Blockers.Add("هیچ خروجی طراحی برای بازبینی انتخاب نشده است.");
            foreach (PbDesignReviewOutput o in outputs.Where(o => !s.Unchanged[o.Id])) s.Blockers.Add($"{o.Label} بعد از انتخاب تغییر کرده یا منسوخ شده است؛ بازبینی تازه لازم است.");
            int open = r.Findings.Count(f => !f.Deleted && (f.Status == DesignFindingStatus.Open || f.Status == DesignFindingStatus.Done));
            if (open > 0) s.Blockers.Add($"{DesignRules.Num(open)} یافته هنوز بسته نشده است (انجام توسط مالک و بستن توسط نفر دیگر).");
            if (r.Type != DesignReviewType.Review)
            {
                if (inputIds.Count == 0) s.Blockers.Add("هیچ ورودی طراحی در بازبینی نیست.");
                if (s.Uncovered.Count > 0) s.Blockers.Add($"{DesignRules.Num(s.Uncovered.Count)} ورودی با مدرک و معیار پذیرش قبول‌شده پوشش داده نشده است.");
                s.Blockers.AddRange(DesignRules.OutputsWithoutEvidence(r.Traces.Where(t => !t.Deleted), outTuples));
            }
            if (r.Type == DesignReviewType.Validation)
            {
                if (r.ValidationMethod == DesignValidationMethod.None || string.IsNullOrWhiteSpace(r.ValidationEvidence)) s.Blockers.Add("روش و مدرک صحه‌گذاری (آزمون، آزمایش میدانی یا شبیه‌سازی) ثبت نشده است.");
                if (string.IsNullOrWhiteSpace(r.CustomerAcceptanceRef) && !(r.CustomerAcceptanceNotApplicable && !string.IsNullOrWhiteSpace(r.CustomerAcceptanceNote))) s.Blockers.Add("مدرک پذیرش مشتری ثبت نشده است (یا «مشتری ندارد» با توضیح).");
            }
            string conflicts = await DesignQueries.ConflictTextAsync(Uow, r.ProductId, r.ProductRevision, ct);
            if (conflicts != null) s.Blockers.Add(conflicts);
            return s;
        }

        // ------------------------------------------------------------------------------------------ reading

        public async Task<ResponseDto<PagedList<DesignReviewListDto>>> GetListAsync(DesignReviewQuery query, CancellationToken ct)
        {
            query ??= new DesignReviewQuery();
            IQueryable<PbDesignReview> q = Uow.Set<PbDesignReview>().AsNoTracking().Where(r => !r.Deleted);
            if (query.ProductId.HasValue) q = q.Where(r => r.ProductId == query.ProductId.Value);
            if (query.ProductRevision.HasValue) q = q.Where(r => r.ProductRevision == query.ProductRevision.Value);
            if (query.PlanId.HasValue) q = q.Where(r => r.PlanId == query.PlanId.Value);
            if (query.Type.HasValue) q = q.Where(r => r.Type == query.Type.Value);
            if (query.Status.HasValue) q = q.Where(r => r.Status == query.Status.Value);
            if (query.BomHeaderId.HasValue) q = q.Where(r => r.Outputs.Any(o => !o.Deleted && o.Kind == DesignOutputKind.Bom && o.BomHeaderId == query.BomHeaderId.Value));
            string text = PbText.Clean(query.Contains);
            if (text != null) q = q.Where(r => r.Number.Contains(text) || r.Title.Contains(text) || r.Plan.Product.Code.Contains(text));
            IQueryable<DesignReviewListDto> projected = q.OrderByDescending(r => r.Id).Select(r => new DesignReviewListDto
            {
                Id = r.Id, Number = r.Number, ProductId = r.ProductId, ProductCode = r.Plan.Product.Code, ProductRevision = r.ProductRevision, PlanId = r.PlanId, PlanNumber = r.Plan.Number, Type = r.Type, Title = r.Title, Status = r.Status,
                Outcome = r.Outcome, ScheduledDate = r.ScheduledDate, HeldDate = r.HeldDate, ClosedAt = r.ClosedAt, CreateDate = r.CreateDate,
                OpenFindings = r.Findings.Count(f => !f.Deleted && (f.Status == DesignFindingStatus.Open || f.Status == DesignFindingStatus.Done)),
            });
            PagedList<DesignReviewListDto> page = await PagedList<DesignReviewListDto>.CreateAsync(projected, query.PageNumber, query.PageSize, ct);
            foreach (DesignReviewListDto d in page) d.TypeText = DesignRules.TypeText(d.Type);
            return ResponseDtoHelper.Ok(page);
        }

        private async Task<DesignReviewDto> ToDtoAsync(PbDesignReview r, CancellationToken ct)
        {
            State s = await StateAsync(r, ct);
            var ids = new List<int?> { r.CreatorUserId, r.HeldByUserId, r.ClosedByUserId };
            ids.AddRange(r.Participants.Select(p => (int?)p.UserId));
            ids.AddRange(r.Findings.SelectMany(f => new int?[] { f.OwnerUserId, f.RaisedByUserId, f.ClosedByUserId }));
            ids.AddRange(r.Traces.Select(t => t.JudgedByUserId));
            ids.AddRange(s.Authors.Select(a => (int?)a));
            Dictionary<int, string> names = await DesignQueries.NamesAsync(Uow, ids, ct);
            var dto = new DesignReviewDto
            {
                Id = r.Id, Number = r.Number, ProductId = r.ProductId, ProductCode = r.Plan?.Product?.Code, ProductRevision = r.ProductRevision, PlanId = r.PlanId, PlanNumber = r.Plan?.Number, PlanTitle = r.Plan?.Title,
                PlanValidationRequired = r.Plan?.ValidationRequired ?? true, Type = r.Type, TypeText = DesignRules.TypeText(r.Type), Stage = r.Stage, Title = r.Title, Status = r.Status, Outcome = r.Outcome, ScheduledDate = r.ScheduledDate,
                HeldDate = r.HeldDate, ClosedAt = r.ClosedAt, CreateDate = r.CreateDate, Minutes = r.Minutes, HeldByUserId = r.HeldByUserId, HeldByName = names.Name(r.HeldByUserId), HeldAt = r.HeldAt, OutcomeNote = r.OutcomeNote,
                ClosedByUserId = r.ClosedByUserId, ClosedByName = names.Name(r.ClosedByUserId), PreviousReviewId = r.PreviousReviewId, VoidReason = r.VoidReason, CreatorUserId = r.CreatorUserId, CreatedByName = names.Name(r.CreatorUserId),
                ValidationMethod = r.ValidationMethod, ValidationEvidence = r.ValidationEvidence, CustomerAcceptanceRef = r.CustomerAcceptanceRef, CustomerAcceptanceNotApplicable = r.CustomerAcceptanceNotApplicable, CustomerAcceptanceNote = r.CustomerAcceptanceNote,
                RowVersion = DesignQueries.B64(r.RowVersion), IndependentCount = s.Independent, Uncovered = s.Uncovered,
                OpenFindings = r.Findings.Count(f => !f.Deleted && (f.Status == DesignFindingStatus.Open || f.Status == DesignFindingStatus.Done)),
            };
            if (r.Status != DesignReviewStatus.Closed && r.Status != DesignReviewStatus.Void) dto.Blockers = s.Blockers.Where(b => !(r.Status == DesignReviewStatus.Held && b.StartsWith("بازبینی هنوز برگزار نشده"))).ToList();
            foreach (PbDesignReviewParticipant p in r.Participants.Where(x => !x.Deleted).OrderBy(x => x.Id))
                dto.Participants.Add(new DesignParticipantDto { Id = p.Id, UserId = p.UserId, Name = names.Name(p.UserId), RoleText = p.RoleText, Independent = !s.Authors.Contains(p.UserId) });
            var inputMap = s.Live.ToDictionary(i => i.Id);
            HashSet<int> uncovered = s.Uncovered.Select(u => u.InputId).ToHashSet();
            foreach (PbDesignReviewInput ri in r.Inputs.Where(x => !x.Deleted))
            {
                PbDesignInput i = inputMap.TryGetValue(ri.InputId, out PbDesignInput live) ? live : await Uow.Set<PbDesignInput>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == ri.InputId, ct);
                if (i == null) continue;
                dto.Inputs.Add(new DesignReviewInputRowDto { InputId = i.Id, Code = i.Code, Kind = i.Kind, KindText = DesignRules.KindText(i.Kind), Statement = i.Statement, Source = i.Source, AcceptanceCriterion = i.AcceptanceCriterion, Status = i.Status, Covered = DesignRules.InScope(r.Type, i.Kind) && !uncovered.Contains(i.Id) });
            }
            dto.Inputs = dto.Inputs.OrderBy(i => i.Code, StringComparer.Ordinal).ToList();
            foreach (PbDesignReviewOutput o in r.Outputs.Where(x => !x.Deleted).OrderBy(x => x.Id))
            {
                var od = new DesignOutputDto { Id = o.Id, Kind = o.Kind, KindText = DesignRules.OutputKindText(o.Kind), DeliverableId = o.DeliverableId, RevisionId = o.RevisionId, BomHeaderId = o.BomHeaderId, OpcRoutingId = o.OpcRoutingId, Label = o.Label, RevisionText = o.RevisionText, ContentHash = o.ContentHash, Unchanged = s.Unchanged[o.Id] };
                foreach (int a in (await DesignGate.AuthorsAsync(Uow, o.Kind, o.RevisionId, o.BomHeaderId, o.OpcRoutingId, ct)).OrderBy(x => x)) od.AuthorNames.Add(names.Name(a) ?? ("#" + a));
                dto.Outputs.Add(od);
            }
            DateTime today = DateTime.Today;
            foreach (PbDesignFinding f in r.Findings.Where(x => !x.Deleted).OrderBy(x => x.Seq))
                dto.Findings.Add(new DesignFindingDto
                {
                    Id = f.Id, Seq = f.Seq, Description = f.Description, OwnerUserId = f.OwnerUserId, OwnerName = names.Name(f.OwnerUserId), DueDate = f.DueDate, Status = f.Status, StatusText = DesignRules.FindingStatusText(f.Status),
                    RaisedByUserId = f.RaisedByUserId, RaisedByName = names.Name(f.RaisedByUserId), DoneAt = f.DoneAt, DoneNote = f.DoneNote, ClosedByUserId = f.ClosedByUserId, ClosedByName = names.Name(f.ClosedByUserId), ClosedAt = f.ClosedAt,
                    CloseNote = f.CloseNote, VoidReason = f.VoidReason, Late = f.DueDate.Date < today && (f.Status == DesignFindingStatus.Open || f.Status == DesignFindingStatus.Done),
                });
            var codes = s.Live.ToDictionary(i => i.Id, i => i.Code);
            foreach (PbDesignTrace t in r.Traces.Where(x => !x.Deleted).OrderBy(x => x.Id))
                dto.Traces.Add(new DesignTraceDto { Id = t.Id, InputId = t.InputId, InputCode = codes.TryGetValue(t.InputId, out string c) ? c : null, OutputId = t.OutputId, Result = t.Result, Evidence = t.Evidence, EvidenceRef = t.EvidenceRef, JudgedByUserId = t.JudgedByUserId, JudgedByName = names.Name(t.JudgedByUserId), JudgedAt = t.JudgedAt });
            bool human = !IsAi;
            bool authorMe = s.Authors.Contains(Me);
            dto.CanEdit = human && r.Status == DesignReviewStatus.Planned && r.CreatorUserId == Me && InRole(DesignRoles.ReviewMakers);
            dto.CanHold = human && r.Status == DesignReviewStatus.Planned && InRole(DesignRoles.ReviewMakers);
            dto.CanClose = human && r.Status == DesignReviewStatus.Held && InRole(DesignRoles.ReviewClosers) && !authorMe;
            dto.CanJudge = human && r.Status == DesignReviewStatus.Held && r.Participants.Any(p => !p.Deleted && p.UserId == Me) && !authorMe;
            dto.CanVoid = human && r.Status != DesignReviewStatus.Void && (r.Status == DesignReviewStatus.Closed ? InRole(DesignRoles.QaOnly) : (r.CreatorUserId == Me || InRole(DesignRoles.Resolvers)));
            return dto;
        }

        public async Task<ResponseDto<DesignReviewDto>> GetAsync(int id, CancellationToken ct)
        {
            PbDesignReview r = await Full(false).FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (r == null) return ResponseDtoHelper.NotFound<DesignReviewDto>(PbMessages.NotFound);
            return ResponseDtoHelper.Ok(await ToDtoAsync(r, ct));
        }

        private async Task<ResponseDto<DesignReviewDto>> SaveAndGetAsync(int id, CancellationToken ct)
        {
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<DesignReviewDto>(conflict);
            return await GetAsync(id, ct);
        }

        // ------------------------------------------------------------------------------------------ save

        private static DesignStageKind StageOf(DesignReviewType t) => t == DesignReviewType.Review ? DesignStageKind.DesignReview : t == DesignReviewType.Verification ? DesignStageKind.Verification : DesignStageKind.Validation;

        private async Task<(PbDesignReviewOutput Row, string Error)> BuildOutputAsync(SaveDesignOutputDto o, PbDesignPlan plan, DesignStructure structure, CancellationToken ct)
        {
            if (o.Kind == DesignOutputKind.Bom)
            {
                if (!o.BomHeaderId.HasValue) return (null, "BOM انتخاب نشده است.");
                PbBomHeader h = await Uow.Set<PbBomHeader>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == o.BomHeaderId.Value && !x.Deleted, ct);
                if (h == null) return (null, "BOM پیدا نشد.");
                bool ownBom = h.ProductId == plan.ProductId && h.Revision == plan.ProductRevision;
                // fix round 1 (H5): any BOM revision of the structure of the product, a sub-assembly without a product of its own included
                if (!ownBom && !structure.HeaderIds.Contains(h.Id)) return (null, "BOM باید BOM همین محصول و بازنگری برنامه، یا یکی از BOMهای زیرمجموعه آن (ساختار BOM) باشد.");
                if (h.Status == PbBomStatus.Obsolete || h.Status == PbBomStatus.Superseded) return (null, $"BOM {h.Code} منسوخ یا جایگزین‌شده است.");
                if (string.IsNullOrEmpty(h.ContentHash) && h.Status != PbBomStatus.Released) return (null, $"BOM {h.Code} هنوز برای بازبینی ارسال نشده است (اثر انگشت محتوا ندارد).");   // a BOM released from paper (the FY seed) has no hash: it is verified as it stands
                return (new PbDesignReviewOutput { Kind = o.Kind, BomHeaderId = h.Id, Label = $"BOM {h.Code}", RevisionText = DesignRules.Rev(h.Revision), ContentHash = h.ContentHash }, null);
            }
            if (o.Kind == DesignOutputKind.Opc)
            {
                if (!o.OpcRoutingId.HasValue) return (null, "مسیر ساخت انتخاب نشده است.");
                PbOpcRouting h = await Uow.Set<PbOpcRouting>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == o.OpcRoutingId.Value && !x.Deleted, ct);
                if (h == null || h.ProductId != plan.ProductId) return (null, "مسیر ساخت باید برای همین محصول باشد.");
                return (new PbDesignReviewOutput { Kind = o.Kind, OpcRoutingId = h.Id, Label = $"مسیر ساخت {h.PartNumber}", RevisionText = DesignRules.Rev(h.Revision), ContentHash = h.ContentHash }, null);
            }
            if (!o.RevisionId.HasValue) return (null, "بازنگری نقشه انتخاب نشده است.");
            var rev = await Uow.Set<PcDocumentRevision>().AsNoTracking().Where(x => x.Id == o.RevisionId.Value && !x.Deleted).Select(x => new { x.Id, x.Code, x.DeliverableId, x.Status }).FirstOrDefaultAsync(ct);
            if (rev == null) return (null, "بازنگری نقشه پیدا نشد.");
            string number = await Uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(d => d.Id == rev.DeliverableId).Select(d => d.DocumentNumber).FirstAsync(ct);
            if (rev.Status == PcDocStatus.Obsolete || rev.Status == PcDocStatus.Superseded) return (null, $"نقشه {number} بازنگری {rev.Code} منسوخ یا جایگزین‌شده است.");
            // fix round 1 (M5): the drawing belongs to the structure of the product of the plan (a part of its BOM tree)
            if (!await DesignGate.DrawingInStructureAsync(Uow, rev.DeliverableId, structure, ct)) return (null, $"نقشه {number} به هیچ قطعه‌ای از ساختار BOM این محصول پیوند نشده است؛ نقشه در بازبینی طراحی همان محصولی می‌آید که قطعه‌اش را دارد.");
            return (new PbDesignReviewOutput { Kind = DesignOutputKind.Drawing, DeliverableId = rev.DeliverableId, RevisionId = rev.Id, Label = $"نقشه {number}", RevisionText = rev.Code, ContentHash = await DesignGate.DrawingFingerprintAsync(Uow, rev.Id, ct) }, null);
        }

        public async Task<ResponseDto<DesignReviewDto>> SaveAsync(SaveDesignReviewDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<DesignReviewDto>(DesignRules.AiNever);
            if (!InRole(DesignRoles.ReviewMakers)) return Bad<DesignReviewDto>(DesignRules.NotAllowed);
            if (dto == null || !Enum.IsDefined(typeof(DesignReviewType), dto.Type)) return Bad<DesignReviewDto>(PbMessages.InvalidData);
            PbDesignPlan plan = await Uow.Set<PbDesignPlan>().Include(p => p.Product).FirstOrDefaultAsync(p => p.Id == dto.PlanId && !p.Deleted, ct);
            if (plan == null) return Bad<DesignReviewDto>("برنامه طراحی پیدا نشد.");
            if (plan.Status != DesignPlanStatus.Approved) return Bad<DesignReviewDto>("بازبینی برای برنامه طراحی تاییدشده ثبت می‌شود.");
            if (dto.Type == DesignReviewType.Validation && !plan.ValidationRequired) return Bad<DesignReviewDto>("برنامه طراحی صحه‌گذاری را معاف کرده است.");
            string title = PbText.Clean(dto.Title) ?? $"{DesignRules.TypeText(dto.Type)} {plan.Product.Code} بازنگری {DesignRules.Rev(plan.ProductRevision)}";
            if (title.Length > DesignLimits.Title) return Bad<DesignReviewDto>("عنوان بیش از ۳۰۰ نویسه است.");
            if (dto.PreviousReviewId.HasValue)
            {
                PbDesignReview prev = await Uow.Set<PbDesignReview>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.PreviousReviewId.Value && !x.Deleted, ct);
                if (prev == null || prev.ProductId != plan.ProductId || prev.ProductRevision != plan.ProductRevision || prev.Type != dto.Type || prev.Status != DesignReviewStatus.Closed || prev.Outcome == DesignReviewOutcome.Accepted)
                    return Bad<DesignReviewDto>("بازبینی قبلی باید بازبینی بسته‌شده با نتیجه رد یا مشروط از همین نوع، محصول و بازنگری باشد.");
            }
            DesignStructure structure = await DesignGate.StructureAsync(Uow, plan.ProductId, plan.ProductRevision, ct);

            // outputs
            var outputs = new List<PbDesignReviewOutput>();
            foreach (SaveDesignOutputDto o in dto.Outputs ?? new List<SaveDesignOutputDto>())
            {
                var (row, err) = await BuildOutputAsync(o, plan, structure, ct);
                if (err != null) return Bad<DesignReviewDto>(err);
                if (outputs.Any(x => x.Kind == row.Kind && x.RevisionId == row.RevisionId && x.BomHeaderId == row.BomHeaderId && x.OpcRoutingId == row.OpcRoutingId)) return Bad<DesignReviewDto>("یک خروجی دو بار آمده است.");
                outputs.Add(row);
            }
            if (outputs.Count == 0) return Bad<DesignReviewDto>("دست‌کم یک خروجی طراحی (نقشه، BOM یا مسیر ساخت) را انتخاب کنید.");
            if (dto.Type == DesignReviewType.Validation && !outputs.Any(o => o.Kind == DesignOutputKind.Bom)) return Bad<DesignReviewDto>("صحه‌گذاری روی BOM محصول انجام می‌شود؛ BOM را انتخاب کنید.");

            // inputs
            List<PbDesignInput> live = await DesignQueries.LiveInputsAsync(Uow, plan.ProductId, plan.ProductRevision, false, ct);
            List<int> inputIds = (dto.InputIds ?? new List<int>()).Distinct().ToList();
            if (inputIds.Count == 0 && dto.Type != DesignReviewType.Review) inputIds = live.Where(i => i.Status == DesignInputStatus.Open && DesignRules.InScope(dto.Type, i.Kind)).Select(i => i.Id).ToList();
            foreach (int id in inputIds)
            {
                PbDesignInput i = live.FirstOrDefault(x => x.Id == id);
                if (i == null) return Bad<DesignReviewDto>("یکی از ورودی‌ها در این محصول و بازنگری معتبر نیست.");
                if (i.Status == DesignInputStatus.Conflict) return Bad<DesignReviewDto>($"ورودی {i.Code} در تعارض است؛ ابتدا تعارض را رفع کنید.");
            }
            if (dto.Type != DesignReviewType.Review && inputIds.Count == 0) return Bad<DesignReviewDto>("برای این نوع بازبینی ورودی طراحی لازم است.");

            // participants
            List<SaveDesignParticipantDto> parts = dto.Participants ?? new List<SaveDesignParticipantDto>();
            if (parts.Count == 0) return Bad<DesignReviewDto>("شرکت‌کنندگان بازبینی را مشخص کنید.");
            if (parts.Select(p => p.UserId).Distinct().Count() != parts.Count) return Bad<DesignReviewDto>("یک نفر دو بار به عنوان شرکت‌کننده آمده است.");
            if (parts.Any(p => PbText.Clean(p.RoleText) != null && p.RoleText.Trim().Length > DesignLimits.RoleText)) return Bad<DesignReviewDto>("نقش شرکت‌کننده بیش از ۱۰۰ نویسه است.");
            string person = await DesignQueries.PersonProblemAsync(Uow, parts.Select(p => p.UserId), ct);
            if (person != null) return Bad<DesignReviewDto>(person);
            var authors = new HashSet<int>();
            foreach (PbDesignReviewOutput o in outputs) foreach (int u in await DesignGate.AuthorsAsync(Uow, o.Kind, o.RevisionId, o.BomHeaderId, o.OpcRoutingId, ct)) authors.Add(u);

            DateTimeOffset now = DateTimeOffset.Now;
            PbDesignReview r;
            if (dto.Id.HasValue)
            {
                r = await Full(true).FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (r == null) return ResponseDtoHelper.NotFound<DesignReviewDto>(PbMessages.NotFound);
                if (r.Status != DesignReviewStatus.Planned) return Bad<DesignReviewDto>("فقط بازبینی برنامه‌ریزی‌شده ویرایش می‌شود.");
                if (r.CreatorUserId != Me) return Bad<DesignReviewDto>("بازبینی را فقط سازنده آن ویرایش می‌کند.");
                if (r.PlanId != plan.Id || r.Type != dto.Type) return Bad<DesignReviewDto>("برنامه و نوع بازبینی تغییر نمی‌کند.");
                if (!string.IsNullOrEmpty(dto.RowVersion))
                    try { Uow.Entry(r).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); } catch (FormatException) { return Bad<DesignReviewDto>(PbMessages.InvalidData); }
            }
            else
            {
                r = new PbDesignReview
                {
                    PlanId = plan.Id, Plan = plan, ProductId = plan.ProductId, ProductRevision = plan.ProductRevision, Type = dto.Type, Stage = StageOf(dto.Type), Status = DesignReviewStatus.Planned, PreviousReviewId = dto.PreviousReviewId,
                    Number = await InventoryNumbers.NextNumberAsync(Uow, DesignNumbers.Review, "DR", now.Date, ct),
                };
                Stamp(r, true);
                Uow.Set<PbDesignReview>().Add(r);
            }
            r.Title = title;
            r.ScheduledDate = dto.ScheduledDate?.Date;
            Stamp(r, false);

            foreach (PbDesignReviewInput old in r.Inputs.Where(x => !x.Deleted && x.Id != 0 && !inputIds.Contains(x.InputId)).ToList()) { old.Delete(); Stamp(old, false); }
            foreach (int id in inputIds.Where(id => !r.Inputs.Any(x => !x.Deleted && x.InputId == id)))
            { var n = new PbDesignReviewInput { InputId = id }; Stamp(n, true); r.Inputs.Add(n); }

            foreach (PbDesignReviewOutput old in r.Outputs.Where(x => !x.Deleted && x.Id != 0).ToList())
            {
                PbDesignReviewOutput keep = outputs.FirstOrDefault(o => o.Kind == old.Kind && o.RevisionId == old.RevisionId && o.BomHeaderId == old.BomHeaderId && o.OpcRoutingId == old.OpcRoutingId);
                if (keep == null) { old.Delete(); Stamp(old, false); }
                else { old.Label = keep.Label; old.RevisionText = keep.RevisionText; old.ContentHash = keep.ContentHash; Stamp(old, false); outputs.Remove(keep); }
            }
            foreach (PbDesignReviewOutput n in outputs) { Stamp(n, true); r.Outputs.Add(n); }

            foreach (PbDesignReviewParticipant old in r.Participants.Where(x => !x.Deleted && x.Id != 0 && !parts.Any(p => p.UserId == x.UserId)).ToList()) { old.Delete(); Stamp(old, false); }
            foreach (SaveDesignParticipantDto p in parts)
            {
                PbDesignReviewParticipant row = r.Participants.FirstOrDefault(x => !x.Deleted && x.UserId == p.UserId);
                if (row == null) { row = new PbDesignReviewParticipant { UserId = p.UserId }; Stamp(row, true); r.Participants.Add(row); }
                row.RoleText = PbText.Clean(p.RoleText); row.Independent = !authors.Contains(p.UserId); Stamp(row, false);
            }
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<DesignReviewDto>(conflict);
            return await GetAsync(r.Id, ct);
        }

        // ------------------------------------------------------------------------------------------ actions

        public async Task<ResponseDto<DesignReviewDto>> ActionAsync(DesignReviewActionDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<DesignReviewDto>(DesignRules.AiNever);
            if (dto == null || PbText.Clean(dto.Action) == null) return Bad<DesignReviewDto>(PbMessages.InvalidData);
            PbDesignReview r = await Full(true).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return ResponseDtoHelper.NotFound<DesignReviewDto>(PbMessages.NotFound);
            int reasonMin = (int)(await DesignGate.SettingsAsync(Uow, ct))[DesignSettingKeys.ReasonMinChars];
            string note = PbText.Clean(dto.Note);
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "hold": return await HoldAsync(r, dto, reasonMin, now, ct);
                case "close": return await CloseAsync(r, dto, reasonMin, now, ct);
                case "void":
                    if (r.Status == DesignReviewStatus.Void) return Bad<DesignReviewDto>("بازبینی پیش‌تر باطل شده است.");
                    if (r.Status == DesignReviewStatus.Closed ? !InRole(DesignRoles.QaOnly) : !(r.CreatorUserId == Me || InRole(DesignRoles.Resolvers))) return Bad<DesignReviewDto>(DesignRules.NotAllowed);
                    if (note == null || note.Length < reasonMin) return Bad<DesignReviewDto>($"دلیل ابطال دست‌کم {reasonMin} نویسه باشد.");
                    if (DesignLimits.Over(note, DesignLimits.Reason, "دلیل ابطال") is string longVoid) return Bad<DesignReviewDto>(longVoid);
                    r.Status = DesignReviewStatus.Void; r.VoidReason = note;
                    Stamp(r, false);
                    return await SaveAndGetAsync(r.Id, ct);
                case "repeat": return await RepeatAsync(r, ct);
                default: return Bad<DesignReviewDto>("کار نامعتبر است.");
            }
        }

        private async Task<ResponseDto<DesignReviewDto>> HoldAsync(PbDesignReview r, DesignReviewActionDto dto, int reasonMin, DateTimeOffset now, CancellationToken ct)
        {
            if (r.Status != DesignReviewStatus.Planned) return Bad<DesignReviewDto>("فقط بازبینی برنامه‌ریزی‌شده برگزار می‌شود.");
            if (!InRole(DesignRoles.ReviewMakers)) return Bad<DesignReviewDto>(DesignRules.NotAllowed);
            string minutes = PbText.Clean(dto.Minutes);
            if (minutes == null || minutes.Length < reasonMin) return Bad<DesignReviewDto>($"صورتجلسه دست‌کم {reasonMin} نویسه باشد.");
            if (minutes.Length > DesignLimits.Minutes) return Bad<DesignReviewDto>("صورتجلسه بیش از ۴۰۰۰ نویسه است.");
            DateTime held = (dto.HeldDate ?? DateTime.Today).Date;
            if (held > DateTime.Today) return Bad<DesignReviewDto>("تاریخ برگزاری در آینده نیست.");
            State s = await StateAsync(r, ct);
            int min = (int)(await DesignGate.SettingsAsync(Uow, ct))[DesignSettingKeys.MinIndependent];
            if (s.Independent < min) return Bad<DesignReviewDto>($"دست‌کم {DesignRules.Num(min)} شرکت‌کننده مستقل (غیر از تهیه‌کنندگان خروجی‌ها) لازم است؛ فعلاً {DesignRules.Num(s.Independent)}.");
            foreach (PbDesignReviewParticipant p in r.Participants.Where(x => !x.Deleted)) { p.Independent = !s.Authors.Contains(p.UserId); Stamp(p, false); }
            r.Status = DesignReviewStatus.Held; r.HeldDate = held; r.HeldByUserId = Me; r.HeldAt = now; r.Minutes = minutes;
            Stamp(r, false);
            return await SaveAndGetAsync(r.Id, ct);
        }

        private async Task<ResponseDto<DesignReviewDto>> CloseAsync(PbDesignReview r, DesignReviewActionDto dto, int reasonMin, DateTimeOffset now, CancellationToken ct)
        {
            if (r.Status != DesignReviewStatus.Held) return Bad<DesignReviewDto>(r.Status == DesignReviewStatus.Planned ? "ابتدا برگزاری بازبینی با صورتجلسه ثبت شود." : "فقط بازبینی برگزارشده بسته می‌شود.");
            if (!InRole(DesignRoles.ReviewClosers)) return Bad<DesignReviewDto>(DesignRules.NotAllowed);
            if (dto.Outcome == DesignReviewOutcome.None || !Enum.IsDefined(typeof(DesignReviewOutcome), dto.Outcome)) return Bad<DesignReviewDto>("نتیجه بازبینی را انتخاب کنید: پذیرفته، پذیرفته با شرط یا رد.");
            State s = await StateAsync(r, ct);
            if (s.Authors.Contains(Me)) return Bad<DesignReviewDto>("بازبینی را کسی می‌بندد که تهیه‌کننده هیچ‌یک از خروجی‌های بازبینی نباشد.");
            string note = PbText.Clean(dto.Note);
            if (dto.Outcome != DesignReviewOutcome.Accepted && (note == null || note.Length < reasonMin)) return Bad<DesignReviewDto>($"برای نتیجه «پذیرفته با شرط» یا «رد» شرح دست‌کم {reasonMin} نویسه بنویسید.");
            if (note != null && note.Length > DesignLimits.Outcome) return Bad<DesignReviewDto>("شرح بیش از ۲۰۰۰ نویسه است.");
            if (dto.Outcome == DesignReviewOutcome.AcceptedWithConditions && !r.Findings.Any(f => !f.Deleted && f.Status != DesignFindingStatus.Void)) return Bad<DesignReviewDto>("پذیرش مشروط باید شرط‌ها را به صورت یافته (با مالک و مهلت) ثبت کرده باشد.");
            if (dto.Outcome == DesignReviewOutcome.Accepted)
            {
                List<string> blockers = s.Blockers.Where(b => !b.StartsWith("بازبینی هنوز برگزار نشده")).ToList();
                if (blockers.Count > 0) return Bad<DesignReviewDto>("بازبینی با نتیجه «پذیرفته» بسته نمی‌شود: " + string.Join("؛ ", blockers));
            }
            r.Status = DesignReviewStatus.Closed; r.Outcome = dto.Outcome; r.OutcomeNote = note; r.ClosedByUserId = Me; r.ClosedAt = now;
            Stamp(r, false);
            ResponseDto<DesignReviewDto> result = await SaveAndGetAsync(r.Id, ct);
            if (result.Success && dto.Outcome != DesignReviewOutcome.Accepted)
                await _notify.NotifyAsync(new NotifyRequest { SourceKey = "design-review:" + r.Id, Module = "productbank", SourceType = "design-review", SourceId = r.Id, Title = $"بازبینی طراحی {r.Number} بدون پذیرش کامل بسته شد", Link = "/product-bank/design/reviews/" + r.Id, Roles = "ENGINEERING,QA" }, ct);
            return result;
        }

        private async Task<ResponseDto<DesignReviewDto>> RepeatAsync(PbDesignReview r, CancellationToken ct)
        {
            if (!InRole(DesignRoles.ReviewMakers)) return Bad<DesignReviewDto>(DesignRules.NotAllowed);
            if (!(r.Status == DesignReviewStatus.Closed && r.Outcome != DesignReviewOutcome.Accepted)) return Bad<DesignReviewDto>("بازبینی تازه برای بازبینی‌ای ساخته می‌شود که رد یا مشروط بسته شده است.");
            PbDesignPlan plan = await Uow.Set<PbDesignPlan>().AsNoTracking().FirstOrDefaultAsync(p => !p.Deleted && p.ProductId == r.ProductId && p.ProductRevision == r.ProductRevision && p.Status == DesignPlanStatus.Approved, ct);
            if (plan == null) return Bad<DesignReviewDto>("برنامه طراحی تاییدشده‌ای برای این محصول و بازنگری نیست.");
            var save = new SaveDesignReviewDto { PlanId = plan.Id, Type = r.Type, Title = r.Title, PreviousReviewId = r.Id };
            // the same scope: every live input of the kind (new ones included), the outputs again with what they are now, the same participants
            foreach (PbDesignReviewOutput o in r.Outputs.Where(x => !x.Deleted))
                save.Outputs.Add(new SaveDesignOutputDto { Kind = o.Kind, RevisionId = o.RevisionId, BomHeaderId = o.BomHeaderId, OpcRoutingId = o.OpcRoutingId });
            foreach (PbDesignReviewParticipant p in r.Participants.Where(x => !x.Deleted)) save.Participants.Add(new SaveDesignParticipantDto { UserId = p.UserId, RoleText = p.RoleText });
            if (r.Type == DesignReviewType.Review) save.InputIds = r.Inputs.Where(x => !x.Deleted).Select(x => x.InputId).ToList();
            return await SaveAsync(save, ct);
        }

        // ------------------------------------------------------------------------------------------ validation record

        public async Task<ResponseDto<DesignReviewDto>> SaveValidationAsync(SaveDesignValidationDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<DesignReviewDto>(DesignRules.AiNever);
            if (!InRole(DesignRoles.ReviewMakers)) return Bad<DesignReviewDto>(DesignRules.NotAllowed);
            if (dto == null) return Bad<DesignReviewDto>(PbMessages.InvalidData);
            PbDesignReview r = await Full(true).FirstOrDefaultAsync(x => x.Id == dto.ReviewId && !x.Deleted, ct);
            if (r == null) return ResponseDtoHelper.NotFound<DesignReviewDto>(PbMessages.NotFound);
            if (r.Type != DesignReviewType.Validation) return Bad<DesignReviewDto>("این بخش فقط برای بازبینی صحه‌گذاری است.");
            if (r.Status != DesignReviewStatus.Planned && r.Status != DesignReviewStatus.Held) return Bad<DesignReviewDto>("مدرک صحه‌گذاری فقط پیش از بستن بازبینی ثبت می‌شود.");
            int reasonMin = (int)(await DesignGate.SettingsAsync(Uow, ct))[DesignSettingKeys.ReasonMinChars];
            if (dto.Method == DesignValidationMethod.None || !Enum.IsDefined(typeof(DesignValidationMethod), dto.Method)) return Bad<DesignReviewDto>("روش صحه‌گذاری را انتخاب کنید: آزمون، آزمایش میدانی یا شبیه‌سازی.");
            string evidence = PbText.Clean(dto.Evidence);
            if (evidence == null || evidence.Length < reasonMin) return Bad<DesignReviewDto>($"مدرک صحه‌گذاری (مرجع گزارش آزمون یا شبیه‌سازی) دست‌کم {reasonMin} نویسه باشد.");
            if (evidence.Length > DesignLimits.ValidationEvidence) return Bad<DesignReviewDto>("مدرک بیش از ۲۰۰۰ نویسه است.");
            string cref = PbText.Clean(dto.CustomerAcceptanceRef), cnote = PbText.Clean(dto.CustomerAcceptanceNote);
            if (dto.CustomerAcceptanceNotApplicable)
            {
                if (cnote == null || cnote.Length < reasonMin) return Bad<DesignReviewDto>($"برای «مشتری ندارد» توضیح دست‌کم {reasonMin} نویسه بنویسید.");
                cref = null;
            }
            else if (cref == null) return Bad<DesignReviewDto>("مدرک پذیرش مشتری را بنویسید یا «مشتری ندارد» را با توضیح انتخاب کنید.");
            if ((cref ?? string.Empty).Length > DesignLimits.CustomerRef || (cnote ?? string.Empty).Length > DesignLimits.Note) return Bad<DesignReviewDto>("متن پذیرش مشتری بیش از اندازه مجاز است (مدرک ۵۰۰ و توضیح ۱۰۰۰ نویسه).");
            r.ValidationMethod = dto.Method; r.ValidationEvidence = evidence; r.CustomerAcceptanceRef = cref; r.CustomerAcceptanceNotApplicable = dto.CustomerAcceptanceNotApplicable; r.CustomerAcceptanceNote = cnote;
            Stamp(r, false);
            return await SaveAndGetAsync(r.Id, ct);
        }

        // ------------------------------------------------------------------------------------------ findings

        public async Task<ResponseDto<DesignReviewDto>> SaveFindingAsync(SaveDesignFindingDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<DesignReviewDto>(DesignRules.AiNever);
            if (!InRole(DesignRoles.ReviewMakers)) return Bad<DesignReviewDto>(DesignRules.NotAllowed);
            if (dto == null) return Bad<DesignReviewDto>(PbMessages.InvalidData);
            PbDesignReview r = await Full(true).FirstOrDefaultAsync(x => x.Id == dto.ReviewId && !x.Deleted, ct);
            if (r == null) return ResponseDtoHelper.NotFound<DesignReviewDto>(PbMessages.NotFound);
            if (r.Status != DesignReviewStatus.Planned && r.Status != DesignReviewStatus.Held) return Bad<DesignReviewDto>("یافته فقط پیش از بستن بازبینی ثبت می‌شود.");
            string desc = PbText.Clean(dto.Description);
            if (desc == null || desc.Length < 5) return Bad<DesignReviewDto>("شرح یافته را بنویسید.");
            if (desc.Length > DesignLimits.Description) return Bad<DesignReviewDto>("شرح یافته بیش از ۱۰۰۰ نویسه است.");
            if (dto.DueDate.Date < DateTime.Today) return Bad<DesignReviewDto>("مهلت یافته در گذشته است.");
            string person = await DesignQueries.PersonProblemAsync(Uow, new[] { dto.OwnerUserId }, ct);
            if (person != null) return Bad<DesignReviewDto>(person);
            PbDesignFinding f;
            if (dto.Id.HasValue)
            {
                f = r.Findings.FirstOrDefault(x => !x.Deleted && x.Id == dto.Id.Value);
                if (f == null) return ResponseDtoHelper.NotFound<DesignReviewDto>("یافته پیدا نشد.");
                if (f.Status != DesignFindingStatus.Open) return Bad<DesignReviewDto>("فقط یافته باز ویرایش می‌شود.");
                if (f.RaisedByUserId != Me) return Bad<DesignReviewDto>("یافته را فقط ثبت‌کننده آن ویرایش می‌کند.");
            }
            else
            {
                f = new PbDesignFinding { Seq = (r.Findings.Where(x => !x.Deleted).Select(x => (int?)x.Seq).Max() ?? 0) + 1, RaisedByUserId = Me, Status = DesignFindingStatus.Open };
                Stamp(f, true);
                r.Findings.Add(f);
            }
            f.Description = desc; f.OwnerUserId = dto.OwnerUserId; f.DueDate = dto.DueDate.Date;
            Stamp(f, false);
            ResponseDto<DesignReviewDto> res = await SaveAndGetAsync(r.Id, ct);
            if (res.Success)
                await _notify.NotifyAsync(new NotifyRequest { SourceKey = $"design-finding:{r.Id}:{f.Seq}", Module = "productbank", SourceType = "design-review", SourceId = r.Id, Title = $"یافته بازبینی طراحی {r.Number} به شما واگذار شد", Link = "/product-bank/design/reviews/" + r.Id, UserIds = new List<int> { dto.OwnerUserId } }, ct);
            return res;
        }

        public async Task<ResponseDto<DesignReviewDto>> FindingActionAsync(DesignFindingActionDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<DesignReviewDto>(DesignRules.AiNever);
            if (dto == null || PbText.Clean(dto.Action) == null) return Bad<DesignReviewDto>(PbMessages.InvalidData);
            PbDesignFinding f = await Uow.Set<PbDesignFinding>().Include(x => x.Review).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (f == null) return ResponseDtoHelper.NotFound<DesignReviewDto>("یافته پیدا نشد.");
            if (f.Review.Status == DesignReviewStatus.Void) return Bad<DesignReviewDto>("بازبینی باطل است.");
            int reasonMin = (int)(await DesignGate.SettingsAsync(Uow, ct))[DesignSettingKeys.ReasonMinChars];
            string note = PbText.Clean(dto.Note);
            if (DesignLimits.Over(note, DesignLimits.Note, "شرح") is string longNote) return Bad<DesignReviewDto>(longNote);
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "done":
                    if (f.Status != DesignFindingStatus.Open) return Bad<DesignReviewDto>("فقط یافته باز، انجام‌شده اعلام می‌شود.");
                    if (f.OwnerUserId != Me) return Bad<DesignReviewDto>("انجام یافته را مالک آن اعلام می‌کند.");
                    if (note == null || note.Length < 5) return Bad<DesignReviewDto>("شرح اقدام انجام‌شده را بنویسید.");
                    f.Status = DesignFindingStatus.Done; f.DoneAt = now; f.DoneNote = note;
                    break;
                case "close":
                    if (f.Status != DesignFindingStatus.Done) return Bad<DesignReviewDto>("فقط یافته‌ای که مالک آن را انجام‌شده اعلام کرده بسته می‌شود.");
                    if (!InRole(DesignRoles.ReviewClosers)) return Bad<DesignReviewDto>(DesignRules.NotAllowed);
                    if (f.OwnerUserId == Me) return Bad<DesignReviewDto>("تفکیک وظایف: یافته را غیر از مالک آن، نفر دیگری می‌بندد.");
                    if (note == null || note.Length < 5) return Bad<DesignReviewDto>("نتیجه بررسی اقدام را بنویسید.");
                    f.Status = DesignFindingStatus.Closed; f.ClosedByUserId = Me; f.ClosedAt = now; f.CloseNote = note;
                    break;
                case "void":
                    if (f.Status != DesignFindingStatus.Open && f.Status != DesignFindingStatus.Done) return Bad<DesignReviewDto>("فقط یافته باز یا انجام‌شده باطل می‌شود.");
                    if (!InRole(DesignRoles.ReviewClosers)) return Bad<DesignReviewDto>(DesignRules.NotAllowed);
                    if (f.OwnerUserId == Me || f.RaisedByUserId == Me) return Bad<DesignReviewDto>("یافته را نفر سومی، غیر از مالک و ثبت‌کننده، باطل می‌کند.");
                    if (note == null || note.Length < reasonMin) return Bad<DesignReviewDto>($"دلیل ابطال دست‌کم {reasonMin} نویسه باشد.");
                    f.Status = DesignFindingStatus.Void; f.VoidReason = note;
                    break;
                default: return Bad<DesignReviewDto>("کار نامعتبر است.");
            }
            Stamp(f, false);
            return await SaveAndGetAsync(f.ReviewId, ct);
        }

        // ------------------------------------------------------------------------------------------ traceability judgement

        public async Task<ResponseDto<DesignReviewDto>> SaveTraceAsync(SaveDesignTraceDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<DesignReviewDto>(DesignRules.AiNever);
            if (dto == null || dto.Rows == null || dto.Rows.Count == 0) return Bad<DesignReviewDto>(PbMessages.InvalidData);
            PbDesignReview r = await Full(true).FirstOrDefaultAsync(x => x.Id == dto.ReviewId && !x.Deleted, ct);
            if (r == null) return ResponseDtoHelper.NotFound<DesignReviewDto>(PbMessages.NotFound);
            if (r.Type == DesignReviewType.Review) return Bad<DesignReviewDto>("ماتریس ردیابی برای تایید و صحه‌گذاری طراحی است.");
            if (r.Status != DesignReviewStatus.Held) return Bad<DesignReviewDto>("قضاوت معیارها پس از برگزاری و تا پیش از بستن بازبینی ثبت می‌شود.");
            if (!r.Participants.Any(p => !p.Deleted && p.UserId == Me)) return Bad<DesignReviewDto>("فقط شرکت‌کنندگان بازبینی معیارها را قضاوت می‌کنند.");
            State s = await StateAsync(r, ct);
            if (s.Authors.Contains(Me)) return Bad<DesignReviewDto>("تهیه‌کننده خروجی نمی‌تواند همان خروجی را قضاوت کند؛ قضاوت با شرکت‌کننده مستقل است.");
            var seen = new HashSet<(int, int)>();
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (SaveDesignTraceRowDto row in dto.Rows)
            {
                if (!Enum.IsDefined(typeof(DesignTraceResult), row.Result)) return Bad<DesignReviewDto>(PbMessages.InvalidData);
                if (!r.Inputs.Any(x => !x.Deleted && x.InputId == row.InputId)) return Bad<DesignReviewDto>("ورودی در این بازبینی نیست.");
                if (!r.Outputs.Any(x => !x.Deleted && x.Id == row.OutputId)) return Bad<DesignReviewDto>("خروجی در این بازبینی نیست.");
                if (!seen.Add((row.InputId, row.OutputId))) return Bad<DesignReviewDto>("یک ورودی و خروجی دو بار آمده است.");
                PbDesignInput input = s.Live.FirstOrDefault(i => i.Id == row.InputId);
                if (input == null || input.Status == DesignInputStatus.Conflict) return Bad<DesignReviewDto>("ورودی در تعارض یا کنار گذاشته شده است.");
                string evidence = PbText.Clean(row.Evidence);
                if (row.Result != DesignTraceResult.Pending && (evidence == null || evidence.Length < 5)) return Bad<DesignReviewDto>($"برای ورودی {input.Code} مدرک (شرح اندازه‌گیری، آزمون یا بررسی) را بنویسید.");
                if ((evidence ?? string.Empty).Length > DesignLimits.Evidence) return Bad<DesignReviewDto>("مدرک بیش از ۱۰۰۰ نویسه است.");
                if ((PbText.Clean(row.EvidenceRef) ?? string.Empty).Length > DesignLimits.EvidenceRef) return Bad<DesignReviewDto>("مرجع مدرک بیش از ۳۰۰ نویسه است.");
                PbDesignTrace t = r.Traces.FirstOrDefault(x => !x.Deleted && x.InputId == row.InputId && x.OutputId == row.OutputId);
                if (row.Result == DesignTraceResult.Pending)
                {
                    if (t != null) { t.Delete(); Stamp(t, false); }
                    continue;
                }
                if (t == null) { t = new PbDesignTrace { InputId = row.InputId, OutputId = row.OutputId }; Stamp(t, true); r.Traces.Add(t); }
                t.Result = row.Result; t.Evidence = evidence; t.EvidenceRef = PbText.Clean(row.EvidenceRef); t.JudgedByUserId = Me; t.JudgedAt = now;
                Stamp(t, false);
            }
            return await SaveAndGetAsync(r.Id, ct);
        }

        // ------------------------------------------------------------------------------------------ matrix

        public async Task<ResponseDto<DesignMatrixDto>> MatrixAsync(int reviewId, CancellationToken ct)
        {
            ResponseDto<DesignReviewDto> res = await GetAsync(reviewId, ct);
            if (!res.Success) return Bad<DesignMatrixDto>(res.Message);
            DesignReviewDto r = res.Data;
            PbProduct product = await Uow.Set<PbProduct>().AsNoTracking().FirstAsync(p => p.Id == r.ProductId, ct);
            var m = new DesignMatrixDto { ReviewId = r.Id, ReviewNumber = r.Number, ReviewTitle = r.Title, Type = r.Type, Status = r.Status, Outcome = r.Outcome, ProductCode = product.Code, ProductTitle = product.Title, ProductRevision = r.ProductRevision, Outputs = r.Outputs, Uncovered = r.Uncovered };
            foreach (DesignReviewInputRowDto i in r.Inputs)
            {
                var row = new DesignMatrixRowDto { Input = i };
                foreach (DesignOutputDto o in r.Outputs)
                    row.Cells.Add(r.Traces.FirstOrDefault(t => t.InputId == i.InputId && t.OutputId == o.Id) ?? new DesignTraceDto { InputId = i.InputId, InputCode = i.Code, OutputId = o.Id, Result = DesignTraceResult.Pending });
                m.Rows.Add(row);
            }
            return ResponseDtoHelper.Ok(m);
        }
    }
}
