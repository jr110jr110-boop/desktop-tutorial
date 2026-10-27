using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Providers;
using Entities.Model.NasbNiro.Quality;

namespace Entities.Model.NasbNiro.Inventory
{
    /// <summary>
    /// Incoming inspection of one receipt line held in quarantine (WI-WH-001, ISO 9001 8.6).
    /// Accepted quantity is released to its warehouse; rejected quantity opens an NCR.
    /// </summary>
    public class QcInspection : BaseEntity
    {
        public string Number { get; set; }

        public int ReceiptDocumentId { get; set; }
        public InventoryDocument ReceiptDocument { get; set; }

        public int ReceiptLineId { get; set; }

        public int ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public int? LotId { get; set; }
        public InventoryLot Lot { get; set; }

        /// <summary>The quarantine warehouse.</summary>
        public int WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; }

        public int? LocationId { get; set; }

        public decimal Quantity { get; set; }

        /// <summary>Serial numbers of the line, one per line of text.</summary>
        public string SerialNumbers { get; set; }

        /// <summary>Warehouse the accepted quantity goes to (chosen at receipt, may be changed by QC).</summary>
        public int? TargetWarehouseId { get; set; }
        public Warehouse TargetWarehouse { get; set; }

        public int? TargetLocationId { get; set; }

        public InspectionStatus Status { get; set; }
        public decimal AcceptedQuantity { get; set; }
        public decimal RejectedQuantity { get; set; }
        public string RejectedSerialNumbers { get; set; }

        /// <summary>Inspection report number (QC form).</summary>
        public string ReportNumber { get; set; }

        public Guid? ReportDocumentId { get; set; }
        public string Note { get; set; }

        public DateTimeOffset? InspectedAt { get; set; }
        public int? InspectedByUserId { get; set; }

        public int? ReleaseDocumentId { get; set; }
        public int? NcrId { get; set; }

        /// <summary>
        /// The NCR (rework or repair) this inspection re-inspects: the system makes the inspection task when the reworked product arrives in the QC-hold warehouse,
        /// and an accepted result links the inspection back to the NCR (R1q D4).
        /// </summary>
        public int? ReinspectionOfNcrId { get; set; }

        /// <summary>Measuring instruments used by the inspector, ids separated by commas ("3,5"); each had to be usable on the day (R1q D16).</summary>
        public string EquipmentIds { get; set; }

