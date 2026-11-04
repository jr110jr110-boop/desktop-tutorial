using System;

namespace Entities.Model.NasbNiro.Attachments
{
    /// <summary>
    /// PB3: the record a file is attached to. The numbers are stored; do not renumber.
    /// Legacy owners (contract, tender, buy order, process, equipment process) are here so that a file removed from them is kept as a voided attachment instead of being deleted.
    /// </summary>
    public enum AttachmentOwnerType
    {
        PurchaseRequest = 1,
        PurchaseRequestLine = 2,
        Inquiry = 3,
        Quote = 4,
        PurchaseOrder = 5,
        /// <summary>A warehouse document: the temporary receipt (purchase receipt into quarantine) and the final one (QC release), or any other document.</summary>
        WarehouseDocument = 6,
        QcInspection = 7,
        Ncr = 8,
        /// <summary>One operation (step) of a production order (MES); also carries the files of its QC decision.</summary>
        ProductionOperation = 9,
        /// <summary>The traceability operation of a serial or lot (the old PartOperationFile).</summary>
        PartOperation = 10,
        SupplierInvoice = 11,
        PaymentRequest = 12,
        TreasuryPayment = 13,
        /// <summary>A cost entry written by hand in project control.</summary>
        ProjectCostEntry = 14,
        Contract = 15,
        Tender = 16,
        PettyCashSettlementLine = 17,
        InventoryItem = 18,
        InventoryLot = 19,
        SerialUnit = 20,
        BuyOrder = 21,
        Process = 22,
        EquipmentProcess = 23,
    }

    public enum AttachmentKind
    {
        Drawing = 1,
        Bom = 2,
        Specification = 3,
        Datasheet = 4,
        QcReport = 5,
        Certificate = 6,
        MeasurementSheet = 7,
        Minutes = 8,
        /// <summary>The supplier's invoice or a receipt: carries prices, so QC and the other technical readers never see it.</summary>
        Invoice = 9,
        /// <summary>Bank slip of a payment: carries amounts, same rule as the invoice.</summary>
        PaymentSlip = 10,
        /// <summary>Delivery note, waybill (بارنامه).</summary>
        DeliveryNote = 11,
        Photo = 12,
        /// <summary>A form of the company (tool damage form, delivery to contractor form, petty-cash forms).</summary>
        Form = 13,
        Other = 99,
    }

    /// <summary>
    /// PB3: one file attached to one record. A record has many. The bytes live in <see cref="Document"/> (SHA-256 kept there); this row says what the file is, who put it there and what became of it.
    /// A file is never deleted: before the owner is approved the uploader may remove the row (<see cref="BaseEntity.Deleted"/>, logged); afterwards it is voided with a reason or superseded by a newer file.
    /// Whether the attachment is locked is not stored: it is derived from the state of the owner each time.
    /// </summary>
    public class RecordAttachment : BaseEntity
    {
        public AttachmentOwnerType OwnerType { get; set; }
        public int OwnerId { get; set; }

        public Guid DocumentId { get; set; }
        public AttachmentKind Kind { get; set; }

        public string Title { get; set; }

        /// <summary>The revision of the document, as the company writes it ("A", "02").</summary>
        public string Revision { get; set; }

        public string FileName { get; set; }
        public long SizeBytes { get; set; }

        public int UploadedByUserId { get; set; }
        public DateTimeOffset UploadedAt { get; set; }

        public bool Voided { get; set; }
        public int? VoidedByUserId { get; set; }
        public DateTimeOffset? VoidedAt { get; set; }
        public string VoidReason { get; set; }

        /// <summary>The newer attachment (same owner) that replaced this one; this one stays in the history.</summary>
        public int? SupersededById { get; set; }

        /// <summary>Navigation for <see cref="SupersededById"/>: the new row and the link on the old one are saved together, in one transaction.</summary>
        public RecordAttachment SupersededBy { get; set; }

        /// <summary>The uploader removed it while the owner was not approved yet (the row is hidden, the file and the log stay).</summary>
        public DateTimeOffset? RemovedAt { get; set; }
        public int? RemovedByUserId { get; set; }
    }
}
