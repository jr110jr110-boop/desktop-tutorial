using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Providers;

namespace Entities.Model.NasbNiro.Procurement
{
    // Package 6 (PB6): the rest of procurement. New tables start with "Proc"; the changes to existing tables are only new columns.
    // The company templates behind it: the purchase contract FO-IP-040-04 (payment 30/30/30/10, +-25 % volume, guarantee cheque), the petty-cash procedure.

    public enum PurchaseRequestPriority
    {
        Low = 1,
        Normal = 2,
        High = 3,
        Urgent = 4,
    }

    /// <summary>
    /// The form (فرم) of a request. Each form has its own fields: goods (project / miscellaneous), service (project / general), vehicle (the form FO-HR-025-02: date, unit,
    /// vehicle type, origin, destination), contract (project / consulting / services / general) and the progress statement. The form decides the old <see cref="PurchaseKind"/>.
    /// </summary>
    public enum PurchaseFormType
    {
        GoodsProject = 1,
        GoodsMiscellaneous = 2,
        ServiceProject = 3,
        ServiceGeneral = 4,
        VehicleProject = 5,
        VehicleGeneral = 6,
        ContractProject = 7,
        ContractConsulting = 8,
        ContractServices = 9,
        ContractGeneral = 10,
        ProgressStatement = 11,
    }

    /// <summary>A frozen revision of an approved purchase request (like <see cref="PurchaseOrderRevision"/>): append only.</summary>
    public class PurchaseRequestRevision
    {
        public int Id { get; set; }

        public int PurchaseRequestId { get; set; }
        public PurchaseRequest PurchaseRequest { get; set; }

        /// <summary>The number of the frozen revision (0 for the original).</summary>
        public int Revision { get; set; }
        public string Reason { get; set; }
        public PurchaseRequestStatus StatusWhenFrozen { get; set; }
        public decimal EstimatedAmount { get; set; }
        public string SnapshotJson { get; set; }
        public string Hash { get; set; }
        public DateTimeOffset FrozenAt { get; set; }
        public int FrozenByUserId { get; set; }
        public DateTimeOffset? PlanningApprovedAt { get; set; }
        public int? PlanningApprovedByUserId { get; set; }
        public DateTimeOffset? ManagementApprovedAt { get; set; }
        public int? ManagementApprovedByUserId { get; set; }
    }

    public enum ProcArchiveOwner
    {
        PurchaseRequest = 1,
        Inquiry = 2,
        PurchaseOrder = 3,
        SupplierInvoice = 4,
        PaymentRequest = 5,
    }

    /// <summary>Archive state of one procurement document (never deleted): archived with a reason, restored with a reason; one row per document.</summary>
    public class ProcArchiveRecord
    {
        public int Id { get; set; }
        public ProcArchiveOwner OwnerType { get; set; }
        public int OwnerId { get; set; }
        public bool IsArchived { get; set; }
        public DateTimeOffset ArchivedAt { get; set; }
        public int ArchivedByUserId { get; set; }
        public string ArchiveReason { get; set; }
        public DateTimeOffset? RestoredAt { get; set; }
        public int? RestoredByUserId { get; set; }
        public string RestoreReason { get; set; }

        /// <summary>How many times the document was archived (the history of each time is in the audit trail).</summary>
        public int Times { get; set; }
    }

    /// <summary>The bridge from a legacy buy order (BuyOrder) to the new chain: one row per converted buy order.</summary>
    public class ProcBuyOrderBridge : BaseEntity
    {
        public int BuyOrderId { get; set; }
        public int PurchaseRequestId { get; set; }
        public PurchaseRequest PurchaseRequest { get; set; }
        public int? PurchaseOrderId { get; set; }
        public PurchaseOrder PurchaseOrder { get; set; }
        public string Note { get; set; }
    }

    // --------------------------------------------------------------------------------------------- supplier contracts (addenda, guarantees, payment terms)

    public enum AddendumKind
    {
        Volume = 1,
        Price = 2,
        /// <summary>Delivery date / extension of the contract time.</summary>
        Time = 3,
        Mixed = 4,
    }

    public enum AddendumStatus
    {
        Draft = 0,
        /// <summary>Sent for approval.</summary>
        Submitted = 1,
        /// <summary>Approved and applied to the order.</summary>
        Applied = 2,
        Rejected = 9,
    }

    /// <summary>الحاقیه: a change to a running supplier contract (volume within the template limit, price, time) with approvals; applied to the order only when approved.</summary>
    public class PurchaseOrderAddendum : BaseEntity
    {
        public string Number { get; set; }
        public int PurchaseOrderId { get; set; }
        public PurchaseOrder PurchaseOrder { get; set; }
        public int Sequence { get; set; }
        public AddendumKind Kind { get; set; }
        public AddendumStatus Status { get; set; }
        public string Reason { get; set; }

