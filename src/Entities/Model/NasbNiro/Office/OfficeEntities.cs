using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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

        /// <summary>
        /// Optional link to a tender (legacy Tender entity); its code is part of the letter number ({code} token). At most one of TenderId and ContractId.
        /// Set on a draft only: it is frozen once the letter is numbered.
        /// </summary>
        public int? TenderId { get; set; }

        /// <summary>Optional link to a project (a contract; the code is the project code of project control, else the contract code). At most one of TenderId and ContractId.</summary>
        public int? ContractId { get; set; }

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

    /// <summary>
    /// Pattern with the tokens {yyyy}, {yy}, {seq} or {seq:n} (n = zero padding; plain {seq} = no padding), {unit}, {kind} and {code}
    /// ({code} = "tender-or-project-code-" when the letter is linked to a tender or project, else nothing). One counter per rule and Persian year.
    /// Company default (office answers, 1405/08): incoming "INN-و-{code}{yyyy}-{seq}", outgoing "INN-{code}{yyyy}-{seq}", internal "INN-د-{code}{yyyy}-{seq}".
    /// </summary>
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
        /// <summary>The night window of the shift (minutes from midnight). Filled from the rate-table keys NightWindowStartMinute / NightWindowEndMinute when the shift is saved; no literal default lives here.</summary>
        public int NightStartMinute { get; set; }
        public int NightEndMinute { get; set; }

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

        /// <summary>Required when the assignment starts inside a month that is already running or closed (back-dating inside the open month).</summary>
        public string Reason { get; set; }
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

        /// <summary>The key is shown once when the device is created; a new key is only issued by the separate rotate action, which stamps these two fields (they are audited, the key itself never is).</summary>
        public DateTimeOffset? KeyRotatedAt { get; set; }
        public int? KeyRotatedByUserId { get; set; }

        /// <summary>
        /// The LAN addresses the device may push from: a comma separated list of IPv4 addresses or IPv4/CIDR ranges (for example "192.168.1.20,10.0.5.0/24"). Empty = any address (the key alone authenticates).
        /// A push from another address is refused and logged. Edited only by HR and PMO; audited.
        /// </summary>
        public string AllowedAddresses { get; set; }
    }

    /// <summary>
    /// One row per push of a device (or per import file): the device, the number of events, how many were new and a SHA-256 of the payload. A lightweight log instead of one audit row per punch.
    /// </summary>
    public class OfDevicePushLog
    {
        public long Id { get; set; }
        public int? DeviceId { get; set; }
        public string DeviceCode { get; set; }
        public DateTimeOffset ReceivedAt { get; set; }
        public int EventCount { get; set; }
        public int Accepted { get; set; }
        public int Duplicates { get; set; }

        /// <summary>SHA-256 (hex) of the received events in canonical order.</summary>
        public string PayloadHash { get; set; }

        /// <summary>Push, or Rejected when the key or the code was wrong (the attempt is kept).</summary>
        public string Outcome { get; set; }
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

        /// <summary>Manual events: the reason (required; audit).</summary>
        public string Reason { get; set; }

        public int? CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>
        /// Only an Approved event counts in the day computation. A device punch is Approved on arrival, except the punch of the employee who created or last rotated that device's key (Pending: a second person decides);
        /// a manual event is a request (Pending) until a second person decides; an imported row is Pending until a second HR user approves its whole batch (ImportBatchId).
        /// </summary>
        public OfClockApproval Approval { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string RejectReason { get; set; }

        /// <summary>The import batch an imported row belongs to (JSON or Excel import); null for every other source.</summary>
        public int? ImportBatchId { get; set; }
        public OfClockImportBatch ImportBatch { get; set; }
    }

    /// <summary>
    /// One import of clock rows (JSON or Excel). The rows are stored as Pending events of this batch; a second HR user - neither the importer nor an employee inside the batch - approves or rejects the batch as a whole,
    /// and the decision is audited (release R1f).
    /// </summary>
    public class OfClockImportBatch
    {
        public int Id { get; set; }
        public int ImportedByUserId { get; set; }
        public DateTimeOffset ImportedAt { get; set; }

        /// <summary>The number of rows the batch stored (new, non-duplicate events).</summary>
        public int Rows { get; set; }

        /// <summary>Json or Excel.</summary>
        public string Source { get; set; }
        public string DeviceCode { get; set; }

        /// <summary>SHA-256 of the received rows in canonical order (the same hash the push log keeps).</summary>
        public string PayloadHash { get; set; }

        /// <summary>The importer's reason (optional).</summary>
        public string Reason { get; set; }

        public OfClockBatchStatus Status { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }

        /// <summary>The approver's note, or the reason of a rejection (required for a rejection).</summary>
        public string DecisionNote { get; set; }
    }

    /// <summary>
    /// Inputs of the daily computation (one row; defaults apply while no row is saved). Company answers (1405/08): lateness up to 15 minutes is allowed (grace 15);
    /// lateness beyond it is deducted with coefficient 3; overtime is counted up to 22 hours a month. Release R1f turned the two controls around overtime and early leave into switches whose starred default is ON
    /// (the company confirms them; switching one OFF is a decision of the CEO): overtime counts only up to an approved overtime request, early leave beyond the grace is deducted like lateness.
    /// ★ Stated explicitly for the company to confirm: (1) the 22 hour cap covers the overtime and the holiday work (HolidayMinutes) of the month together - they share one bucket, filled in date order -
    /// and night hours (NightMinutes) are a separate sheet column that is not capped; (2) the day is the shift's scheduled minutes = end - start - break (08:00-16:00 with a 30 minute
    /// break = 450), for the lateness deduction (deducted minutes / 450 of a day, at most one day a day) and for an hourly leave (120 minutes = 120/450 of a day); an employee without a shift counts the rate-table
    /// key DefaultDayMinutes.
    /// </summary>
    public class OfAttendancePolicy
    {
        public int Id { get; set; }

        /// <summary>Delay (and early leave) up to this many minutes is allowed. Beyond it the whole delay counts.</summary>
        public int GraceMinutes { get; set; } = 15;

        /// <summary>Overtime allowed per day without a request; null means not configured (then only requested overtime counts).</summary>
        public int? MaxOvertimeMinutesPerDay { get; set; }

        /// <summary>
        /// Overtime and holiday work count only up to the hours of an approved overtime request of that day; the rest is reported as unapproved overtime and is not paid. Stored in the long-standing column
        /// OvertimeNeedsPreApproval (renamed in code only, no migration); a new row starts with the switch on, and the one-time upgrade step "Upgrade:R1f:OvertimeApproval" turns it on for the rows that
        /// already exist (an audit row records it). Switching it OFF afterwards is a decision of the CEO; the value used is frozen into the month snapshot.
        /// </summary>
        [Column("OvertimeNeedsPreApproval")]
        public bool OvertimeRequiresApproval { get; set; } = true;

        /// <summary>Early leave beyond the grace is deducted like lateness (minutes x the same coefficient) unless this switch is set. Stored inverted so that the default of an existing row (false) means the deduction is on.</summary>
        public bool EarlyLeaveDeductionDisabled { get; set; }

        /// <summary>
        /// Who confirms the month close: false (default) only FINANCEMANAGER; true also a second HR user (never the preparer). The preparer can never confirm.
        /// </summary>
        public bool MonthConfirmByHrPeer { get; set; }

        /// <summary>Overtime is rounded down to a multiple of this many minutes (0 or 1 = no rounding).</summary>
        public int RoundingMinutes { get; set; }

        /// <summary>Overtime above this many minutes a month is neither counted nor paid (default 1320 = 22 hours; the monthly sheet takes the capped value).</summary>
        public int MonthlyOvertimeCapMinutes { get; set; } = 1320;

        /// <summary>A delay beyond the grace is deducted as delay minutes x this coefficient (default 3); 0 switches the deduction off. The deduction reaches payroll as fractions of an absence day.</summary>
        public decimal DelayPenaltyFactor { get; set; } = 3m;

        /// <summary>
        /// Set once the company rules (grace 15) were applied to this row: either it was created after the rules, or the one-time upgrade moved a row that still had the first-release
        /// defaults (grace 0 with the overtime approval switch on). Rows an admin changed otherwise are never touched; the flag stops the upgrade from running again. The overtime approval switch has its own one-time step (R1fUpgrades).
        /// </summary>
        public bool CompanyRulesApplied { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
        public int UpdatedByUserId { get; set; }
    }

    /// <summary>A saved column mapping for the Excel export of the time-attendance software (PW Kara): which column holds the personnel number, the date, the time ...</summary>
    public class OfClockImportProfile : BaseEntity
    {
        public string Name { get; set; }

        /// <summary>JSON of ClockFileMappingDto.</summary>
        public string MappingJson { get; set; }
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

        /// <summary>Deducted time for the delay of the day: DelayMinutes x the policy coefficient at the time of the computation (0 when the delay is inside the grace).</summary>
        public int DelayDeductionMinutes { get; set; }
        public int EarlyLeaveMinutes { get; set; }

        /// <summary>Deducted time for early leave beyond the grace: EarlyLeaveMinutes x the delay coefficient (0 when the company switch is off).</summary>
        public int EarlyLeaveDeductionMinutes { get; set; }
        public int OvertimeMinutes { get; set; }

        /// <summary>Overtime or holiday work that no approved overtime request covers (only counted when the policy requires approval): reported, never paid.</summary>
        public int UnapprovedOvertimeMinutes { get; set; }
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

    /// <summary>
    /// The HR close of a month in two steps: HR prepares (ClosedAt / ClosedByUserId are the preparation; the sheets are written and locked and a snapshot is frozen), a second person confirms.
    /// Payroll is calculated and approved only for a Confirmed month.
    /// </summary>
    public class OfAttendanceMonth
    {
        public int Id { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }

        /// <summary>When the month was (last) prepared.</summary>
        public DateTimeOffset ClosedAt { get; set; }

        /// <summary>The preparer: cannot confirm.</summary>
        public int ClosedByUserId { get; set; }
        public int SheetCount { get; set; }
        public int SkippedLocked { get; set; }

        public OfMonthStatus Status { get; set; }
        public DateTimeOffset? ConfirmedAt { get; set; }
        public int? ConfirmedByUserId { get; set; }
        public string RejectReason { get; set; }

        /// <summary>JSON of the numbers written to the sheets at preparation (frozen) and its SHA-256: confirmation checks that the sheets still hold exactly these numbers.</summary>
        public string SnapshotJson { get; set; }
        public string SnapshotHash { get; set; }

        /// <summary>The attendance policy values the preparation used (grace, caps, coefficients, the three switches), frozen next to the snapshot; the hash covers both. Null for a month closed before this rule.</summary>
        public string PolicyJson { get; set; }
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
