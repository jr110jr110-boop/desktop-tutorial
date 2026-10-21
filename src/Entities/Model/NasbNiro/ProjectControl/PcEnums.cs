namespace Entities.Model.NasbNiro.ProjectControl
{
    // Project control (PMO) module enums. Numbers are stored; never renumber.

    /// <summary>WI-PL-005 contract category letters: MA, SP, SE.</summary>
    public enum PcCategory { Manufacturing = 1, SpareParts = 2, Services = 3 }

    public enum PcInitiationStep
    {
        AnnouncementReceived = 1,
        CodeAssigned = 2,
        UnitsNotified = 3,
        ManagerSelected = 4,
        CharterApproved = 5,
        PepApproved = 6,
        KomHeld = 7,
        BomRequested = 8,
        WbsDrafted = 9,
        BaselineApproved = 10,
    }

    public enum PcReviewKind { StandardProduct = 1, SpecialOrder = 2 }
    public enum PcReviewDecision { Pending = 0, Accept = 1, Reject = 2, Modify = 3 }
    public enum PcAmendmentType { Quantity = 1, Time = 2, Price = 3 }
    public enum PcAmendmentOrigin { Customer = 1, Company = 2 }

    public enum PcMilestoneKind { Advance = 1, Progress = 2, Delivery = 3, Retention = 4, Other = 9 }
    public enum PcMilestoneTrigger { Date = 1, ProgressPercent = 2, MilestoneDone = 3, ReleaseNote = 4, ShipmentDelivered = 5, HandoverMinutes = 6, ClientLetter = 7 }
    public enum PcMilestoneStatus { Planned = 0, Due = 1, StatementIssued = 2, Invoiced = 3, Paid = 4 }

    public enum PcGuaranteeType { Bid = 1, Advance = 2, Performance = 3, Warranty = 4, Other = 9 }
    public enum PcGuaranteeStatus { Requested = 1, Issued = 2, Active = 3, ExtensionPending = 4, Expired = 5, Released = 6, Cancelled = 7 }
    public enum PcGuaranteeRelease { FinalDelivery = 1, EndOfWarranty = 2, ClientLetter = 3 }
    public enum PcGuaranteeIssuer { Bank = 1, Fund = 2 }
    public enum PcGuaranteeEventKind { Issue = 1, Extension = 2, Cancellation = 3 }
    public enum PcGuaranteeSource { ContractGuaranty = 1, TenderGuaranty = 2 }

    public enum PcPenaltyStatus { Computed = 0, Waived = 1, Negotiated = 2 }
    public enum PcVariationStatus { Draft = 0, Submitted = 1, Approved = 2, Rejected = 3 }

    public enum PcWbsNodeType { Project = 1, Phase = 2, Product = 3, Assembly = 4, Part = 5, WorkPackage = 6, Milestone = 7 }
    public enum PcPhase { Engineering = 1, Procurement = 2, Construction = 3, Testing = 4, Delivery = 5, Closeout = 6 }

    public enum PcSourceType { Process = 1, Assembly = 2, ItpActivity = 3, EngineeringDeliverable = 4, ChecklistStep = 5, PurchaseOrderLine = 6, Manual = 7, Milestone = 8 }
    public enum PcDependencyType { FS = 1, SS = 2, FF = 3, SF = 4 }
    public enum PcDependencyOrigin { BomPredecessor = 1, AssemblyPredecessor = 2, ProcessSort = 3, Procurement = 4, Manual = 5 }

    public enum PcConstraint
    {
        None = 0,
        StartNoEarlierThan = 1,
        FinishNoEarlierThan = 2,
        StartNoLaterThan = 3,
        FinishNoLaterThan = 4,
        MustStartOn = 5,
        MustFinishOn = 6,
    }

    public enum PcProgressMethod
    {
        Manual = 0,
        EngineeringIssue = 1,
        SupplyReceipt = 2,
        ServiceDocs = 3,
        Contractor = 4,
        AssemblyChecklist = 5,
        ItpPoints = 6,
        Quantity = 7,
        Milestone = 8,
    }

    public enum PcActivityStatus { NotStarted = 0, InProgress = 1, Done = 2, OnHold = 3 }

    /// <summary>Event that completes a Milestone-method activity.</summary>
    public enum PcMilestoneEvent { None = 0, ReleaseNoteReleased = 1, ShipmentShipped = 2, ShipmentDelivered = 3, HandoverReceived = 4 }

    public enum PcBaselineKind { Master = 1, Detailed = 2, Cost = 3 }
    public enum PcBaselineStatus { Draft = 0, ManagersSigned = 1, Approved = 2, Superseded = 3 }

    public enum PcChangeOrigin { ClientChangeOrder = 1, InternalRequest = 2 }
    public enum PcChangeType { Scope = 1, Schedule = 2, Assumption = 3, Constraint = 4, Team = 5, Method = 6, Standard = 7, ClientApprovalCondition = 8, CommunicationPlan = 9, MeasurementSystem = 10 }
    public enum PcChangeStatus { Draft = 0, Registered = 1, InAnalysis = 2, Decided = 3, Implementing = 4, Closed = 5, Rejected = 6 }
    public enum PcChangeDecision { None = 0, Approve = 1, Reject = 2, Defer = 3, MoreInfo = 4 }
    public enum PcVote { None = 0, Approve = 1, Reject = 2 }

    public enum PcClaimDirection { AgainstClient = 1, AgainstCompany = 2 }
    public enum PcClaimStatus { Issue = 0, Quantified = 1, Notified = 2, Negotiating = 3, Settled = 4, Withdrawn = 5, Litigation = 6 }

    public enum PcIssueState { NotIssued = 0, FirstIssue = 1, SecondIssue = 2, Approved = 3 }
    public enum PcTransmittalDirection { Outgoing = 1, Incoming = 2 }
    public enum PcTransmittalPurpose { ForApproval = 1, ForInformation = 2, AsBuilt = 3 }

    public enum PcReportKind { ClientMonthly = 1, InternalWeekly = 2, CeoMonthly = 3, FinalBookCompany = 4, FinalBookClient = 5 }
    public enum PcShipmentStatus { Planned = 0, Packed = 1, Released = 2, Shipped = 3, Delivered = 4, HandoverReceived = 5 }
    public enum PcAccrualMode { Linear = 1, AtEnd = 2 }
    public enum PcCostClass { Direct = 1, Indirect = 2, Contingency = 3, ManagementReserve = 4 }
    public enum PcResourceClass { Labour = 1, Equipment = 2, Material = 3, Subcontract = 4 }
}
