using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.OfficeDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Office
{
    public interface IMeetingService
    {
        Task<ResponseDto<PagedList<MeetingListDto>>> GetMeetingsAsync(MeetingQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<MeetingDto>> GetMeetingAsync(int id, CancellationToken cancellationToken);

        /// <summary>The ids of the meetings the caller may see (the same rule as <see cref="GetMeetingAsync"/>, as one query).</summary>
        Task<IQueryable<int>> VisibleMeetingIdsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<MeetingDto>> SaveMeetingAsync(SaveMeetingDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<MeetingDto>> MeetingActionAsync(MeetingActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<DecisionDto>>> GetDecisionsAsync(DecisionQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<DecisionDto>> DecisionActionAsync(DecisionActionDto dto, CancellationToken cancellationToken);

        /// <summary>Alerts the chair of every open decision past its due date (once per decision). Run daily and on demand.</summary>
        Task<ResponseDto<OverdueCheckResultDto>> CheckOverdueAsync(CancellationToken cancellationToken);
    }

    /// <summary>Meetings and minutes (صورتجلسات): agenda, attendees, minutes approved by the chair and decisions that land in the owners' inboxes.</summary>
    public class MeetingService : IMeetingService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public MeetingService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => OfficeText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => OfficeText.Bad<T>(m);

        private sealed class Ctx
        {
            public List<ActingPosition> Acting;
            public List<int> Pos;
            public bool IsCeo;
        }

        private async Task<Ctx> ContextAsync(CancellationToken ct)
        {
            List<ActingPosition> acting = await OfficeActors.ActingAsync(_uow, Me, ct);
            return new Ctx { Acting = acting, Pos = acting.Select(a => a.PositionId).ToList(), IsCeo = _user.IsInRole(OfficeRoles.Ceo) };
        }

        private bool CanSee(OfMeeting m, Ctx c)
        {
            if (m.CreatorUserId == Me || c.IsCeo) return true;
            if (m.ChairPositionId.HasValue && c.Pos.Contains(m.ChairPositionId.Value)) return true;
            if (m.SecretaryPositionId.HasValue && c.Pos.Contains(m.SecretaryPositionId.Value)) return true;
            if ((m.Attendees ?? new List<OfMeetingAttendee>()).Any(a => a.UserId == Me || (a.PositionId.HasValue && c.Pos.Contains(a.PositionId.Value)))) return true;
            return (m.Decisions ?? new List<OfDecision>()).Any(d => c.Pos.Contains(d.OwnerPositionId));
        }

        private bool CanEdit(OfMeeting m, Ctx c) =>
            m.CreatorUserId == Me || (m.ChairPositionId.HasValue && c.Pos.Contains(m.ChairPositionId.Value)) || (m.SecretaryPositionId.HasValue && c.Pos.Contains(m.SecretaryPositionId.Value));

        private IQueryable<OfMeeting> Visible(IQueryable<OfMeeting> q, Ctx c)
        {
            int me = Me;
            List<int> pos = c.Pos;
            bool ceo = c.IsCeo;
            return q.Where(m => m.CreatorUserId == me || ceo
                || (m.ChairPositionId != null && pos.Contains(m.ChairPositionId.Value))
                || (m.SecretaryPositionId != null && pos.Contains(m.SecretaryPositionId.Value))
                || m.Attendees.Any(a => a.UserId == me || (a.PositionId != null && pos.Contains(a.PositionId.Value)))
                || m.Decisions.Any(d => pos.Contains(d.OwnerPositionId)));
        }

        public async Task<IQueryable<int>> VisibleMeetingIdsAsync(CancellationToken ct)
        {
            Ctx c = await ContextAsync(ct);
            return Visible(_uow.Set<OfMeeting>().AsNoTracking().Where(m => !m.Deleted), c).Select(m => m.Id);
        }

        private async Task<OfMeeting> LoadAsync(int id, bool tracking, CancellationToken ct)
        {
            IQueryable<OfMeeting> q = _uow.Set<OfMeeting>().Where(m => m.Id == id && !m.Deleted)
                .Include(m => m.Agenda).Include(m => m.Attendees).Include(m => m.Decisions).Include(m => m.Links).AsSplitQuery();
            if (!tracking) q = q.AsNoTracking();
            return await q.FirstOrDefaultAsync(ct);
        }

        // -------------------------------------------------------------------------------- reading

        private DecisionDto ToDto(OfDecision d, OfMeeting m, Dictionary<int, string> positions, Dictionary<int, string> users, Dictionary<int, int> agendaSeq, Ctx c)
        {
            DateTime today = DateTime.Today;
            // what the caller may do now (the same checks as DecisionActionAsync): done by the owner position or its deputy; cancel by the chair, the creator or the CEO
            var actions = new List<string>();
            if (d.Status == OfDecisionStatus.Open && m != null && m.Status == OfMeetingStatus.MinutesApproved)
            {
                if (c.Pos.Contains(d.OwnerPositionId)) actions.Add("done");
                if (c.IsCeo || m.CreatorUserId == Me || (m.ChairPositionId.HasValue && c.Pos.Contains(m.ChairPositionId.Value))) actions.Add("cancel");
            }
            return new DecisionDto
            {
                AllowedActions = actions,
                Id = d.Id, MeetingId = d.MeetingId, MeetingNumber = m?.Number, MeetingTitle = m?.Title, AgendaId = d.AgendaId, AgendaSeq = d.AgendaId.HasValue && agendaSeq.TryGetValue(d.AgendaId.Value, out int s) ? s : (int?)null,
                Seq = d.Seq, Text = d.Text, OwnerPositionId = d.OwnerPositionId, OwnerPositionTitle = positions.Title(d.OwnerPositionId), DueDate = d.DueDate, DueDateText = OfficeText.Date(d.DueDate),
                Status = d.Status, Overdue = d.Status == OfDecisionStatus.Open && d.DueDate.HasValue && d.DueDate.Value.Date < today, DoneNote = d.DoneNote, DoneAt = d.DoneAt,
                DoneByName = users.Name(d.DoneByUserId), EvidenceDocumentId = d.EvidenceDocumentId,
            };
        }

        private List<string> AllowedActions(OfMeeting m, Ctx c)
        {
            var a = new List<string>();
            bool edit = CanEdit(m, c);
            bool chair = m.ChairPositionId.HasValue && c.Pos.Contains(m.ChairPositionId.Value);
            switch (m.Status)
            {
                case OfMeetingStatus.Planned: if (edit) { a.Add("save"); a.Add("hold"); a.Add("cancel"); } break;
                case OfMeetingStatus.Held: if (edit) { a.Add("save"); a.Add("submit-minutes"); a.Add("cancel"); } break;
                case OfMeetingStatus.MinutesDraft:
                    if (edit) { a.Add("save"); a.Add("cancel"); }
                    if (chair) a.Add("approve-minutes");
                    break;
                case OfMeetingStatus.MinutesApproved:
                    if (m.Attendees.Any(x => x.Present && x.SignedAt == null && (x.UserId == Me || (x.PositionId.HasValue && c.Pos.Contains(x.PositionId.Value))))) a.Add("sign-minutes");
                    break;
            }
            return a;
        }

        private async Task<MeetingDto> BuildAsync(OfMeeting m, Ctx c, CancellationToken ct)
        {
            var positionIds = new List<int?> { m.ChairPositionId, m.SecretaryPositionId };
            positionIds.AddRange(m.Attendees.Select(a => a.PositionId));
            positionIds.AddRange(m.Decisions.Select(d => (int?)d.OwnerPositionId));
            Dictionary<int, string> positions = await OfficeActors.PositionTitlesAsync(_uow, positionIds, ct);
            var userIds = new List<int?> { m.MinutesApprovedByUserId, m.CreatorUserId };
            userIds.AddRange(m.Attendees.Select(a => a.UserId));
            userIds.AddRange(m.Decisions.Select(d => d.DoneByUserId));
            Dictionary<int, string> users = await InventoryUsers.GetNamesAsync(_uow, userIds, ct);
            Dictionary<int, int> agendaSeq = m.Agenda.ToDictionary(a => a.Id, a => a.Seq);
            DateTime today = DateTime.Today;
            var res = new MeetingDto
            {
                Id = m.Id, Number = m.Number, Title = m.Title, Kind = m.Kind, StartAt = m.StartAt, StartAtText = OfficeText.Stamp(m.StartAt), Place = m.Place, Status = m.Status,
                ChairPositionTitle = positions.Title(m.ChairPositionId), OpenDecisions = m.Decisions.Count(x => x.Status == OfDecisionStatus.Open),
                OverdueDecisions = m.Decisions.Count(x => x.Status == OfDecisionStatus.Open && x.DueDate.HasValue && x.DueDate.Value.Date < today),
                EndAt = m.EndAt, ChairPositionId = m.ChairPositionId, SecretaryPositionId = m.SecretaryPositionId, SecretaryPositionTitle = positions.Title(m.SecretaryPositionId),
                MinutesApprovedAt = m.MinutesApprovedAt, MinutesApprovedAtText = OfficeText.Stamp(m.MinutesApprovedAt), MinutesApprovedByName = users.Name(m.MinutesApprovedByUserId),
                MinutesApprovedOnBehalf = m.MinutesApprovedOnBehalf, CancelReason = m.CancelReason, CreatedByName = users.Name(m.CreatorUserId),
                AllowedActions = AllowedActions(m, c), RowVersion = m.RowVersion == null ? null : Convert.ToBase64String(m.RowVersion),
            };
            res.Agenda = m.Agenda.OrderBy(a => a.Seq).Select(a => new MeetingAgendaDto { Id = a.Id, Seq = a.Seq, Title = a.Title, Minutes = a.Minutes }).ToList();
            res.Attendees = m.Attendees.OrderBy(a => a.Id).Select(a => new MeetingAttendeeDto
            {
                Id = a.Id, PositionId = a.PositionId, PositionTitle = positions.Title(a.PositionId), UserId = a.UserId, UserName = users.Name(a.UserId), ExternalName = a.ExternalName,
                Present = a.Present, SignedAt = a.SignedAt, SignedAtText = OfficeText.Stamp(a.SignedAt),
            }).ToList();
            res.Decisions = m.Decisions.OrderBy(x => x.Seq).Select(x => ToDto(x, m, positions, users, agendaSeq, c)).ToList();
            res.Links = m.Links.Select(k => new RecordLinkDto { Kind = k.Kind, RecordId = k.RecordId, Text = k.Text }).ToList();
            return res;
        }

        public async Task<ResponseDto<PagedList<MeetingListDto>>> GetMeetingsAsync(MeetingQuery query, CancellationToken ct)
        {
            query ??= new MeetingQuery();
            Ctx c = await ContextAsync(ct);
            int me = Me;
            IQueryable<OfMeeting> q = Visible(_uow.Set<OfMeeting>().AsNoTracking().Where(m => !m.Deleted), c);
            if (query.Status.HasValue) q = q.Where(m => m.Status == query.Status.Value);
            if (query.Kind.HasValue) q = q.Where(m => m.Kind == query.Kind.Value);
            if (query.DateFrom.HasValue) { DateTime from = query.DateFrom.Value.Date; q = q.Where(m => m.StartAt >= from); }
            if (query.DateTo.HasValue) { DateTime to = query.DateTo.Value.Date.AddDays(1); q = q.Where(m => m.StartAt < to); }
            if (query.MineOnly == true) q = q.Where(m => m.CreatorUserId == me);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(m => m.Title.Contains(text) || m.Number.Contains(text));
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<OfMeeting> rows = await q.Include(m => m.Decisions).OrderByDescending(m => m.StartAt).Skip((number - 1) * size).Take(size).AsSplitQuery().ToListAsync(ct);
            Dictionary<int, string> positions = await OfficeActors.PositionTitlesAsync(_uow, rows.Select(m => m.ChairPositionId), ct);
            DateTime today = DateTime.Today;
            List<MeetingListDto> items = rows.Select(m => new MeetingListDto
            {
                Id = m.Id, Number = m.Number, Title = m.Title, Kind = m.Kind, StartAt = m.StartAt, StartAtText = OfficeText.Stamp(m.StartAt), Place = m.Place, Status = m.Status,
                ChairPositionTitle = positions.Title(m.ChairPositionId), OpenDecisions = m.Decisions.Count(x => x.Status == OfDecisionStatus.Open),
                OverdueDecisions = m.Decisions.Count(x => x.Status == OfDecisionStatus.Open && x.DueDate.HasValue && x.DueDate.Value.Date < today),
            }).ToList();
            return ResponseDtoHelper.Ok(F2Page.Make(items, total, number, size));
        }

        public async Task<ResponseDto<MeetingDto>> GetMeetingAsync(int id, CancellationToken ct)
        {
            Ctx c = await ContextAsync(ct);
            OfMeeting m = await LoadAsync(id, false, ct);
            if (m == null || !CanSee(m, c)) return OfficeText.NotFound<MeetingDto>("جلسه پیدا نشد.");
            return ResponseDtoHelper.Ok(await BuildAsync(m, c, ct));
        }

        // -------------------------------------------------------------------------------- saving

        public async Task<ResponseDto<MeetingDto>> SaveMeetingAsync(SaveMeetingDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Title) == null) return Bad<MeetingDto>("عنوان جلسه را وارد کنید.");
            if (dto.StartAt == default(DateTime)) return Bad<MeetingDto>("زمان جلسه را وارد کنید.");
            if (!Enum.IsDefined(typeof(OfMeetingKind), dto.Kind)) return Bad<MeetingDto>(OfficeText.InvalidData);
            if (dto.EndAt.HasValue && dto.EndAt.Value < dto.StartAt) return Bad<MeetingDto>("پایان جلسه از شروع آن زودتر است.");
            if (!dto.ChairPositionId.HasValue) return Bad<MeetingDto>("رئیس جلسه (سمت) را مشخص کنید.");
            Ctx c = await ContextAsync(ct);
            var positionIds = new List<int> { dto.ChairPositionId.Value };
            if (dto.SecretaryPositionId.HasValue) positionIds.Add(dto.SecretaryPositionId.Value);
            positionIds.AddRange((dto.Attendees ?? new List<MeetingAttendeeDto>()).Where(a => a.PositionId.HasValue).Select(a => a.PositionId.Value));
            positionIds.AddRange((dto.Decisions ?? new List<DecisionDto>()).Select(d => d.OwnerPositionId));
            positionIds = positionIds.Distinct().ToList();
            if (await _uow.Set<OfPosition>().CountAsync(p => positionIds.Contains(p.Id) && !p.Deleted && p.IsActive, ct) != positionIds.Count) return Bad<MeetingDto>("یکی از سمت‌های انتخاب‌شده (رئیس، دبیر، حاضران یا مسئول مصوبه) پیدا نشد.");
            List<MeetingAttendeeDto> attendees = (dto.Attendees ?? new List<MeetingAttendeeDto>()).Where(a => a.PositionId.HasValue || a.UserId.HasValue || Clean(a.ExternalName) != null).ToList();
            List<int> userIds = attendees.Where(a => a.UserId.HasValue).Select(a => a.UserId.Value).Distinct().ToList();
            if (userIds.Count > 0 && await _uow.Set<User>().CountAsync(u => userIds.Contains(u.Id), ct) != userIds.Count) return Bad<MeetingDto>("یکی از کاربران حاضر پیدا نشد.");
            List<MeetingAgendaDto> agenda = (dto.Agenda ?? new List<MeetingAgendaDto>()).Where(a => Clean(a.Title) != null).ToList();
            List<DecisionDto> decisions = dto.Decisions ?? new List<DecisionDto>();
            foreach (DecisionDto d in decisions)
            {
                if (Clean(d.Text) == null) return Bad<MeetingDto>("متن هر مصوبه را بنویسید.");
                if (d.AgendaSeq.HasValue && (d.AgendaSeq.Value < 1 || d.AgendaSeq.Value > agenda.Count)) return Bad<MeetingDto>("بند دستور جلسه یک مصوبه معتبر نیست.");
            }
            List<RecordLinkDto> links = (dto.Links ?? new List<RecordLinkDto>()).Where(k => k.RecordId.HasValue || Clean(k.Text) != null).ToList();
            if (links.Any(k => !Enum.IsDefined(typeof(OfRelatedKind), k.Kind))) return Bad<MeetingDto>(OfficeText.InvalidData);

            DateTimeOffset now = DateTimeOffset.Now;
            OfMeeting m;
            if (dto.Id.HasValue)
            {
                m = await LoadAsync(dto.Id.Value, true, ct);
                if (m == null || !CanSee(m, c)) return OfficeText.NotFound<MeetingDto>("جلسه پیدا نشد.");
                if (!CanEdit(m, c)) return Bad<MeetingDto>(OfficeText.NotAllowed);
                if (m.Status == OfMeetingStatus.MinutesApproved || m.Status == OfMeetingStatus.Cancelled) return Bad<MeetingDto>("صورتجلسه تایید‌شده یا جلسه لغوشده قابل ویرایش نیست.");
                if (Clean(dto.RowVersion) != null)
                {
                    try { _uow.Entry(m).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); }
                    catch (FormatException) { return Bad<MeetingDto>(OfficeText.InvalidData); }
                }
            }
            else
            {
                string number = await InventoryNumbers.NextNumberAsync(_uow, OfficeNumbers.Meeting, "MT", dto.StartAt.Date, ct);
                m = new OfMeeting
                {
                    Number = number, Status = OfMeetingStatus.Planned, CreateDate = now, CreatorUserId = Me,
                    Agenda = new List<OfMeetingAgenda>(), Attendees = new List<OfMeetingAttendee>(), Decisions = new List<OfDecision>(), Links = new List<OfRecordLink>(),
                };
                _uow.Set<OfMeeting>().Add(m);
            }
            m.Title = Clean(dto.Title); m.Kind = dto.Kind; m.StartAt = dto.StartAt; m.EndAt = dto.EndAt; m.Place = Clean(dto.Place); m.ChairPositionId = dto.ChairPositionId;
            m.SecretaryPositionId = dto.SecretaryPositionId; m.ModifyDate = now; m.ModifierUserId = Me;

            // agenda: matched by position in the list (Seq), so decisions keep pointing at their item
            for (int i = 0; i < agenda.Count; i++)
            {
                OfMeetingAgenda item = m.Agenda.FirstOrDefault(x => x.Seq == i + 1);
                if (item == null) { item = new OfMeetingAgenda { Seq = i + 1 }; m.Agenda.Add(item); }
                item.Title = Clean(agenda[i].Title); item.Minutes = Clean(agenda[i].Minutes);
            }
            foreach (OfMeetingAgenda old in m.Agenda.Where(x => x.Seq > agenda.Count).ToList())
            {
                foreach (OfDecision d in m.Decisions.Where(x => x.AgendaId == old.Id)) d.AgendaId = null;
                m.Agenda.Remove(old);
                if (old.Id != 0) _uow.Set<OfMeetingAgenda>().Remove(old);
            }
            _uow.RemoveRange(m.Attendees.Where(a => a.Id != 0).ToList());
            m.Attendees = new List<OfMeetingAttendee>();
            foreach (MeetingAttendeeDto a in attendees)
                m.Attendees.Add(new OfMeetingAttendee { PositionId = a.PositionId, UserId = a.UserId, ExternalName = a.PositionId.HasValue || a.UserId.HasValue ? null : Clean(a.ExternalName), Present = a.Present });
            _uow.RemoveRange(m.Links.Where(k => k.Id != 0).ToList());
            m.Links = new List<OfRecordLink>();
            foreach (RecordLinkDto k in links) m.Links.Add(new OfRecordLink { Kind = k.Kind, RecordId = k.RecordId, Text = Clean(k.Text) });
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<MeetingDto>(OfficeText.Concurrency); }
            catch (DbUpdateException) { return Bad<MeetingDto>("ذخیره جلسه انجام نشد؛ دوباره تلاش کنید."); }

            // decisions: open ones are replaced by the list, finished ones stay
            Dictionary<int, int> agendaIdBySeq = m.Agenda.ToDictionary(a => a.Seq, a => a.Id);
            int seq = m.Decisions.Select(x => x.Seq).DefaultIfEmpty(0).Max();
            foreach (OfDecision old in m.Decisions.Where(x => x.Status == OfDecisionStatus.Open && !decisions.Any(d => d.Id == x.Id)).ToList())
            {
                m.Decisions.Remove(old);
                _uow.Set<OfDecision>().Remove(old);
            }
            foreach (DecisionDto d in decisions)
            {
                OfDecision target = d.Id.HasValue ? m.Decisions.FirstOrDefault(x => x.Id == d.Id.Value) : null;
                if (d.Id.HasValue && target == null) return Bad<MeetingDto>("مصوبه پیدا نشد.");
                if (target != null && target.Status != OfDecisionStatus.Open) continue;
                if (target == null)
                {
                    target = new OfDecision { Seq = ++seq, Status = OfDecisionStatus.Open };
                    m.Decisions.Add(target);
                }
                target.Text = Clean(d.Text); target.OwnerPositionId = d.OwnerPositionId; target.DueDate = d.DueDate?.Date;
                target.AgendaId = d.AgendaSeq.HasValue && agendaIdBySeq.TryGetValue(d.AgendaSeq.Value, out int aid) ? aid : (int?)null;
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<MeetingDto>(OfficeText.Concurrency); }
            catch (DbUpdateException) { return Bad<MeetingDto>("ذخیره مصوبات انجام نشد؛ دوباره تلاش کنید."); }
            return ResponseDtoHelper.Ok(await BuildAsync(await LoadAsync(m.Id, false, ct), c, ct));
        }

        // -------------------------------------------------------------------------------- actions

        private void AddInbox(string key, OfInboxKind kind, string title, OfMeeting m, int? positionId, DateTime? due = null)
        {
            _uow.Set<OfInboxItem>().Add(new OfInboxItem
            {
                RecipientPositionId = positionId, Kind = kind, Title = title.Length > 300 ? title.Substring(0, 300) : title, SourceModule = "office", SourceType = "meeting", SourceId = m.Id, SourceKey = key,
                Link = "/office/meetings/" + m.Id, DueDate = due, Status = OfInboxStatus.Unread, CreatedAt = DateTimeOffset.Now,
            });
        }

        private async Task CloseKeyAsync(string key, string reason, CancellationToken ct)
        {
            List<OfInboxItem> items = await _uow.Set<OfInboxItem>().Where(i => i.SourceKey == key && (i.Status == OfInboxStatus.Unread || i.Status == OfInboxStatus.Read)).ToListAsync(ct);
            foreach (OfInboxItem i in items) { i.Status = OfInboxStatus.Done; i.ClosedAt = DateTimeOffset.Now; i.ClosedReason = reason; }
        }

        public async Task<ResponseDto<MeetingDto>> MeetingActionAsync(MeetingActionDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<MeetingDto>(OfficeText.InvalidData);
            Ctx c = await ContextAsync(ct);
            OfMeeting m = await LoadAsync(dto.Id, true, ct);
            if (m == null || !CanSee(m, c)) return OfficeText.NotFound<MeetingDto>("جلسه پیدا نشد.");
            if (Clean(dto.RowVersion) != null)
            {
                try { _uow.Entry(m).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); }
                catch (FormatException) { return Bad<MeetingDto>(OfficeText.InvalidData); }
            }
            DateTimeOffset now = DateTimeOffset.Now;
            switch ((dto.Action ?? "").Trim().ToLowerInvariant())
            {
                case "hold":
                    if (!CanEdit(m, c)) return Bad<MeetingDto>(OfficeText.NotAllowed);
                    if (m.Status != OfMeetingStatus.Planned) return Bad<MeetingDto>("فقط جلسه برنامه‌ریزی‌شده برگزار می‌شود.");
                    if (dto.PresentAttendeeIds != null && dto.PresentAttendeeIds.Count > 0)
                        foreach (OfMeetingAttendee a in m.Attendees) a.Present = dto.PresentAttendeeIds.Contains(a.Id);
                    m.Status = OfMeetingStatus.Held;
                    break;
                case "submit-minutes":
                    if (!CanEdit(m, c)) return Bad<MeetingDto>(OfficeText.NotAllowed);
                    if (m.Status != OfMeetingStatus.Held) return Bad<MeetingDto>("صورتجلسه پس از برگزاری جلسه برای تایید فرستاده می‌شود.");
                    if (!m.Agenda.Any(a => Clean(a.Minutes) != null) && m.Decisions.Count == 0) return Bad<MeetingDto>("متن صورتجلسه یا مصوبات خالی است.");
                    m.Status = OfMeetingStatus.MinutesDraft;
                    AddInbox("meeting:" + m.Id + ":minutes", OfInboxKind.Approval, "تایید صورتجلسه: " + m.Title, m, m.ChairPositionId);
                    break;
                case "approve-minutes":
                {
                    if (m.Status != OfMeetingStatus.MinutesDraft) return Bad<MeetingDto>("صورتجلسه در انتظار تایید نیست.");
                    ActingPosition acting = m.ChairPositionId.HasValue ? c.Acting.FirstOrDefault(a => a.PositionId == m.ChairPositionId.Value) : null;
                    if (acting == null) return Bad<MeetingDto>("صورتجلسه را فقط رئیس جلسه (یا جانشین رسمی او) تایید می‌کند.");
                    m.Status = OfMeetingStatus.MinutesApproved; m.MinutesApprovedAt = now; m.MinutesApprovedByUserId = Me; m.MinutesApprovedOnBehalf = acting.OnBehalf;
                    await CloseKeyAsync("meeting:" + m.Id + ":minutes", "صورتجلسه تایید شد", ct);
                    foreach (OfDecision d in m.Decisions.Where(x => x.Status == OfDecisionStatus.Open))
                        AddInbox("decision:" + d.Id, OfInboxKind.MeetingAction, "مصوبه جلسه " + m.Number + ": " + d.Text, m, d.OwnerPositionId, d.DueDate);
                    break;
                }
                case "sign-minutes":
                {
                    if (m.Status != OfMeetingStatus.MinutesApproved) return Bad<MeetingDto>("امضای صورتجلسه پس از تایید رئیس جلسه ممکن است.");
                    OfMeetingAttendee mine = m.Attendees.FirstOrDefault(a => a.Present && a.SignedAt == null && (a.UserId == Me || (a.PositionId.HasValue && c.Pos.Contains(a.PositionId.Value))));
                    if (mine == null) return Bad<MeetingDto>("شما در فهرست حاضران این جلسه نیستید یا قبلاً امضا کرده‌اید.");
                    mine.SignedAt = now;
                    break;
                }
                case "cancel":
                    if (!CanEdit(m, c)) return Bad<MeetingDto>(OfficeText.NotAllowed);
                    if (m.Status == OfMeetingStatus.MinutesApproved || m.Status == OfMeetingStatus.Cancelled) return Bad<MeetingDto>("این جلسه دیگر لغو نمی‌شود.");
                    if (Clean(dto.Reason) == null) return Bad<MeetingDto>("علت لغو را بنویسید.");
                    m.Status = OfMeetingStatus.Cancelled; m.CancelReason = Clean(dto.Reason);
                    await CloseKeyAsync("meeting:" + m.Id + ":minutes", "جلسه لغو شد", ct);
                    break;
                default:
                    return Bad<MeetingDto>("عمل نامعتبر است.");
            }
            m.ModifyDate = now; m.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<MeetingDto>(OfficeText.Concurrency); }
            return ResponseDtoHelper.Ok(await BuildAsync(await LoadAsync(m.Id, false, ct), c, ct));
        }

        // -------------------------------------------------------------------------------- decisions

        public async Task<ResponseDto<PagedList<DecisionDto>>> GetDecisionsAsync(DecisionQuery query, CancellationToken ct)
        {
            query ??= new DecisionQuery();
            Ctx c = await ContextAsync(ct);
            int me = Me;
            List<int> pos = c.Pos;
            bool ceo = c.IsCeo;
            DateTime today = DateTime.Today;
            // a person sees the decisions of meetings they may see, and always the ones they own
            var q = from d in _uow.Set<OfDecision>().AsNoTracking()
                    join m in _uow.Set<OfMeeting>().AsNoTracking() on d.MeetingId equals m.Id
                    where !m.Deleted && m.Status == OfMeetingStatus.MinutesApproved
                        && (ceo || m.CreatorUserId == me || pos.Contains(d.OwnerPositionId) || (m.ChairPositionId != null && pos.Contains(m.ChairPositionId.Value))
                            || (m.SecretaryPositionId != null && pos.Contains(m.SecretaryPositionId.Value))
                            || m.Attendees.Any(a => a.UserId == me || (a.PositionId != null && pos.Contains(a.PositionId.Value))))
                    select new { d, m };
            if (query.Status.HasValue) q = q.Where(x => x.d.Status == query.Status.Value);
            if (query.OverdueOnly == true) q = q.Where(x => x.d.Status == OfDecisionStatus.Open && x.d.DueDate != null && x.d.DueDate < today);
            if (query.MineOnly == true) q = q.Where(x => pos.Contains(x.d.OwnerPositionId));
            if (query.MeetingId.HasValue) q = q.Where(x => x.d.MeetingId == query.MeetingId.Value);
            if (query.OwnerPositionId.HasValue) q = q.Where(x => x.d.OwnerPositionId == query.OwnerPositionId.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(x => x.d.Text.Contains(text));
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            var rows = await q.OrderBy(x => x.d.DueDate == null).ThenBy(x => x.d.DueDate).ThenBy(x => x.d.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            Dictionary<int, string> positions = await OfficeActors.PositionTitlesAsync(_uow, rows.Select(r => (int?)r.d.OwnerPositionId), ct);
            Dictionary<int, string> users = await InventoryUsers.GetNamesAsync(_uow, rows.Select(r => r.d.DoneByUserId), ct);
            var none = new Dictionary<int, int>();
            return ResponseDtoHelper.Ok(F2Page.Make(rows.Select(r => ToDto(r.d, r.m, positions, users, none, c)).ToList(), total, number, size));
        }

        public async Task<ResponseDto<DecisionDto>> DecisionActionAsync(DecisionActionDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<DecisionDto>(OfficeText.InvalidData);
            Ctx c = await ContextAsync(ct);
            OfDecision d = await _uow.Set<OfDecision>().FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (d == null) return OfficeText.NotFound<DecisionDto>("مصوبه پیدا نشد.");
            OfMeeting m = await _uow.Set<OfMeeting>().FirstOrDefaultAsync(x => x.Id == d.MeetingId && !x.Deleted, ct);
            if (m == null || m.Status != OfMeetingStatus.MinutesApproved) return Bad<DecisionDto>("مصوبه فقط پس از تایید صورتجلسه اجرا می‌شود.");
            if (d.Status != OfDecisionStatus.Open) return Bad<DecisionDto>("این مصوبه دیگر باز نیست.");
            string note = Clean(dto.Note);
            DateTimeOffset now = DateTimeOffset.Now;
            switch ((dto.Action ?? "").Trim().ToLowerInvariant())
            {
                case "done":
                {
                    ActingPosition acting = c.Acting.FirstOrDefault(a => a.PositionId == d.OwnerPositionId && (!dto.ActingPositionId.HasValue || dto.ActingPositionId.Value == a.PositionId));
                    if (acting == null) return Bad<DecisionDto>("فقط مسئول مصوبه (یا جانشین رسمی او) آن را انجام‌شده اعلام می‌کند.");
                    if (note == null) return Bad<DecisionDto>("شرح اقدام انجام‌شده را بنویسید.");
                    if (dto.EvidenceDocumentId.HasValue && !await _uow.Set<Entities.Model.Document>().AnyAsync(x => x.Id == dto.EvidenceDocumentId.Value, ct)) return Bad<DecisionDto>("فایل مستند پیدا نشد.");
                    d.Status = OfDecisionStatus.Done; d.DoneAt = now; d.DoneByUserId = Me; d.DoneOnBehalf = acting.OnBehalf; d.DoneNote = note; d.EvidenceDocumentId = dto.EvidenceDocumentId;
                    break;
                }
                case "cancel":
                    if (!(c.IsCeo || m.CreatorUserId == Me || (m.ChairPositionId.HasValue && c.Pos.Contains(m.ChairPositionId.Value)))) return Bad<DecisionDto>(OfficeText.NotAllowed);
                    if (note == null) return Bad<DecisionDto>("علت لغو مصوبه را بنویسید.");
                    d.Status = OfDecisionStatus.Cancelled; d.DoneAt = now; d.DoneByUserId = Me; d.DoneNote = note;
                    break;
                default:
                    return Bad<DecisionDto>("عمل نامعتبر است.");
            }
            await CloseKeyAsync("decision:" + d.Id, "مصوبه بسته شد", ct);
            await CloseKeyAsync("decision-overdue:" + d.Id, "مصوبه بسته شد", ct);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<DecisionDto>(OfficeText.Concurrency); }
            Dictionary<int, string> positions = await OfficeActors.PositionTitlesAsync(_uow, new int?[] { d.OwnerPositionId }, ct);
            Dictionary<int, string> users = await InventoryUsers.GetNamesAsync(_uow, new int?[] { d.DoneByUserId }, ct);
            return ResponseDtoHelper.Ok(ToDto(d, m, positions, users, new Dictionary<int, int>(), c));
        }

        public async Task<ResponseDto<OverdueCheckResultDto>> CheckOverdueAsync(CancellationToken ct)
        {
            DateTime today = DateTime.Today;
            var overdue = await (from d in _uow.Set<OfDecision>().AsNoTracking()
                                 join m in _uow.Set<OfMeeting>().AsNoTracking() on d.MeetingId equals m.Id
                                 where d.Status == OfDecisionStatus.Open && d.DueDate != null && d.DueDate < today && m.Status == OfMeetingStatus.MinutesApproved && !m.Deleted
                                 select new { d.Id, d.Text, d.MeetingId, d.OwnerPositionId, d.DueDate, m.ChairPositionId, m.Number }).ToListAsync(ct);
            var result = new OverdueCheckResultDto { OverdueDecisions = overdue.Count };
            if (overdue.Count == 0) return ResponseDtoHelper.Ok(result);
            List<string> keys = overdue.Select(o => "decision-overdue:" + o.Id).ToList();
            HashSet<string> alerted = (await _uow.Set<OfInboxItem>().AsNoTracking().Where(i => i.SourceKey != null && keys.Contains(i.SourceKey)).Select(i => i.SourceKey).ToListAsync(ct)).ToHashSet();
            foreach (var o in overdue)
            {
                string key = "decision-overdue:" + o.Id;
                if (alerted.Contains(key) || !o.ChairPositionId.HasValue) continue;
                _uow.Set<OfInboxItem>().Add(new OfInboxItem
                {
                    RecipientPositionId = o.ChairPositionId, Kind = OfInboxKind.Alert, Title = ("مصوبه معوق جلسه " + o.Number + ": " + o.Text).Length > 300 ? ("مصوبه معوق جلسه " + o.Number).ToString() : "مصوبه معوق جلسه " + o.Number + ": " + o.Text,
                    SourceModule = "office", SourceType = "meeting", SourceId = o.MeetingId, SourceKey = key, Link = "/office/meetings/" + o.MeetingId, DueDate = o.DueDate, Priority = OfUrgency.Urgent,
                    Status = OfInboxStatus.Unread, CreatedAt = DateTimeOffset.Now,
                });
                result.AlertsCreated++;
            }
            if (result.AlertsCreated > 0) await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(result);
        }
    }
}