        public decimal AmountBefore { get; set; }
        public decimal AmountAfter { get; set; }
        public DateTime? DeliveryDateBefore { get; set; }
        public DateTime? DeliveryDateAfter { get; set; }

        /// <summary>The largest volume change in percent among the lines (absolute); the template limit is a setting.</summary>
        public decimal MaxVolumeChangePercent { get; set; }
        public bool NeedsCeo { get; set; }

        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset? OrderApprovedAt { get; set; }
        public int? OrderApprovedByUserId { get; set; }
        public DateTimeOffset? CeoApprovedAt { get; set; }
        public int? CeoApprovedByUserId { get; set; }
        public DateTimeOffset? AppliedAt { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public int? RejectedByUserId { get; set; }
        public string RejectReason { get; set; }

        public ICollection<PurchaseOrderAddendumLine> Lines { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class PurchaseOrderAddendumLine
    {
        public int Id { get; set; }
        public int AddendumId { get; set; }
        public PurchaseOrderAddendum Addendum { get; set; }
        public int OrderLineId { get; set; }
        public PurchaseOrderLine OrderLine { get; set; }
        public decimal QuantityBefore { get; set; }
        public decimal QuantityAfter { get; set; }
        public decimal UnitPriceBefore { get; set; }
        public decimal UnitPriceAfter { get; set; }
    }

    public enum GuaranteeKind
    {
        Cheque = 1,
        BankGuarantee = 2,
        PromissoryNote = 3,
    }

    public enum GuaranteePurpose
    {
        Advance = 1,
        Performance = 2,
        Retention = 3,
        Other = 9,
    }

    public enum SupplierGuaranteeStatus
    {
        Active = 0,
        ReleaseRequested = 1,
        Released = 2,
        Expired = 3,
        Claimed = 4,
    }

    /// <summary>Register of the guarantees a supplier or contractor gave (cheque, bank guarantee, promissory note) with expiry alerts and a controlled release.</summary>
    public class SupplierGuarantee : BaseEntity
    {
        public string Number { get; set; }
        public int ProviderId { get; set; }
        public Provider Provider { get; set; }
        public int? PurchaseOrderId { get; set; }
        public PurchaseOrder PurchaseOrder { get; set; }
        public GuaranteeKind Kind { get; set; }
        public GuaranteePurpose Purpose { get; set; }
        public decimal Amount { get; set; }
        public string GuaranteeNumber { get; set; }
        public string Issuer { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public SupplierGuaranteeStatus Status { get; set; }

        /// <summary>The conditions under which the guarantee is released (for example: after the final tests and the documents).</summary>
        public string ReleaseConditions { get; set; }
        public DateTimeOffset? ReleaseRequestedAt { get; set; }
        public int? ReleaseRequestedByUserId { get; set; }
        public string ReleaseReason { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public int? ReleasedByUserId { get; set; }
        public DateTimeOffset? LastAlertAt { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public enum PaymentTermKind
    {
        /// <summary>پیش‌پرداخت: after the order is released (and the guarantee given).</summary>
        Advance = 1,
        /// <summary>After delivery and the QC approval.</summary>
        AfterDeliveryQc = 2,
        /// <summary>Cheque after the invoice (default 60 days).</summary>
        ChequeAfterInvoice = 3,
        /// <summary>Retention (حسن انجام کار): after the documents are delivered.</summary>
        RetentionAfterDocuments = 4,
        Other = 9,
    }

    public enum PaymentTermStatus
    {
        Pending = 0,
        PaymentRequested = 1,
        Paid = 2,
    }

    /// <summary>A payment milestone of the contract (template 30/30/30/10) that drives the payment requests.</summary>
    public class PurchaseOrderPaymentTerm
    {
        public int Id { get; set; }
        public int PurchaseOrderId { get; set; }
        public PurchaseOrder PurchaseOrder { get; set; }
        public int RowNumber { get; set; }
        public PaymentTermKind Kind { get; set; }
        public decimal Percent { get; set; }
        public decimal Amount { get; set; }

        /// <summary>For the cheque after the invoice: the days after the invoice (60 in the template).</summary>
        public int? DueDays { get; set; }
        public string Condition { get; set; }
        public PaymentTermStatus Status { get; set; }
        public int? PaymentRequestId { get; set; }

        /// <summary>Retention: somebody other than the buyer confirmed that the documents were delivered.</summary>
        public DateTimeOffset? ConditionConfirmedAt { get; set; }
        public int? ConditionConfirmedByUserId { get; set; }
    }
}
