using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.EngineeringDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Engineering
{
    public interface IEnRegistrationService
    {
        Task<ResponseDto<PagedList<EnRegistrationDto>>> GetRegistrationsAsync(EnRegistrationQuery query, CancellationToken ct);
        Task<ResponseDto<EnRegistrationDto>> GetRegistrationAsync(int id, CancellationToken ct);
        Task<ResponseDto<EnRegistrationDto>> ProposeAsync(EnRegistrationProposeDto dto, CancellationToken ct);
        Task<ResponseDto<EnRegistrationDto>> DecideAsync(EnRegistrationDecisionDto dto, CancellationToken ct);
    }

    /// <summary>A New design becomes a platform only on the proposal of the head of engineering and the approval of the CEO (two distinct persons, never a seat). The approval binds to the hash of the proposal; only at the approval is the dossier created (in the same save).</summary>
    public class EnRegistrationService : IEnRegistrationService
    {
        public const string KeyPrefix = "EN-REG-";
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public EnRegistrationService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;

        internal static string Hash(int caseId, int productId, string code, string basis, string rationale) =>
            EnHash.Of("REG|" + EnText.Num(caseId) + "|" + EnText.Num(productId) + "|" + code + "|" + EnHash.Norm(basis) + "|" + EnHash.Norm(rationale));

        internal static string StatusText(EnRegStatus s) => s == EnRegStatus.Proposed ? "منتظر تصمیم مدیرعامل" : s == EnRegStatus.Approved ? "تاییدشده (پلتفرم ثبت شد)" : s == EnRegStatus.Rejected ? "ردشده" : "پس‌گرفته یا باطل";

        private static string DecideReason(EnPlatformRegistration r, bool ceo, bool seat, int me) =>
            seat ? EnText.AiNever : r.Status != EnRegStatus.Proposed ? "فقط پیشنهاد در انتظار تصمیم تصمیم‌گیری می‌شود." : !ceo ? EnText.NotAllowed + " (تصمیم با مدیرعامل است)" : r.ProposedByUserId == me ? EnText.SameUser : null;

        private static string WithdrawReason(EnPlatformRegistration r, bool seat, int me) =>
            seat ? EnText.AiNever : r.Status != EnRegStatus.Proposed ? "فقط پیشنهاد در انتظار پس گرفته می‌شود." : r.ProposedByUserId != me ? EnText.NotAllowed + " (فقط پیشنهاددهنده پس می‌گیرد)" : null;

        private async Task<List<EnRegistrationDto>> DtosAsync(List<EnPlatformRegistration> rows, CancellationToken ct)
        {
            bool ceo = _user.IsInRole(EnRoles.Ceo), seat = await EnActors.IsSeatAsync(_uow, Me, ct);
            Dictionary<int, string> names = await EnUsers.NamesAsync(_uow, rows.SelectMany(r => new int?[] { r.ProposedByUserId, r.ApprovedByUserId, r.RejectedByUserId }), ct);
            List<int> caseIds = rows.Select(r => r.CaseId).Distinct().ToList(), productIds = rows.Select(r => r.ProductId).Distinct().ToList();
            Dictionary<int, string> cases = await _uow.Set<EnCase>().AsNoTracking().Where(c => caseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Number, ct);
            Dictionary<int, string> products = await _uow.Set<PbProduct>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            return rows.Select(r =>
            {
                string decide = DecideReason(r, ceo, seat, Me), withdraw = WithdrawReason(r, seat, Me);
                return new EnRegistrationDto
                {
                    Id = r.Id, Number = r.Number, CaseId = r.CaseId, CaseNumber = cases.TryGetValue(r.CaseId, out string cn) ? cn : null, ProductId = r.ProductId, ProductCode = products.TryGetValue(r.ProductId, out string pc) ? pc : null,
                    ReferenceCode = r.ReferenceCode, StandardBasis = r.StandardBasis, Rationale = r.Rationale, Status = (int)r.Status, StatusText = StatusText(r.Status), ProposedByName = names.Name(r.ProposedByUserId), ProposedAtText = EnText.Stamp(r.ProposedAt),
                    DecidedByName = names.Name(r.ApprovedByUserId ?? r.RejectedByUserId), DecidedAtText = EnText.Stamp(r.ApprovedAt ?? r.RejectedAt), DecisionNote = r.DecisionNote, DossierId = r.DossierId,
                    WaitingHours = r.Status == EnRegStatus.Proposed ? Math.Max(0, (int)Math.Floor((now - r.ProposedAt).TotalHours)) : 0, RowVersion = EnConcurrency.Text(r.RowVersion),
                    CanDecide = decide == null, DecideBlockedReason = decide, CanWithdraw = withdraw == null, WithdrawBlockedReason = withdraw,
                };
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<EnRegistrationDto>>> GetRegistrationsAsync(EnRegistrationQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<PagedList<EnRegistrationDto>>(EnText.NotAllowed);
            query ??= new EnRegistrationQuery();
            IQueryable<EnPlatformRegistration> q = _uow.Set<EnPlatformRegistration>().AsNoTracking().Where(x => !x.Deleted);
            if (query.CaseId.HasValue) q = q.Where(x => x.CaseId == query.CaseId.Value);
            if (query.Status.HasValue) { EnRegStatus s = (EnRegStatus)query.Status.Value; q = q.Where(x => x.Status == s); }
            if (query.OnlyOpen) q = q.Where(x => x.Status == EnRegStatus.Proposed);
            return ResponseDtoHelper.Ok(await EnPaging.CutAsync(q.OrderByDescending(x => x.Id), query.PageNumber, query.PageSize, rows => DtosAsync(rows, ct), ct));
        }

        public async Task<ResponseDto<EnRegistrationDto>> GetRegistrationAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<EnRegistrationDto>(EnText.NotAllowed);
            EnPlatformRegistration r = await _uow.Set<EnPlatformRegistration>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (r == null) return EnText.NotFound<EnRegistrationDto>("پیشنهاد ثبت پلتفرم پیدا نشد.");
            return ResponseDtoHelper.Ok((await DtosAsync(new List<EnPlatformRegistration> { r }, ct))[0]);
        }

        private async Task SyncAsync(EnPlatformRegistration r, CancellationToken ct) =>
            await OfficeGates.SyncAsync(_notify, KeyPrefix + EnText.Num(r.Id), r.Status == EnRegStatus.Proposed, "Engineering", nameof(EnPlatformRegistration), r.Id,
                "پیشنهاد ثبت طرح جدید به عنوان پلتفرم (" + r.Number + ", استاندارد " + r.ReferenceCode + ") منتظر تصمیم مدیرعامل است.", "/engineering/platform-registrations/" + EnText.Num(r.Id), EnRoles.Ceo, ct, null, OfInboxKind.Task);

        /// <summary>Why the case cannot take a registration proposal at all (null = it can): it is a New design that reached engineering, the PMO released its handover, it has a standard and no other proposal of the case waits. The case page flag and the service use this one predicate.</summary>
        internal static string CaseProblem(EnCase c, bool releasedHandover, bool otherOpenProposal) =>
            c.Kind != EnCaseKind.Turbine || c.Classification != EnClassification.New ? "فقط پروندهٔ طرح جدید می‌تواند پلتفرم شود."
            // only after the PMO released the handover of the case: the documents of the design exist and were handed over
            : !releasedHandover ? "ثبت پلتفرم فقط پس از ابلاغ تحویل مدارک این پرونده (تحویل ابلاغ‌شده توسط PMO) ممکن است."
            : string.IsNullOrWhiteSpace(c.ReferenceCode) ? "استاندارد پروژهٔ این پرونده هنوز ثبت نشده است."
            : otherOpenProposal ? "پیشنهاد بازی برای این پرونده منتظر تصمیم مدیرعامل است." : null;

        internal static async Task<string> CaseProblemAsync(IUnitOfWork uow, EnCase c, int? exceptId, CancellationToken ct)
        {
            bool released = await uow.Set<EnHandover>().AsNoTracking().AnyAsync(h => h.CaseId == c.Id && !h.Deleted && h.Status == EnHandoverStatus.Released, ct);
            bool open = await uow.Set<EnPlatformRegistration>().AsNoTracking().AnyAsync(x => !x.Deleted && x.Status == EnRegStatus.Proposed && x.CaseId == c.Id && x.Id != (exceptId ?? 0), ct);
            return CaseProblem(c, released, open);
        }

        /// <summary>The flag of the case page: who may propose (the engineering manager, never a seat) and the case-level refusal, from the same pieces the service uses.</summary>
        internal static string CaseProposalRefusal(EnCase c, bool manager, bool seat, bool releasedHandover, bool openProposal) =>
            !manager ? EnText.NotAllowed + " (پیشنهاد با مدیر مهندسی است)" : seat ? EnText.AiNever : CaseProblem(c, releasedHandover, openProposal);

        /// <summary>Why the content cannot be registered now (null = it can): the case problem, the product is active and belongs to the case, the standard is the case standard, the product has no dossier for it.</summary>
        private async Task<string> ContentProblemAsync(int caseId, int productId, string code, int? exceptId, CancellationToken ct)
        {
            EnCase c = await _uow.Set<EnCase>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == caseId && !x.Deleted, ct);
            if (c == null) return EnText.NotFoundCase;
            string caseProblem = await CaseProblemAsync(_uow, c, exceptId, ct);
            if (caseProblem != null) return caseProblem;
            PbProduct p = await _uow.Set<PbProduct>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == productId && !x.Deleted, ct);
            if (p == null || !p.IsActive) return "محصول فعال پیدا نشد.";
            if (!await EnOrigin.BelongsToCaseAsync(_uow, c, productId, ct)) return "این محصول به این پروندهٔ طراحی جدید تعلق ندارد (محصول باید همان محصول دستور کار قرارداد پرونده باشد).";
            if (!string.Equals(c.ReferenceCode, code, StringComparison.Ordinal)) return "استاندارد پلتفرم باید همان استاندارد پروژهٔ این پرونده (" + (c.ReferenceCode ?? "—") + ") باشد.";
            if (await _uow.Set<EnPlatformDossier>().AsNoTracking().AnyAsync(d => d.ProductId == productId && d.ReferenceCode == code && !d.Deleted, ct)) return "برای این محصول و استاندارد پرونده پلتفرم از پیش وجود دارد.";
            return null;
        }

        public async Task<ResponseDto<EnRegistrationDto>> ProposeAsync(EnRegistrationProposeDto dto, CancellationToken ct)
        {
            if (dto == null) return EnText.Bad<EnRegistrationDto>(EnText.InvalidData);
            if (!_user.IsInRole(EnRoles.Manager)) return EnText.Bad<EnRegistrationDto>(EnText.NotAllowed + " (پیشنهاد با مدیر مهندسی است)");
            if (await EnActors.IsSeatAsync(_uow, Me, ct)) return EnText.Bad<EnRegistrationDto>(EnText.AiNever);
            string code = EnDossierBuilder.NormCode(dto.ReferenceCode), basis = EnText.Clean(dto.StandardBasis), rationale = EnText.Clean(dto.Rationale);
            string why = code == null ? "کد استاندارد را بنویسید." : EnText.Over(code, 60, "کد استاندارد") ?? EnText.ReasonShort(basis, 5, "مبنای استاندارد (شمارهٔ دیتاشیت و بند)") ?? EnText.Over(basis, 500, "مبنای استاندارد") ?? EnText.ReasonShort(rationale, 30, "دلیل پلتفرم‌شدن") ?? EnText.Over(rationale, 2000, "دلیل");
            why ??= await ContentProblemAsync(dto.CaseId, dto.ProductId, code, null, ct);
            why ??= await EnReferenceRules.ApprovedCodeProblemAsync(_uow, code, ct);
            if (why != null) return EnText.Bad<EnRegistrationDto>(why);
            DateTimeOffset now = DateTimeOffset.Now;
            EnPlatformRegistration r = new EnPlatformRegistration
            {
                CaseId = dto.CaseId, ProductId = dto.ProductId, ReferenceCode = code, StandardBasis = basis, Rationale = rationale, ContentHash = Hash(dto.CaseId, dto.ProductId, code, basis, rationale), Status = EnRegStatus.Proposed,
                ProposedByUserId = Me, ProposedAt = now, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
            };
            r.Number = await EnNumbers.NextAsync(_uow, EnNumbers.PlatformRegistration, "EPR", now.DateTime, ct);
            _uow.Set<EnPlatformRegistration>().Add(r);
            await _uow.SaveChangesAsync(ct);
            await SyncAsync(r, ct);
            return await GetRegistrationAsync(r.Id, ct);
        }

        /// <summary>Copies state, file, revision and units from the lines of the Released handover of the case to the matching dossier lines (document type + title).</summary>
        private async Task CopyFromHandoverAsync(EnPlatformDossier d, int caseId, CancellationToken ct)
        {
            EnHandover h = await _uow.Set<EnHandover>().AsNoTracking().Include(x => x.Lines).Where(x => x.CaseId == caseId && !x.Deleted && x.Status == EnHandoverStatus.Released).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (h == null) return;
            foreach (EnPlatformDocument x in d.Documents)
            {
                EnHandoverLine l = h.Lines.OrderBy(y => y.Seq).FirstOrDefault(y => EnDossierBuilder.TypeTitle(y.DocumentType, y.Title) == EnDossierBuilder.TypeTitle(x.DocumentType, x.Title));
                if (l == null) continue;
                if (l.Units != null) x.Units = l.Units;
                if (x.SystemProven || l.LinkPath != null || l.State != EnLineState.Available || !l.DocumentId.HasValue) continue;   // a system line is proven by the bank (ProveAsync), not copied
                x.State = EnLineState.Available; x.DocumentId = l.DocumentId; x.Revision = l.Revision;
            }
        }

        public async Task<ResponseDto<EnRegistrationDto>> DecideAsync(EnRegistrationDecisionDto dto, CancellationToken ct)
        {
            if (dto == null) return EnText.Bad<EnRegistrationDto>(EnText.InvalidData);
            string decision = (dto.Decision ?? string.Empty).Trim().ToLowerInvariant();
            if (decision != "approve" && decision != "reject" && decision != "withdraw") return EnText.Bad<EnRegistrationDto>("تصمیم نامعتبر است (approve، reject، withdraw).");
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<EnRegistrationDto>(EnText.NotAllowed);
            EnPlatformRegistration r = await _uow.Set<EnPlatformRegistration>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return EnText.NotFound<EnRegistrationDto>("پیشنهاد ثبت پلتفرم پیدا نشد.");
            if (!EnConcurrency.Apply(_uow, r, dto.RowVersion)) return EnText.Bad<EnRegistrationDto>(EnText.InvalidData);
            bool ceo = _user.IsInRole(EnRoles.Ceo), seat = await EnActors.IsSeatAsync(_uow, Me, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            string note = EnText.Clean(dto.Note);
            string why = decision == "withdraw" ? WithdrawReason(r, seat, Me) : DecideReason(r, ceo, seat, Me);
            if (why == null && decision != "approve") why = EnText.ReasonShort(note, EnRules.ReasonMin, decision == "reject" ? "دلیل رد" : "دلیل پس‌گرفتن");
            why ??= EnText.Over(note, 1000, "توضیح");
            if (why != null) return EnText.Bad<EnRegistrationDto>(why);
            if (decision == "approve")
            {
                // the approval binds to the exact proposal and to the state of the world it was made in
                string problem = Hash(r.CaseId, r.ProductId, r.ReferenceCode, r.StandardBasis, r.Rationale) != r.ContentHash ? "محتوای پیشنهاد تغییر کرده است." : await ContentProblemAsync(r.CaseId, r.ProductId, r.ReferenceCode, r.Id, ct);
                if (problem != null)
                {
                    r.Status = EnRegStatus.Void; r.DecisionNote = EnText.Cut("پیشنهاد باطل شد: " + problem, 1000); r.ModifyDate = now; r.ModifierUserId = Me;
                    await _uow.SaveChangesAsync(ct);
                    await SyncAsync(r, ct);
                    return EnText.Bad<EnRegistrationDto>(r.DecisionNote);
                }
                EnPlatformDossier d = await EnDossierBuilder.EnsureAsync(_uow, r.ProductId, r.ReferenceCode, r.StandardBasis, Me, now, ct);
                await CopyFromHandoverAsync(d, r.CaseId, ct);   // the dossier starts with what the Released handover carried: state, file, revision and units of every matching line
                r.Status = EnRegStatus.Approved; r.ApprovedByUserId = Me; r.ApprovedAt = now; r.DecisionNote = note; r.ModifyDate = now; r.ModifierUserId = Me;
                EnCase c = await _uow.Set<EnCase>().FirstOrDefaultAsync(x => x.Id == r.CaseId, ct);
                if (c != null) EnEvents.Add(_uow, c, "platform-registered", Me, r.Number + ": " + r.ReferenceCode);
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException) { return EnText.Bad<EnRegistrationDto>(EnText.Concurrency); }
                catch (DbUpdateException) { return EnText.Bad<EnRegistrationDto>("برای این محصول و استاندارد هم‌زمان پرونده ساخته شد."); }
                r.DossierId = d.Id;
                await _uow.SaveChangesAsync(ct);
            }
            else
            {
                if (decision == "reject") { r.Status = EnRegStatus.Rejected; r.RejectedByUserId = Me; r.RejectedAt = now; }
                else r.Status = EnRegStatus.Void;
                r.DecisionNote = note; r.ModifyDate = now; r.ModifierUserId = Me;
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException) { return EnText.Bad<EnRegistrationDto>(EnText.Concurrency); }
            }
            await SyncAsync(r, ct);
            return await GetRegistrationAsync(r.Id, ct);
        }
    }
}
