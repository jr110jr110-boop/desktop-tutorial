namespace Entities.Model.NasbNiro.Sales
{
    public enum SlCustomerType { EndUser = 1, Epc = 2, Distributor = 3, Government = 4 }

    public enum SlIndustry { OilGas = 1, Petrochemical = 2, Power = 3, Steel = 4, Sugar = 5, Other = 9 }

    public enum SlCustomerStatus { Prospect = 1, Active = 2, Inactive = 3 }

    public enum SlActivityKind { Call = 1, Meeting = 2, Email = 3, Visit = 4, Letter = 5 }

    /// <summary>What the company sells. The first six values equal <c>ItpProductType</c>.</summary>
    public enum SlProductType { SteamTurbine = 1, OrcUnit = 2, Condenser = 3, Pump = 4, LubeOilSystem = 5, Valve = 6, SpareParts = 7, Service = 8, Other = 9 }

    public enum SlInquiryStatus { Received = 1, UnderReview = 2, NoBid = 3, Bidding = 4, Submitted = 5, Won = 6, Lost = 7, Cancelled = 8 }

    /// <summary>Draft: sales prepares the checklist. WaitingDecision: the CEO decides. Bid / NoBid: decided.</summary>
    public enum SlBidStatus { Draft = 1, WaitingDecision = 2, Bid = 3, NoBid = 4 }

    public enum SlSanctionCheck { NotChecked = 0, Clear = 1, Concern = 2 }

    public enum SlEstimateStatus { Draft = 1, Final = 2, Superseded = 3 }

    public enum SlCostKind { Material = 1, Labour = 2, Engineering = 3, Subcontract = 4, PackingTransport = 5 }

    /// <summary>Where the unit cost of an estimate line came from.</summary>
    public enum SlCostSource { Manual = 0, AverageCost = 1, LastPurchase = 2, LastPurchaseOrder = 3, WorkCenterRate = 4 }

    public enum SlProposalStatus { Draft = 1, Approved = 2, Submitted = 3, Revised = 4, Won = 5, Lost = 6, Cancelled = 7 }

    public enum SlReasonKind { Won = 1, Lost = 2, NoBid = 3 }

    public enum SlLinkSource { Contract = 1, Tender = 2 }

    public enum SlLinkMethod { LegacyMatch = 1, Manual = 2, FromProposal = 3 }

    public enum SlPriceListStatus { Draft = 1, Approved = 2, Retired = 3 }

    public enum SlQuotationStatus { Draft = 1, Sent = 2, Accepted = 3, Rejected = 4, Expired = 5, Cancelled = 6 }

    public enum SlOrderStatus { Draft = 1, Confirmed = 2, PartlyDelivered = 3, Delivered = 4, Invoiced = 5, Closed = 6, Cancelled = 7 }
}
