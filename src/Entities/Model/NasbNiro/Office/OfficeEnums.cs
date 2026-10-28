namespace Entities.Model.NasbNiro.Office
{
    // Office automation (release O1): organisation, inbox, letters, meetings, attendance, requests. Tables start with "Of".

    public enum OfInboxKind { Letter = 1, Referral = 2, Approval = 3, Task = 4, Alert = 5, MeetingAction = 6 }

    public enum OfInboxStatus { Unread = 0, Read = 1, Done = 2, Cancelled = 3 }

    /// <summary>Urgency of a letter and priority of an inbox item.</summary>
    public enum OfUrgency { Normal = 0, Urgent = 1, Immediate = 2 }

    public enum OfLetterKind { Incoming = 1, Outgoing = 2, Internal = 3 }

    public enum OfLetterStatus { Draft = 0, InApproval = 1, Signed = 2, Registered = 3, Sent = 4, Received = 5, Archived = 6, Cancelled = 7 }

    public enum OfClassification { Normal = 0, Confidential = 1, Secret = 2 }

    public enum OfDelegationStatus { Approved = 0, Pending = 1, Rejected = 2 }

    public enum OfRecipientType { To = 1, Cc = 2 }

    public enum OfDelivery { Hand = 1, Post = 2, Courier = 3, Email = 4, Fax = 5 }

    public enum OfPartyKind { Customer = 1, Supplier = 2, Organisation = 3 }

    public enum OfRelatedKind { Contract = 1, Project = 2, PurchaseOrder = 3, Tender = 4, Inquiry = 5, ServiceRequest = 6, WorkOrder = 7, Other = 99 }

    public enum OfApprovalStatus { Pending = 0, Approved = 1, Returned = 2 }

    public enum OfReferralAction { ForAction = 1, ForInformation = 2, ForComment = 3 }

    public enum OfReferralStatus { Pending = 0, Done = 1, Cancelled = 2 }

    public enum OfMeetingKind { Management = 1, Project = 2, Technical = 3, Quality = 4, Customer = 5, Other = 6 }

    public enum OfMeetingStatus { Planned = 0, Held = 1, MinutesDraft = 2, MinutesApproved = 3, Cancelled = 4 }

    public enum OfDecisionStatus { Open = 0, Done = 1, Cancelled = 2 }

    public enum OfClockDirection { Unknown = 0, In = 1, Out = 2 }

    public enum OfClockSource { Device = 1, Import = 2, Manual = 3 }

    public enum OfLeaveKind { DailyLeave = 1, HourlyLeave = 2, SickLeave = 3, UnpaidLeave = 4, Mission = 5, Overtime = 6, ForgotClock = 7 }

    public enum OfLeaveStatus { Draft = 0, Submitted = 1, Approved = 2, Recorded = 3, Rejected = 4, Cancelled = 5 }

    public enum OfDayStatus { Present = 1, Absent = 2, Leave = 3, Mission = 4, Rest = 5, Incomplete = 6 }

    /// <summary>
    /// Approval state of a clock event. 0 = Approved so that every existing row, every device push and every import keeps counting; a manual event is a request that starts Pending and counts
    /// only once a second person approved it (release R1f, 4-eyes).
    /// </summary>
    public enum OfClockApproval { Approved = 0, Pending = 1, Rejected = 2 }

    /// <summary>
    /// State of an imported clock batch (a JSON or Excel import): the rows wait as Pending until a second HR user approves or rejects the whole batch (release R1f).
    /// </summary>
    public enum OfClockBatchStatus { Pending = 0, Approved = 1, Rejected = 2 }

    /// <summary>
    /// State of the HR month close (two steps: HR prepares, a second person confirms). 0 = Confirmed so that the months closed before the two-step rule stay closed.
    /// </summary>
    public enum OfMonthStatus { Confirmed = 0, Prepared = 1, Rejected = 2 }

    public enum OfRequestStatus { Draft = 0, InApproval = 1, Approved = 2, Rejected = 3, Cancelled = 4, Completed = 5 }
}
