using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.Office
{
    // ------------------------------------------------------------------------------------------ organisation

    /// <summary>Organisation chart node. The root unit "شرکت" is seeded; HR enters the rest.</summary>
    public class OfUnit : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public int? ParentId { get; set; }
        public int? ManagerPositionId { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>A seat in the organisation. Letters and referrals go to positions, so a replacement or deputy inherits the inbox.</summary>
    public class OfPosition : BaseEntity
    {
        public string Title { get; set; }
        public int UnitId { get; set; }

        /// <summary>The current holder (system user).</summary>
        public int? UserId { get; set; }

        /// <summary>The holder as an employee of the payroll module (FnEmployee).</summary>
        public int? EmployeeId { get; set; }

        public bool IsManager { get; set; }
        public bool CanSign { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>The deputy sees and acts on the position's inbox between the two dates; "on behalf of" is recorded.</summary>
    public class OfDelegation : BaseEntity
    {
        public int PositionId { get; set; }
        public int DeputyUserId { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public bool IsActive { get; set; } = true;
        public string Reason { get; set; }

        /// <summary>A delegation made by somebody other than the holder is Pending until the holder (or the CEO) approves it; only Approved ones give access.</summary>
        public OfDelegationStatus Status { get; set; } = OfDelegationStatus.Approved;

        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
    }

    /// <summary>The stored signature image of a user (an uploaded document).</summary>
    public class OfUserSignature
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public Guid ImageDocumentId { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    // ------------------------------------------------------------------------------------------ inbox

    /// <summary>One line of the unified inbox. Items of a gate share a SourceKey and are closed together when the gate passes.</summary>
    public class OfInboxItem
    {
        public long Id { get; set; }
        public int? RecipientPositionId { get; set; }
        public int? RecipientUserId { get; set; }

        /// <summary>Upper case role name: every holder of the role sees the item.</summary>
        public string RecipientRole { get; set; }

        public OfInboxKind Kind { get; set; }
        public string Title { get; set; }
        public string SourceModule { get; set; }
        public string SourceType { get; set; }
        public int? SourceId { get; set; }
        public string SourceKey { get; set; }
        public string Link { get; set; }
        public DateTime? DueDate { get; set; }
        public OfUrgency Priority { get; set; }
        public OfInboxStatus Status { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? ReadAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public string ClosedReason { get; set; }
    }

    // ------------------------------------------------------------------------------------------ letters

    /// <summary>Customer, supplier or any other organisation that writes to us or receives our letters.</summary>
    public class OfExternalParty : BaseEntity
    {
        public string Name { get; set; }
        public OfPartyKind Kind { get; set; }
        public int? ProviderId { get; set; }
        public int? CustomerId { get; set; }
        public string Address { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class OfLetter : BaseEntity
    {
        public OfLetterKind Kind { get; set; }

        /// <summary>Assigned at registration (incoming) or at signature (outgoing, internal). Never reused.</summary>
        public string Number { get; set; }

        public int? NumberRuleId { get; set; }
        public DateTime Date { get; set; }
        public string Subject { get; set; }

        /// <summary>Rich text, sanitised on the way in.</summary>
        public string BodyHtml { get; set; }

        public int? SenderPositionId { get; set; }
        public int? SenderPartyId { get; set; }
        public OfClassification Classification { get; set; }
        public OfUrgency Urgency { get; set; }

        /// <summary>Incoming letters: the number and date written by the sender.</summary>
        public string IncomingNumber { get; set; }
        public DateTime? IncomingDate { get; set; }

        public OfLetterStatus Status { get; set; }
        public int? SignerPositionId { get; set; }
        public int ApprovalRound { get; set; }
        public DateTimeOffset? SignedAt { get; set; }
        public int? SignedByUserId { get; set; }

        /// <summary>Set when a deputy signed for the signing position.</summary>
        public int? SignedOnBehalfOfPositionId { get; set; }

        public Guid? SignatureDocumentId { get; set; }

        /// <summary>SHA-256 (Base64) of the signature image bytes at signature.</summary>
        public string SignatureFileHash { get; set; }

        /// <summary>SHA-256 of the content at signature (Base64); the letter cannot change afterwards.</summary>
        public string ContentHash { get; set; }

        public bool PaperCopy { get; set; }
        public OfDelivery? DeliveryMethod { get; set; }
        public DateTime? SentDate { get; set; }
        public string DeliveryReceipt { get; set; }
        public Guid? DeliveryReceiptDocumentId { get; set; }
        public DateTime? ReceiptDate { get; set; }

        public int? ReplyToLetterId { get; set; }
        public int? FollowUpLetterId { get; set; }
        public int? TemplateId { get; set; }

        public DateTimeOffset? RegisteredAt { get; set; }
        public int? RegisteredByUserId { get; set; }

        /// <summary>Loaded from the legacy archive: the number is kept as written and does not use the new sequence.</summary>
        public bool Imported { get; set; }

        public string CancelReason { get; set; }
        public DateTimeOffset? CancelledAt { get; set; }

        public ICollection<OfLetterRecipient> Recipients { get; set; }
        public ICollection<OfLetterApproval> Approvals { get; set; }
        public ICollection<OfLetterAttachment> Attachments { get; set; }
        public ICollection<OfRecordLink> Links { get; set; }
        public ICollection<OfReferral> Referrals { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class OfLetterRecipient
    {
        public int Id { get; set; }
        public int LetterId { get; set; }
        public OfLetter Letter { get; set; }
        public OfRecipientType Type { get; set; }
        public int? PositionId { get; set; }
        public int? PartyId { get; set; }
    }

    public class OfLetterApproval
    {
        public int Id { get; set; }
        public int LetterId { get; set; }
        public OfLetter Letter { get; set; }
        public int Round { get; set; }
        public int Seq { get; set; }
        public int PositionId { get; set; }
        public OfApprovalStatus Status { get; set; }
        public int? ActedByUserId { get; set; }

        /// <summary>A deputy acted for the position.</summary>
        public bool OnBehalf { get; set; }

        public string Comment { get; set; }
        public DateTimeOffset? ActedAt { get; set; }
    }

    public class OfLetterAttachment
    {
        public int Id { get; set; }
        public int LetterId { get; set; }
        public OfLetter Letter { get; set; }
        public Guid DocumentId { get; set; }
        public string FileName { get; set; }

        /// <summary>SHA-256 (Base64) of the file bytes at signature; verification recomputes it from the stored file.</summary>
        public string FileHash { get; set; }
    }

    /// <summary>A record a letter or a meeting is about (contract, project, purchase order, tender, inquiry, service request ...).</summary>
    public class OfRecordLink
    {
        public int Id { get; set; }
        public int? LetterId { get; set; }
        public OfLetter Letter { get; set; }
        public int? MeetingId { get; set; }
        public OfMeeting Meeting { get; set; }
        public OfRelatedKind Kind { get; set; }
        public int? RecordId { get; set; }
        public string Text { get; set; }
    }

    public class OfLetterTemplate : BaseEntity
    {
        public string Title { get; set; }
        public OfLetterKind? LetterKind { get; set; }
        public string HeaderHtml { get; set; }
        public string FooterHtml { get; set; }

        /// <summary>Body with placeholders such as {subject}, {date}, {number}, {recipient}.</summary>
        public string BodyHtml { get; set; }

        /// <summary>Position ids separated by commas: the default approval chain.</summary>
        public string ApprovalChain { get; set; }

        public int? SignerPositionId { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>"{yy}/{unit}/{seq:5}" and tokens {yyyy}, {yy}, {seq:n}, {unit}, {kind}. One counter per rule and Persian year.</summary>
    public class OfNumberingRule : BaseEntity
    {
        public OfLetterKind Kind { get; set; }
        public int? UnitId { get; set; }
        public string Pattern { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class OfNumberCounter
    {
        public int Id { get; set; }
        public int RuleId { get; set; }
        public int Year { get; set; }
        public int Last { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class OfReferral
    {
        public int Id { get; set; }
        public int LetterId { get; set; }
        public OfLetter Letter { get; set; }
        public int? ParentId { get; set; }
        public int? FromPositionId { get; set; }
        public int ToPositionId { get; set; }
        public string Instruction { get; set; }
        public OfReferralAction Action { get; set; }
        public DateTime? DueDate { get; set; }
        public OfReferralStatus Status { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }
        public bool OnBehalf { get; set; }
        public DateTimeOffset? DoneAt { get; set; }
        public int? DoneByUserId { get; set; }
        public bool DoneOnBehalf { get; set; }
        public string DoneNote { get; set; }
        public ICollection<OfReferralEntry> Entries { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class OfReferralEntry
    {
        public int Id { get; set; }
        public int ReferralId { get; set; }
        public OfReferral Referral { get; set; }
        public int UserId { get; set; }
        public bool OnBehalf { get; set; }
        public string Text { get; set; }
        public DateTimeOffset At { get; set; }
    }

    // ------------------------------------------------------------------------------------------ meetings

    public class OfMeeting : BaseEntity
    {
        public string Number { get; set; }
        public string Title { get; set; }
        public OfMeetingKind Kind { get; set; }
        public DateTime StartAt { get; set; }
        public DateTime? EndAt { get; set; }
        public string Place { get; set; }
        public int? ChairPositionId { get; set; }
        public int? SecretaryPositionId { get; set; }
        public OfMeetingStatus Status { get; set; }
        public DateTimeOffset? MinutesApprovedAt { get; set; }
        public int? MinutesApprovedByUserId { get; set; }
        public bool MinutesApprovedOnBehalf { get; set; }
        public string CancelReason { get; set; }

        public ICollection<OfMeetingAgenda> Agenda { get; set; }
        public ICollection<OfMeetingAttendee> Attendees { get; set; }
        public ICollection<OfDecision> Decisions { get; set; }
        public ICollection<OfRecordLink> Links { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class OfMeetingAgenda
    {
        public int Id { get; set; }
        public int MeetingId { get; set; }
        public OfMeeting Meeting { get; set; }
        public int Seq { get; set; }
        public string Title { get; set; }

        /// <summary>Minutes of the item.</summary>
        public string Minutes { get; set; }
    }

    public class OfMeetingAttendee
    {
        public int Id { get; set; }
        public int MeetingId { get; set; }
        public OfMeeting Meeting { get; set; }
        public int? PositionId { get; set; }
        public int? UserId { get; set; }
        public string ExternalName { get; set; }
        public bool Present { get; set; }
        public DateTimeOffset? SignedAt { get; set; }
    }

    public class OfDecision
    {
        public int Id { get; set; }
        public int MeetingId { get; set; }
        public OfMeeting Meeting { get; set; }
        public int? AgendaId { get; set; }
        public int Seq { get; set; }
        public string Text { get; set; }
        public int OwnerPositionId { get; set; }
        public DateTime? DueDate { get; set; }
        public OfDecisionStatus Status { get; set; }
        public string DoneNote { get; set; }
        public DateTimeOffset? DoneAt { get; set; }
        public int? DoneByUserId { get; set; }
        public bool DoneOnBehalf { get; set; }
        public Guid? EvidenceDocumentId { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ attendance

    /// <summary>A working shift. Times are minutes from midnight; the end may be smaller than the start (the shift crosses midnight).</summary>
    public class OfShift : BaseEntity
    {
        public string Title { get; set; }
        public int StartMinute { get; set; }
        public int EndMinute { get; set; }
        public int BreakMinutes { get; set; }
        public int NightStartMinute { get; set; } = 22 * 60;
        public int NightEndMinute { get; set; } = 6 * 60;

        /// <summary>Bit n is set when the weekday with number n (0 = Sunday ... 6 = Saturday) is a working day of the shift.</summary>
        public int WorkDaysMask { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class OfShiftAssignment
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public int ShiftId { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>A time clock or middleware on the LAN that pushes events. The key is shown once and stored as a hash.</summary>
    public class OfDevice : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string Location { get; set; }
        public string ApiKeyHash { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTimeOffset? LastPushAt { get; set; }
    }

    public class OfClockEvent
    {
        public long Id { get; set; }
        public int EmployeeId { get; set; }

        /// <summary>Local time as recorded by the device (no time zone).</summary>
        public DateTime Timestamp { get; set; }

        public string DeviceCode { get; set; }
        public OfClockDirection Direction { get; set; }
        public OfClockSource Source { get; set; }

        /// <summary>Manual events: the reason (audit).</summary>
        public string Reason { get; set; }

        public int? CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>Inputs of the daily computation (one row). Only the grace is seeded (0).</summary>
    public class OfAttendancePolicy
    {
        public int Id { get; set; }
        public int GraceMinutes { get; set; }

        /// <summary>Overtime allowed per day without a request; null means not configured (then only requested overtime counts).</summary>
        public int? MaxOvertimeMinutesPerDay { get; set; }

        public bool OvertimeNeedsPreApproval { get; set; } = true;

        /// <summary>Overtime is rounded down to a multiple of this many minutes (0 or 1 = no rounding).</summary>
        public int RoundingMinutes { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
        public int UpdatedByUserId { get; set; }
    }

    public class OfAttendanceDay
    {
        public long Id { get; set; }
        public int EmployeeId { get; set; }
        public DateTime Date { get; set; }
        public int? ShiftId { get; set; }
        public OfDayStatus Status { get; set; }
        public bool NonWorkingDay { get; set; }
        public DateTime? FirstIn { get; set; }
        public DateTime? LastOut { get; set; }
        public int WorkedMinutes { get; set; }
        public int DelayMinutes { get; set; }
        public int EarlyLeaveMinutes { get; set; }
        public int OvertimeMinutes { get; set; }
        public int NightMinutes { get; set; }
        public int HolidayMinutes { get; set; }
        public int AbsenceDays { get; set; }
        public int UnpaidLeaveDays { get; set; }
        public decimal PaidLeaveDays { get; set; }

        /// <summary>Approved sick leave (paid by insurance): kept apart from PaidLeaveDays so it never uses the annual leave balance.</summary>
        public decimal SickLeaveDays { get; set; }
        public int MissionDays { get; set; }
        public string Note { get; set; }
        public DateTimeOffset ComputedAt { get; set; }
    }

    public class OfAttendanceMonth
    {
        public int Id { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public DateTimeOffset ClosedAt { get; set; }
        public int ClosedByUserId { get; set; }
        public int SheetCount { get; set; }
        public int SkippedLocked { get; set; }
    }

    public class OfLeaveRequest : BaseEntity
    {
        public string Number { get; set; }
        public int EmployeeId { get; set; }
        public OfLeaveKind Kind { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        /// <summary>Hourly leave and hourly mission: the time window (minutes from midnight).</summary>
        public int? FromMinute { get; set; }
        public int? ToMinute { get; set; }

        /// <summary>Overtime pre-approval: the approved hours.</summary>
        public decimal? Hours { get; set; }

        /// <summary>Forgot clock: the time to record and the direction.</summary>
        public OfClockDirection? Direction { get; set; }

        /// <summary>Days taken from the annual leave balance (daily and hourly leave).</summary>
        public decimal Days { get; set; }

        public string Reason { get; set; }
        public Guid? AttachmentDocumentId { get; set; }
        public OfLeaveStatus Status { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? ApproverPositionId { get; set; }
        public int? ApprovedByUserId { get; set; }
        public bool ApprovedOnBehalf { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string ApproverComment { get; set; }
        public int? RecordedByUserId { get; set; }
        public DateTimeOffset? RecordedAt { get; set; }
        public string RejectReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ general requests

    public class OfRequestType : BaseEntity
    {
        public string Title { get; set; }

        /// <summary>JSON array of fields: name, label, type (text, number, date, select), required, options.</summary>
        public string FieldsJson { get; set; }

        /// <summary>Position ids separated by commas. Empty: the requester's unit manager approves.</summary>
        public string ApprovalChain { get; set; }

        /// <summary>Where the approved request is carried out (module or unit), as a note.</summary>
        public string TargetNote { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class OfRequest : BaseEntity
    {
        public string Number { get; set; }
        public int TypeId { get; set; }
        public int RequesterUserId { get; set; }
        public string ValuesJson { get; set; }
        public OfRequestStatus Status { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string Note { get; set; }
        public ICollection<OfRequestApproval> Approvals { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class OfRequestApproval
    {
        public int Id { get; set; }
        public int RequestId { get; set; }
        public OfRequest Request { get; set; }
        public int Step { get; set; }
        public int PositionId { get; set; }
        public OfApprovalStatus Status { get; set; }
        public int? ActedByUserId { get; set; }
        public bool OnBehalf { get; set; }
        public string Comment { get; set; }
        public DateTimeOffset? ActedAt { get; set; }
    }
}
