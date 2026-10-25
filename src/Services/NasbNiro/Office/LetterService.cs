using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Tenders;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.OfficeDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Office
{
    public interface ILetterService
    {
        Task<ResponseDto<PagedList<LetterListDto>>> GetLettersAsync(LetterQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<LetterDto>> GetLetterAsync(int id, CancellationToken cancellationToken);

        /// <summary>The ids of the letters the caller may read in full (the same rule as <see cref="GetLetterAsync"/>, as one query that can be composed with others on the same unit of work).</summary>
        Task<IQueryable<int>> ReadableLetterIdsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<LetterDto>> SaveLetterAsync(SaveLetterDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<LetterDto>> LetterActionAsync(LetterActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<LetterVerifyDto>> VerifyAsync(int id, CancellationToken cancellationToken);

        /// <summary>Whether the user may read this file of the letter (attachment, signature image or receipt): full access to the letter only.</summary>
        Task<bool> CanReadFileAsync(int letterId, Guid documentId, CancellationToken cancellationToken);
        Task<ResponseDto<LetterDto>> ReferAsync(ReferLetterDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<LetterDto>> ReferralActionAsync(ReferralActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<ExternalPartyDto>>> GetPartiesAsync(OfficePageQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<ExternalPartyDto>> SavePartyAsync(SaveExternalPartyDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<NumberingRuleDto>>> GetRulesAsync(OfficePageQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<NumberingRuleDto>> SaveRuleAsync(SaveNumberingRuleDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<LetterTemplateDto>>> GetTemplatesAsync(OfficePageQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<LetterTemplateDto>> SaveTemplateAsync(SaveLetterTemplateDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ImportLettersResultDto>> ImportAsync(ImportLettersDto dto, CancellationToken cancellationToken);

        /// <summary>
        /// Rahkaran archive (letters are Word documents, the export is PDF): attaches each PDF to the imported letter whose old number is in the file name.
        /// Reports the files that match no letter (or several) and the imported letters that still have no file. ★ Awaiting a real export sample.
        /// </summary>
        Task<ResponseDto<ArchiveFilesResultDto>> AttachArchiveFilesAsync(ArchiveFilesDto dto, List<ArchiveFileInput> files, CancellationToken cancellationToken);
    }

    /// <summary>Letters (مکاتبات): drafting, approval chain, signature with a content hash, numbering by rule, registers, referrals and the legacy archive import.</summary>
    public partial class LetterService : ILetterService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly global::Services.Contracts.IDocumentService _documents;

        public LetterService(IUnitOfWork uow, IInventoryUserContext user, global::Services.Contracts.IDocumentService documents)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => OfficeText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => OfficeText.Bad<T>(m);

        private enum Access { None, Meta, Full }

        private sealed class DeputyGroup
        {
            public DateTime Since;
            public List<int> Positions;
        }

        private sealed class Ctx
        {
            public List<ActingPosition> Acting;

            /// <summary>Every position the user acts for (held or as a deputy).</summary>
            public List<int> Pos;

            /// <summary>Positions the user holds himself.</summary>
            public List<int> Holder;

            /// <summary>Deputy positions grouped by the start of the delegation.</summary>
            public List<DeputyGroup> Deputy;
            public bool IsSec;
            public bool IsCeo;

            /// <summary>The positions that count as a party of this issued letter: a deputy does not count for a Secret letter dated before his delegation started.</summary>
            public List<int> PosFor(OfLetter l)
            {
                if (l.Classification != OfClassification.Secret || Deputy.Count == 0) return Pos;
                var list = new List<int>(Holder);
                foreach (DeputyGroup g in Deputy) if (l.Date.Date >= g.Since.Date) list.AddRange(g.Positions);
                return list;
            }
        }

        private async Task<Ctx> ContextAsync(CancellationToken ct)
        {
            List<ActingPosition> acting = await OfficeActors.ActingAsync(_uow, Me, ct);
            return new Ctx
            {
                Acting = acting, Pos = acting.Select(a => a.PositionId).ToList(), Holder = acting.Where(a => !a.OnBehalf).Select(a => a.PositionId).ToList(),
                Deputy = acting.Where(a => a.OnBehalf).GroupBy(a => (a.Since ?? DateTime.Today).Date).Select(g => new DeputyGroup { Since = g.Key, Positions = g.Select(x => x.PositionId).ToList() }).ToList(),
                IsSec = _user.IsInRole(OfficeRoles.Secretariat), IsCeo = _user.IsInRole(OfficeRoles.Ceo),
            };
        }

        /// <summary>
        /// Who reads what. Full: the writer, the explicit parties (sender, signer, approval chain, recipients, the positions of active referrals) and the CEO.
        /// Company decision (office answers 1405/08, replacing the earlier "register data only" rule): the central-office secretariat (role SECRETARIAT) reads Normal and Confidential letters in full once they are numbered
        /// (content, attachments, search, lists, prints); for Secret letters it gets register data only (Meta): number, date, kind, classification, parties, delivery data. Only the parties and the CEO read a Secret letter.
        /// The AI seat OFFICE-DRAFT also runs as SECRETARIAT but keeps its own Normal-only filter (<see cref="SecretariatReadsContent"/> is not what decides what the seat reads).
        /// A deputy never reads Secret letters dated before his delegation started. A cancelled referral gives no access (its creator keeps it).
        /// </summary>
        private Access AccessOf(OfLetter l, Ctx c)
        {
            int me = Me;
            bool creator = l.CreatorUserId == me;
            if (l.Status == OfLetterStatus.Draft) return creator ? Access.Full : Access.None;
            bool sender = l.SenderPositionId.HasValue && c.Pos.Contains(l.SenderPositionId.Value);
            bool signer = l.SignerPositionId.HasValue && c.Pos.Contains(l.SignerPositionId.Value);
            bool chain = (l.Approvals ?? new List<OfLetterApproval>()).Any(a => c.Pos.Contains(a.PositionId));
            if (l.Status == OfLetterStatus.InApproval) return creator || sender || signer || chain ? Access.Full : Access.None;
            List<int> pos = c.PosFor(l);
            sender = l.SenderPositionId.HasValue && pos.Contains(l.SenderPositionId.Value);
            signer = l.SignerPositionId.HasValue && pos.Contains(l.SignerPositionId.Value);
            chain = (l.Approvals ?? new List<OfLetterApproval>()).Any(a => pos.Contains(a.PositionId));
            bool recipient = (l.Recipients ?? new List<OfLetterRecipient>()).Any(r => r.PositionId.HasValue && pos.Contains(r.PositionId.Value));
            bool referral = (l.Referrals ?? new List<OfReferral>()).Any(r => r.CreatedByUserId == me
                || (r.Status != OfReferralStatus.Cancelled && (pos.Contains(r.ToPositionId) || (r.FromPositionId.HasValue && pos.Contains(r.FromPositionId.Value)))));
            if (creator || sender || signer || chain || recipient || referral) return Access.Full;
            if (c.IsCeo) return Access.Full;
            if (c.IsSec && l.Number != null) return SecretariatReadsContent(l.Classification) ? Access.Full : Access.Meta;
            return Access.None;
        }

        /// <summary>The secretariat reads the content of Normal and Confidential letters; Secret letters stay restricted to their parties and the CEO. Keep <see cref="FullExpr"/> in step.</summary>
        internal static bool SecretariatReadsContent(OfClassification classification) => classification != OfClassification.Secret;

        private static Expression<Func<OfLetter, bool>> PartyOf(List<int> pos, int me) =>
            l => (l.SenderPositionId != null && pos.Contains(l.SenderPositionId.Value))
                || (l.SignerPositionId != null && pos.Contains(l.SignerPositionId.Value))
                || l.Approvals.Any(a => pos.Contains(a.PositionId))
                || l.Recipients.Any(r => r.PositionId != null && pos.Contains(r.PositionId.Value))
                || l.Referrals.Any(r => r.CreatedByUserId == me
                    || (r.Status != OfReferralStatus.Cancelled && (pos.Contains(r.ToPositionId) || (r.FromPositionId != null && pos.Contains(r.FromPositionId.Value)))));

        /// <summary>The letters the user may read in full (same rules as <see cref="AccessOf"/>).</summary>
        private Expression<Func<OfLetter, bool>> FullExpr(Ctx c)
        {
            int me = Me;
            List<int> pos = c.Pos;
            Expression<Func<OfLetter, bool>> draft = l => l.Status == OfLetterStatus.Draft && l.CreatorUserId == me;
            Expression<Func<OfLetter, bool>> approval = l => l.Status == OfLetterStatus.InApproval && (l.CreatorUserId == me
                || (l.SenderPositionId != null && pos.Contains(l.SenderPositionId.Value))
                || (l.SignerPositionId != null && pos.Contains(l.SignerPositionId.Value))
                || l.Approvals.Any(a => pos.Contains(a.PositionId)));
            Expression<Func<OfLetter, bool>> issued = l => l.Status != OfLetterStatus.Draft && l.Status != OfLetterStatus.InApproval;
            Expression<Func<OfLetter, bool>> access;
            if (c.IsCeo) access = l => true;
            else
            {
                Expression<Func<OfLetter, bool>> mine = l => l.CreatorUserId == me;
                Expression<Func<OfLetter, bool>> ordinary = Pred.And<OfLetter>(l => l.Classification != OfClassification.Secret, PartyOf(pos, me));
                Expression<Func<OfLetter, bool>> secretParty = PartyOf(c.Holder, me);
                foreach (DeputyGroup g in c.Deputy)
                {
                    DateTime since = g.Since.Date;
                    secretParty = Pred.Or(secretParty, Pred.And<OfLetter>(l => l.Date >= since, PartyOf(g.Positions, me)));
                }
                Expression<Func<OfLetter, bool>> secret = Pred.And<OfLetter>(l => l.Classification == OfClassification.Secret, secretParty);
                access = Pred.Or(mine, Pred.Or(ordinary, secret));
                // the secretariat reads numbered Normal and Confidential letters (same rule as SecretariatReadsContent); Secret ones only through the party rules above
                if (c.IsSec) access = Pred.Or<OfLetter>(access, l => l.Number != null && l.Classification != OfClassification.Secret);
            }
            return Pred.Or(Pred.Or(draft, approval), Pred.And(issued, access));
        }

        public async Task<IQueryable<int>> ReadableLetterIdsAsync(CancellationToken ct)
        {
            Ctx c = await ContextAsync(ct);
            return _uow.Set<OfLetter>().AsNoTracking().Where(l => !l.Deleted).Where(FullExpr(c)).Select(l => l.Id);
        }

        /// <summary>Everything the user may see: the letters read in full, plus (secretariat) the numbered Secret ones as register data only.</summary>
        private IQueryable<OfLetter> Visible(IQueryable<OfLetter> q, Ctx c, Expression<Func<OfLetter, bool>> full)
        {
            if (!c.IsSec) return q.Where(full);
            return q.Where(Pred.Or<OfLetter>(full, l => l.Number != null && l.Status != OfLetterStatus.Draft && l.Status != OfLetterStatus.InApproval));
        }

        // -------------------------------------------------------------------------------- reading

        private async Task<(Dictionary<int, string> Positions, Dictionary<int, string> Parties, Dictionary<int, string> Users)> NamesAsync(IEnumerable<OfLetter> letters, CancellationToken ct)
        {
            List<OfLetter> list = letters.ToList();
            var positionIds = new List<int?>();
            var partyIds = new List<int>();
            var userIds = new List<int?>();
            foreach (OfLetter l in list)
            {
                positionIds.Add(l.SenderPositionId); positionIds.Add(l.SignerPositionId);
                if (l.SenderPartyId.HasValue) partyIds.Add(l.SenderPartyId.Value);
                userIds.Add(l.CreatorUserId); userIds.Add(l.SignedByUserId);
                foreach (OfLetterRecipient r in l.Recipients ?? new List<OfLetterRecipient>()) { positionIds.Add(r.PositionId); if (r.PartyId.HasValue) partyIds.Add(r.PartyId.Value); }
                foreach (OfLetterApproval a in l.Approvals ?? new List<OfLetterApproval>()) { positionIds.Add(a.PositionId); userIds.Add(a.ActedByUserId); }
                if (l.SignedOnBehalfOfPositionId.HasValue) positionIds.Add(l.SignedOnBehalfOfPositionId);
                foreach (OfReferral r in l.Referrals ?? new List<OfReferral>())
                {
                    positionIds.Add(r.FromPositionId); positionIds.Add(r.ToPositionId); userIds.Add(r.CreatedByUserId); userIds.Add(r.DoneByUserId);
                    foreach (OfReferralEntry e in r.Entries ?? new List<OfReferralEntry>()) userIds.Add(e.UserId);
                }
            }
            partyIds = partyIds.Distinct().ToList();
            Dictionary<int, string> parties = partyIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<OfExternalParty>().AsNoTracking().Where(p => partyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            return (await OfficeActors.PositionTitlesAsync(_uow, positionIds, ct), parties, await InventoryUsers.GetNamesAsync(_uow, userIds, ct));
        }

        private static string Join(IEnumerable<string> parts) => string.Join("، ", parts.Where(p => p != null));

        private LetterListDto FillList(LetterListDto d, OfLetter l, Access access, Dictionary<int, string> positions, Dictionary<int, string> parties, Dictionary<int, LinkInfo> links = null)
        {
            d.Id = l.Id; d.Kind = l.Kind; d.Number = l.Number; d.Date = l.Date; d.DateText = OfficeText.Date(l.Date); d.Status = l.Status; d.Classification = l.Classification;
            d.Urgency = l.Urgency; d.Imported = l.Imported; d.MetadataOnly = access == Access.Meta;
            d.Subject = access == Access.Meta ? (l.Classification == OfClassification.Secret ? "(نامه سری — فقط مشخصات ثبت)" : "(نامه محرمانه — فقط مشخصات ثبت)") : l.Subject;
            d.TenderId = l.TenderId; d.ContractId = l.ContractId;
            if (links != null && links.TryGetValue(l.Id, out LinkInfo link)) { d.LinkCode = link.Code; d.LinkTitle = link.Title; }
            // the parties are register data: the secretariat sees them, never the subject or the text
            d.SenderName = l.SenderPositionId.HasValue ? positions.Title(l.SenderPositionId) : (l.SenderPartyId.HasValue && parties.TryGetValue(l.SenderPartyId.Value, out string pn) ? pn : null);
            d.RecipientNames = Join((l.Recipients ?? new List<OfLetterRecipient>()).Where(r => r.Type == OfRecipientType.To)
                .Select(r => r.PositionId.HasValue ? positions.Title(r.PositionId) : (r.PartyId.HasValue && parties.TryGetValue(r.PartyId.Value, out string rn) ? rn : null)));
            return d;
        }

        public async Task<ResponseDto<PagedList<LetterListDto>>> GetLettersAsync(LetterQuery query, CancellationToken ct)
        {
            query ??= new LetterQuery();
            Ctx c = await ContextAsync(ct);
            int me = Me;
            Expression<Func<OfLetter, bool>> full = FullExpr(c);
            IQueryable<OfLetter> q = Visible(_uow.Set<OfLetter>().AsNoTracking().Where(l => !l.Deleted), c, full);
            if (query.Kind.HasValue) q = q.Where(l => l.Kind == query.Kind.Value);
            if (query.Status.HasValue) q = q.Where(l => l.Status == query.Status.Value);
            if (query.Classification.HasValue) q = q.Where(l => l.Classification == query.Classification.Value);
            string number = Clean(query.Number);
            if (number != null) q = q.Where(l => l.Number != null && l.Number.Contains(number));
            string subject = Clean(query.Subject);
            // subject, text and linked-record filters run only on the letters the user may read in full: otherwise a search would reveal the content of the others
            if (subject != null) q = q.Where(Pred.And<OfLetter>(full, l => l.Subject.Contains(subject)));
            if (query.DateFrom.HasValue) { DateTime from = query.DateFrom.Value.Date; q = q.Where(l => l.Date >= from); }
            if (query.DateTo.HasValue) { DateTime to = query.DateTo.Value.Date; q = q.Where(l => l.Date <= to); }
            string party = Clean(query.Party);
            if (party != null)
            {
                List<int> partyIds = await _uow.Set<OfExternalParty>().AsNoTracking().Where(p => p.Name.Contains(party)).Select(p => p.Id).ToListAsync(ct);
                q = q.Where(l => (l.SenderPartyId != null && partyIds.Contains(l.SenderPartyId.Value)) || l.Recipients.Any(r => r.PartyId != null && partyIds.Contains(r.PartyId.Value)));
            }
            if (query.RelatedKind.HasValue)
            {
                OfRelatedKind rk = query.RelatedKind.Value;
                int? rid = query.RelatedRecordId;
                q = q.Where(Pred.And<OfLetter>(full, l => l.Links.Any(k => k.Kind == rk && (rid == null || k.RecordId == rid))));
            }
            // text search: subject and body (LIKE; full-text indexes are a deployment option)
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(Pred.Or(Pred.And<OfLetter>(full, l => l.Subject.Contains(text) || (l.BodyHtml != null && l.BodyHtml.Contains(text))), l => l.Number != null && l.Number.Contains(text)));
            if (query.MineOnly == true) q = q.Where(l => l.CreatorUserId == me);
            if (query.WaitingForMe == true)
            {
                List<int> pos = c.Pos;
                q = q.Where(l => l.Status == OfLetterStatus.InApproval
                    && (l.Approvals.Any(a => a.Round == l.ApprovalRound && a.Status == OfApprovalStatus.Pending && pos.Contains(a.PositionId))
                        || (l.SignerPositionId != null && pos.Contains(l.SignerPositionId.Value) && !l.Approvals.Any(a => a.Round == l.ApprovalRound && a.Status == OfApprovalStatus.Pending))));
            }
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int pageNo, out int size);
            List<OfLetter> rows = await q.Include(l => l.Recipients).Include(l => l.Approvals).Include(l => l.Referrals)
                .OrderByDescending(l => l.Date).ThenByDescending(l => l.Id).Skip((pageNo - 1) * size).Take(size).AsSplitQuery().ToListAsync(ct);
            var names = await NamesAsync(rows, ct);
            Dictionary<int, LinkInfo> links = await LinksAsync(rows, ct);
            List<LetterListDto> items = rows.Select(l => FillList(new LetterListDto(), l, AccessOf(l, c), names.Positions, names.Parties, links)).ToList();
            return ResponseDtoHelper.Ok(F2Page.Make(items, total, pageNo, size));
        }

        private async Task<OfLetter> LoadAsync(int id, bool tracking, CancellationToken ct)
        {
            IQueryable<OfLetter> q = _uow.Set<OfLetter>().Where(l => l.Id == id && !l.Deleted)
                .Include(l => l.Recipients).Include(l => l.Approvals).Include(l => l.Attachments).Include(l => l.Links).Include(l => l.Referrals).ThenInclude(r => r.Entries)
                .AsSplitQuery();
            if (!tracking) q = q.AsNoTracking();
            return await q.FirstOrDefaultAsync(ct);
        }

        private List<string> AllowedActions(OfLetter l, Ctx c)
        {
            var a = new List<string>();
            bool creator = l.CreatorUserId == Me;
            switch (l.Status)
            {
                case OfLetterStatus.Draft:
                    if (creator && l.Kind == OfLetterKind.Incoming && c.IsSec) { a.Add("save"); a.Add("register"); a.Add("cancel"); }
                    else if (creator) { a.Add("save"); a.Add("submit"); a.Add("cancel"); }
                    break;
                case OfLetterStatus.InApproval:
                    OfLetterApproval next = l.Approvals.Where(x => x.Round == l.ApprovalRound && x.Status == OfApprovalStatus.Pending).OrderBy(x => x.Seq).FirstOrDefault();
                    if (next != null && c.Pos.Contains(next.PositionId) && !creator) { a.Add("approve"); a.Add("return"); }
                    if (next == null && l.SignerPositionId.HasValue && c.Pos.Contains(l.SignerPositionId.Value)) { a.Add("sign"); a.Add("return"); }
                    if (creator) a.Add("cancel");
                    break;
                case OfLetterStatus.Signed:
                    if (c.IsSec) { a.Add("register"); a.Add("send"); a.Add("cancel"); }
                    break;
                case OfLetterStatus.Registered:
                    if (c.IsSec) { a.Add("send"); a.Add("archive"); a.Add("cancel"); }
                    break;
                case OfLetterStatus.Sent:
                case OfLetterStatus.Received:
                    if (c.IsSec) { a.Add("receipt"); a.Add("archive"); a.Add("cancel"); }
                    if (CanRefer(l, c)) a.Add("refer");
                    break;
            }
            return a;
        }

        private bool CanRefer(OfLetter l, Ctx c)
        {
            if (l.Status != OfLetterStatus.Sent && l.Status != OfLetterStatus.Received && l.Status != OfLetterStatus.Registered) return false;
            return c.Pos.Any(p => HoldsLetter(l, p));
        }

        /// <summary>A position holds the letter when it is the sender, a recipient or the target of a referral.</summary>
        private static bool HoldsLetter(OfLetter l, int positionId) =>
            l.SenderPositionId == positionId || (l.Recipients ?? new List<OfLetterRecipient>()).Any(r => r.PositionId == positionId)
            || (l.Referrals ?? new List<OfReferral>()).Any(r => r.ToPositionId == positionId && r.Status != OfReferralStatus.Cancelled);

        private ReferralDto ToDto(OfReferral r, Dictionary<int, string> positions, Dictionary<int, string> users) => new ReferralDto
        {
            Id = r.Id, LetterId = r.LetterId, ParentId = r.ParentId, FromPositionId = r.FromPositionId, FromPositionTitle = positions.Title(r.FromPositionId), ToPositionId = r.ToPositionId,
            ToPositionTitle = positions.Title(r.ToPositionId), Instruction = r.Instruction, Action = r.Action, DueDate = r.DueDate, DueDateText = OfficeText.Date(r.DueDate), Status = r.Status,
            CreatedByName = users.Name(r.CreatedByUserId), OnBehalf = r.OnBehalf, CreatedAt = r.CreatedAt, CreatedAtText = OfficeText.Stamp(r.CreatedAt), DoneAt = r.DoneAt,
            DoneByName = users.Name(r.DoneByUserId), DoneNote = r.DoneNote,
            CanCancel = r.Status == OfReferralStatus.Pending && (r.CreatedByUserId == Me || _user.IsInRole(OfficeRoles.Secretariat)),
            Entries = (r.Entries ?? new List<OfReferralEntry>()).OrderBy(e => e.At).Select(e => new ReferralEntryDto { UserName = users.Name(e.UserId), OnBehalf = e.OnBehalf, Text = e.Text, At = e.At, AtText = OfficeText.Stamp(e.At) }).ToList(),
        };

        private async Task<LetterDto> BuildAsync(OfLetter l, Ctx c, CancellationToken ct)
        {
            Access access = AccessOf(l, c);
            var names = await NamesAsync(new[] { l }, ct);
            var d = new LetterDto();
            FillList(d, l, access, names.Positions, names.Parties, await LinksAsync(new[] { l }, ct));
            if (access == Access.Meta)
            {
                // register data only (a Secret letter seen by the secretariat): parties, delivery fields and what the secretariat may do. No subject, text, attachments, signature image or referral notes.
                d.Recipients = l.Recipients.OrderBy(r => r.Type).ThenBy(r => r.Id).Select(r => new RecipientDto
                {
                    Type = r.Type, PositionTitle = names.Positions.Title(r.PositionId),
                    PartyName = r.PartyId.HasValue && names.Parties.TryGetValue(r.PartyId.Value, out string mpn) ? mpn : null,
                }).ToList();
                d.SignedAt = l.SignedAt; d.SignedAtText = OfficeText.Stamp(l.SignedAt); d.PaperCopy = l.PaperCopy; d.DeliveryMethod = l.DeliveryMethod; d.SentDate = l.SentDate;
                d.SentDateText = OfficeText.Date(l.SentDate); d.DeliveryReceipt = l.DeliveryReceipt; d.CancelReason = l.CancelReason;
                d.AllowedActions = AllowedActions(l, c).Where(x => x != "refer").ToList();
                d.RowVersion = l.RowVersion == null ? null : Convert.ToBase64String(l.RowVersion);
                return d;
            }
            d.BodyHtml = l.BodyHtml; d.SenderPositionId = l.SenderPositionId; d.SenderPartyId = l.SenderPartyId; d.IncomingNumber = l.IncomingNumber; d.IncomingDate = l.IncomingDate;
            d.IncomingDateText = OfficeText.Date(l.IncomingDate); d.SignerPositionId = l.SignerPositionId; d.SignerPositionTitle = names.Positions.Title(l.SignerPositionId);
            d.ApprovalRound = l.ApprovalRound; d.SignedAt = l.SignedAt; d.SignedAtText = OfficeText.Stamp(l.SignedAt); d.SignedByName = names.Users.Name(l.SignedByUserId);
            d.SignedOnBehalfOfPosition = names.Positions.Title(l.SignedOnBehalfOfPositionId); d.SignatureDocumentId = l.SignatureDocumentId; d.ContentHash = l.ContentHash;
            d.PaperCopy = l.PaperCopy; d.DeliveryMethod = l.DeliveryMethod; d.SentDate = l.SentDate; d.SentDateText = OfficeText.Date(l.SentDate); d.DeliveryReceipt = l.DeliveryReceipt;
            d.DeliveryReceiptDocumentId = l.DeliveryReceiptDocumentId; d.ReplyToLetterId = l.ReplyToLetterId; d.FollowUpLetterId = l.FollowUpLetterId; d.TemplateId = l.TemplateId;
            d.CancelReason = l.CancelReason; d.CreatedByName = names.Users.Name(l.CreatorUserId);
            d.Recipients = l.Recipients.OrderBy(r => r.Type).ThenBy(r => r.Id).Select(r => new RecipientDto
            {
                Type = r.Type, PositionId = r.PositionId, PositionTitle = names.Positions.Title(r.PositionId), PartyId = r.PartyId,
                PartyName = r.PartyId.HasValue && names.Parties.TryGetValue(r.PartyId.Value, out string pn) ? pn : null,
            }).ToList();
            d.Approvals = l.Approvals.OrderBy(a => a.Round).ThenBy(a => a.Seq).Select(a => new LetterApprovalDto
            {
                Round = a.Round, Seq = a.Seq, PositionId = a.PositionId, PositionTitle = names.Positions.Title(a.PositionId), Status = a.Status, ActedByName = names.Users.Name(a.ActedByUserId),
                OnBehalf = a.OnBehalf, Comment = a.Comment, ActedAt = a.ActedAt, ActedAtText = OfficeText.Stamp(a.ActedAt),
            }).ToList();
            d.Attachments = l.Attachments.Select(a => new LetterAttachmentDto { DocumentId = a.DocumentId, FileName = a.FileName }).ToList();
            d.Links = l.Links.Select(k => new RecordLinkDto { Kind = k.Kind, RecordId = k.RecordId, Text = k.Text }).ToList();
            var all = l.Referrals.OrderBy(r => r.Id).Select(r => ToDto(r, names.Positions, names.Users)).ToList();
            foreach (ReferralDto r in all)
            {
                ReferralDto parent = r.ParentId.HasValue ? all.FirstOrDefault(x => x.Id == r.ParentId.Value) : null;
                if (parent != null) parent.Children.Add(r); else d.Referrals.Add(r);
            }
            d.AllowedActions = AllowedActions(l, c);
            d.RowVersion = l.RowVersion == null ? null : Convert.ToBase64String(l.RowVersion);
            return d;
        }

        public async Task<ResponseDto<LetterDto>> GetLetterAsync(int id, CancellationToken ct)
        {
            Ctx c = await ContextAsync(ct);
            OfLetter l = await LoadAsync(id, false, ct);
            // a letter the person may not see is reported as missing, so its existence is not revealed
            if (l == null || AccessOf(l, c) == Access.None) return OfficeText.NotFound<LetterDto>("نامه پیدا نشد.");
            return ResponseDtoHelper.Ok(await BuildAsync(l, c, ct));
        }

        // -------------------------------------------------------------------------------- inbox helpers (same save as the change)

        private void AddInbox(string key, OfInboxKind kind, string title, int letterId, int? positionId, int? userId, string role, DateTime? due = null, OfUrgency priority = OfUrgency.Normal)
        {
            _uow.Set<OfInboxItem>().Add(new OfInboxItem
            {
                RecipientPositionId = positionId, RecipientUserId = userId, RecipientRole = role, Kind = kind, Title = title.Length > 300 ? title.Substring(0, 300) : title,
                SourceModule = "office", SourceType = "letter", SourceId = letterId, SourceKey = key, Link = "/office/letters/" + letterId, DueDate = due, Priority = priority,
                Status = OfInboxStatus.Unread, CreatedAt = DateTimeOffset.Now,
            });
        }

        private async Task CloseKeyAsync(string key, string reason, CancellationToken ct)
        {
            List<OfInboxItem> items = await _uow.Set<OfInboxItem>().Where(i => i.SourceKey == key && (i.Status == OfInboxStatus.Unread || i.Status == OfInboxStatus.Read)).ToListAsync(ct);
            foreach (OfInboxItem i in items) { i.Status = OfInboxStatus.Done; i.ClosedAt = DateTimeOffset.Now; i.ClosedReason = reason; }
        }

        private async Task CloseLetterItemsAsync(int letterId, string reason, CancellationToken ct)
        {
            List<OfInboxItem> items = await _uow.Set<OfInboxItem>().Where(i => i.SourceModule == "office" && i.SourceType == "letter" && i.SourceId == letterId
                && (i.Status == OfInboxStatus.Unread || i.Status == OfInboxStatus.Read) && i.Kind != OfInboxKind.Letter && i.Kind != OfInboxKind.Referral).ToListAsync(ct);
            foreach (OfInboxItem i in items) { i.Status = OfInboxStatus.Cancelled; i.ClosedAt = DateTimeOffset.Now; i.ClosedReason = reason; }
        }

        private void DeliverToPositions(OfLetter l)
        {
            foreach (OfLetterRecipient r in l.Recipients.Where(x => x.PositionId.HasValue).GroupBy(x => x.PositionId.Value).Select(g => g.OrderBy(x => x.Type).First()))
                AddInbox("letter:" + l.Id + ":delivery", OfInboxKind.Letter, (r.Type == OfRecipientType.Cc ? "رونوشت نامه: " : "نامه جدید: ") + l.Subject, l.Id, r.PositionId, null, null, null, l.Urgency);
        }

        // -------------------------------------------------------------------------------- tender / project link

        private sealed class LinkInfo
        {
            public string Code;
            public string Title;
        }

        /// <summary>The project code users know («کد پروژه»): the 9-character code issued by project control for the contract; a contract without one yet falls back to its contract code. ★</summary>
        internal static string ProjectCodeOf(string projectControlCode, string contractCode) => Clean(projectControlCode) ?? Clean(contractCode);

        /// <summary>The code and the label of the tender or project of each letter (one query per kind).</summary>
        private async Task<Dictionary<int, LinkInfo>> LinksAsync(IEnumerable<OfLetter> letters, CancellationToken ct)
        {
            List<OfLetter> list = letters.Where(l => l.TenderId.HasValue || l.ContractId.HasValue).ToList();
            var result = new Dictionary<int, LinkInfo>();
            if (list.Count == 0) return result;
            List<int> tenderIds = list.Where(l => l.TenderId.HasValue).Select(l => l.TenderId.Value).Distinct().ToList();
            List<int> contractIds = list.Where(l => l.ContractId.HasValue).Select(l => l.ContractId.Value).Distinct().ToList();
            Dictionary<int, string> tenders = tenderIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<Tender>().AsNoTracking().Where(t => tenderIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Code, ct);
            var contracts = new Dictionary<int, (string Code, string Title)>();
            if (contractIds.Count > 0)
            {
                var rows = await _uow.Set<ContractEntity>().AsNoTracking().Where(c => contractIds.Contains(c.Id)).Select(c => new { c.Id, c.Code, c.Title }).ToListAsync(ct);
                Dictionary<int, string> terms = await _uow.Set<PcContractTerms>().AsNoTracking().Where(t => !t.Deleted && contractIds.Contains(t.ContractId) && t.ProjectCode != null).ToDictionaryAsync(t => t.ContractId, t => t.ProjectCode, ct);
                foreach (var c in rows) contracts[c.Id] = (ProjectCodeOf(terms.TryGetValue(c.Id, out string pc) ? pc : null, c.Code), c.Title);
            }
            foreach (OfLetter l in list)
            {
                if (l.TenderId.HasValue && tenders.TryGetValue(l.TenderId.Value, out string tc)) result[l.Id] = new LinkInfo { Code = Clean(tc), Title = "مناقصه " + Clean(tc) };
                else if (l.ContractId.HasValue && contracts.TryGetValue(l.ContractId.Value, out var ct2))
                    result[l.Id] = new LinkInfo { Code = ProjectCodeOf(ct2.Code, null), Title = "پروژه " + ProjectCodeOf(ct2.Code, null) + (Clean(ct2.Title) != null ? " — " + Clean(ct2.Title) : "") };
            }
            return result;
        }

        /// <summary>The code that goes into the number (null without a link). An error when the linked tender or project has no code.</summary>
        private async Task<(string Code, string Error)> LinkCodeAsync(OfLetter l, CancellationToken ct)
        {
            if (!l.TenderId.HasValue && !l.ContractId.HasValue) return (null, null);
            Dictionary<int, LinkInfo> links = await LinksAsync(new[] { l }, ct);
            if (!links.TryGetValue(l.Id, out LinkInfo link) || link.Code == null) return (null, "کد مناقصه یا پروژه مرتبط با نامه پیدا نشد؛ پیوند نامه را اصلاح کنید.");
            return (link.Code, null);
        }

        /// <summary>Checks the optional link of a draft: at most one of the two, the record exists and has a code.</summary>
        private async Task<string> CheckLinkAsync(int? tenderId, int? contractId, CancellationToken ct)
        {
            if (tenderId.HasValue && contractId.HasValue) return "نامه فقط به یک مناقصه یا یک پروژه وصل می‌شود، نه هر دو.";
            if (tenderId.HasValue)
            {
                string code = await _uow.Set<Tender>().AsNoTracking().Where(t => t.Id == tenderId.Value && !t.Deleted).Select(t => t.Code).FirstOrDefaultAsync(ct);
                if (code == null) return "مناقصه پیدا نشد.";
                if (Clean(code) == null) return "این مناقصه کد ندارد؛ ابتدا کد مناقصه را ثبت کنید.";
            }
            if (contractId.HasValue)
            {
                var c = await _uow.Set<ContractEntity>().AsNoTracking().Where(x => x.Id == contractId.Value && !x.Deleted).Select(x => new { x.Code }).FirstOrDefaultAsync(ct);
                if (c == null) return "پروژه (قرارداد) پیدا نشد.";
                string pc = await _uow.Set<PcContractTerms>().AsNoTracking().Where(t => t.ContractId == contractId.Value && !t.Deleted).Select(t => t.ProjectCode).FirstOrDefaultAsync(ct);
                if (ProjectCodeOf(pc, c.Code) == null) return "این پروژه کد ندارد؛ ابتدا کد پروژه را صادر کنید.";
            }
            return null;
        }

        // -------------------------------------------------------------------------------- numbering

        private static string KindCode(OfLetterKind k) => k == OfLetterKind.Incoming ? "و" : k == OfLetterKind.Outgoing ? "ص" : "د";

        /// <summary>
        /// Tokens: {yyyy} Persian year (4 digits), {yy}, {unit}, {kind}, {seq} (no padding) or {seq:n} (zero padded to n digits), {code}: "code-" of the tender or project the letter is linked to, nothing without a link.
        /// Company numbers: incoming INN-و-{code}{yyyy}-{seq}, outgoing INN-{code}{yyyy}-{seq}, internal INN-د-{code}{yyyy}-{seq}, for example INN-و-1405-25 or INN-T123-1405-25.
        /// </summary>
        internal static string FormatNumber(string pattern, int year, int sequence, string unitCode, OfLetterKind kind, string code = null)
        {
            return System.Text.RegularExpressions.Regex.Replace(pattern, @"\{(yyyy|yy|unit|kind|code|seq(?::(\d{1,9}))?)\}", m =>
            {
                string token = m.Groups[1].Value;
                if (token == "yyyy") return year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
                if (token == "yy") return (year % 100).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                if (token == "unit") return unitCode ?? "CO";
                if (token == "kind") return KindCode(kind);
                if (token == "code") return string.IsNullOrWhiteSpace(code) ? "" : code.Trim() + "-";
                int width = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : 1;
                return sequence.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(width, '0');
            });
        }

        /// <summary>The company's default pattern of each kind (office answers 1405/08).</summary>
        internal static string DefaultPattern(OfLetterKind kind) =>
            kind == OfLetterKind.Incoming ? "INN-و-{code}{yyyy}-{seq}" : kind == OfLetterKind.Outgoing ? "INN-{code}{yyyy}-{seq}" : "INN-د-{code}{yyyy}-{seq}";

        /// <summary>The pattern the module was first seeded with; a general rule that still has it is moved to the company default.</summary>
        internal const string FirstDefaultPattern = "{yy}/{kind}/{unit}/{seq:5}";

        /// <summary>
        /// Seeds the default rules once, and moves a general rule (no unit) that still has the very first default pattern to the company pattern of its kind. Idempotent; a rule an admin changed,
        /// and every rule of a unit, is never touched. Counters and issued numbers stay as they are.
        /// </summary>
        private async Task EnsureRulesAsync(CancellationToken ct)
        {
            List<OfNumberingRule> existing = await _uow.Set<OfNumberingRule>().Where(r => !r.Deleted).ToListAsync(ct);
            DateTimeOffset now = DateTimeOffset.Now;
            if (existing.Count > 0)
            {
                List<OfNumberingRule> old = existing.Where(r => r.UnitId == null && r.Pattern == FirstDefaultPattern).ToList();
                if (old.Count == 0) return;
                foreach (OfNumberingRule r in old) { r.Pattern = DefaultPattern(r.Kind); r.ModifyDate = now; r.ModifierUserId = Me; }
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateException)
                {
                    // another request moved them first
                    foreach (OfNumberingRule r in old) await _uow.Entry(r).ReloadAsync(ct);
                }
                return;
            }
            foreach (OfLetterKind k in new[] { OfLetterKind.Incoming, OfLetterKind.Outgoing, OfLetterKind.Internal })
                _uow.Set<OfNumberingRule>().Add(new OfNumberingRule { Kind = k, UnitId = null, Pattern = DefaultPattern(k), IsActive = true, CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me });
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                foreach (OfNumberingRule r in _uow.Set<OfNumberingRule>().Local.Where(x => x.Id == 0).ToList()) _uow.Entry(r).State = EntityState.Detached;
            }
        }

        /// <summary>Takes the next number of the rule that fits the letter: the rule of the unit if there is one, else the general rule of the kind.</summary>
        private async Task<(string Number, int RuleId, string Error)> NextNumberAsync(OfLetterKind kind, int? unitId, string unitCode, DateTime date, string linkCode, CancellationToken ct)
        {
            await EnsureRulesAsync(ct);
            List<OfNumberingRule> rules = await _uow.Set<OfNumberingRule>().Where(r => !r.Deleted && r.IsActive && r.Kind == kind).ToListAsync(ct);
            OfNumberingRule rule = rules.FirstOrDefault(r => unitId.HasValue && r.UnitId == unitId) ?? rules.FirstOrDefault(r => r.UnitId == null);
            if (rule == null) return (null, 0, "قاعده شماره‌گذاری فعالی برای این نوع نامه تعریف نشده است.");
            int year = InventoryText.PersianYear(date);
            OfNumberCounter counter = _uow.Set<OfNumberCounter>().Local.FirstOrDefault(x => x.RuleId == rule.Id && x.Year == year)
                ?? await _uow.Set<OfNumberCounter>().FirstOrDefaultAsync(x => x.RuleId == rule.Id && x.Year == year, ct);
            if (counter == null)
            {
                counter = new OfNumberCounter { RuleId = rule.Id, Year = year, Last = 0 };
                _uow.Set<OfNumberCounter>().Add(counter);
            }
            counter.Last++;
            // numbers already issued by another rule of the same kind (an older general rule, a changed pattern) are skipped, so one collision never blocks a unit for good
            for (int guard = 0; guard < 1000; guard++)
            {
                string number = FormatNumber(rule.Pattern, year, counter.Last, unitCode, kind, linkCode);
                bool taken = await _uow.Set<OfLetter>().AnyAsync(x => x.Kind == kind && x.Number == number && !x.Deleted && !x.Imported, ct);
                if (!taken) return (number, rule.Id, null);
                counter.Last++;
            }
            return (null, 0, "شماره آزاد برای این قاعده پیدا نشد؛ الگوی شماره‌گذاری را بررسی کنید.");
        }

        /// <summary>
        /// Saves a letter that just took a number. When the save fails because another request took the same counter (or the same number), the counter is re-read, the number is taken again
        /// and the save is retried (at most three tries). A conflict on the letter itself is never retried.
        /// </summary>
        private async Task<string> SaveRenumberingAsync(Func<Task<string>> renumber, CancellationToken ct)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { await _uow.SaveChangesAsync(ct); return null; }
                catch (DbUpdateConcurrencyException ex)
                {
                    if (attempt >= 2 || ex.Entries.Any(e => e.Entity is OfLetter)) return OfficeText.Concurrency;
                }
                catch (DbUpdateException)
                {
                    if (attempt >= 2) return "ذخیره انجام نشد (احتمالاً هم‌زمان شماره گرفته شد)؛ دوباره تلاش کنید.";
                }
                foreach (OfNumberCounter counter in _uow.Set<OfNumberCounter>().Local.ToList())
                {
                    var entry = _uow.Entry(counter);
                    if (entry.State == EntityState.Added) entry.State = EntityState.Detached;
                    else await entry.ReloadAsync(ct);
                }
                string error = await renumber();
                if (error != null) return error;
            }
        }

        private async Task<(int? UnitId, string Code)> UnitOfAsync(int? positionId, CancellationToken ct)
        {
            if (positionId.HasValue)
            {
                var u = await (from p in _uow.Set<OfPosition>().AsNoTracking() join un in _uow.Set<OfUnit>().AsNoTracking() on p.UnitId equals un.Id where p.Id == positionId.Value select new { un.Id, un.Code }).FirstOrDefaultAsync(ct);
                if (u != null) return (u.Id, u.Code);
            }
            OfUnit root = await OfficeActors.EnsureRootUnitAsync(_uow, ct);
            return (root?.Id, root?.Code ?? "CO");
        }

        // -------------------------------------------------------------------------------- content hash

        private const string MissingFile = "MISSING";

        /// <summary>SHA-256 of the bytes of every attachment and of the signature image as they are stored now.</summary>
        private async Task<Dictionary<Guid, string>> CurrentFileHashesAsync(OfLetter l, CancellationToken ct)
        {
            var ids = l.Attachments.Select(a => a.DocumentId).ToList();
            if (l.SignatureDocumentId.HasValue) ids.Add(l.SignatureDocumentId.Value);
            var result = new Dictionary<Guid, string>();
            foreach (Guid id in ids.Distinct()) result[id] = await _documents.GetFileHashAsync(id) ?? MissingFile;
            return result;
        }

        /// <summary>The hash of the letter content, including the SHA-256 of every attached file and of the signature image (so a swapped file is detected).</summary>
        internal static string ComputeHash(OfLetter l, IDictionary<Guid, string> fileHashes)
        {
            string FileHash(Guid id) => fileHashes != null && fileHashes.TryGetValue(id, out string h) ? h : MissingFile;
            var sb = new StringBuilder();
            sb.Append(l.Number).Append('|').Append(l.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).Append('|').Append((int)l.Kind).Append('|').Append((int)l.Classification)
              .Append('|').Append((int)l.Urgency).Append('|').Append(l.Subject).Append('|').Append(l.BodyHtml).Append('|').Append(l.SenderPositionId).Append('|').Append(l.SenderPartyId).Append('|').Append(l.SignerPositionId).Append('|');
            foreach (OfLetterRecipient r in l.Recipients.OrderBy(x => x.Type).ThenBy(x => x.PositionId).ThenBy(x => x.PartyId)) sb.Append((int)r.Type).Append(':').Append(r.PositionId).Append(':').Append(r.PartyId).Append(';');
            sb.Append('|');
            foreach (OfLetterAttachment a in l.Attachments.OrderBy(x => x.DocumentId)) sb.Append(a.DocumentId).Append(':').Append(FileHash(a.DocumentId)).Append(';');
            sb.Append('|');
            if (l.SignatureDocumentId.HasValue) sb.Append(l.SignatureDocumentId.Value).Append(':').Append(FileHash(l.SignatureDocumentId.Value));
            sb.Append('|');
            foreach (OfRecordLink k in l.Links.OrderBy(x => x.Kind).ThenBy(x => x.RecordId)) sb.Append((int)k.Kind).Append(':').Append(k.RecordId).Append(':').Append(k.Text).Append(';');
            using (SHA256 sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        public async Task<ResponseDto<LetterVerifyDto>> VerifyAsync(int id, CancellationToken ct)
        {
            Ctx c = await ContextAsync(ct);
            OfLetter l = await LoadAsync(id, false, ct);
            if (l == null || AccessOf(l, c) != Access.Full) return OfficeText.NotFound<LetterVerifyDto>("نامه پیدا نشد.");
            // recomputed from the files as they are stored now, not from the hashes kept at signing
            Dictionary<Guid, string> current = await CurrentFileHashesAsync(l, ct);
            string computed = ComputeHash(l, current);
            var changed = new List<Guid>();
            foreach (OfLetterAttachment a in l.Attachments)
                if (a.FileHash != null && current.TryGetValue(a.DocumentId, out string h) && h != a.FileHash) changed.Add(a.DocumentId);
            if (l.SignatureDocumentId.HasValue && l.SignatureFileHash != null && current.TryGetValue(l.SignatureDocumentId.Value, out string sh) && sh != l.SignatureFileHash) changed.Add(l.SignatureDocumentId.Value);
            return ResponseDtoHelper.Ok(new LetterVerifyDto
            {
                LetterId = l.Id, Number = l.Number, Signed = l.ContentHash != null, StoredHash = l.ContentHash, ComputedHash = computed, Valid = l.ContentHash != null && l.ContentHash == computed && changed.Count == 0,
                ChangedDocumentIds = changed,
            });
        }

        public async Task<bool> CanReadFileAsync(int letterId, Guid documentId, CancellationToken ct)
        {
            Ctx c = await ContextAsync(ct);
            OfLetter l = await LoadAsync(letterId, false, ct);
            if (l == null || AccessOf(l, c) != Access.Full) return false;
            return l.Attachments.Any(a => a.DocumentId == documentId) || l.SignatureDocumentId == documentId || l.DeliveryReceiptDocumentId == documentId;
        }

        // -------------------------------------------------------------------------------- saving

        private async Task<bool> DocumentsExistAsync(IEnumerable<Guid> ids, CancellationToken ct)
        {
            List<Guid> list = ids.Distinct().ToList();
            if (list.Count == 0) return true;
            int found = await _uow.Set<Entities.Model.Document>().CountAsync(d => list.Contains(d.Id), ct);
            return found == list.Count;
        }

        public async Task<ResponseDto<LetterDto>> SaveLetterAsync(SaveLetterDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Subject) == null) return Bad<LetterDto>("موضوع نامه را بنویسید.");
            if (!Enum.IsDefined(typeof(OfLetterKind), dto.Kind) || !Enum.IsDefined(typeof(OfClassification), dto.Classification) || !Enum.IsDefined(typeof(OfUrgency), dto.Urgency)) return Bad<LetterDto>(OfficeText.InvalidData);
            Ctx c = await ContextAsync(ct);
            if (dto.Kind == OfLetterKind.Incoming && !c.IsSec) return Bad<LetterDto>("ثبت نامه وارده فقط توسط دبیرخانه انجام می‌شود.");
            await OfficeActors.EnsureRootUnitAsync(_uow, ct);

            OfLetter l = null;
            if (dto.Id.HasValue)
            {
                l = await LoadAsync(dto.Id.Value, true, ct);
                if (l == null || l.CreatorUserId != Me) return OfficeText.NotFound<LetterDto>("نامه پیدا نشد.");
                if (l.Status != OfLetterStatus.Draft) return Bad<LetterDto>("نامه‌ای که برای تایید یا امضا فرستاده شده، امضا یا ثبت شده قابل ویرایش نیست.");
                if (l.Kind != dto.Kind) return Bad<LetterDto>("نوع نامه پس از ثبت تغییر نمی‌کند.");
                if (Clean(dto.RowVersion) != null)
                {
                    try { _uow.Entry(l).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); }
                    catch (FormatException) { return Bad<LetterDto>(OfficeText.InvalidData); }
                }
            }

            // sender
            int? senderPosition = dto.SenderPositionId;
            if (dto.Kind != OfLetterKind.Incoming)
            {
                if (!senderPosition.HasValue && c.Acting.Count == 1) senderPosition = c.Acting[0].PositionId;
                if (!senderPosition.HasValue) return Bad<LetterDto>("سمت فرستنده را مشخص کنید (نامه از طرف یک سمت سازمانی نوشته می‌شود).");
                if (!c.Pos.Contains(senderPosition.Value)) return Bad<LetterDto>("شما در این سمت مجاز به نوشتن نامه نیستید.");
            }
            else
            {
                if (!dto.SenderPartyId.HasValue || !await _uow.Set<OfExternalParty>().AnyAsync(p => p.Id == dto.SenderPartyId.Value && !p.Deleted, ct)) return Bad<LetterDto>("سازمان فرستنده نامه وارده را انتخاب کنید.");
                if (Clean(dto.IncomingNumber) == null) return Bad<LetterDto>("شماره نامه فرستنده را وارد کنید.");
            }

            // recipients
            List<RecipientDto> recipients = (dto.Recipients ?? new List<RecipientDto>()).Where(r => r.PositionId.HasValue || r.PartyId.HasValue).ToList();
            if (!recipients.Any(r => r.Type == OfRecipientType.To)) return Bad<LetterDto>("حداقل یک گیرنده (به) لازم است.");
            if (recipients.Any(r => r.PositionId.HasValue && r.PartyId.HasValue)) return Bad<LetterDto>("هر گیرنده یا سمت داخلی است یا سازمان بیرونی.");
            if (dto.Kind == OfLetterKind.Internal && recipients.Any(r => r.PartyId.HasValue)) return Bad<LetterDto>("نامه داخلی فقط به سمت‌های داخلی نوشته می‌شود.");
            if (dto.Kind == OfLetterKind.Incoming && recipients.Any(r => r.PartyId.HasValue)) return Bad<LetterDto>("گیرنده نامه وارده باید سمت داخلی باشد.");
            if (recipients.Any(r => !Enum.IsDefined(typeof(OfRecipientType), r.Type))) return Bad<LetterDto>(OfficeText.InvalidData);
            List<int> posIds = recipients.Where(r => r.PositionId.HasValue).Select(r => r.PositionId.Value).Distinct().ToList();
            if (posIds.Count > 0 && await _uow.Set<OfPosition>().CountAsync(p => posIds.Contains(p.Id) && !p.Deleted && p.IsActive, ct) != posIds.Count) return Bad<LetterDto>("یکی از سمت‌های گیرنده پیدا نشد یا غیرفعال است.");
            List<int> partyIds = recipients.Where(r => r.PartyId.HasValue).Select(r => r.PartyId.Value).Distinct().ToList();
            if (partyIds.Count > 0 && await _uow.Set<OfExternalParty>().CountAsync(p => partyIds.Contains(p.Id) && !p.Deleted, ct) != partyIds.Count) return Bad<LetterDto>("یکی از سازمان‌های گیرنده پیدا نشد.");

            // signer, template, reply
            int? signer = dto.SignerPositionId;
            if (signer.HasValue && !await _uow.Set<OfPosition>().AnyAsync(p => p.Id == signer.Value && !p.Deleted && p.IsActive && p.CanSign, ct)) return Bad<LetterDto>("سمت امضاکننده مجاز به امضا نیست.");
            OfLetterTemplate template = null;
            if (dto.TemplateId.HasValue)
            {
                template = await _uow.Set<OfLetterTemplate>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.TemplateId.Value && !t.Deleted && t.IsActive, ct);
                if (template == null) return Bad<LetterDto>("قالب نامه پیدا نشد.");
                if (!signer.HasValue) signer = template.SignerPositionId;
            }
            if (dto.ReplyToLetterId.HasValue)
            {
                OfLetter original = await LoadAsync(dto.ReplyToLetterId.Value, false, ct);
                if (original == null || AccessOf(original, c) != Access.Full) return Bad<LetterDto>("نامه‌ای که به آن پاسخ می‌دهید پیدا نشد.");
            }
            List<LetterAttachmentDto> attachments = dto.Attachments ?? new List<LetterAttachmentDto>();
            if (!await DocumentsExistAsync(attachments.Select(a => a.DocumentId), ct)) return Bad<LetterDto>("یکی از فایل‌های پیوست پیدا نشد؛ ابتدا فایل را بارگذاری کنید.");
            List<RecordLinkDto> links = (dto.Links ?? new List<RecordLinkDto>()).Where(k => k.RecordId.HasValue || Clean(k.Text) != null).ToList();
            if (links.Any(k => !Enum.IsDefined(typeof(OfRelatedKind), k.Kind))) return Bad<LetterDto>(OfficeText.InvalidData);

            // the optional tender / project link: part of the number, so only a draft takes it (a numbered letter is never edited)
            string linkError = await CheckLinkAsync(dto.TenderId, dto.ContractId, ct);
            if (linkError != null) return Bad<LetterDto>(linkError);
            if (dto.Kind == OfLetterKind.Incoming && dto.Register) await EnsureRulesAsync(ct);

            string body = OfficeHtml.Sanitize(dto.BodyHtml);
            if (body == null && template != null) body = OfficeHtml.Sanitize(template.BodyHtml);
            DateTimeOffset now = DateTimeOffset.Now;
            if (l == null)
            {
                l = new OfLetter { Kind = dto.Kind, Status = OfLetterStatus.Draft, CreateDate = now, CreatorUserId = Me, Recipients = new List<OfLetterRecipient>(), Approvals = new List<OfLetterApproval>(), Attachments = new List<OfLetterAttachment>(), Links = new List<OfRecordLink>(), Referrals = new List<OfReferral>() };
                _uow.Set<OfLetter>().Add(l);
            }
            else
            {
                _uow.RemoveRange(l.Recipients.ToList()); _uow.RemoveRange(l.Attachments.ToList()); _uow.RemoveRange(l.Links.ToList());
                l.Recipients = new List<OfLetterRecipient>(); l.Attachments = new List<OfLetterAttachment>(); l.Links = new List<OfRecordLink>();
            }
            l.Date = (dto.Date ?? DateTime.Today).Date; l.Subject = Clean(dto.Subject); l.BodyHtml = body; l.SenderPositionId = senderPosition; l.SenderPartyId = dto.Kind == OfLetterKind.Incoming ? dto.SenderPartyId : null;
            l.Classification = dto.Classification; l.Urgency = dto.Urgency; l.IncomingNumber = dto.Kind == OfLetterKind.Incoming ? Clean(dto.IncomingNumber) : null;
            l.IncomingDate = dto.Kind == OfLetterKind.Incoming ? dto.IncomingDate?.Date : null; l.SignerPositionId = signer; l.PaperCopy = dto.PaperCopy;
            l.ReplyToLetterId = dto.ReplyToLetterId; l.TemplateId = dto.TemplateId; l.TenderId = dto.TenderId; l.ContractId = dto.ContractId; l.ModifyDate = now; l.ModifierUserId = Me;
            foreach (RecipientDto r in recipients) l.Recipients.Add(new OfLetterRecipient { Type = r.Type, PositionId = r.PositionId, PartyId = r.PartyId });
            foreach (LetterAttachmentDto a in attachments) l.Attachments.Add(new OfLetterAttachment { DocumentId = a.DocumentId, FileName = Clean(a.FileName) });
            foreach (RecordLinkDto k in links) l.Links.Add(new OfRecordLink { Kind = k.Kind, RecordId = k.RecordId, Text = Clean(k.Text) });

            bool registeredNow = false;
            if (dto.Kind == OfLetterKind.Incoming && dto.Register)
            {
                string error = await RegisterIncomingAsync(l, ct);
                if (error != null) { DetachNew(l); return Bad<LetterDto>(error); }
                registeredNow = true;
            }
            if (registeredNow)
            {
                string saveError = await SaveRenumberingAsync(() => RegisterIncomingAsync(l, ct), ct);
                if (saveError != null) return Bad<LetterDto>(saveError);
            }
            else
            {
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException) { return Bad<LetterDto>(OfficeText.Concurrency); }
                catch (DbUpdateException) { return Bad<LetterDto>("ثبت نامه انجام نشد (احتمالاً هم‌زمان شماره گرفته شد)؛ دوباره تلاش کنید."); }
            }
            if (registeredNow)
            {
                DeliverToPositions(l);
                await _uow.SaveChangesAsync(ct);
            }
            if (l.ReplyToLetterId.HasValue)
            {
                OfLetter original = await _uow.Set<OfLetter>().FirstOrDefaultAsync(x => x.Id == l.ReplyToLetterId.Value, ct);
                if (original != null && !original.FollowUpLetterId.HasValue) { original.FollowUpLetterId = l.Id; await _uow.SaveChangesAsync(ct); }
            }
            return ResponseDtoHelper.Ok(await BuildAsync(await LoadAsync(l.Id, false, ct), c, ct));
        }

        private void DetachNew(OfLetter l)
        {
            if (l.Id == 0) _uow.Entry(l).State = EntityState.Detached;
        }

        /// <summary>Numbers an incoming letter and delivers it to the inboxes of the recipient positions.</summary>
        private async Task<string> RegisterIncomingAsync(OfLetter l, CancellationToken ct)
        {
            int? firstTo = l.Recipients.Where(r => r.Type == OfRecipientType.To && r.PositionId.HasValue).Select(r => r.PositionId).FirstOrDefault();
            var unit = await UnitOfAsync(firstTo, ct);
            var link = await LinkCodeAsync(l, ct);
            if (link.Error != null) return link.Error;
            l.Date = DateTime.Today;
            var number = await NextNumberAsync(OfLetterKind.Incoming, unit.UnitId, unit.Code, l.Date, link.Code, ct);
            if (number.Error != null) return number.Error;
            l.Number = number.Number; l.NumberRuleId = number.RuleId; l.Status = OfLetterStatus.Received; l.RegisteredAt = DateTimeOffset.Now; l.RegisteredByUserId = Me;
            return null;
        }
    }
}
