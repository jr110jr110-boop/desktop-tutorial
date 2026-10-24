using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Office;

namespace ViewModels.NasbNiro.OfficeDtos
{
    public class MeetingQuery : OfficePageQuery
    {
        public OfMeetingStatus? Status { get; set; }
        public OfMeetingKind? Kind { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public bool? MineOnly { get; set; }
    }

    public class MeetingListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public OfMeetingKind Kind { get; set; }
        public DateTime StartAt { get; set; }
        public string StartAtText { get; set; }
        public string Place { get; set; }
        public OfMeetingStatus Status { get; set; }
        public string ChairPositionTitle { get; set; }
        public int OpenDecisions { get; set; }
        public int OverdueDecisions { get; set; }
    }

    public class MeetingAgendaDto
    {
        public int? Id { get; set; }
        public int Seq { get; set; }
        public string Title { get; set; }
        public string Minutes { get; set; }
    }

    public class MeetingAttendeeDto
    {
        public int? Id { get; set; }
        public int? PositionId { get; set; }
        public string PositionTitle { get; set; }
        public int? UserId { get; set; }
        public string UserName { get; set; }
        public string ExternalName { get; set; }
        public bool Present { get; set; }
        public DateTimeOffset? SignedAt { get; set; }
        public string SignedAtText { get; set; }
    }

    public class DecisionDto
    {
        public int? Id { get; set; }
        public int MeetingId { get; set; }
        public string MeetingNumber { get; set; }
        public string MeetingTitle { get; set; }
        public int? AgendaId { get; set; }
        public int? AgendaSeq { get; set; }
        public int Seq { get; set; }
        public string Text { get; set; }
        public int OwnerPositionId { get; set; }
        public string OwnerPositionTitle { get; set; }
        public DateTime? DueDate { get; set; }
        public string DueDateText { get; set; }
        public OfDecisionStatus Status { get; set; }
        public bool Overdue { get; set; }
        public string DoneNote { get; set; }
        public DateTimeOffset? DoneAt { get; set; }
        public string DoneByName { get; set; }
        public Guid? EvidenceDocumentId { get; set; }
    }

    public class MeetingDto : MeetingListDto
    {
        public DateTime? EndAt { get; set; }
        public int? ChairPositionId { get; set; }
        public int? SecretaryPositionId { get; set; }
        public string SecretaryPositionTitle { get; set; }
        public DateTimeOffset? MinutesApprovedAt { get; set; }
        public string MinutesApprovedAtText { get; set; }
        public string MinutesApprovedByName { get; set; }
        public bool MinutesApprovedOnBehalf { get; set; }
        public string CancelReason { get; set; }
        public string CreatedByName { get; set; }
        public List<MeetingAgendaDto> Agenda { get; set; } = new List<MeetingAgendaDto>();
        public List<MeetingAttendeeDto> Attendees { get; set; } = new List<MeetingAttendeeDto>();
        public List<DecisionDto> Decisions { get; set; } = new List<DecisionDto>();
        public List<RecordLinkDto> Links { get; set; } = new List<RecordLinkDto>();

        /// <summary>save, hold, submit-minutes, approve-minutes, sign-minutes, cancel.</summary>
        public List<string> AllowedActions { get; set; } = new List<string>();

        public string RowVersion { get; set; }
    }

    public class SaveMeetingDto
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public OfMeetingKind Kind { get; set; } = OfMeetingKind.Management;
        public DateTime StartAt { get; set; }
        public DateTime? EndAt { get; set; }
        public string Place { get; set; }
        public int? ChairPositionId { get; set; }
        public int? SecretaryPositionId { get; set; }
        public List<MeetingAgendaDto> Agenda { get; set; } = new List<MeetingAgendaDto>();
        public List<MeetingAttendeeDto> Attendees { get; set; } = new List<MeetingAttendeeDto>();
        public List<RecordLinkDto> Links { get; set; } = new List<RecordLinkDto>();

        /// <summary>Decisions of the minutes; each names its agenda item by AgendaSeq.</summary>
        public List<DecisionDto> Decisions { get; set; } = new List<DecisionDto>();

        public string RowVersion { get; set; }
    }

    public class MeetingActionDto
    {
        public int Id { get; set; }

        /// <summary>hold, submit-minutes, approve-minutes, sign-minutes, cancel.</summary>
        public string Action { get; set; }

        public string Reason { get; set; }
        public int? ActingPositionId { get; set; }

        /// <summary>hold: the attendees who were present (attendee ids); empty keeps the flags as saved.</summary>
        public List<int> PresentAttendeeIds { get; set; }

        public string RowVersion { get; set; }
    }

    public class DecisionQuery : OfficePageQuery
    {
        public OfDecisionStatus? Status { get; set; }
        public bool? OverdueOnly { get; set; }

        /// <summary>Only decisions owned by a position I hold or act for.</summary>
        public bool? MineOnly { get; set; }

        public int? MeetingId { get; set; }
        public int? OwnerPositionId { get; set; }
    }

    public class DecisionActionDto
    {
        public int Id { get; set; }

        /// <summary>done or cancel.</summary>
        public string Action { get; set; }

        public string Note { get; set; }
        public Guid? EvidenceDocumentId { get; set; }
        public int? ActingPositionId { get; set; }
    }

    public class OverdueCheckResultDto
    {
        public int OverdueDecisions { get; set; }
        public int AlertsCreated { get; set; }
    }
}
