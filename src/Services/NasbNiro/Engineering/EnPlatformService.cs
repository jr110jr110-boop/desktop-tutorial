using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.EngineeringDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Engineering
{
    internal static class EnPlatformRules
    {
        public static bool Satisfied(EnPlatformDocument d) => d.State == EnLineState.Available || d.State == EnLineState.NotApplicable;

        public static bool RequiredMissing(EnPlatformDocument d) => d.Required && !Satisfied(d);

        public static string Hash(int productId, int referenceRevision, IEnumerable<EnPlatformDocument> docs) =>
            EnHash.Of(EnText.Num(productId) + "#" + EnText.Num(referenceRevision) + "#" + string.Join(";", docs.OrderBy(d => d.Seq).ThenBy(d => d.Id).Select(d =>
                string.Join("|", EnText.Num(d.Seq), EnHash.Norm(d.DocumentType), EnHash.Norm(d.Discipline), EnHash.Norm(d.Title), d.Required ? "1" : "0", d.TenderStage ? "1" : "0", EnText.Num((int)d.State),
                    d.DocumentId?.ToString("N") ?? "-", d.ControlledDocumentId?.ToString() ?? "-", EnHash.Norm(d.Revision), EnHash.Norm(d.NotApplicableReason), d.SystemProven ? "1" : "0"))));

        /// <summary>Whether the platform is complete and confirmed right now: confirmed, the hash still matches, and the dossier is against the current reference list.</summary>
        public static async Task<bool> IsCompleteAsync(IUnitOfWork uow, int productId, CancellationToken ct)
        {
            EnPlatformDossier d = await uow.Set<EnPlatformDossier>().AsNoTracking().Include(x => x.Documents).FirstOrDefaultAsync(x => x.ProductId == productId && !x.Deleted, ct);
            if (d == null || d.Status != EnDossierStatus.Complete || d.ConfirmedHash == null) return false;
            if (d.ConfirmedHash != Hash(d.ProductId, d.ReferenceRevision, d.Documents)) return false;
            EnReferenceList current = await EnReferenceRules.CurrentAsync(uow, false, ct, d.ReferenceCode);
            return current != null && d.ReferenceListId == current.Id && !d.Documents.Any(RequiredMissing);
        }
    }

    public interface IEnPlatformService
    {
        Task<ResponseDto<PagedList<EnPlatformListDto>>> GetPlatformsAsync(EnPlatformQuery query, CancellationToken ct);
        Task<ResponseDto<EnDossierDto>> GetDossierAsync(int productId, CancellationToken ct);
        Task<ResponseDto<EnPlatformSummaryDto>> GetSummaryAsync(CancellationToken ct);
        Task<ResponseDto<EnDossierDto>> DossierActionAsync(EnDossierActionDto dto, CancellationToken ct);
        Task<ResponseDto<EnDossierDto>> SupplyLineAsync(EnSupplyLineDto dto, CancellationToken ct);
    }

    /// <summary>The dossier of engineering documents of every platform (an active product of the bank) against the approved reference list. The system proves what it can (a released BOM, an approved routing); the rest is supplied by engineers and confirmed by someone who did not touch it.</summary>
    public class EnPlatformService : IEnPlatformService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public EnPlatformService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;

        // ---------------------------------------------------------------- the rules

        private static string AssignReason(bool manager, bool seat, int codes) =>
            !manager ? "فقط مدیر مهندسی استاندارد مرجع پلتفرم را تعیین می‌کند." : seat ? EnText.AiNever : codes == 0 ? "فهرست مدارک مرجع تاییدشده‌ای وجود ندارد." : null;

        private static bool IsStale(EnPlatformDossier d, Dictionary<string, int> currentIds) =>
            d.Status != EnDossierStatus.NotAssessed && (d.ReferenceCode == null || !currentIds.TryGetValue(d.ReferenceCode, out int id) || d.ReferenceListId != id);

        /// <summary>The id of the current approved list of each Code.</summary>
        private async Task<Dictionary<string, int>> CurrentIdsAsync(CancellationToken ct)
        {
            List<EnReferenceList> rows = await _uow.Set<EnReferenceList>().AsNoTracking().Where(l => !l.Deleted && l.Status == EnRefStatus.Approved).ToListAsync(ct);
            return rows.GroupBy(l => l.Code).ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.ApprovedAt).ThenByDescending(l => l.Id).First().Id);
        }

        private static string AssessReason(bool writer, bool hasReference) =>
            !writer ? EnText.NotAllowed : !hasReference ? "فهرست مدارک مرجع تاییدشده‌ای وجود ندارد؛ ابتدا فهرست مرجع تایید شود." : null;

        private static string CompleteReason(EnPlatformDossier d, bool writer, bool seat) =>
            !writer ? EnText.NotAllowed : seat ? EnText.AiNever : d.Status == EnDossierStatus.NotAssessed ? "ابتدا ارزیابی انجام شود."
            : d.Status != EnDossierStatus.Incomplete ? "این پرونده در وضعیت «ناقص» نیست."
            : d.Documents.Any(EnPlatformRules.RequiredMissing) ? "ردیف‌های الزامی کامل نشده‌اند (" + EnText.Num(d.Documents.Count(EnPlatformRules.RequiredMissing)) + " مورد)." : null;

        private static string ConfirmReason(EnPlatformDossier d, bool approver, bool seat, int me) =>
            !approver ? EnText.NotAllowed : seat ? EnText.AiNever : d.Status != EnDossierStatus.Completing ? "فقط پرونده «کامل‌شده، منتظر تایید» تایید می‌شود."
            : d.Documents.Any(EnPlatformRules.RequiredMissing) ? "ردیف‌های الزامی کامل نیستند." : EnEditors.Has(d.EditorUserIds, me) || d.CreatorUserId == me ? EnText.SameUser : null;

        private static string ReturnReasonOf(EnPlatformDossier d, bool approver) =>
            !approver ? EnText.NotAllowed : d.Status != EnDossierStatus.Completing && d.Status != EnDossierStatus.Complete ? "فقط پروندهٔ کامل‌شده یا کامل برگشت می‌خورد." : null;

        private static string SupplyReason(EnPlatformDossier d, bool writer) =>
            !writer ? EnText.NotAllowed : d.Status == EnDossierStatus.NotAssessed ? "ابتدا ارزیابی انجام شود." : null;

        // ---------------------------------------------------------------- read

        private static void Counts(EnPlatformDossier d, out int required, out int available, out int missing, out int tenderMissing)
        {
            List<EnPlatformDocument> req = d.Documents.Where(x => x.Required).ToList();
            required = req.Count; available = req.Count(EnPlatformRules.Satisfied); missing = required - available; tenderMissing = req.Count(x => x.TenderStage && !EnPlatformRules.Satisfied(x));
        }

        public async Task<ResponseDto<PagedList<EnPlatformListDto>>> GetPlatformsAsync(EnPlatformQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<PagedList<EnPlatformListDto>>(EnText.NotAllowed);
            query ??= new EnPlatformQuery();
            IQueryable<PbProduct> q = _uow.Set<PbProduct>().AsNoTracking().Where(p => !p.Deleted && p.IsActive);
            string text = EnText.Clean(query.Contains);
            if (text != null) q = q.Where(p => p.Code.Contains(text) || p.Title.Contains(text));
            if (query.Status.HasValue)
            {
                EnDossierStatus s = (EnDossierStatus)query.Status.Value;
                if (s == EnDossierStatus.NotAssessed) q = q.Where(p => !_uow.Set<EnPlatformDossier>().Any(d => d.ProductId == p.Id && !d.Deleted && d.Status != EnDossierStatus.NotAssessed));
                else q = q.Where(p => _uow.Set<EnPlatformDossier>().Any(d => d.ProductId == p.Id && !d.Deleted && d.Status == s));
            }
            Dictionary<string, int> currentIds = await CurrentIdsAsync(ct);
            return ResponseDtoHelper.Ok(await EnPaging.CutAsync(q.OrderBy(p => p.Code).ThenBy(p => p.Id), query.PageNumber, query.PageSize, async rows =>
            {
                List<int> ids = rows.Select(r => r.Id).ToList();
                List<EnPlatformDossier> dossiers = await _uow.Set<EnPlatformDossier>().AsNoTracking().Include(d => d.Documents).Where(d => !d.Deleted && ids.Contains(d.ProductId)).ToListAsync(ct);
                return rows.Select(p =>
                {
                    EnPlatformDossier d = dossiers.FirstOrDefault(x => x.ProductId == p.Id);
                    EnPlatformListDto dto = new EnPlatformListDto { ProductId = p.Id, ProductCode = p.Code, ProductTitle = p.Title, Status = d == null ? 0 : (int)d.Status, StatusText = EnText.Dossier(d?.Status ?? EnDossierStatus.NotAssessed), DossierId = d?.Id };
                    if (d != null)
                    {
                        Counts(d, out int r, out int a, out int m, out int t);
                        dto.RequiredCount = r; dto.AvailableCount = a; dto.MissingCount = m; dto.TenderStageMissingCount = t; dto.ReferenceRevision = d.ReferenceRevision;
                        dto.Stale = IsStale(d, currentIds); dto.ConfirmedAtText = EnText.Stamp(d.ConfirmedAt);
                    }
                    return dto;
                }).ToList();
            }, ct));
        }

        private async Task<EnDossierDto> DossierDtoAsync(PbProduct product, EnPlatformDossier d, EnReferenceList current, CancellationToken ct)
        {
            bool writer = _user.IsInRole(EnRoles.Writers), approver = _user.IsInRole(EnRoles.RefApprovers);
            bool seat = await EnActors.IsSeatAsync(_uow, Me, ct);
            EnDossierDto dto = new EnDossierDto { ProductId = product.Id, ProductCode = product.Code, ProductTitle = product.Title, Status = 0, StatusText = EnText.Dossier(EnDossierStatus.NotAssessed) };
            List<string> codes = await EnReferenceRules.ApprovedCodesAsync(_uow, ct);
            dto.ApprovedCodes = codes; dto.ReferenceCode = d?.ReferenceCode;
            string assess = AssessReason(writer, current != null);
            if (assess != null && current == null && codes.Count > 1 && (d == null || d.ReferenceCode == null)) assess = "چند استاندارد مرجع تاییدشده وجود دارد؛ مدیر مهندسی باید استاندارد این پلتفرم را تعیین کند.";
            dto.CanAssess = assess == null; dto.AssessBlockedReason = assess;
            string assign = AssignReason(_user.IsInRole(EnRoles.Manager), seat, codes.Count);
            dto.CanAssign = assign == null; dto.AssignBlockedReason = assign;
            if (d == null)
            {
                dto.SupplyBlockedReason = "ابتدا ارزیابی انجام شود."; dto.CompleteBlockedReason = dto.SupplyBlockedReason; dto.ConfirmBlockedReason = dto.SupplyBlockedReason; dto.ReturnBlockedReason = dto.SupplyBlockedReason;
                return dto;
            }
            Dictionary<int, string> names = await EnUsers.NamesAsync(_uow, d.Documents.Select(x => x.SuppliedByUserId).Concat(new int?[] { d.ConfirmedByUserId }), ct);
            Counts(d, out int r, out int a, out int m, out int t);
            string complete = CompleteReason(d, writer, seat), confirm = ConfirmReason(d, approver, seat, Me), ret = ReturnReasonOf(d, approver), supply = SupplyReason(d, writer);
            dto.Id = d.Id; dto.ReferenceListId = d.ReferenceListId; dto.ReferenceRevision = d.ReferenceRevision; dto.Status = (int)d.Status; dto.StatusText = EnText.Dossier(d.Status);
            dto.ReferenceTitle = d.ReferenceListId.HasValue ? await _uow.Set<EnReferenceList>().AsNoTracking().Where(l => l.Id == d.ReferenceListId.Value).Select(l => l.Title).FirstOrDefaultAsync(ct) : null;
            dto.Stale = current != null && d.ReferenceListId != current.Id; dto.ReferenceCode = d.ReferenceCode;
            dto.RequiredCount = r; dto.AvailableCount = a; dto.MissingCount = m; dto.TenderStageMissingCount = t;
            dto.AssessedAtText = EnText.Stamp(d.AssessedAt); dto.ConfirmedByName = names.Name(d.ConfirmedByUserId); dto.ConfirmedAtText = EnText.Stamp(d.ConfirmedAt); dto.Note = d.Note;
            dto.TouchedByMe = EnEditors.Has(d.EditorUserIds, Me); dto.RowVersion = EnConcurrency.Text(d.RowVersion);
            dto.CanSupply = supply == null; dto.SupplyBlockedReason = supply; dto.CanComplete = complete == null; dto.CompleteBlockedReason = complete;
            dto.CanConfirm = confirm == null; dto.ConfirmBlockedReason = confirm; dto.CanReturn = ret == null; dto.ReturnBlockedReason = ret;
            dto.Lines = d.Documents.OrderBy(x => x.Seq).ThenBy(x => x.Id).Select(x => new EnDossierLineDto
            {
                Id = x.Id, Seq = x.Seq, DocumentType = x.DocumentType, Discipline = x.Discipline, Title = x.Title, Required = x.Required, TenderStage = x.TenderStage, State = (int)x.State, StateText = EnText.LineState(x.State),
                DocumentId = x.DocumentId, ControlledDocumentId = x.ControlledDocumentId, Revision = x.Revision, NotApplicableReason = x.NotApplicableReason, SystemProven = x.SystemProven,
                SuppliedByName = names.Name(x.SuppliedByUserId), SuppliedAtText = EnText.Stamp(x.SuppliedAt), RowVersion = EnConcurrency.Text(x.RowVersion),
            }).ToList();
            return dto;
        }

        public async Task<ResponseDto<EnDossierDto>> GetDossierAsync(int productId, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<EnDossierDto>(EnText.NotAllowed);
            PbProduct product = await _uow.Set<PbProduct>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId && !p.Deleted, ct);
            if (product == null) return EnText.NotFound<EnDossierDto>("محصول پیدا نشد.");
            EnPlatformDossier d = await _uow.Set<EnPlatformDossier>().AsNoTracking().Include(x => x.Documents).FirstOrDefaultAsync(x => x.ProductId == productId && !x.Deleted, ct);
            return ResponseDtoHelper.Ok(await DossierDtoAsync(product, d, await EnReferenceRules.CurrentAsync(_uow, false, ct, d?.ReferenceCode), ct));
        }

        public async Task<ResponseDto<EnPlatformSummaryDto>> GetSummaryAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<EnPlatformSummaryDto>(EnText.NotAllowed);
            List<int> products = await _uow.Set<PbProduct>().AsNoTracking().Where(p => !p.Deleted && p.IsActive).Select(p => p.Id).ToListAsync(ct);
            List<EnPlatformDossier> dossiers = await _uow.Set<EnPlatformDossier>().AsNoTracking().Where(d => !d.Deleted && products.Contains(d.ProductId)).ToListAsync(ct);
            Dictionary<string, int> currentIds = await CurrentIdsAsync(ct);
            EnReferenceList current = await EnReferenceRules.CurrentAsync(_uow, false, ct);   // the sole basis when there is exactly one
            EnPlatformSummaryDto s = new EnPlatformSummaryDto { Products = products.Count, HasApprovedReference = currentIds.Count > 0, ReferenceRevision = current?.Revision ?? 0 };
            s.Incomplete = dossiers.Count(d => d.Status == EnDossierStatus.Incomplete);
            s.Completing = dossiers.Count(d => d.Status == EnDossierStatus.Completing);
            s.Complete = dossiers.Count(d => d.Status == EnDossierStatus.Complete);
            s.NotAssessed = products.Count - s.Incomplete - s.Completing - s.Complete;
            s.Stale = dossiers.Count(d => IsStale(d, currentIds));
            return ResponseDtoHelper.Ok(s);
        }

        // ---------------------------------------------------------------- assess

        private async Task ProveAsync(EnPlatformDossier d, CancellationToken ct)
        {
            bool bomReleased = await _uow.Set<PbBomHeader>().AsNoTracking().AnyAsync(b => !b.Deleted && b.ProductId == d.ProductId && b.Status == PbBomStatus.Released, ct);
            bool opcApproved = await _uow.Set<PbOpcRouting>().AsNoTracking().AnyAsync(o => !o.Deleted && o.ProductId == d.ProductId && o.Status == PbRoutingStatus.Approved, ct);
            foreach (EnPlatformDocument x in d.Documents)
            {
                string key = EnReferenceRules.Key(x.DocumentType);
                bool proven = (key == EnDocTypes.Bom && bomReleased) || (key == EnDocTypes.Opc && opcApproved);
                if (proven && x.State != EnLineState.NotApplicable && !(x.State == EnLineState.Available && !x.SystemProven))
                { x.State = EnLineState.Available; x.SystemProven = true; x.DocumentId = null; x.ControlledDocumentId = null; }
                else if (!proven && x.SystemProven)
                { x.State = EnLineState.Missing; x.SystemProven = false; }
            }
        }

        private static string Merge(EnPlatformDossier d, EnReferenceList current)
        {
            List<EnPlatformDocument> existing = d.Documents.ToList();
            HashSet<EnPlatformDocument> matched = new HashSet<EnPlatformDocument>();
            foreach (EnReferenceLine line in current.Lines.OrderBy(l => l.Seq))
            {
                string key = EnReferenceRules.Key(line.DocumentType) + "|" + EnHash.Norm(line.Discipline).ToUpperInvariant() + "|" + EnHash.Norm(line.Title).ToUpperInvariant();
                EnPlatformDocument doc = existing.FirstOrDefault(x => !matched.Contains(x) && (x.ReferenceLineId == line.Id || (EnReferenceRules.Key(x.DocumentType) + "|" + EnHash.Norm(x.Discipline).ToUpperInvariant() + "|" + EnHash.Norm(x.Title).ToUpperInvariant()) == key));
                if (doc == null)
                {
                    doc = new EnPlatformDocument { Dossier = d, State = EnLineState.Missing };
                    d.Documents.Add(doc);
                }
                matched.Add(doc);
                doc.ReferenceLineId = line.Id; doc.Seq = line.Seq; doc.DocumentType = line.DocumentType; doc.Discipline = line.Discipline; doc.Title = line.Title; doc.Required = line.Required; doc.TenderStage = line.TenderStage;
            }
            // a line the new list dropped keeps its file and its state but stops being required
            foreach (EnPlatformDocument x in existing.Where(x => !matched.Contains(x))) { x.Required = false; x.TenderStage = false; x.ReferenceLineId = null; }
            return null;
        }

        private static void Recompute(EnPlatformDossier d, bool afterAssess)
        {
            bool confirmedStillValid = d.Status == EnDossierStatus.Complete && d.ConfirmedHash != null && d.ConfirmedHash == EnPlatformRules.Hash(d.ProductId, d.ReferenceRevision, d.Documents) && !d.Documents.Any(EnPlatformRules.RequiredMissing);
            if (confirmedStillValid) return;
            if (d.Status == EnDossierStatus.Complete || d.Status == EnDossierStatus.Completing || d.Status == EnDossierStatus.NotAssessed || afterAssess)
            {
                d.Status = EnDossierStatus.Incomplete; d.ConfirmedHash = null; d.ConfirmedAt = null; d.ConfirmedByUserId = null;
            }
        }

        public async Task<ResponseDto<EnDossierDto>> DossierActionAsync(EnDossierActionDto dto, CancellationToken ct)
        {
            if (dto == null || dto.ProductId <= 0) return EnText.Bad<EnDossierDto>(EnText.InvalidData);
            string action = (dto.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action != "assess" && action != "assign" && action != "complete" && action != "confirm" && action != "return") return EnText.Bad<EnDossierDto>("عمل نامعتبر است (assess، assign، complete، confirm یا return).");
            PbProduct product = await _uow.Set<PbProduct>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.ProductId && !p.Deleted, ct);
            if (product == null) return EnText.NotFound<EnDossierDto>("محصول پیدا نشد.");
            bool writer = _user.IsInRole(EnRoles.Writers), approver = _user.IsInRole(EnRoles.RefApprovers);
            bool seat = await EnActors.IsSeatAsync(_uow, Me, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            string note = EnText.Clean(dto.Note);
            EnPlatformDossier d = await _uow.Set<EnPlatformDossier>().Include(x => x.Documents).FirstOrDefaultAsync(x => x.ProductId == dto.ProductId && !x.Deleted, ct);
            if (action == "assign")
            {
                // the standard basis of the platform (API 611 or API 612) is chosen by the engineering manager, never by a rule
                List<string> codes = await EnReferenceRules.ApprovedCodesAsync(_uow, ct);
                string pick = EnText.Clean(dto.ReferenceCode);
                string why = AssignReason(_user.IsInRole(EnRoles.Manager), seat, codes.Count);
                if (why == null && string.IsNullOrEmpty(pick)) why = "استاندارد مرجع را انتخاب کنید.";
                if (why == null && !codes.Contains(pick)) why = "برای این استاندارد مرجع فهرست تاییدشده‌ای وجود ندارد.";
                if (why == null && d != null && d.ReferenceCode != null && d.ReferenceCode != pick) why = EnText.ReasonShort(note, EnRules.ReasonMin, "دلیل تغییر استاندارد مرجع");
                if (why != null) return EnText.Bad<EnDossierDto>(why);
                if (!product.IsActive) return EnText.Bad<EnDossierDto>("فقط محصول فعال ارزیابی می‌شود.");
                if (d == null)
                {
                    d = new EnPlatformDossier { ProductId = product.Id, Status = EnDossierStatus.NotAssessed, CreateDate = now, CreatorUserId = Me };
                    _uow.Set<EnPlatformDossier>().Add(d);
                }
                else if (!EnConcurrency.Apply(_uow, d, dto.RowVersion)) return EnText.Bad<EnDossierDto>(EnText.InvalidData);
                if (d.ReferenceCode != pick)
                {
                    // a changed standard voids the confirmation; the agent re-builds the lines against the new list
                    d.ReferenceCode = pick; d.ReferenceListId = null; d.ReferenceRevision = 0;
                    if (d.Status != EnDossierStatus.NotAssessed) { d.Status = EnDossierStatus.NotAssessed; d.ConfirmedHash = null; d.ConfirmedAt = null; d.ConfirmedByUserId = null; d.Note = EnText.Cut("تغییر استاندارد مرجع به " + pick + ": " + note, 1000); }
                }
            }
            else if (action == "assess")
            {
                List<string> codes = await EnReferenceRules.ApprovedCodesAsync(_uow, ct);
                string code = d?.ReferenceCode;
                if (code == null && codes.Count == 1) code = codes[0];
                EnReferenceList current = code == null ? null : await EnReferenceRules.CurrentAsync(_uow, false, ct, code);
                string why = AssessReason(writer, current != null || codes.Count > 1);
                if (why != null) return EnText.Bad<EnDossierDto>(why);
                if (!product.IsActive) return EnText.Bad<EnDossierDto>("فقط محصول فعال ارزیابی می‌شود.");
                if (d == null)
                {
                    d = new EnPlatformDossier { ProductId = product.Id, Status = EnDossierStatus.NotAssessed, CreateDate = now, CreatorUserId = Me };
                    _uow.Set<EnPlatformDossier>().Add(d);
                }
                else if (!EnConcurrency.Apply(_uow, d, dto.RowVersion)) return EnText.Bad<EnDossierDto>(EnText.InvalidData);
                if (current == null)
                {
                    // several approved standards and none chosen for this platform: the dossier waits empty for the manager's choice (ENG.PLATFORM.ASSIGN)
                    d.ModifyDate = now; d.ModifierUserId = Me;
                    try { await _uow.SaveChangesAsync(ct); }
                    catch (DbUpdateConcurrencyException) { return EnText.Bad<EnDossierDto>(EnText.Concurrency); }
                    return await GetDossierAsync(dto.ProductId, ct);
                }
                d.ReferenceCode = code;
                bool changedList = d.ReferenceListId != current.Id;
                string before = EnPlatformRules.Hash(d.ProductId, d.ReferenceRevision, d.Documents);
                Merge(d, current);
                d.ReferenceListId = current.Id; d.ReferenceRevision = current.Revision;
                await ProveAsync(d, ct);
                d.AssessedAt = now;
                bool changed = changedList || before != EnPlatformRules.Hash(d.ProductId, d.ReferenceRevision, d.Documents);
                if (d.Status == EnDossierStatus.NotAssessed || changed) Recompute(d, true);
            }
            else
            {
                if (d == null) return EnText.NotFound<EnDossierDto>(EnText.NotFoundDossier);
                if (!EnConcurrency.Apply(_uow, d, dto.RowVersion)) return EnText.Bad<EnDossierDto>(EnText.InvalidData);
                string why;
                if (action == "complete")
                {
                    why = CompleteReason(d, writer, seat);
                    if (why != null) return EnText.Bad<EnDossierDto>(why);
                    d.Status = EnDossierStatus.Completing; d.EditorUserIds = EnEditors.With(d.EditorUserIds, Me);
                }
                else if (action == "confirm")
                {
                    why = ConfirmReason(d, approver, seat, Me);
                    if (why != null) return EnText.Bad<EnDossierDto>(why);
                    EnReferenceList current = await EnReferenceRules.CurrentAsync(_uow, false, ct, d.ReferenceCode);
                    if (current == null || d.ReferenceListId != current.Id) return EnText.Bad<EnDossierDto>("فهرست مرجع تغییر کرده است؛ ابتدا ارزیابی دوباره انجام شود.");
                    d.Status = EnDossierStatus.Complete; d.ConfirmedByUserId = Me; d.ConfirmedAt = now; d.ConfirmedHash = EnPlatformRules.Hash(d.ProductId, d.ReferenceRevision, d.Documents);
                }
                else
                {
                    why = ReturnReasonOf(d, approver) ?? EnText.ReasonShort(note, EnRules.ReasonMin, "دلیل برگشت");
                    if (why != null) return EnText.Bad<EnDossierDto>(why);
                    if (dto.LineId.HasValue)
                    {
                        EnPlatformDocument line = d.Documents.FirstOrDefault(x => x.Id == dto.LineId.Value);
                        if (line == null) return EnText.NotFound<EnDossierDto>("ردیف پیدا نشد.");
                        if (line.SystemProven) return EnText.Bad<EnDossierDto>("این ردیف را سامانه از وضعیت BOM یا مسیر ساخت اثبات کرده است و برگشت نمی‌خورد.");
                        line.State = EnLineState.Missing; line.DocumentId = null; line.ControlledDocumentId = null; line.Revision = null; line.NotApplicableReason = null;
                    }
                    d.Status = EnDossierStatus.Incomplete; d.ConfirmedHash = null; d.ConfirmedAt = null; d.ConfirmedByUserId = null; d.Note = EnText.Cut("برگشت: " + note, 1000);
                }
            }
            d.ModifyDate = now; d.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnDossierDto>(EnText.Concurrency); }
            return await GetDossierAsync(dto.ProductId, ct);
        }

        // ---------------------------------------------------------------- supply

        public async Task<ResponseDto<EnDossierDto>> SupplyLineAsync(EnSupplyLineDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Writers)) return EnText.Bad<EnDossierDto>(EnText.NotAllowed);
            if (dto == null || dto.LineId <= 0) return EnText.Bad<EnDossierDto>(EnText.InvalidData);
            EnPlatformDocument line = await _uow.Set<EnPlatformDocument>().Include(x => x.Dossier).ThenInclude(x => x.Documents).FirstOrDefaultAsync(x => x.Id == dto.LineId, ct);
            if (line == null || line.Dossier.Deleted) return EnText.NotFound<EnDossierDto>("ردیف پیدا نشد.");
            EnPlatformDossier d = line.Dossier;
            string why = SupplyReason(d, true);
            if (why != null) return EnText.Bad<EnDossierDto>(why);
            if (!EnConcurrency.Apply(_uow, line, dto.RowVersion)) return EnText.Bad<EnDossierDto>(EnText.InvalidData);
            if (line.SystemProven) return EnText.Bad<EnDossierDto>("این ردیف را سامانه از وضعیت BOM یا مسیر ساخت اثبات کرده است؛ نیازی به فایل نیست.");
            string revision = EnText.Clean(dto.Revision);
            string problem = EnText.Over(revision, 30, "بازنگری");
            if (problem != null) return EnText.Bad<EnDossierDto>(problem);
            if (dto.NotApplicable)
            {
                string reason = EnText.Clean(dto.NotApplicableReason);
                problem = EnText.ReasonShort(reason, EnRules.ReasonMin, "دلیل غیرقابل‌اجرا بودن") ?? EnText.Over(reason, 500, "دلیل");
                if (problem != null) return EnText.Bad<EnDossierDto>(problem);
                line.State = EnLineState.NotApplicable; line.NotApplicableReason = reason; line.DocumentId = null; line.ControlledDocumentId = null; line.Revision = null;
            }
            else
            {
                if (!dto.DocumentId.HasValue && !dto.ControlledDocumentId.HasValue) return EnText.Bad<EnDossierDto>("فایل یا مدرک کنترل‌شده را مشخص کنید.");
                if (dto.DocumentId.HasValue)
                {
                    problem = await EnFiles.CheckAsync(_uow, dto.DocumentId.Value, Me, line.Id, ct);
                    if (problem != null) return EnText.Bad<EnDossierDto>(problem);
                }
                if (dto.ControlledDocumentId.HasValue && !await _uow.Set<ControlledDocument>().AsNoTracking().AnyAsync(c => c.Id == dto.ControlledDocumentId.Value && !c.Deleted, ct))
                    return EnText.Bad<EnDossierDto>("مدرک کنترل‌شده پیدا نشد.");
                line.State = EnLineState.Available; line.NotApplicableReason = null; line.DocumentId = dto.DocumentId; line.ControlledDocumentId = dto.ControlledDocumentId; line.Revision = revision;
            }
            DateTimeOffset now = DateTimeOffset.Now;
            line.SuppliedByUserId = Me; line.SuppliedAt = now;
            d.EditorUserIds = EnEditors.With(d.EditorUserIds, Me); d.LastEditedByUserId = Me; d.ModifyDate = now; d.ModifierUserId = Me;
            Recompute(d, false);
            if (d.Status == EnDossierStatus.Completing) d.Status = EnDossierStatus.Incomplete;   // a change after "complete" needs the engineer to say it again
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnDossierDto>(EnText.Concurrency); }
            return await GetDossierAsync(d.ProductId, ct);
        }
    }
}
