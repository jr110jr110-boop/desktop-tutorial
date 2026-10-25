namespace Entities.Model.NasbNiro.AfterSales
{
    /// <summary>Priority of a service request. The SLA table (hours) is keyed by it; no hours are seeded.</summary>
    public enum SvPriority { Critical = 1, High = 2, Normal = 3, Low = 4 }

    public enum SvRequestType { Breakdown = 1, Inspection = 2, Commissioning = 3, Overhaul = 4, SpareParts = 5, TechnicalQuery = 6, Complaint = 7 }

    public enum SvRequestStatus { Open = 1, Assigned = 2, InProgress = 3, WaitingCustomer = 4, Resolved = 5, Closed = 6 }

    /// <summary>Where the warranty period of an installed unit came from.</summary>
    public enum SvWarrantySource { None = 0, Months = 1, Guarantee = 2, Manual = 3 }

    /// <summary>Status of an installed unit (computed from the dates and the active LTSA, except Decommissioned which a person sets).</summary>
    public enum SvUnitStatus { InWarranty = 1, OutOfWarranty = 2, UnderLtsa = 3, Decommissioned = 4 }

    public enum SvOrderKind { Field = 1, Workshop = 2 }

    /// <summary>Draft: made by the system (an LTSA visit) and waiting for service to confirm. Planned: engineers and dates are set. InProgress: work started. Closed: signed report, costs posted.</summary>
    public enum SvOrderStatus { Draft = 1, Planned = 2, InProgress = 3, Closed = 4, Cancelled = 5 }

    /// <summary>None: not decided yet. Warranty: only for a unit in warranty and a cause that is not misuse. Billable: the customer pays. Goodwill: the company pays without warranty (the CEO decides).</summary>
    public enum SvWarrantyDecision { None = 0, Warranty = 1, Billable = 2, Goodwill = 3 }

    public enum SvTimeKind { Travel = 1, Work = 2, Waiting = 3 }

    public enum SvContractStatus { Draft = 1, Active = 2, Expired = 3, Cancelled = 4 }

    public enum SvSpareSource { Manual = 1, MdrList = 2 }

    /// <summary>Delivery: after the handover of a project. Service: after a closed service order.</summary>
    public enum SlSurveyKind { Delivery = 1, Service = 2 }

    public enum SlSurveyStatus { Drafted = 1, Sent = 2, Answered = 3, Skipped = 4 }
}
