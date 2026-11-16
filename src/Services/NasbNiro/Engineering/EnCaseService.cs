using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.EngineeringDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.Engineering
{
    public interface IEnCaseService
    {
        Task<ResponseDto<PagedList<EnCaseDto>>> GetCasesAsync(EnCaseQuery query, CancellationToken ct);
        Task<ResponseDto<EnCaseDto>> GetCaseAsync(int id, CancellationToken ct);
        Task<ResponseDto<EnGateStateDto>> GetGateStateAsync(int? inquiryId, int? contractId, CancellationToken ct);
        Task<ResponseDto<EnCaseDto>> CaseActionAsync(EnCaseActionDto dto, CancellationToken ct);
    }

    /// <summary>The engineering case: classification (platform or new design) with the maker-checker chain, the tender technical package, the charter review and the close of a case.</summary>
    public class EnCaseService : IEnCaseService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IEdmsService _edms;
        private readonly INotificationService _notify;

        public EnCaseService(IUnitOfWork uow, IInventoryUserContext user, IEdmsService edms, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _edms = edms ?? throw new ArgumentNullException(nameof(edms));
            _notify = notify;
        }

        private int Me => _user.UserId;

        private sealed class Who
        {
            public int Me;
            public bool Writer, Manager, Ceo, Seat, Std;
        }

        private async Task<Who> WhoAsync(CancellationToken ct) => new Who
        {
            Me = Me, Writer = _user.IsInRole(EnRoles.Writers), Manager = _user.IsInRole(EnRoles.Manager), Ceo = _user.IsInRole(EnRoles.Ceo), Seat = await EnActors.IsSeatAsync(_uow, Me, ct), Std = _user.IsInRole(EnRoles.StandardSetters),
        };

        // ---------------------------------------------------------------- the rules (one predicate for the button and the service)

        private static string Need(bool ok, string message) => ok ? null : message;

        private static string ProposeReason(EnCase c, Who w) =>
            !w.Writer ? EnText.NotAllowed : c.Status != EnCaseStatus.Classifying ? "فقط پرونده در حال تعیین نوع پیشنهاد می‌گیرد." : null;

        private static string ChangeReason(EnCase c, Who w) =>
            !w.Manager ? EnText.NotAllowed : w.Seat ? EnText.AiNever : c.Kind != EnCaseKind.Turbine ? "برای قطعات یدکی و خدمات پس از فروش نوع‌بندی وجود ندارد؛ رای فنی را مدیر مهندسی تایید یا برگشت می‌دهد." : c.Status != EnCaseStatus.AwaitingEngConfirm ? "فقط پروندهٔ منتظر تایید مدیر مهندسی تغییر می‌کند." : null;

        private const string StandardMissingText = "استاندارد پروژه هنوز از دیتاشیت کارفرما ثبت نشده است؛ ابتدا استاندارد و مبنای آن (شمارهٔ دیتاشیت و بند) ثبت شود.";

        private static string StandardReason(EnCase c, Who w) =>
            !w.Std ? EnText.NotAllowed : w.Seat ? EnText.AiNever : !EnCaseRules.Open(c.Status) ? "پروندهٔ بسته یا لغوشده استاندارد نمی‌گیرد."
            : c.Status >= EnCaseStatus.HandedOver ? "پس از تحویل مدارک به PMO استاندارد تغییر نمی‌کند؛ ابتدا تحویل برگردانده یا باطل شود." : null;

        private static string ConfirmReason(EnCase c, Who w) =>
            !w.Manager ? EnText.NotAllowed : w.Seat ? EnText.AiNever : c.Status != EnCaseStatus.AwaitingEngConfirm ? "فقط پروندهٔ منتظر تایید مدیر مهندسی تایید می‌شود."
            : !c.ProposedAt.HasValue ? "پیشنهاد نوع پرونده هنوز ثبت نشده است."
            : (c.Kind != EnCaseKind.Turbine && string.IsNullOrWhiteSpace(c.OpinionText)) ? "رای فنی مهندسی هنوز ثبت نشده است."
            : string.IsNullOrWhiteSpace(c.ReferenceCode) ? StandardMissingText
            : EnEditors.Touched(0, c.EditorUserIds, w.Me) || c.ProposedByUserId == w.Me ? EnText.SameUser + " مدیری که پیشنهاد را ساخته یا تغییر داده تایید نمی‌کند." : null;

        private static string OpinionReason(EnCase c, Who w) =>
            !w.Writer ? EnText.NotAllowed : w.Seat ? EnText.AiNever : c.Kind == EnCaseKind.Turbine ? "رای فنی فقط برای پروندهٔ قطعات یدکی و خدمات پس از فروش ثبت می‌شود."
            : c.Status != EnCaseStatus.Classifying && c.Status != EnCaseStatus.AwaitingEngConfirm ? "رای فنی فقط تا پیش از تایید مدیر مهندسی ثبت یا ویرایش می‌شود." : null;

        private static string ReturnReasonOf(EnCase c, Who w) =>
            c.Status == EnCaseStatus.AwaitingEngConfirm ? (w.Manager ? (w.Seat ? EnText.AiNever : null) : EnText.NotAllowed)
            : c.Status == EnCaseStatus.AwaitingCeoApproval ? (w.Ceo ? (w.Seat ? EnText.AiNever : null) : EnText.NotAllowed)
            : "فقط پروندهٔ منتظر تایید برگشت می‌خورد.";

        private static string ApproveReason(EnCase c, Who w, string gap = null) =>
            !w.Ceo ? EnText.NotAllowed : w.Seat ? EnText.AiNever : c.Status != EnCaseStatus.AwaitingCeoApproval ? "فقط پروندهٔ منتظر تصویب مدیرعامل تصویب می‌شود."
            : EnEditors.Touched(0, c.EditorUserIds, w.Me) || c.ProposedByUserId == w.Me || c.EngConfirmedByUserId == w.Me ? EnText.SameUser
            : string.IsNullOrWhiteSpace(c.ReferenceCode) ? StandardMissingText
            : c.ClassificationHash != EnCaseRules.ClassHash(c) ? "محتوای پیشنهاد پس از تایید مدیر مهندسی تغییر کرده است؛ پرونده را برگردانید." : gap;

        private static string ReclassifyReason(EnCase c, Who w) =>
            !w.Manager ? EnText.NotAllowed : w.Seat ? EnText.AiNever : c.Kind != EnCaseKind.Turbine ? "برای قطعات یدکی و خدمات پس از فروش نوع‌بندی پلتفرم یا طراحی جدید وجود ندارد."
            : c.Status != EnCaseStatus.TenderPreparation && c.Status != EnCaseStatus.TenderIssued && c.Status != EnCaseStatus.Awarded && c.Status != EnCaseStatus.InEngineering ? "تغییر نوع فقط پس از تصویب و پیش از تحویل مدارک به PMO ممکن است." : null;

        private static string ConceptReason(EnCase c, Who w) =>
            !w.Writer ? EnText.NotAllowed : c.Status == EnCaseStatus.Classifying || c.Status == EnCaseStatus.AwaitingEngConfirm || c.Status == EnCaseStatus.AwaitingCeoApproval || c.Status == EnCaseStatus.TenderPreparation ? null
            : "یادداشت طراحی مفهومی فقط تا پیش از ابلاغ بسته فنی ثبت می‌شود.";

        private static string ReleaseReason(EnCase c, Who w, int docs, HashSet<int> docMakers) =>
            !w.Manager ? EnText.NotAllowed : w.Seat ? EnText.AiNever : c.Status != EnCaseStatus.TenderPreparation ? "فقط پرونده در مرحلهٔ تهیهٔ بسته فنی ابلاغ می‌شود."
            : !c.TenderPackageId.HasValue ? "بستهٔ فنی هنوز ساخته نشده است."
            : (c.Kind != EnCaseKind.Turbine && string.IsNullOrWhiteSpace(c.OpinionText)) ? "رای فنی تاییدشدهٔ مهندسی برای ابلاغ بستهٔ فنی لازم است."
            : (c.Classification == EnClassification.New && docs < 1) ? "برای طراحی جدید دست‌کم یک سند مفهومی در بسته لازم است."
            : (c.Classification == EnClassification.New && EnText.Clean(c.ConceptNote) == null) ? "برای طراحی جدید یادداشت طراحی مفهومی (آنچه تعریف شده و آنچه فرض شده) الزامی است."
            : EnEditors.Touched(0, c.EditorUserIds, w.Me) || docMakers.Contains(w.Me) ? EnText.SameUser + " مدیری که بسته را ساخته یا ویرایش کرده ابلاغ نمی‌کند." : null;

        private static string CharterReason(EnCase c, Who w, PcProjectCharter charter) =>
            !w.Manager ? EnText.NotAllowed : w.Seat ? EnText.AiNever : !c.ContractId.HasValue ? "این پرونده قرارداد ندارد."
            : c.Status != EnCaseStatus.Awarded && c.Status != EnCaseStatus.InEngineering ? "بررسی منشور پس از برنده‌شدن و پیش از تحویل مدارک ممکن است."
            : charter == null ? "منشور پروژه هنوز ثبت نشده است." : !charter.ManagerAcceptedAt.HasValue ? "مدیر پروژه هنوز منشور را نپذیرفته است." : charter.ApprovedAt.HasValue ? "منشور قبلاً توسط مدیرعامل تایید شده است."
            : (c.CharterReviewOutcome != EnCharterOutcome.None && c.CharterHash == EnCaseRules.CharterHash(charter)) ? "این محتوای منشور قبلاً بررسی شده است." : null;

        private static string CancelReason(EnCase c, Who w) =>
            !w.Manager ? EnText.NotAllowed : w.Seat ? EnText.AiNever : c.InquiryId.HasValue ? "پروندهٔ استعلام با نتیجهٔ استعلام بسته می‌شود."
            : c.Status == EnCaseStatus.HandedOver || c.Status == EnCaseStatus.Accepted || c.Status == EnCaseStatus.Released || !EnCaseRules.Open(c.Status) ? "این پرونده دیگر لغو نمی‌شود." : null;

        // ---------------------------------------------------------------- read

        private async Task<List<EnCaseDto>> DtosAsync(List<EnCase> rows, bool withEvents, CancellationToken ct)
        {
            Who w = await WhoAsync(ct);
            List<int> inquiryIds = rows.Where(r => r.InquiryId.HasValue).Select(r => r.InquiryId.Value).ToList();
            List<int> contractIds = rows.Where(r => r.ContractId.HasValue).Select(r => r.ContractId.Value).ToList();
            List<int> caseIds = rows.Select(r => r.Id).ToList();
            var inquiries = await _uow.Set<SlInquiry>().AsNoTracking().Where(i => inquiryIds.Contains(i.Id)).Select(i => new { i.Id, i.Number, i.Status, i.CustomerId }).ToListAsync(ct);
            var contracts = await _uow.Set<ContractEntity>().AsNoTracking().Where(c => contractIds.Contains(c.Id)).Select(c => new { c.Id, c.Code, c.Employer }).ToListAsync(ct);
            List<int> customerIds = inquiries.Select(i => i.CustomerId).Distinct().ToList();
            Dictionary<int, string> customers = await _uow.Set<FnCustomer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            List<int> productIds = rows.SelectMany(r => new[] { r.ProductId, r.ProposedProductId }).Where(x => x.HasValue).Select(x => x.Value).Distinct().ToList();
            Dictionary<int, string> products = await _uow.Set<PbProduct>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
            Dictionary<int, string> names = await EnUsers.NamesAsync(_uow, rows.SelectMany(r => new int?[] { r.OpinionByUserId, r.ProposedByUserId, r.EngConfirmedByUserId, r.CeoApprovedByUserId, r.TenderReleasedByUserId, r.CharterReviewedByUserId }), ct);
            int[] seats = await EnActors.SeatIdsAsync(_uow, ct);
            var clar = await _uow.Set<EnClarification>().AsNoTracking().Where(x => !x.Deleted && caseIds.Contains(x.CaseId) && x.Status != EnClarStatus.Closed && x.Status != EnClarStatus.Void).Select(x => x.CaseId).ToListAsync(ct);
            var handovers = await _uow.Set<EnHandover>().AsNoTracking().Where(h => !h.Deleted && caseIds.Contains(h.CaseId) && (h.Status == EnHandoverStatus.Draft || h.Status == EnHandoverStatus.Submitted || h.Status == EnHandoverStatus.Returned || h.Status == EnHandoverStatus.Accepted)).OrderBy(h => h.Id).Select(h => new { h.CaseId, h.Id }).ToListAsync(ct);
            Dictionary<int, List<EnCaseEvent>> events = withEvents ? (await _uow.Set<EnCaseEvent>().AsNoTracking().Where(e => caseIds.Contains(e.CaseId)).OrderBy(e => e.Id).ToListAsync(ct)).GroupBy(e => e.CaseId).ToDictionary(g => g.Key, g => g.ToList()) : new Dictionary<int, List<EnCaseEvent>>();
            Dictionary<int, string> eventUsers = withEvents ? await EnUsers.NamesAsync(_uow, events.Values.SelectMany(l => l).Select(e => e.UserId), ct) : new Dictionary<int, string>();
            List<EnCaseDto> result = new List<EnCaseDto>();
            foreach (EnCase c in rows)
            {
                var inq = inquiries.FirstOrDefault(i => i.Id == c.InquiryId);
                var con = contracts.FirstOrDefault(i => i.Id == c.ContractId);
                int docs = 0; string packageCode = null; bool stale = false; HashSet<int> makers = new HashSet<int>();
                if (c.TenderPackageId.HasValue)
                {
                    packageCode = await _uow.Set<PcTenderPackage>().AsNoTracking().Where(p => p.Id == c.TenderPackageId.Value).Select(p => p.Code).FirstOrDefaultAsync(ct);
                    var pd = await _uow.Set<PcTenderDocument>().AsNoTracking().Where(d => d.PackageId == c.TenderPackageId.Value && !d.Deleted).Select(d => new { d.CreatorUserId, d.IssuedByUserId }).ToListAsync(ct);
                    docs = pd.Count;
                    foreach (var d in pd) { if (!seats.Contains(d.CreatorUserId)) makers.Add(d.CreatorUserId); if (d.IssuedByUserId.HasValue && !seats.Contains(d.IssuedByUserId.Value)) makers.Add(d.IssuedByUserId.Value); }
                    stale = c.Status == EnCaseStatus.TenderIssued && c.TenderHash != await EnCaseRules.TenderHashAsync(_uow, c, ct);
                }
                PcProjectCharter charter = c.ContractId.HasValue ? await EnCaseRules.CharterAsync(_uow, c.ContractId.Value, ct) : null;
                bool platform = c.Classification == EnClassification.Platform && c.ProductId.HasValue;
                int missing = 0; bool incomplete = false;
                if (platform)
                {
                    incomplete = !await EnPlatformRules.IsCompleteAsync(_uow, c.ProductId.Value, c.ReferenceCode, ct);
                    missing = await _uow.Set<EnPlatformDocument>().AsNoTracking().CountAsync(d => d.Dossier.ProductId == c.ProductId.Value && d.Dossier.ReferenceCode == c.ReferenceCode && !d.Dossier.Deleted && d.Required && d.State == EnLineState.Missing, ct);
                }
                string propose = ProposeReason(c, w), change = ChangeReason(c, w), confirm = ConfirmReason(c, w), approve = ApproveReason(c, w, await EnGaps.ApproveRefusalAsync(_uow, c, ct)), ret = ReturnReasonOf(c, w), reclass = ReclassifyReason(c, w), concept = ConceptReason(c, w);
                string opinionR = OpinionReason(c, w);
                string release = ReleaseReason(c, w, docs, makers), charterR = CharterReason(c, w, charter), cancel = CancelReason(c, w), setStd = StandardReason(c, w);
                EnCaseDto dto = new EnCaseDto
                {
                    Id = c.Id, Kind = (int)c.Kind, KindText = EnText.Kind(c.Kind), OpinionText = c.OpinionText, OpinionByName = names.Name(c.OpinionByUserId), OpinionAtText = EnText.Stamp(c.OpinionAt), HandoverListCode = c.HandoverListCode, CanSaveOpinion = opinionR == null, SaveOpinionBlockedReason = opinionR, ReferenceCode = c.ReferenceCode, StandardEvidence = c.StandardEvidence, StandardAttachmentId = c.StandardAttachmentId, StandardMissing = string.IsNullOrWhiteSpace(c.ReferenceCode), CanSetStandard = setStd == null, SetStandardBlockedReason = setStd, Number = c.Number, Title = c.Title, InquiryId = c.InquiryId, InquiryNumber = inq?.Number, InquiryStatus = inq == null ? (int?)null : (int)inq.Status, ContractId = c.ContractId, ContractCode = con?.Code,
                    CustomerName = inq != null && customers.TryGetValue(inq.CustomerId, out string cn) ? cn : con?.Employer, CustomerRequirementNote = c.CustomerRequirementNote, Status = (int)c.Status, StatusText = EnText.Case(c.Status),
                    Classification = (int)c.Classification, ClassificationText = EnText.Classification(c.Classification), ProductId = c.ProductId, ProductCode = c.ProductId.HasValue && products.TryGetValue(c.ProductId.Value, out string pc) ? pc : null,
                    PlatformWithChanges = c.PlatformWithChanges, ChangeNote = c.ChangeNote,
                    ProposedClassification = (int)c.ProposedClassification, ProposedClassificationText = EnText.Classification(c.ProposedClassification), ProposedProductId = c.ProposedProductId,
                    ProposedProductCode = c.ProposedProductId.HasValue && products.TryGetValue(c.ProposedProductId.Value, out string ppc) ? ppc : null, ProposedWithChanges = c.ProposedWithChanges, ProposedChangeNote = c.ProposedChangeNote,
                    ProposalBasis = c.ProposalBasis, ProposedByUserId = c.ProposedByUserId, ProposedByName = names.Name(c.ProposedByUserId), ProposedByAgent = c.ProposedByUserId.HasValue && seats.Contains(c.ProposedByUserId.Value), ProposedAtText = EnText.Stamp(c.ProposedAt),
                    EngConfirmedByName = names.Name(c.EngConfirmedByUserId), EngConfirmedAtText = EnText.Stamp(c.EngConfirmedAt), CeoApprovedByName = names.Name(c.CeoApprovedByUserId), CeoApprovedAtText = EnText.Stamp(c.CeoApprovedAt), ReturnReason = c.ReturnReason,
                    PlatformIncomplete = platform && incomplete, PlatformMissingRequired = missing,
                    PlatformWarning = platform && incomplete ? "مدارک این پلتفرم هنوز کامل و تایید نشده است (" + EnText.Num(missing) + " مدرک الزامی کم است)؛ این فقط هشدار است و بسته فنی مناقصه را متوقف نمی‌کند، اما تحویل مدارک به PMO بدون آن بسته می‌ماند." : null,
                    TenderPackageId = c.TenderPackageId, TenderPackageCode = packageCode, TenderDocumentCount = docs, TenderReleasedByName = names.Name(c.TenderReleasedByUserId), TenderReleasedAtText = EnText.Stamp(c.TenderReleasedAt), TenderStale = stale, ConceptNote = c.ConceptNote,
                    CharterReviewOutcome = (int)c.CharterReviewOutcome, CharterReviewOutcomeText = EnText.Charter(c.CharterReviewOutcome), CharterReviewedByName = names.Name(c.CharterReviewedByUserId), CharterReviewedAtText = EnText.Stamp(c.CharterReviewedAt),
                    CharterReviewNote = c.CharterReviewNote, CharterExists = charter != null, CharterStale = charter != null && c.CharterReviewOutcome != EnCharterOutcome.None && c.CharterHash != EnCaseRules.CharterHash(charter),
                    OpenClarifications = clar.Count(x => x == c.Id), OpenHandoverId = handovers.Where(h => h.CaseId == c.Id).Select(h => (int?)h.Id).FirstOrDefault(), ClosedReason = c.ClosedReason,
                    TouchedByMe = EnEditors.Has(c.EditorUserIds, Me), RowVersion = EnConcurrency.Text(c.RowVersion),
                    CanPropose = propose == null, ProposeBlockedReason = propose, CanChange = change == null, ChangeBlockedReason = change, CanConfirm = confirm == null, ConfirmBlockedReason = confirm,
                    CanApprove = approve == null, ApproveBlockedReason = approve, CanReturn = ret == null, ReturnBlockedReason = ret, CanReclassify = reclass == null, ReclassifyBlockedReason = reclass,
                    CanSaveConcept = concept == null, SaveConceptBlockedReason = concept, CanReleaseTender = release == null, ReleaseTenderBlockedReason = release, CanCharterReview = charterR == null, CharterReviewBlockedReason = charterR,
                    CanCancel = cancel == null, CancelBlockedReason = cancel,
                };
                if (withEvents && events.TryGetValue(c.Id, out List<EnCaseEvent> list))
                    dto.Events = list.Select(e => new EnCaseEventDto { Kind = e.Kind, At = e.At, AtText = EnText.Stamp(e.At), UserId = e.UserId, UserName = eventUsers.Name(e.UserId), Note = e.Note }).ToList();
                EnStandardGap gapLive = await EnGaps.LiveAsync(_uow, c.Id, ct);
                if (gapLive != null) dto.StandardGap = new EnGapSummaryDto { Id = gapLive.Id, Number = gapLive.Number, Status = (int)gapLive.Status, StatusText = EnStandardGapService.StatusText(gapLive.Status), Summary = gapLive.Summary };
                result.Add(dto);
            }
            return result;
        }

        public async Task<ResponseDto<PagedList<EnCaseDto>>> GetCasesAsync(EnCaseQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<PagedList<EnCaseDto>>(EnText.NotAllowed);
            query ??= new EnCaseQuery();
            IQueryable<EnCase> q = _uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted);
            if (query.Status.HasValue) { EnCaseStatus s = (EnCaseStatus)query.Status.Value; q = q.Where(c => c.Status == s); }
            if (query.Classification.HasValue) { EnClassification k = (EnClassification)query.Classification.Value; q = q.Where(c => c.Classification == k); }
            if (query.InquiryId.HasValue) q = q.Where(c => c.InquiryId == query.InquiryId.Value);
            if (query.ContractId.HasValue) q = q.Where(c => c.ContractId == query.ContractId.Value);
            if (query.OnlyOpen) q = q.Where(c => c.Status != EnCaseStatus.ClosedLost && c.Status != EnCaseStatus.Cancelled && c.Status != EnCaseStatus.Released);
            string text = EnText.Clean(query.Contains);
            if (text != null) q = q.Where(c => c.Number.Contains(text) || c.Title.Contains(text));
            return ResponseDtoHelper.Ok(await EnPaging.CutAsync(q.OrderByDescending(c => c.Id), query.PageNumber, query.PageSize, rows => DtosAsync(rows, false, ct), ct));
        }

        public async Task<ResponseDto<EnCaseDto>> GetCaseAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<EnCaseDto>(EnText.NotAllowed);
            EnCase c = await _uow.Set<EnCase>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (c == null) return EnText.NotFound<EnCaseDto>(EnText.NotFoundCase);
            return ResponseDtoHelper.Ok((await DtosAsync(new List<EnCase> { c }, true, ct))[0]);
        }

        public async Task<ResponseDto<EnGateStateDto>> GetGateStateAsync(int? inquiryId, int? contractId, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<EnGateStateDto>(EnText.NotAllowed);
            if (!inquiryId.HasValue && !contractId.HasValue) return EnText.Bad<EnGateStateDto>(EnText.InvalidData);
            EnGateResult g = inquiryId.HasValue ? await EnGates.ForInquiryAsync(_uow, inquiryId.Value, ct) : await EnGates.ForContractAsync(_uow, contractId.Value, ct);
            string reason = g.Reason;
            if (!inquiryId.HasValue && g.Controlled) reason = await EnGates.InstantiateRefusalAsync(_uow, contractId.Value, ct);
            return ResponseDtoHelper.Ok(new EnGateStateDto
            {
                Controlled = g.Controlled, Legacy = g.Legacy, LegacyNote = g.LegacyNote, CaseId = g.Case?.Id, CaseNumber = g.Case?.Number, CaseStatus = g.Case == null ? (int?)null : (int)g.Case.Status, CaseStatusText = g.Case == null ? null : EnText.Case(g.Case.Status),
                Passed = g.Legacy || !g.Controlled || reason == null, BlockedReason = reason,
            });
        }

        // ---------------------------------------------------------------- actions

        private static readonly string[] Actions = { "open", "propose", "change", "confirm", "return", "approve", "reclassify", "save-concept", "draft-tender", "revalidate-tender", "release-tender", "award", "close", "cancel", "charter-confirm", "charter-return", "set-standard", "save-opinion" };

        private async Task<string> ProductProblemAsync(int? productId, EnClassification kind)
        {
            if (kind != EnClassification.Platform) return null;
            if (!productId.HasValue) return "برای نوع پلتفرم، محصول بانک را مشخص کنید.";
            return await _uow.Set<PbProduct>().AsNoTracking().AnyAsync(p => p.Id == productId.Value && !p.Deleted && p.IsActive) ? null : "محصول فعال با این شناسه در بانک محصول نیست.";
        }

        public async Task<ResponseDto<EnCaseDto>> CaseActionAsync(EnCaseActionDto dto, CancellationToken ct)
        {
            if (dto == null) return EnText.Bad<EnCaseDto>(EnText.InvalidData);
            string action = (dto.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (!Actions.Contains(action)) return EnText.Bad<EnCaseDto>("عمل نامعتبر است (" + string.Join("، ", Actions) + ").");
            Who w = await WhoAsync(ct);
            DateTimeOffset now = DateTimeOffset.Now;
            if (action == "open") return await OpenAsync(dto, w, now, ct);
            if (!dto.Id.HasValue) return EnText.Bad<EnCaseDto>(EnText.InvalidData);
            EnCase c = await _uow.Set<EnCase>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
            if (c == null) return EnText.NotFound<EnCaseDto>(EnText.NotFoundCase);
            if (!EnConcurrency.Apply(_uow, c, dto.RowVersion)) return EnText.Bad<EnCaseDto>(EnText.InvalidData);
            string note = EnText.Clean(dto.Note);
            string why;
            switch (action)
            {
                case "propose":
                {
                    why = ProposeReason(c, w);
                    if (why != null) return EnText.Bad<EnCaseDto>(why);
                    string basis = EnText.Clean(dto.Basis);
                    if (basis == null || basis.Length < 10) return EnText.Bad<EnCaseDto>("مبنای پیشنهاد (شواهد) را بنویسید.");
                    why = EnText.Over(basis, 2000, "مبنای پیشنهاد") ?? EnText.Over(dto.ChangeNote, 1000, "یادداشت تغییرات");
                    if (why != null) return EnText.Bad<EnCaseDto>(why);
                    c.ProposalBasis = basis;
                    if (c.Kind != EnCaseKind.Turbine)
                    {
                        // spare parts and service: no classification; the related platform (optional) and the evidence are recorded, the opinion completes the proposal
                        if (dto.ProductId.HasValue && !await _uow.Set<PbProduct>().AsNoTracking().AnyAsync(p => p.Id == dto.ProductId.Value && !p.Deleted && p.IsActive, ct)) return EnText.Bad<EnCaseDto>("محصول فعال با این شناسه در بانک محصول نیست.");
                        c.ProposedClassification = EnClassification.None; c.ProposedProductId = dto.ProductId; c.ProposedWithChanges = false; c.ProposedChangeNote = null;
                        c.ProposedByUserId = Me; c.ProposedAt = now; c.ReturnReason = null;
                        if (!w.Seat) c.EditorUserIds = EnEditors.With(c.EditorUserIds, Me);
                        if (c.Status == EnCaseStatus.Classifying && !string.IsNullOrWhiteSpace(c.OpinionText)) c.Status = EnCaseStatus.AwaitingEngConfirm;
                        EnEvents.Add(_uow, c, "proposed", Me, (c.ProposedProductId.HasValue ? "پلتفرم مرتبط پیشنهاد شد — " : "پلتفرم مرتبطی مشخص نشد — ") + basis);
                        break;
                    }
                    if (dto.Classification == 0) { EnEvents.Add(_uow, c, "analysed", Me, basis); break; }   // the agent could not decide: only the evidence is stored, an engineer picks
                    EnClassification kind = (EnClassification)dto.Classification;
                    if (kind != EnClassification.Platform && kind != EnClassification.New) return EnText.Bad<EnCaseDto>(EnText.InvalidData);
                    why = await ProductProblemAsync(dto.ProductId, kind);
                    if (why != null) return EnText.Bad<EnCaseDto>(why);
                    ApplyProposal(c, kind, dto);
                    c.ProposedByUserId = Me; c.ProposedAt = now; c.ReturnReason = null; c.Status = EnCaseStatus.AwaitingEngConfirm;
                    if (!w.Seat) c.EditorUserIds = EnEditors.With(c.EditorUserIds, Me);
                    EnEvents.Add(_uow, c, "proposed", Me, EnText.Classification(kind) + " — " + basis);
                    break;
                }
                case "change":
                {
                    why = ChangeReason(c, w);
                    if (why != null) return EnText.Bad<EnCaseDto>(why);
                    EnClassification kind = (EnClassification)dto.Classification;
                    if (kind != EnClassification.Platform && kind != EnClassification.New) return EnText.Bad<EnCaseDto>(EnText.InvalidData);
                    why = await ProductProblemAsync(dto.ProductId, kind) ?? EnText.Over(dto.ChangeNote, 1000, "یادداشت تغییرات") ?? EnText.ReasonShort(note, EnRules.ReasonMin, "دلیل تغییر");
                    if (why != null) return EnText.Bad<EnCaseDto>(why);
                    ApplyProposal(c, kind, dto);
                    c.ProposalBasis = EnText.Cut((c.ProposalBasis ?? string.Empty) + " | تغییر مدیر مهندسی: " + note, 2000);
                    c.EditorUserIds = EnEditors.With(c.EditorUserIds, Me);   // he changed it: another manager confirms
                    EnEvents.Add(_uow, c, "changed", Me, EnText.Classification(kind) + " — " + note);
                    break;
                }
                case "save-opinion":
                {
                    why = OpinionReason(c, w);
                    string opinion = EnText.Clean(dto.OpinionText);
                    if (why == null && (opinion == null || opinion.Length < 30)) why = "رای فنی مهندسی (پاسخ فنی به درخواست) دست‌کم ۳۰ نویسه باشد.";
                    why ??= EnText.Over(opinion, 2000, "رای فنی");
                    if (why != null) return EnText.Bad<EnCaseDto>(why);
                    c.OpinionText = opinion; c.OpinionByUserId = Me; c.OpinionAt = now; c.EditorUserIds = EnEditors.With(c.EditorUserIds, Me);   // the writer never confirms it
                    if (c.Status == EnCaseStatus.Classifying && c.ProposedAt.HasValue) c.Status = EnCaseStatus.AwaitingEngConfirm;
                    EnEvents.Add(_uow, c, "opinion-saved", Me, null);
                    break;
                }
                case "confirm" when c.Kind != EnCaseKind.Turbine:
                {
                    why = ConfirmReason(c, w);
                    string list = EnDossierBuilder.NormCode(dto.HandoverListCode);
                    if (why == null && list != null) why = EnText.Over(list, 60, "کد فهرست مدارک") ?? (await EnReferenceRules.CurrentAsync(_uow, false, ct, list) == null ? "فهرست مدارک مرجع تاییدشده‌ای با این کد وجود ندارد." : null);
                    if (why != null) return EnText.Bad<EnCaseDto>(why);
                    c.HandoverListCode = list; c.Classification = EnClassification.None; c.ProductId = c.ProposedProductId; c.PlatformWithChanges = false; c.ChangeNote = null;
                    c.ClassificationHash = EnCaseRules.ClassHash(c); c.EngConfirmedByUserId = Me; c.EngConfirmedAt = now; c.ReturnReason = null;
                    bool open = c.InquiryId.HasValue && await _uow.Set<SlInquiry>().AsNoTracking().AnyAsync(i => i.Id == c.InquiryId.Value && i.Status != SlInquiryStatus.Won, ct);
                    c.Status = open ? EnCaseStatus.TenderPreparation : EnCaseStatus.Awarded;   // no CEO classification gate for spare parts and service
                    EnEvents.Add(_uow, c, "eng-confirmed", Me, "رای فنی تایید شد");
                    break;
                }
                case "confirm":
                    why = ConfirmReason(c, w);
                    if (why != null) return EnText.Bad<EnCaseDto>(why);
                    c.ClassificationHash = EnCaseRules.ClassHash(c); c.EngConfirmedByUserId = Me; c.EngConfirmedAt = now; c.Status = EnCaseStatus.AwaitingCeoApproval;
                    EnEvents.Add(_uow, c, "eng-confirmed", Me, null);
                    break;
                case "set-standard":
                {
                    why = StandardReason(c, w);
                    string code = EnDossierBuilder.NormCode(dto.ReferenceCode), evidence = EnText.Clean(dto.StandardEvidence);
                    if (why == null) why = code == null ? "استاندارد پروژه را از دیتاشیت کارفرما بنویسید." : EnText.Over(code, 40, "کد استاندارد") ?? EnText.ReasonShort(evidence, 5, "مبنای استاندارد (شمارهٔ دیتاشیت و بند)") ?? EnText.Over(evidence, 500, "مبنای استاندارد");
                    bool change = c.ReferenceCode != null && c.ReferenceCode != code;
                    if (why == null && change) why = EnText.ReasonShort(note, EnRules.ReasonMin, "دلیل تغییر استاندارد");
                    if (why == null && dto.StandardAttachmentId.HasValue) why = await EnFiles.CheckOwnAsync(_uow, dto.StandardAttachmentId.Value, Me, ct);
                    if (why != null) return EnText.Bad<EnCaseDto>(why);
                    c.ReferenceCode = code; c.StandardEvidence = evidence; c.StandardAttachmentId = dto.StandardAttachmentId;
                    if (change && c.Status != EnCaseStatus.Classifying)
                    {
                        // a changed standard after the manager's confirmation or the CEO's approval is a new decision round (the approval bound to the old content)
                        bool decided = c.Status != EnCaseStatus.AwaitingEngConfirm;
                        c.Status = EnCaseStatus.AwaitingEngConfirm; c.EngConfirmedByUserId = null; c.EngConfirmedAt = null; c.CeoApprovedByUserId = null; c.CeoApprovedAt = null; c.ClassificationHash = null;
                        c.TenderReleasedByUserId = null; c.TenderReleasedAt = null; c.TenderHash = null; c.ReturnReason = EnText.Cut("استاندارد پروژه تغییر کرد: " + note, 1000);
                        if (decided) { c.ProposedClassification = c.Classification == EnClassification.None ? c.ProposedClassification : c.Classification; if (c.Classification != EnClassification.None) { c.ProposedProductId = c.ProductId; c.ProposedWithChanges = c.PlatformWithChanges; c.ProposedChangeNote = c.ChangeNote; } }
                    }
                    EnEvents.Add(_uow, c, change ? "standard-changed" : "standard-set", Me, code + " — " + evidence + (change ? " — " + note : string.Empty));
                    break;
                }
                case "return":
                    why = ReturnReasonOf(c, w) ?? EnText.ReasonShort(note, EnRules.ReasonMin, "دلیل برگشت");
                    if (why != null) return EnText.Bad<EnCaseDto>(why);
                    if (c.Status == EnCaseStatus.AwaitingEngConfirm)
                    {
                        c.Status = EnCaseStatus.Classifying; c.ProposedAt = null; c.ProposalBasis = null; c.ProposedClassification = EnClassification.None; c.ProposedProductId = null; c.ProposedWithChanges = false; c.ProposedChangeNote = null;
                        EnEvents.Add(_uow, c, "returned-by-manager", Me, note);
                    }
                    else
                    {
                        c.Status = EnCaseStatus.AwaitingEngConfirm; c.EngConfirmedByUserId = null; c.EngConfirmedAt = null; c.ClassificationHash = null;
                        EnEvents.Add(_uow, c, "returned-by-ceo", Me, note);
                    }
                    c.ReturnReason = EnText.Cut(note, 1000);
                    break;
                case "approve":
                {
                    why = ApproveReason(c, w, await EnGaps.ApproveRefusalAsync(_uow, c, ct));
                    if (why != null) return EnText.Bad<EnCaseDto>(why);
                    c.ApprovedGapHash = (await EnGaps.LiveAsync(_uow, c.Id, ct))?.ContentHash;
                    c.Classification = c.ProposedClassification; c.ProductId = c.ProposedProductId; c.PlatformWithChanges = c.ProposedWithChanges; c.ChangeNote = c.ProposedChangeNote;
                    c.CeoApprovedByUserId = Me; c.CeoApprovedAt = now; c.ReturnReason = null;
                    // a platform ordered under a standard gets its own document set (P, S): the lines of the Approved list of S, files reused from the other dossiers of the product
                    if (c.Classification == EnClassification.Platform && c.ProductId.HasValue) await EnDossierBuilder.EnsureAsync(_uow, c.ProductId.Value, c.ReferenceCode, c.StandardEvidence, Me, now, ct);
                    bool inquiryOpen = c.InquiryId.HasValue && await _uow.Set<SlInquiry>().AsNoTracking().AnyAsync(i => i.Id == c.InquiryId.Value && i.Status != SlInquiryStatus.Won, ct);
                    c.Status = inquiryOpen ? EnCaseStatus.TenderPreparation : EnCaseStatus.Awarded;
                    EnEvents.Add(_uow, c, "ceo-approved", Me, EnText.Classification(c.Classification));
                    break;
                }
                case "reclassify":
                    why = ReclassifyReason(c, w) ?? EnText.ReasonShort(note, EnRules.ReasonMin, "دلیل تغییر نوع");
                    if (why != null) return EnText.Bad<EnCaseDto>(why);
                    c.Status = EnCaseStatus.AwaitingEngConfirm; c.EngConfirmedByUserId = null; c.EngConfirmedAt = null; c.CeoApprovedByUserId = null; c.CeoApprovedAt = null; c.ClassificationHash = null;
                    c.TenderReleasedByUserId = null; c.TenderReleasedAt = null; c.TenderHash = null; c.ReturnReason = EnText.Cut(note, 1000);
                    c.ProposedClassification = c.Classification; c.ProposedProductId = c.ProductId; c.ProposedWithChanges = c.PlatformWithChanges; c.ProposedChangeNote = c.ChangeNote;
                    c.EditorUserIds = EnEditors.With(c.EditorUserIds, Me);
                    EnEvents.Add(_uow, c, "reclassify-requested", Me, note);
                    break;
                case "save-concept":
                    why = ConceptReason(c, w);
                    if (why == null && (EnText.Clean(dto.ConceptNote) == null || dto.ConceptNote.Trim().Length < 10)) why = "یادداشت طراحی مفهومی را بنویسید (آنچه تعریف شده و آنچه فرض شده).";
                    why ??= EnText.Over(dto.ConceptNote, 2000, "یادداشت طراحی مفهومی");
                    if (why != null) return EnText.Bad<EnCaseDto>(why);
                    c.ConceptNote = dto.ConceptNote.Trim(); c.EditorUserIds = EnEditors.With(c.EditorUserIds, Me);
                    EnEvents.Add(_uow, c, "concept-saved", Me, null);
                    break;
                case "draft-tender":
                {
                    ResponseDto<EnCaseDto> r = await DraftTenderAsync(c, w, now, ct);
                    return r;
                }
                case "revalidate-tender":
                {
                    if (!w.Writer) return EnText.Bad<EnCaseDto>(EnText.NotAllowed);
                    if (c.Status != EnCaseStatus.TenderIssued || !c.TenderPackageId.HasValue) return EnText.Bad<EnCaseDto>("بسته فنی ابلاغ‌شده‌ای برای بررسی نیست.");
                    if (c.TenderHash == await EnCaseRules.TenderHashAsync(_uow, c, ct)) return ResponseDtoHelper.Ok((await DtosAsync(new List<EnCase> { c }, true, ct))[0], "مدارک بسته پس از ابلاغ تغییری نکرده است.");
                    c.Status = EnCaseStatus.TenderPreparation; c.TenderReleasedByUserId = null; c.TenderReleasedAt = null; c.TenderHash = null;
                    EnEvents.Add(_uow, c, "tender-voided", Me, "مدارک بسته پس از ابلاغ تغییر کرد؛ ابلاغ باطل شد.");
                    break;
                }
                case "release-tender":
                {
                    int docs = c.TenderPackageId.HasValue ? await _uow.Set<PcTenderDocument>().AsNoTracking().CountAsync(d => d.PackageId == c.TenderPackageId.Value && !d.Deleted, ct) : 0;
                    HashSet<int> makers = new HashSet<int>();
                    if (c.TenderPackageId.HasValue)
                    {
                        int[] seats = await EnActors.SeatIdsAsync(_uow, ct);
                        var pd = await _uow.Set<PcTenderDocument>().AsNoTracking().Where(d => d.PackageId == c.TenderPackageId.Value && !d.Deleted).Select(d => new { d.CreatorUserId, d.IssuedByUserId }).ToListAsync(ct);
                        foreach (var d in pd) { if (!seats.Contains(d.CreatorUserId)) makers.Add(d.CreatorUserId); if (d.IssuedByUserId.HasValue && !seats.Contains(d.IssuedByUserId.Value)) makers.Add(d.IssuedByUserId.Value); }
                    }
                    why = ReleaseReason(c, w, docs, makers);
                    if (why != null) return EnText.Bad<EnCaseDto>(why);
                    c.TenderHash = await EnCaseRules.TenderHashAsync(_uow, c, ct); c.TenderReleasedByUserId = Me; c.TenderReleasedAt = now; c.Status = EnCaseStatus.TenderIssued;
                    EnEvents.Add(_uow, c, "tender-released", Me, "تعداد مدارک بسته: " + EnText.Num(docs));
                    break;
                }
                case "award":
                {
                    if (!w.Writer) return EnText.Bad<EnCaseDto>(EnText.NotAllowed);
                    if (!c.InquiryId.HasValue || (c.Status != EnCaseStatus.TenderPreparation && c.Status != EnCaseStatus.TenderIssued)) return EnText.Bad<EnCaseDto>("فقط پروندهٔ استعلام در مرحلهٔ مناقصه برنده می‌شود.");
                    bool won = await _uow.Set<SlInquiry>().AsNoTracking().AnyAsync(i => i.Id == c.InquiryId.Value && i.Status == SlInquiryStatus.Won, ct);
                    if (!won) return EnText.Bad<EnCaseDto>("استعلام هنوز برنده نشده است.");
                    int? contract = await _uow.Set<SlProposal>().AsNoTracking().Where(p => p.InquiryId == c.InquiryId.Value && !p.Deleted && p.ContractId.HasValue).OrderByDescending(p => p.Id).Select(p => p.ContractId).FirstOrDefaultAsync(ct);
                    if (!contract.HasValue) return EnText.Bad<EnCaseDto>("قرارداد این استعلام هنوز ساخته نشده است.");
                    if (await _uow.Set<EnCase>().AnyAsync(x => x.ContractId == contract.Value && x.Id != c.Id && !x.Deleted, ct)) return EnText.Bad<EnCaseDto>("برای این قرارداد پروندهٔ مهندسی دیگری باز است.");
                    c.ContractId = contract; c.Status = EnCaseStatus.Awarded;
                    EnEvents.Add(_uow, c, "awarded", Me, null);
                    break;
                }
                case "close":
                {
                    if (!w.Writer) return EnText.Bad<EnCaseDto>(EnText.NotAllowed);
                    if (!c.InquiryId.HasValue || !EnCaseRules.Open(c.Status) || c.Status >= EnCaseStatus.Awarded) return EnText.Bad<EnCaseDto>("این پرونده با نتیجهٔ استعلام بسته نمی‌شود.");
                    var inq = await _uow.Set<SlInquiry>().AsNoTracking().Where(i => i.Id == c.InquiryId.Value).Select(i => new { i.Status, i.CancelReason }).FirstAsync(ct);
                    if (inq.Status != SlInquiryStatus.Lost && inq.Status != SlInquiryStatus.NoBid && inq.Status != SlInquiryStatus.Cancelled) return EnText.Bad<EnCaseDto>("استعلام هنوز باز است.");
                    c.Status = inq.Status == SlInquiryStatus.Cancelled ? EnCaseStatus.Cancelled : EnCaseStatus.ClosedLost;
                    c.ClosedReason = EnText.Cut(EnText.Clean(inq.CancelReason) ?? (inq.Status == SlInquiryStatus.Lost ? "استعلام بازنده شد." : inq.Status == SlInquiryStatus.NoBid ? "شرکت در مناقصه انجام نشد." : "استعلام لغو شد."), 1000);
                    EnEvents.Add(_uow, c, "closed", Me, c.ClosedReason);
                    break;
                }
                case "cancel":
                    why = CancelReason(c, w) ?? EnText.ReasonShort(note, EnRules.ReasonMin, "دلیل لغو");
                    if (why != null) return EnText.Bad<EnCaseDto>(why);
                    c.Status = EnCaseStatus.Cancelled; c.ClosedReason = EnText.Cut(note, 1000);
                    EnEvents.Add(_uow, c, "cancelled", Me, note);
                    break;
                default:   // charter-confirm, charter-return
                {
                    PcProjectCharter charter = c.ContractId.HasValue ? await EnCaseRules.CharterAsync(_uow, c.ContractId.Value, ct) : null;
                    why = CharterReason(c, w, charter);
                    if (why == null && action == "charter-return") why = EnText.ReasonShort(note, EnRules.ReasonMin, "دلیل برگشت منشور");
                    if (why != null) return EnText.Bad<EnCaseDto>(why);
                    c.CharterReviewOutcome = action == "charter-confirm" ? EnCharterOutcome.Confirmed : EnCharterOutcome.Returned;
                    c.CharterReviewedByUserId = Me; c.CharterReviewedAt = now; c.CharterHash = EnCaseRules.CharterHash(charter); c.CharterReviewNote = EnText.Cut(note, 1000);
                    EnEvents.Add(_uow, c, action, Me, note);
                    break;
                }
            }
            c.ModifyDate = now; c.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnCaseDto>(EnText.Concurrency); }
            await SyncGatesAsync(c, action, ct);
            return await GetCaseAsync(c.Id, ct);
        }

        private static void ApplyProposal(EnCase c, EnClassification kind, EnCaseActionDto dto)
        {
            c.ProposedClassification = kind; c.ProposedProductId = kind == EnClassification.Platform ? dto.ProductId : null;
            c.ProposedWithChanges = kind == EnClassification.Platform && dto.WithChanges; c.ProposedChangeNote = c.ProposedWithChanges ? EnText.Clean(dto.ChangeNote) : null;
        }

        /// <summary>The notification that the commercial side receives when the technical package is released (and the close of it).</summary>
        private async Task SyncGatesAsync(EnCase c, string action, CancellationToken ct)
        {
            if (action != "release-tender" && action != "revalidate-tender" && action != "award" && action != "close" && action != "reclassify") return;
            bool waiting = c.Status == EnCaseStatus.TenderIssued;
            await OfficeGates.SyncAsync(_notify, "EN-TENDER-" + c.Id, waiting, "Engineering", nameof(EnCase), c.Id, "بسته فنی مناقصه " + c.Number + " از مهندسی رسید", "/engineering/cases/" + c.Id, EnRoles.Commercial, ct, null, OfInboxKind.Task);
        }

        private async Task<ResponseDto<EnCaseDto>> OpenAsync(EnCaseActionDto dto, Who w, DateTimeOffset now, CancellationToken ct)
        {
            if (!w.Writer) return EnText.Bad<EnCaseDto>(EnText.NotAllowed);
            if (dto.InquiryId.HasValue == dto.ContractId.HasValue) return EnText.Bad<EnCaseDto>("یکی از استعلام یا قرارداد را مشخص کنید.");
            DateTimeOffset? start = await EnControl.StartAsync(_uow, ct);
            EnCase c = new EnCase { Status = EnCaseStatus.Classifying, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me };
            if (dto.InquiryId.HasValue)
            {
                SlInquiry i = await _uow.Set<SlInquiry>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.InquiryId.Value && !x.Deleted, ct);
                if (i == null) return EnText.NotFound<EnCaseDto>("استعلام پیدا نشد.");
                if (i.Status != SlInquiryStatus.Received && i.Status != SlInquiryStatus.UnderReview && i.Status != SlInquiryStatus.Bidding) return EnText.Bad<EnCaseDto>("پروندهٔ مهندسی فقط برای استعلام باز (دریافت‌شده، در بررسی یا در حال پیشنهاد) باز می‌شود.");
                if (EnControl.IsLegacy(i.CreateDate, start)) return EnText.Bad<EnCaseDto>(EnControl.LegacyNote(start));
                if (await _uow.Set<EnCase>().AnyAsync(x => x.InquiryId == i.Id && !x.Deleted, ct)) return EnText.Bad<EnCaseDto>("برای این استعلام پروندهٔ مهندسی وجود دارد.");
                c.Kind = i.ProductType == SlProductType.SpareParts ? EnCaseKind.SpareParts : i.ProductType == SlProductType.Service ? EnCaseKind.Service : EnCaseKind.Turbine;
                c.InquiryId = i.Id; c.Title = EnText.Cut(i.Title, 300); c.CustomerRequirementNote = EnText.Cut(i.Note, 2000);
            }
            else
            {
                ContractEntity k = await _uow.Set<ContractEntity>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.ContractId.Value && !x.Deleted, ct);
                if (k == null) return EnText.NotFound<EnCaseDto>("قرارداد پیدا نشد.");
                if (EnControl.IsLegacy(k.CreateDate, start)) return EnText.Bad<EnCaseDto>(EnControl.LegacyNote(start));
                if (!await _uow.Set<WorkOrder>().AsNoTracking().AnyAsync(x => x.ContractId == k.Id && !x.Deleted, ct)) return EnText.Bad<EnCaseDto>("قرارداد هنوز دستور کار ندارد.");
                if (await _uow.Set<EnCase>().AnyAsync(x => x.ContractId == k.Id && !x.Deleted, ct)) return EnText.Bad<EnCaseDto>("برای این قرارداد پروندهٔ مهندسی وجود دارد.");
                c.ContractId = k.Id; c.Title = EnText.Cut(k.Title, 300);
            }
            c.Number = await EnNumbers.NextAsync(_uow, EnNumbers.Case, "ENG", now.DateTime, ct);
            _uow.Set<EnCase>().Add(c);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return EnText.Bad<EnCaseDto>("برای این مورد هم‌زمان پروندهٔ مهندسی ساخته شد."); }
            EnEvents.Add(_uow, c, "opened", Me, null);
            await _uow.SaveChangesAsync(ct);
            return await GetCaseAsync(c.Id, ct);
        }

        /// <summary>The tender package: created from the inquiry, and (platform) the dossier documents of the tender stage added as the files appear. Idempotent: it adds only what is not yet in the package.</summary>
        private async Task<ResponseDto<EnCaseDto>> DraftTenderAsync(EnCase c, Who w, DateTimeOffset now, CancellationToken ct)
        {
            if (!w.Writer) return EnText.Bad<EnCaseDto>(EnText.NotAllowed);
            if (c.Status != EnCaseStatus.TenderPreparation) return EnText.Bad<EnCaseDto>("بسته فنی فقط در مرحلهٔ تهیهٔ بسته فنی ساخته می‌شود.");
            var inquiry = c.InquiryId.HasValue ? await _uow.Set<SlInquiry>().AsNoTracking().Where(i => i.Id == c.InquiryId.Value).Select(i => new { i.Number, i.Title, i.Status, i.TenderId }).FirstOrDefaultAsync(ct) : null;
            if (inquiry == null || inquiry.Status != SlInquiryStatus.Bidding) return EnText.Bad<EnCaseDto>("بسته فنی فقط برای استعلامی ساخته می‌شود که مدیرعامل شرکت در آن را تصمیم گرفته است.");
            if (!c.TenderPackageId.HasValue)
            {
                ResponseDto<PcTenderPackageDto> created = await _edms.SaveTenderPackageAsync(new PcTenderPackageSaveDto { Title = EnText.Cut("بسته فنی " + inquiry.Number + " — " + inquiry.Title, 300), TenderId = inquiry.TenderId, Note = "پروندهٔ مهندسی " + c.Number }, ct);
                if (!created.Success) return EnText.Bad<EnCaseDto>(created.Message);
                c.TenderPackageId = created.Data.Id;
                EnEvents.Add(_uow, c, "tender-package-created", Me, created.Data.Code);
            }
            List<string> missing = new List<string>();
            if (c.Classification == EnClassification.Platform && c.ProductId.HasValue)
            {
                List<EnPlatformDocument> lines = await _uow.Set<EnPlatformDocument>().AsNoTracking().Where(d => d.Dossier.ProductId == c.ProductId.Value && d.Dossier.ReferenceCode == c.ReferenceCode && !d.Dossier.Deleted && d.TenderStage).OrderBy(d => d.Seq).ToListAsync(ct);
                List<Guid?> present = await _uow.Set<PcTenderDocument>().AsNoTracking().Where(d => d.PackageId == c.TenderPackageId.Value && !d.Deleted).Select(d => d.DocumentId).ToListAsync(ct);
                foreach (EnPlatformDocument l in lines)
                {
                    if (l.State == EnLineState.NotApplicable) continue;
                    if (l.State != EnLineState.Available || (!l.DocumentId.HasValue && !l.SystemProven)) { missing.Add(l.Title); continue; }
                    if (!l.DocumentId.HasValue || present.Contains(l.DocumentId)) continue;   // a system-proven line (released BOM) has no file
                    ResponseDto<PcTenderPackageDto> added = await _edms.SaveTenderDocumentAsync(new PcTenderDocumentSaveDto { PackageId = c.TenderPackageId.Value, Title = l.Title, Discipline = l.Discipline, DocumentType = l.DocumentType, Revision = l.Revision, DocumentId = l.DocumentId }, ct);
                    if (!added.Success) return EnText.Bad<EnCaseDto>(added.Message);
                }
            }
            c.ModifyDate = now; c.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnCaseDto>(EnText.Concurrency); }
            ResponseDto<EnCaseDto> result = await GetCaseAsync(c.Id, ct);
            if (result.Success && missing.Count > 0) result.Data.PlatformWarning = (result.Data.PlatformWarning == null ? string.Empty : result.Data.PlatformWarning + " ") + "مدارک مرحلهٔ مناقصه که هنوز در پرونده پلتفرم نیست: " + string.Join("، ", missing) + ".";
            return result;
        }
    }
}
