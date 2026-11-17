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

        /// <summary>Whether the dossier (product, standard) is complete and confirmed right now: confirmed, the hash still matches, and the dossier is against the current approved list of its standard.</summary>
        public static async Task<bool> IsCompleteAsync(IUnitOfWork uow, int productId, string code, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            EnPlatformDossier d = await uow.Set<EnPlatformDossier>().AsNoTracking().Include(x => x.Documents).FirstOrDefaultAsync(x => x.ProductId == productId && x.ReferenceCode == code && !x.Deleted, ct);
            if (d == null || d.Status != EnDossierStatus.Complete || d.ConfirmedHash == null) return false;
            if (d.ConfirmedHash != Hash(d.ProductId, d.ReferenceRevision, d.Documents)) return false;
            EnReferenceList current = await EnReferenceRules.CurrentAsync(uow, false, ct, d.ReferenceCode);
            return current != null && d.ReferenceListId == current.Id && !d.Documents.Any(RequiredMissing);
        }
    }

    /// <summary>Building a dossier from the approved list of its standard: shared by the dossier service and the case (a platform case creates the dossier of its client standard).</summary>
    internal static class EnDossierBuilder
    {
        public static string LineKey(string type, string discipline, string title) => EnReferenceRules.Key(type) + "|" + EnHash.Norm(discipline).ToUpperInvariant() + "|" + EnHash.Norm(title).ToUpperInvariant();

        /// <summary>Document type + title, trimmed and case-insensitive: the match that lets a file be reused across the dossiers of one product.</summary>
        public static string TypeTitle(string type, string title) => EnReferenceRules.Key(type) + "|" + EnHash.Norm(title).ToUpperInvariant();

        public static string NormCode(string code)
        {
            string c = EnText.Clean(code);
            return c == null ? null : c.ToUpperInvariant();
        }

        public static async Task ProveAsync(IUnitOfWork uow, EnPlatformDossier d, CancellationToken ct)
        {
            bool bomReleased = await uow.Set<PbBomHeader>().AsNoTracking().AnyAsync(b => !b.Deleted && b.ProductId == d.ProductId && b.Status == PbBomStatus.Released, ct);
            bool opcApproved = await uow.Set<PbOpcRouting>().AsNoTracking().AnyAsync(o => !o.Deleted && o.ProductId == d.ProductId && o.Status == PbRoutingStatus.Approved, ct);
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

        public static void Merge(EnPlatformDossier d, EnReferenceList current)
        {
            List<EnPlatformDocument> existing = d.Documents.ToList();
            HashSet<EnPlatformDocument> matched = new HashSet<EnPlatformDocument>();
            foreach (EnReferenceLine line in current.Lines.OrderBy(l => l.Seq))
            {
                string key = LineKey(line.DocumentType, line.Discipline, line.Title);
                EnPlatformDocument doc = existing.FirstOrDefault(x => !matched.Contains(x) && (x.ReferenceLineId == line.Id || LineKey(x.DocumentType, x.Discipline, x.Title) == key));
                if (doc == null)
                {
                    doc = new EnPlatformDocument { Dossier = d, State = EnLineState.Missing };
                    d.Documents.Add(doc);
                }
                matched.Add(doc);
                doc.ReferenceLineId = line.Id; doc.Seq = line.Seq; doc.DocumentType = line.DocumentType; doc.Discipline = line.Discipline; doc.Title = line.Title; doc.Required = line.Required; doc.TenderStage = line.TenderStage; doc.Units = line.Units;
            }
            // a line the new list dropped keeps its file and its state but stops being required
            foreach (EnPlatformDocument x in existing.Where(x => !matched.Contains(x))) { x.Required = false; x.TenderStage = false; x.ReferenceLineId = null; }
        }

        /// <summary>The dossier (product, standard); created (not saved) when it does not exist: the lines from the Approved list of the standard (none while the standard has no Approved list), and every line that matches a line of another dossier of the product takes its file, revision and state, so nothing is asked twice.</summary>
        public static async Task<EnPlatformDossier> EnsureAsync(IUnitOfWork uow, int productId, string code, string basis, int userId, DateTimeOffset now, CancellationToken ct)
        {
            EnPlatformDossier d = await uow.Set<EnPlatformDossier>().Include(x => x.Documents).FirstOrDefaultAsync(x => x.ProductId == productId && x.ReferenceCode == code && !x.Deleted, ct);
            if (d != null) return d;
            d = new EnPlatformDossier { ProductId = productId, ReferenceCode = code, StandardBasis = EnText.Cut(basis, 500), Status = EnDossierStatus.NotAssessed, CreateDate = now, CreatorUserId = userId, ModifyDate = now, ModifierUserId = userId };
            uow.Set<EnPlatformDossier>().Add(d);
            EnReferenceList list = await EnReferenceRules.CurrentAsync(uow, false, ct, code);
            if (list == null) return d;
            Merge(d, list);
            d.ReferenceListId = list.Id; d.ReferenceRevision = list.Revision; d.AssessedAt = now; d.Status = EnDossierStatus.Incomplete;
            await ProveAsync(uow, d, ct);
            List<EnPlatformDocument> others = await uow.Set<EnPlatformDocument>().AsNoTracking().Where(x => x.Dossier.ProductId == productId && !x.Dossier.Deleted && x.State == EnLineState.Available && !x.SystemProven).OrderBy(x => x.Id).ToListAsync(ct);
            foreach (EnPlatformDocument x in d.Documents.Where(x => x.State == EnLineState.Missing))
            {
                EnPlatformDocument src = others.FirstOrDefault(o => TypeTitle(o.DocumentType, o.Title) == TypeTitle(x.DocumentType, x.Title));
                if (src == null) continue;
                x.State = EnLineState.Available; x.DocumentId = src.DocumentId; x.ControlledDocumentId = src.ControlledDocumentId; x.Revision = src.Revision; x.SuppliedByUserId = src.SuppliedByUserId; x.SuppliedAt = src.SuppliedAt;
            }
            return d;
        }
    }

    public interface IEnPlatformService
    {
        Task<ResponseDto<PagedList<EnPlatformListDto>>> GetPlatformsAsync(EnPlatformQuery query, CancellationToken ct);
        Task<ResponseDto<EnDossierDto>> GetDossierAsync(int productId, string code, CancellationToken ct);
        Task<ResponseDto<EnPlatformSummaryDto>> GetSummaryAsync(CancellationToken ct);
        Task<ResponseDto<EnDossierDto>> DossierActionAsync(EnDossierActionDto dto, CancellationToken ct);
        Task<ResponseDto<EnDossierDto>> SupplyLineAsync(EnSupplyLineDto dto, CancellationToken ct);
    }

    /// <summary>The dossier of engineering documents of every platform (an active product of the bank) against the approved reference list. The system proves what it can (a released BOM, an approved routing); the rest is supplied by engineers and confirmed by someone who did not touch it.</summary>
    public class EnPlatformService : IEnPlatformService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly global::Services.NasbNiro.Office.INotificationService _notify;

        public EnPlatformService(IUnitOfWork uow, IInventoryUserContext user, global::Services.NasbNiro.Office.INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;

        // ---------------------------------------------------------------- the rules

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
            !approver ? EnText.NotAllowed + " (تایید کامل بودن مدارک پلتفرم فقط با مدیر مهندسی است)" : seat ? EnText.AiNever : d.Status != EnDossierStatus.Completing ? "فقط پرونده «کامل‌شده، منتظر تایید» تایید می‌شود."
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
                if (s == EnDossierStatus.NotAssessed) q = q.Where(p => !_uow.Set<EnPlatformDossier>().Any(d => d.ProductId == p.Id && !d.Deleted) || _uow.Set<EnPlatformDossier>().Any(d => d.ProductId == p.Id && !d.Deleted && d.Status == s));
                else q = q.Where(p => _uow.Set<EnPlatformDossier>().Any(d => d.ProductId == p.Id && !d.Deleted && d.Status == s));
            }
            Dictionary<string, int> currentIds = await CurrentIdsAsync(ct);
            return ResponseDtoHelper.Ok(await EnPaging.CutAsync(q.OrderBy(p => p.Code).ThenBy(p => p.Id), query.PageNumber, query.PageSize, async rows =>
            {
                List<int> ids = rows.Select(r => r.Id).ToList();
                List<EnPlatformDossier> dossiers = await _uow.Set<EnPlatformDossier>().AsNoTracking().Include(d => d.Documents).Where(d => !d.Deleted && ids.Contains(d.ProductId)).ToListAsync(ct);
                List<EnPlatformListDto> result = new List<EnPlatformListDto>();
                foreach (PbProduct p in rows)
                {
                    List<EnPlatformDossier> mine = dossiers.Where(x => x.ProductId == p.Id).OrderBy(x => x.ReferenceCode, StringComparer.Ordinal).ToList();
                    if (mine.Count == 0)
                    {
                        // the product has no dossier: a virtual row NotAssessed (the engineers record the standard of its original data sheet)
                        result.Add(new EnPlatformListDto { ProductId = p.Id, ProductCode = p.Code, ProductTitle = p.Title, Status = 0, StatusText = EnText.Dossier(EnDossierStatus.NotAssessed), NativeStandardMissing = true });
                        continue;
                    }
                    foreach (EnPlatformDossier d in mine)
                    {
                        if (query.Status.HasValue && (int)d.Status != query.Status.Value) continue;
                        EnPlatformListDto dto = new EnPlatformListDto { ProductId = p.Id, ProductCode = p.Code, ProductTitle = p.Title, Status = (int)d.Status, StatusText = EnText.Dossier(d.Status), DossierId = d.Id, ReferenceCode = d.ReferenceCode, StandardBasis = d.StandardBasis };
                        Counts(d, out int r, out int a, out int m, out int t);
                        dto.RequiredCount = r; dto.AvailableCount = a; dto.MissingCount = m; dto.TenderStageMissingCount = t; dto.ReferenceRevision = d.ReferenceRevision;
                        dto.Stale = IsStale(d, currentIds); dto.ConfirmedAtText = EnText.Stamp(d.ConfirmedAt);
                        result.Add(dto);
                    }
                }
                return result;
            }, ct));
        }

        private string RecordStandardReason(bool writer, bool seat, PbProduct product) =>
            !writer ? EnText.NotAllowed : seat ? EnText.AiNever : !product.IsActive ? "فقط محصول فعال ارزیابی می‌شود." : null;

        private async Task<EnDossierDto> DossierDtoAsync(PbProduct product, EnPlatformDossier d, List<EnPlatformDossier> all, EnReferenceList current, CancellationToken ct)
        {
            bool writer = _user.IsInRole(EnRoles.Writers), approver = _user.IsInRole(EnRoles.Manager);
            bool seat = await EnActors.IsSeatAsync(_uow, Me, ct);
            EnDossierDto dto = new EnDossierDto { ProductId = product.Id, ProductCode = product.Code, ProductTitle = product.Title, Status = 0, StatusText = EnText.Dossier(EnDossierStatus.NotAssessed), NativeStandardMissing = all.Count == 0 };
            dto.OtherDossiers = all.Where(x => d == null || x.Id != d.Id).OrderBy(x => x.ReferenceCode, StringComparer.Ordinal).Select(x => new EnDossierRefDto { Id = x.Id, ReferenceCode = x.ReferenceCode, Status = (int)x.Status, StatusText = EnText.Dossier(x.Status) }).ToList();
            string record = RecordStandardReason(writer, seat, product);
            dto.CanRecordStandard = record == null; dto.RecordStandardBlockedReason = record;
            if (d == null)
            {
                string none = all.Count == 0 ? "استاندارد دیتاشیت اصلی این پلتفرم هنوز ثبت نشده است." : "برای این استاندارد پرونده‌ای نیست.";
                dto.AssessBlockedReason = none; dto.SupplyBlockedReason = none; dto.CompleteBlockedReason = none; dto.ConfirmBlockedReason = none; dto.ReturnBlockedReason = none;
                return dto;
            }
            string assess = AssessReason(writer, current != null);
            dto.CanAssess = assess == null; dto.AssessBlockedReason = assess;
            Dictionary<int, string> names = await EnUsers.NamesAsync(_uow, d.Documents.Select(x => x.SuppliedByUserId).Concat(new int?[] { d.ConfirmedByUserId }), ct);
            Counts(d, out int r, out int a, out int m, out int t);
            string complete = CompleteReason(d, writer, seat), confirm = ConfirmReason(d, approver, seat, Me), ret = ReturnReasonOf(d, approver), supply = SupplyReason(d, writer);
            dto.Id = d.Id; dto.ReferenceListId = d.ReferenceListId; dto.ReferenceRevision = d.ReferenceRevision; dto.Status = (int)d.Status; dto.StatusText = EnText.Dossier(d.Status);
            dto.ReferenceTitle = d.ReferenceListId.HasValue ? await _uow.Set<EnReferenceList>().AsNoTracking().Where(l => l.Id == d.ReferenceListId.Value).Select(l => l.Title).FirstOrDefaultAsync(ct) : null;
            dto.Stale = current != null && d.ReferenceListId != current.Id; dto.ReferenceCode = d.ReferenceCode; dto.StandardBasis = d.StandardBasis;
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

        public async Task<ResponseDto<EnDossierDto>> GetDossierAsync(int productId, string code, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<EnDossierDto>(EnText.NotAllowed);
            PbProduct product = await _uow.Set<PbProduct>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId && !p.Deleted, ct);
            if (product == null) return EnText.NotFound<EnDossierDto>("محصول پیدا نشد.");
            List<EnPlatformDossier> all = await _uow.Set<EnPlatformDossier>().AsNoTracking().Include(x => x.Documents).Where(x => x.ProductId == productId && !x.Deleted).ToListAsync(ct);
            string norm = EnDossierBuilder.NormCode(code);
            EnPlatformDossier d = norm != null ? all.FirstOrDefault(x => x.ReferenceCode == norm) : all.OrderBy(x => x.ReferenceCode, StringComparer.Ordinal).FirstOrDefault();
            return ResponseDtoHelper.Ok(await DossierDtoAsync(product, d, all, d == null ? null : await EnReferenceRules.CurrentAsync(_uow, false, ct, d.ReferenceCode), ct));
        }

        public async Task<ResponseDto<EnPlatformSummaryDto>> GetSummaryAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<EnPlatformSummaryDto>(EnText.NotAllowed);
            List<int> products = await _uow.Set<PbProduct>().AsNoTracking().Where(p => !p.Deleted && p.IsActive).Select(p => p.Id).ToListAsync(ct);
            List<EnPlatformDossier> dossiers = await _uow.Set<EnPlatformDossier>().AsNoTracking().Where(d => !d.Deleted && products.Contains(d.ProductId)).ToListAsync(ct);
            Dictionary<string, int> currentIds = await CurrentIdsAsync(ct);
            EnPlatformSummaryDto s = new EnPlatformSummaryDto { Products = products.Count, HasApprovedReference = currentIds.Count > 0, ReferenceRevision = 0 };
            s.Incomplete = dossiers.Count(d => d.Status == EnDossierStatus.Incomplete);
            s.Completing = dossiers.Count(d => d.Status == EnDossierStatus.Completing);
            s.Complete = dossiers.Count(d => d.Status == EnDossierStatus.Complete);
            s.NotAssessed = products.Count(id => dossiers.All(d => d.ProductId != id)) + dossiers.Count(d => d.Status == EnDossierStatus.NotAssessed);
            s.Stale = dossiers.Count(d => IsStale(d, currentIds));
            return ResponseDtoHelper.Ok(s);
        }

        // ---------------------------------------------------------------- assess

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
            if (action != "native-standard" && action != "assess" && action != "complete" && action != "confirm" && action != "return") return EnText.Bad<EnDossierDto>("عمل نامعتبر است (native-standard، assess، complete، confirm یا return).");
            PbProduct product = await _uow.Set<PbProduct>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.ProductId && !p.Deleted, ct);
            if (product == null) return EnText.NotFound<EnDossierDto>("محصول پیدا نشد.");
            bool writer = _user.IsInRole(EnRoles.Writers), approver = _user.IsInRole(EnRoles.Manager);
            bool seat = await EnActors.IsSeatAsync(_uow, Me, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            string note = EnText.Clean(dto.Note);
            string code = EnDossierBuilder.NormCode(dto.ReferenceCode);
            List<EnPlatformDossier> all = await _uow.Set<EnPlatformDossier>().Include(x => x.Documents).Where(x => x.ProductId == dto.ProductId && !x.Deleted).ToListAsync(ct);
            EnPlatformDossier d = code != null ? all.FirstOrDefault(x => x.ReferenceCode == code) : (all.Count == 1 ? all[0] : null);
            if (action == "native-standard")
            {
                // a fact of the original data sheet of the project this platform was built for: humans only; the standard is the client's, chosen from the approved reference lists
                string why = RecordStandardReason(writer, seat, product);
                string basis = EnText.Clean(dto.StandardBasis);
                if (why == null && code == null) why = "استاندارد دیتاشیت اصلی پلتفرم را بنویسید.";
                why ??= EnText.Over(code, 60, "کد استاندارد") ?? EnText.ReasonShort(basis, 5, "مبنای استاندارد (شمارهٔ دیتاشیت و بند)") ?? EnText.Over(basis, 500, "مبنای استاندارد");
                if (why == null) why = await EnReferenceRules.ApprovedCodeProblemAsync(_uow, code, ct);   // chosen from the approved lists, not typed
                if (why == null && d != null) why = "برای این استاندارد پرونده‌ای از پیش وجود دارد.";
                if (why == null && !await EnOrigin.HasDossierAsync(_uow, product.Id, ct) && await EnOrigin.IsUnregisteredNewDesignAsync(_uow, product.Id, ct)) why = EnOrigin.UnregisteredNew;
                if (why != null) return EnText.Bad<EnDossierDto>(why);
                d = new EnPlatformDossier { ProductId = product.Id, ReferenceCode = code, StandardBasis = basis, Status = EnDossierStatus.NotAssessed, CreateDate = now, CreatorUserId = Me };
                _uow.Set<EnPlatformDossier>().Add(d);
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateException) { return EnText.Bad<EnDossierDto>("برای این استاندارد هم‌زمان پرونده ساخته شد."); }
                return await GetDossierAsync(dto.ProductId, d.ReferenceCode, ct);
            }
            if (d == null) return EnText.NotFound<EnDossierDto>(all.Count > 1 ? "استاندارد پرونده را مشخص کنید؛ این محصول بیش از یک پرونده دارد." : EnText.NotFoundDossier);
            if (!EnConcurrency.Apply(_uow, d, dto.RowVersion)) return EnText.Bad<EnDossierDto>(EnText.InvalidData);
            string problem;
            if (action == "assess")
            {
                EnReferenceList current = await EnReferenceRules.CurrentAsync(_uow, false, ct, d.ReferenceCode);
                problem = AssessReason(writer, current != null);
                if (problem == null && !product.IsActive) problem = "فقط محصول فعال ارزیابی می‌شود.";
                if (problem != null) return EnText.Bad<EnDossierDto>(problem);
                bool changedList = d.ReferenceListId != current.Id;
                string before = EnPlatformRules.Hash(d.ProductId, d.ReferenceRevision, d.Documents);
                EnDossierBuilder.Merge(d, current);
                d.ReferenceListId = current.Id; d.ReferenceRevision = current.Revision;
                await EnDossierBuilder.ProveAsync(_uow, d, ct);
                d.AssessedAt = now;
                bool changed = changedList || before != EnPlatformRules.Hash(d.ProductId, d.ReferenceRevision, d.Documents);
                if (d.Status == EnDossierStatus.NotAssessed || changed) Recompute(d, true);
            }
            else if (action == "complete")
            {
                problem = CompleteReason(d, writer, seat);
                if (problem != null) return EnText.Bad<EnDossierDto>(problem);
                d.Status = EnDossierStatus.Completing; d.EditorUserIds = EnEditors.With(d.EditorUserIds, Me);
            }
            else if (action == "confirm")
            {
                problem = ConfirmReason(d, approver, seat, Me);
                if (problem != null) return EnText.Bad<EnDossierDto>(problem);
                EnReferenceList current = await EnReferenceRules.CurrentAsync(_uow, false, ct, d.ReferenceCode);
                if (current == null || d.ReferenceListId != current.Id) return EnText.Bad<EnDossierDto>("فهرست مرجع تغییر کرده است؛ ابتدا ارزیابی دوباره انجام شود.");
                d.Status = EnDossierStatus.Complete; d.ConfirmedByUserId = Me; d.ConfirmedAt = now; d.ConfirmedHash = EnPlatformRules.Hash(d.ProductId, d.ReferenceRevision, d.Documents);
            }
            else
            {
                problem = ReturnReasonOf(d, approver) ?? EnText.ReasonShort(note, EnRules.ReasonMin, "دلیل برگشت");
                if (problem != null) return EnText.Bad<EnDossierDto>(problem);
                if (dto.LineId.HasValue)
                {
                    EnPlatformDocument line = d.Documents.FirstOrDefault(x => x.Id == dto.LineId.Value);
                    if (line == null) return EnText.NotFound<EnDossierDto>("ردیف پیدا نشد.");
                    if (line.SystemProven) return EnText.Bad<EnDossierDto>("این ردیف را سامانه از وضعیت BOM یا مسیر ساخت اثبات کرده است و برگشت نمی‌خورد.");
                    line.State = EnLineState.Missing; line.DocumentId = null; line.ControlledDocumentId = null; line.Revision = null; line.NotApplicableReason = null;
                }
                d.Status = EnDossierStatus.Incomplete; d.ConfirmedHash = null; d.ConfirmedAt = null; d.ConfirmedByUserId = null; d.Note = EnText.Cut("برگشت: " + note, 1000);
            }
            d.ModifyDate = now; d.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnDossierDto>(EnText.Concurrency); }
            return await GetDossierAsync(dto.ProductId, d.ReferenceCode, ct);
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
                if (line.Required)
                {
                    // an instruction against a requirement of the standard is not executed: it becomes a deviation that the head of engineering AND the CEO must approve
                    problem = EnText.ReasonShort(reason, EnDeviations.InstructionMin, "دلیل انحراف از استاندارد") ?? EnText.Over(reason, 2000, "دلیل");
                    if (problem != null) return EnText.Bad<EnDossierDto>(problem);
                    if (await _uow.Set<EnDeviation>().AnyAsync(x => !x.Deleted && x.TargetType == EnDeviations.LineType && x.TargetId == line.Id && (x.Status == EnDeviationStatus.Requested || x.Status == EnDeviationStatus.ManagerApproved), ct))
                        return EnText.Bad<EnDossierDto>("برای این بند درخواست انحراف باز وجود دارد؛ منتظر تصمیم مدیر مهندسی و مدیرعامل بمانید.");
                    DateTimeOffset at = DateTimeOffset.Now;
                    string clause = line.ReferenceLineId.HasValue ? await _uow.Set<EnReferenceLine>().AsNoTracking().Where(l => l.Id == line.ReferenceLineId.Value).Select(l => l.StandardClause).FirstOrDefaultAsync(ct) : null;
                    EnDeviation dev = new EnDeviation
                    {
                        Kind = EnDeviationKind.NotApplicable, TargetType = EnDeviations.LineType, TargetId = line.Id, ProductId = d.ProductId, StandardCode = d.ReferenceCode, StandardClause = clause, Requirement = EnText.Cut(line.Title, 300), Instruction = reason,
                        RequestedByUserId = Me, RequestedAt = at, Status = EnDeviationStatus.Requested, ContentHash = EnDeviations.LineHash(line, d.ReferenceCode, reason), CreateDate = at, CreatorUserId = Me, ModifyDate = at, ModifierUserId = Me,
                    };
                    dev.Number = await EnNumbers.NextAsync(_uow, EnNumbers.Deviation, "EDV", at.DateTime, ct);
                    _uow.Set<EnDeviation>().Add(dev);
                    d.EditorUserIds = EnEditors.With(d.EditorUserIds, Me); d.ModifyDate = at; d.ModifierUserId = Me;
                    try { await _uow.SaveChangesAsync(ct); }
                    catch (DbUpdateConcurrencyException) { return EnText.Bad<EnDossierDto>(EnText.Concurrency); }
                    await EnDeviations.SyncAsync(_notify, dev, ct);
                    ResponseDto<EnDossierDto> shown = await GetDossierAsync(d.ProductId, d.ReferenceCode, ct);
                    return shown.Success ? ResponseDtoHelper.Ok(shown.Data, "بند الزامی است؛ غیرقابل‌اجرا شدن آن اعمال نشد و به‌صورت درخواست انحراف " + dev.Number + " برای تصمیم مدیر مهندسی و مدیرعامل ثبت شد.") : shown;
                }
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
                await EnDeviations.VoidOpenAsync(_uow, _notify, EnDeviations.LineType, line.Id, "بند تامین شد و درخواست انحراف دیگر لازم نیست.", Me, ct);
                line.State = EnLineState.Available; line.NotApplicableReason = null; line.DocumentId = dto.DocumentId; line.ControlledDocumentId = dto.ControlledDocumentId; line.Revision = revision;
            }
            DateTimeOffset now = DateTimeOffset.Now;
            string editorsFull = EnEditors.Full(d.EditorUserIds, Me);
            if (editorsFull != null) return EnText.Bad<EnDossierDto>(editorsFull);
            line.SuppliedByUserId = Me; line.SuppliedAt = now;
            d.EditorUserIds = EnEditors.With(d.EditorUserIds, Me); d.LastEditedByUserId = Me; d.ModifyDate = now; d.ModifierUserId = Me;
            Recompute(d, false);
            if (d.Status == EnDossierStatus.Completing) d.Status = EnDossierStatus.Incomplete;   // a change after "complete" needs the engineer to say it again
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnDossierDto>(EnText.Concurrency); }
            return await GetDossierAsync(d.ProductId, d.ReferenceCode, ct);
        }
    }
}
