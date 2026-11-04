using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.Finance
{
    // PB6: the petty-cash flow of the company procedure (request -> approval -> top-up; the summary FO-AC-005-07 built from the lines of a period with the evidence the procedure asks for).
    // The money itself (the holder, the balance, the settlement and its postings) stays in FnPettyCash / FnPettyCashSettlement; these tables are the request and the document flow around it.

    public enum FnPettyRequestStatus
    {
        Draft = 0,
        Submitted = 1,
        /// <summary>The accountant checked it.</summary>
        Controlled = 2,
        /// <summary>The finance manager approved the amount.</summary>
        Approved = 3,
        /// <summary>The top-up was paid and the balance raised.</summary>
        ToppedUp = 4,
        Rejected = 9,
    }

    public class FnPettyCashRequest : BaseEntity
    {
        public string Number { get; set; }
        public int PettyCashId { get; set; }
        public FnPettyCash PettyCash { get; set; }
        public FnPettyRequestStatus Status { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; }
        public DateTime RequestDate { get; set; }

        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset? ControlledAt { get; set; }
        public int? ControlledByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ToppedUpAt { get; set; }
        public int? ToppedUpByUserId { get; set; }
        public string PaymentReference { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public int? RejectedByUserId { get; set; }
        public string RejectReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public enum FnSummaryStatus
    {
        Draft = 0,
        /// <summary>The holder signed.</summary>
        Submitted = 1,
        /// <summary>Accounting control done.</summary>
        Controlled = 2,
        /// <summary>The finance manager approved.</summary>
        Approved = 3,
        /// <summary>The managing director gave the final approval.</summary>
        Final = 4,
        Returned = 5,
        Rejected = 9,
    }

    /// <summary>صورت‌خلاصه هزینه‌های تنخواه FO-AC-005-07.</summary>
    public class FnPettyCashSummary : BaseEntity
    {
        /// <summary>The serial number of the summary (the archive number of the paper form).</summary>
        public string Number { get; set; }
        public int PettyCashId { get; set; }
        public FnPettyCash PettyCash { get; set; }
        public DateTime Date { get; set; }
        public DateTime PeriodFrom { get; set; }
        public DateTime PeriodTo { get; set; }
        public FnSummaryStatus Status { get; set; }

        /// <summary>The number of invoices and receipts attached (the paper originals the holder hands over).</summary>
        public int AttachmentsCount { get; set; }
        public decimal PreviousBalance { get; set; }
        public decimal Charge { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal Balance { get; set; }
        public string TotalInWords { get; set; }
        public string Notes { get; set; }

        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset? ControlledAt { get; set; }
        public int? ControlledByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? FinalAt { get; set; }
        public int? FinalByUserId { get; set; }
        public string ReturnReason { get; set; }
        public int? SettlementId { get; set; }
        public string EditorUserIds { get; set; }

        public ICollection<FnPettyCashSummaryLine> Lines { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public enum FnPettyChannel
    {
        PettyCash = 1,
        Direct = 2,
    }

    public enum FnPettyDocType
    {
        Invoice = 1,
        Payment = 2,
        Receipt = 3,
    }

    public enum FnPettyExpenseKind
    {
        Goods = 1,
        Service = 2,
        /// <summary>Freight / courier / taxi: needs the vehicle form.</summary>
        Freight = 3,
        /// <summary>Bank fees of the period, summed in one row.</summary>
        BankFees = 4,
        /// <summary>No invoice can be had: the miscellaneous expense form.</summary>
        Miscellaneous = 5,
    }

    public enum FnPettyGoodsClass
    {
        None = 0,
        Consumable = 1,
        Tool = 2,
        /// <summary>Raw material, standard parts, final part: a temporary receipt, then QC, then the final receipt.</summary>
        QcRequired = 3,
    }

    public enum FnPettyPayKind
    {
        BankTransfer = 1,
        Pos = 2,
        Online = 3,
        Cash = 4,
        /// <summary>A carrier's waybill (بیجک).</summary>
        Waybill = 5,
        /// <summary>Snapp / courier digital receipt.</summary>
        DigitalRide = 6,
    }

    public class FnPettyCashSummaryLine
    {
        public int Id { get; set; }
        public int SummaryId { get; set; }
        public FnPettyCashSummary Summary { get; set; }
        public int RowNumber { get; set; }
        public DateTime? Date { get; set; }
        public string InvoiceNumber { get; set; }

        /// <summary>The number of the approved purchase request (goods), or "*" (services).</summary>
        public string PrApprovalNumber { get; set; }

        /// <summary>The number of the warehouse receipt (goods), or "*" (services).</summary>
        public string WarehouseReceiptNumber { get; set; }

        /// <summary>The project code, or «عمومی».</summary>
        public string ProjectCode { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
        public FnPettyChannel Channel { get; set; }
        public FnPettyDocType DocType { get; set; }
        public FnPettyExpenseKind ExpenseKind { get; set; }
        public FnPettyGoodsClass GoodsClass { get; set; }
        public FnPettyPayKind PayKind { get; set; }

        /// <summary>The name of the receiver in the bank slip (bank transfers).</summary>
        public string RecipientName { get; set; }

        /// <summary>The tracking code of an online payment.</summary>
        public string TrackingNumber { get; set; }

        /// <summary>The line of the settlement (the accounting side) this row came from.</summary>
        public int? SettlementLineId { get; set; }
        public int? PurchaseRequestId { get; set; }
        public int? ReceiptDocumentId { get; set; }
    }

    /// <summary>The vehicle form FO-HR-025-02 of a freight row: date, unit, vehicle type, origin, destination, description; the requester is the signature.</summary>
    public class FnPettyCashVehicleForm
    {
        public int Id { get; set; }
        public int LineId { get; set; }
        public FnPettyCashSummaryLine Line { get; set; }
        public DateTime Date { get; set; }
        public string Unit { get; set; }
        public string VehicleType { get; set; }
        public string Origin { get; set; }
        public string Destination { get; set; }
        public string Description { get; set; }
        public int RequestedByUserId { get; set; }
    }

    /// <summary>The miscellaneous expense form FO-AC-003-02 (an expense that has no invoice): signed by the requesting unit's manager, with the project code.</summary>
    public class FnPettyCashMiscForm
    {
        public int Id { get; set; }
        public int LineId { get; set; }
        public FnPettyCashSummaryLine Line { get; set; }
        public string RequestingUnit { get; set; }
        public string ProjectCode { get; set; }
        public string WhyNoInvoice { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; }
        public int RequestedByUserId { get; set; }

        /// <summary>The manager of the requesting unit who signed the form (not the holder).</summary>
        public int? UnitManagerUserId { get; set; }
        public DateTimeOffset? SignedAt { get; set; }
    }
}