        /// <summary>The measured values of the inspection as JSON (characteristic, nominal, tolerances, value, unit, instrument, pass): the inspection is "measured" when this is not empty (review M12).</summary>
        public string MeasurementsJson { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>
    /// A request to relax a quality gate of an item (incoming inspection off, a lower certificate type, a longer shelf life): a second person (QA) must approve it before it
    /// applies, whether purchase orders of the item are open or not (R1q D11, review M8). A stricter control applies at once and is kept here as an approved row.
    /// The Old* columns hold the gates the item had when the request was made: the approval is refused when they have changed since.
    /// </summary>
    public class ItemQualityChange : BaseEntity
    {
        public int ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public bool RequiresIncomingInspection { get; set; }
        public CertificateType? RequiredCertificateType { get; set; }
        public int? ShelfLifeDays { get; set; }

        /// <summary>The gates of the item when the request was made (review M8): a decision is refused once the item no longer has these values.</summary>
        public bool OldRequiresIncomingInspection { get; set; }

        public CertificateType? OldRequiredCertificateType { get; set; }
        public int? OldShelfLifeDays { get; set; }

        public string Reason { get; set; }

        public ItemQualityChangeStatus Status { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public int? DecidedByUserId { get; set; }
        public string DecisionNote { get; set; }
    }

    /// <summary>Non-conformance report (گزارش عدم انطباق) per PR-QC-002 and form FO-QC-013.</summary>
    public class NonConformance : BaseEntity
    {
        public string Number { get; set; }
        public DateTime ReportDate { get; set; }
        public NcrSource Source { get; set; }
        public NcrStatus Status { get; set; }
        public NcrSeverity Severity { get; set; }

        public int ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public int? LotId { get; set; }
        public InventoryLot Lot { get; set; }

        public decimal Quantity { get; set; }

        /// <summary>Serial numbers, one per line of text.</summary>
        public string SerialNumbers { get; set; }

        /// <summary>Where the non-conforming product is held (normally the quarantine warehouse).</summary>
        public int? WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; }

        public int? LocationId { get; set; }

        public int? ProviderId { get; set; }
        public Provider Provider { get; set; }

        public int? WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        public int? ContractId { get; set; }
        public ContractEntity Contract { get; set; }

        public int? InspectionId { get; set; }
        public int? ReceiptDocumentId { get; set; }

        /// <summary>The product belongs to a subcontractor or supplier: a rejected piece goes back to them, not to scrap.</summary>
        public bool OwnedByContractor { get; set; }

        public string Requirement { get; set; }
        public string Description { get; set; }

        public NcrCauseCategory CauseCategory { get; set; }
        public string RootCause { get; set; }

        public NcrDisposition Disposition { get; set; }
        public string DispositionNote { get; set; }

        /// <summary>Major NCR: minutes of the technical committee and its members.</summary>
        public string CommitteeMinutesNumber { get; set; }
        public string CommitteeMembers { get; set; }

        // approvals of the disposition
        public DateTimeOffset? QualityApprovedAt { get; set; }
        public int? QualityApprovedByUserId { get; set; }
        public DateTimeOffset? EngineeringApprovedAt { get; set; }
        public int? EngineeringApprovedByUserId { get; set; }
        public DateTimeOffset? ManufacturingApprovedAt { get; set; }
        public int? ManufacturingApprovedByUserId { get; set; }

        /// <summary>Concession (مجوز ارفاقی), issued by the deputy for design and manufacturing.</summary>
        public string ConcessionNumber { get; set; }
        public DateTimeOffset? ConcessionApprovedAt { get; set; }
        public int? ConcessionApprovedByUserId { get; set; }

        /// <summary>The contract needs the customer to accept deviations (for example API 611/612): the concession needs the customer's approval.</summary>
        public bool CustomerApprovalRequired { get; set; }
        public string CustomerApprovalReference { get; set; }

        public int? TargetWarehouseId { get; set; }
        public int? ResultDocumentId { get; set; }
        public DateTimeOffset? ExecutedAt { get; set; }
        public int? ExecutedByUserId { get; set; }

        public string CorrectiveAction { get; set; }
        public string CorrectiveActionOwner { get; set; }
        public DateTime? CorrectiveActionDueDate { get; set; }
        public string EffectivenessCheck { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public int? ClosedByUserId { get; set; }

        // ----- quality module additions (PR-QC-002 / spec 5.5)
        /// <summary>Date the decision is due: 7 days for a minor, 14 days for a major NCR (company settings).</summary>
        public DateTime? DueDate { get; set; }

        public RootCauseMethod RootCauseMethod { get; set; }

        /// <summary>The corrective action record; a major NCR gets one drafted by the system.</summary>
        public int? CapaId { get; set; }
        public Capa Capa { get; set; }

        public decimal? CostImpact { get; set; }
        public int? TimeImpactDays { get; set; }
        public string CustomerReturnRef { get; set; }

        /// <summary>Measuring instrument found out of tolerance that caused this NCR.</summary>
        public int? AffectedEquipmentId { get; set; }

        /// <summary>Record or report the system drafted this NCR from (QIR-/TR- number).</summary>
        public string SourceRecordNumber { get; set; }

        // ----- R1q: maker-checker, evidence of execution, re-inspection
        /// <summary>Every user who edited the NCR, ids between commas (",5,7,"): none of them may sign for QC or close it (R1q D3).</summary>
        public string EditorUserIds { get; set; }

        /// <summary>What the re-inspection after a rework or repair is: QcInspection (product in the QC-hold warehouse), QcRecord or TestReport (R1q D4).</summary>
        public string ReinspectionKind { get; set; }

        /// <summary>The accepted re-inspection; closing a rework or repair NCR needs it.</summary>
        public int? ReinspectionRecordId { get; set; }

        /// <summary>Scrap evidence: the confirmed warehouse document that took the product to scrap (R1q D5).</summary>
        public int? ScrapDocumentId { get; set; }

        /// <summary>Scrap evidence for a serial item: the serial unit whose status is Scrapped (first unit of the NCR).</summary>
        public int? ScrappedSerialUnitId { get; set; }

        /// <summary>Production rework operation (MfOperation, IsRework) that did the rework; filled by the production module.</summary>
        public int? ReworkOperationId { get; set; }

        public string CancelReason { get; set; }
        public DateTimeOffset? CancelledAt { get; set; }
        public int? CancelledByUserId { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>
    /// Material request (درخواست کالا): production asks, planning (PMO) and the manufacturing manager approve.
    /// The warehouse issues only against an approved request and never more than what is left of it.
    /// </summary>
    public class MaterialRequest : BaseEntity
    {
        public string Number { get; set; }
        public DateTime RequestDate { get; set; }
        public MaterialRequestStatus Status { get; set; }

        /// <summary>Project / work order (cost centre). Empty only for general use (maintenance, overhead) with a written purpose.</summary>
        public int? WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        /// <summary>Warehouse the material is wanted from (optional).</summary>
        public int? WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; }

        public string Purpose { get; set; }

        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset? PlanningApprovedAt { get; set; }
        public int? PlanningApprovedByUserId { get; set; }
        public DateTimeOffset? ManufacturingApprovedAt { get; set; }
        public int? ManufacturingApprovedByUserId { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public int? RejectedByUserId { get; set; }
        public string RejectReason { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public int? ClosedByUserId { get; set; }

        public ICollection<MaterialRequestLine> Lines { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class MaterialRequestLine
    {
        public int Id { get; set; }

        public int MaterialRequestId { get; set; }
        public MaterialRequest MaterialRequest { get; set; }

        public int RowNumber { get; set; }

        public int ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public decimal Quantity { get; set; }

        /// <summary>Kept in step with the confirmed issues made against the request.</summary>
        public decimal IssuedQuantity { get; set; }

        public string Description { get; set; }
    }

    /// <summary>Stock count (شمارش موجودی): a snapshot of the balances, the counted quantities and the approved differences.</summary>
    public class StockCount : BaseEntity
    {
        public string Number { get; set; }
        public StockCountKind Kind { get; set; }
        public StockCountStatus Status { get; set; }
        public DateTime CountDate { get; set; }

        public int WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; }

        /// <summary>Only this item group (code prefix), for a cycle count.</summary>
        public string GroupCode { get; set; }

        public int? LocationId { get; set; }

        public string Description { get; set; }

        public DateTimeOffset SnapshotAt { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset? WarehouseApprovedAt { get; set; }
        public int? WarehouseApprovedByUserId { get; set; }
        public DateTimeOffset? PlanningApprovedAt { get; set; }
        public int? PlanningApprovedByUserId { get; set; }

        /// <summary>A count in a quarantine, scrap or contractor warehouse also needs a QC or QA user who did not count or approve it (R1q D31, review M10).</summary>
        public DateTimeOffset? QualityApprovedAt { get; set; }
        public int? QualityApprovedByUserId { get; set; }
        public DateTimeOffset? PostedAt { get; set; }
        public int? PostedByUserId { get; set; }

        public int? IncreaseDocumentId { get; set; }
        public int? DecreaseDocumentId { get; set; }

        public ICollection<StockCountLine> Lines { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class StockCountLine
    {
        public int Id { get; set; }

        public int StockCountId { get; set; }
        public StockCount StockCount { get; set; }

        public int ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public int? LocationId { get; set; }
        public WarehouseLocation Location { get; set; }

        public int? LotId { get; set; }
        public InventoryLot Lot { get; set; }

        public decimal SystemQuantity { get; set; }
        public decimal? CountedQuantity { get; set; }

        /// <summary>Second count of a line with a difference (recount before approval).</summary>
        public decimal? RecountedQuantity { get; set; }

        public string Note { get; set; }
        public int? CountedByUserId { get; set; }
    }
}
