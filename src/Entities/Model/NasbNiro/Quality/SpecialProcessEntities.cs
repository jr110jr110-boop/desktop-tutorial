using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.Quality
{
    // Package C (special processes and material verification): the master of special processes, the qualification of a process (procedure, equipment, operators, validity), the run record
    // with the parameters checked on the server, the approval of a supplier for a process, the material specification with its limits, and the rows of a structured EN 10204 certificate.
    // Records are never deleted physically: a qualification, a run, a supplier approval and a specification are voided or superseded with a reason.

    /// <summary>Draft and Submitted are editable by the maker only; Approved is the only state a run may use; later states never change.</summary>
    public enum SpQualStatus { Draft = 0, Submitted = 1, Approved = 2, Rejected = 3, Void = 4, Superseded = 5 }

    public enum SpRunResult { Pending = 0, Pass = 1, Fail = 2 }

    /// <summary>QA review of a run: None until a second person looked at the record and the chart.</summary>
    public enum SpReviewStatus { None = 0, Approved = 1, Rejected = 2 }

    public enum SpRunStatus { Recorded = 0, Void = 1 }

    /// <summary>What an equipment row of a qualification is: a machine (furnace, line, bath) or a measuring instrument (thermocouple, thickness gauge).</summary>
    public enum SpEquipmentKind { Machine = 1, Instrument = 2 }

    /// <summary>Temperature uniformity survey (TUS) and system accuracy test (SAT) of a furnace.</summary>
    public enum SpSurveyKind { None = 0, Tus = 1, Sat = 2 }

    public enum SpApprovalStatus { Pending = 0, Approved = 1, Rejected = 2, Void = 3, Superseded = 4 }
    public enum SpChangeStatus { Pending = 0, Approved = 1, Rejected = 2 }

    public enum MatSpecStatus { Draft = 0, Submitted = 1, Approved = 2, Superseded = 3, Void = 4 }

    public enum CertValueKind { Chemistry = 1, Mechanical = 2 }

    /// <summary>Server verdict of one row of a certificate (or of the whole certificate): NotJudged until the certificate has a material specification.</summary>
    public enum CertRowStatus { NotJudged = 0, Pass = 1, Fail = 2 }

    /// <summary>A kind of special process (heat treatment, nitriding, coating, brazing, plating, NDT). Editable list; a type is deactivated, never deleted.</summary>
    public class SpProcessType : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }

        /// <summary>The process needs a machine (furnace, bath, line) that is part of the qualification.</summary>
        public bool RequiresMachine { get; set; } = true;

        /// <summary>The machine needs a survey (TUS or SAT) that is valid on the day of the run.</summary>
        public bool RequiresSurvey { get; set; }

        /// <summary>The class of the measuring instrument the run needs (thermocouple, thickness gauge ...); null = no instrument is needed.</summary>
        public EquipmentType? RequiredInstrumentClass { get; set; }

        /// <summary>The qualified procedure of the process: a controlled document of the EDMS (the revision is chosen in the qualification).</summary>
        public int? ProcedureDeliverableId { get; set; }

        /// <summary>The process is controlled by the existing personnel qualification (NDT); the run gate of this package does not apply to it.</summary>
        public bool ExistingControl { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<SpParameter> Parameters { get; set; } = new List<SpParameter>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>
    /// Fix round 1 (H1): a change of a process type that WEAKENS a control (clears the machine, survey or instrument requirement, widens or removes a limit, deactivates the type) is requested here and applied only
    /// when a second person (QA manager or PMO, never the requester, never an AI seat) approves it. The requested content is kept as JSON; the row is kept for ever.
    /// </summary>
    public class SpTypeChange : BaseEntity
    {
        public int TypeId { get; set; }
        public SpProcessType Type { get; set; }
        public string PayloadJson { get; set; }
        public string Summary { get; set; }
        public string Reason { get; set; }
        public SpChangeStatus Status { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }
        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>A parameter of the process (temperature, time, rate, atmosphere, thickness ...) with the widest limits the company allows.</summary>
    public class SpParameter
    {
        public int Id { get; set; }
        public int TypeId { get; set; }
        public SpProcessType Type { get; set; }
        public int SortOrder { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }

        /// <summary>A run without a value for this parameter is incomplete.</summary>
        public bool Required { get; set; } = true;
    }

    /// <summary>
    /// The qualification of a special process: the procedure revision, the equipment (with survey and calibrated instruments), the qualified operators, the limits and the period. Prepared by a maker,
    /// approved by QA (never the maker or an editor, never an AI seat). The limits are copied into it, so a later change of the master does not move a qualified process.
    /// </summary>
    public class SpQualification : BaseEntity
    {
        public string Number { get; set; }
        public int TypeId { get; set; }
        public SpProcessType Type { get; set; }
        public string Title { get; set; }

        public int ProcedureDeliverableId { get; set; }
        public int ProcedureRevisionId { get; set; }

        /// <summary>The document number and the revision code at the time of the qualification (kept so a printed record stays readable).</summary>
        public string ProcedureNumber { get; set; }
        public string ProcedureRevisionCode { get; set; }

        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
        public SpQualStatus Status { get; set; }
        public string Note { get; set; }

        public DateTimeOffset? SubmittedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string RejectReason { get; set; }
        public string VoidReason { get; set; }
        public int? VoidedByUserId { get; set; }
        public DateTimeOffset? VoidedAt { get; set; }
        public int? SupersedesId { get; set; }

        /// <summary>Everybody who saved the qualification, ids between commas: none of them approves it.</summary>
        public string EditorUserIds { get; set; }

        public ICollection<SpQualLimit> Limits { get; set; } = new List<SpQualLimit>();
        public ICollection<SpQualEquipment> Equipments { get; set; } = new List<SpQualEquipment>();
        public ICollection<SpQualOperator> Operators { get; set; } = new List<SpQualOperator>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class SpQualLimit
    {
        public int Id { get; set; }
        public int QualificationId { get; set; }
        public SpQualification Qualification { get; set; }
        public int SortOrder { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public bool Required { get; set; } = true;
    }

    /// <summary>A machine (legacy Machine: furnace, line, bath) or a measuring instrument that the qualification covers; a machine row carries its survey.</summary>
    public class SpQualEquipment
    {
        public int Id { get; set; }
        public int QualificationId { get; set; }
        public SpQualification Qualification { get; set; }
        public SpEquipmentKind Kind { get; set; }
        public int? MachineId { get; set; }
        public int? EquipmentId { get; set; }
        public SpSurveyKind SurveyKind { get; set; }
        public DateTime? SurveyDate { get; set; }
        public DateTime? SurveyDueDate { get; set; }
        public Guid? SurveyDocumentId { get; set; }
        public string SurveyNote { get; set; }
    }

    /// <summary>A person (competence record of the quality module) who may run the process under this qualification.</summary>
    public class SpQualOperator
    {
        public int Id { get; set; }
        public int QualificationId { get; set; }
        public SpQualification Qualification { get; set; }
        public int PersonId { get; set; }
    }

    /// <summary>
    /// One run of a special process on a production operation (lot / serials), a legacy part operation or a subcontracted step. The parameters are judged on the server against the limits of the qualification
    /// (for a subcontracted run: against the master limits of the process). A failed run opens an NCR. A second person (QA) reviews the record and the chart.
    /// </summary>
    public class SpRun : BaseEntity
    {
        public string Number { get; set; }
        public int TypeId { get; set; }
        public SpProcessType Type { get; set; }
        public int? QualificationId { get; set; }
        public SpQualification Qualification { get; set; }

        public bool IsSubcontract { get; set; }
        public int? SupplierId { get; set; }
        public int? ReceiptDocumentId { get; set; }

        public int? OrderId { get; set; }
        public int? OperationId { get; set; }
        public int? PartOperationId { get; set; }
        public int? WorkOrderId { get; set; }
        public int? ItemId { get; set; }
        public int? LotId { get; set; }
        public string SerialNumbers { get; set; }
        public decimal Quantity { get; set; }

        public int? OperatorPersonId { get; set; }
        public int? MachineId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime EndedAt { get; set; }

        /// <summary>The furnace chart or the record of the line (an attachment).</summary>
        public Guid? ChartDocumentId { get; set; }
        public string ChartFileName { get; set; }

        /// <summary>A subcontracted run: the certificate of the supplier.</summary>
        public Guid? CertificateDocumentId { get; set; }
        public string CertificateNumber { get; set; }

        public SpRunResult Result { get; set; }
        public SpRunStatus Status { get; set; }
        public SpReviewStatus ReviewStatus { get; set; }
        public int? ReviewedByUserId { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
        public string ReviewNote { get; set; }
        public int? NcrId { get; set; }
        public string VoidReason { get; set; }
        public int? VoidedByUserId { get; set; }
        public DateTimeOffset? VoidedAt { get; set; }
        public string Note { get; set; }

        /// <summary>The verdict text computed by the server (the parameters that failed or are missing).</summary>
        public string ResultText { get; set; }

        public ICollection<SpRunValue> Values { get; set; } = new List<SpRunValue>();
        public ICollection<SpRunInstrument> Instruments { get; set; } = new List<SpRunInstrument>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class SpRunValue
    {
        public int Id { get; set; }
        public int RunId { get; set; }
        public SpRun Run { get; set; }
        public int SortOrder { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal Value { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public bool Pass { get; set; }
    }

    public class SpRunInstrument
    {
        public int Id { get; set; }
        public int RunId { get; set; }
        public SpRun Run { get; set; }
        public int EquipmentId { get; set; }
    }

    /// <summary>The approval of a supplier for a special process (the scope of the approved vendor list per process). Two persons; a validity period; evidence of the audit or accreditation.</summary>
    public class SpSupplierApproval : BaseEntity
    {
        public int ProviderId { get; set; }
        public int TypeId { get; set; }
        public SpProcessType Type { get; set; }
        public SpApprovalStatus Status { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
        public string AccreditationNumber { get; set; }
        public Guid? EvidenceDocumentId { get; set; }
        public DateTime? AuditDate { get; set; }
        public string Note { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string RejectReason { get; set; }
        public string VoidReason { get; set; }
        public int? VoidedByUserId { get; set; }
        public DateTimeOffset? VoidedAt { get; set; }
        public string EditorUserIds { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>
    /// A material specification (grade + standard): the limits of chemistry and mechanical properties, the EN 10204 type the grade needs and the items that are bought to it. Maintained by engineering,
    /// approved by a second person (QA or QC), versioned: a correction is a new version that supersedes the old one.
    /// </summary>
    public class MaterialSpec : BaseEntity
    {
        public string Grade { get; set; }
        public string Standard { get; set; }

        /// <summary>The normalised grade and standard (upper case, no spaces): one family of versions has one key.</summary>
        public string SpecKey { get; set; }
        public string Title { get; set; }
        public CertificateType RequiredCertificateType { get; set; }
        public int Version { get; set; } = 1;
        public MatSpecStatus Status { get; set; }
        public string Reason { get; set; }
        public int? SupersedesId { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string VoidReason { get; set; }
        public string ContentHash { get; set; }
        public string EditorUserIds { get; set; }

        public ICollection<MaterialSpecLimit> Limits { get; set; } = new List<MaterialSpecLimit>();
        public ICollection<MaterialSpecItem> Items { get; set; } = new List<MaterialSpecItem>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class MaterialSpecLimit
    {
        public int Id { get; set; }
        public int SpecId { get; set; }
        public MaterialSpec Spec { get; set; }
        public int SortOrder { get; set; }
        public CertValueKind Kind { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }

        /// <summary>A certificate without a value for this row is incomplete.</summary>
        public bool Required { get; set; } = true;
    }

    /// <summary>An item (coded material) that is bought to the specification; its required certificate type follows the specification.</summary>
    public class MaterialSpecItem
    {
        public int Id { get; set; }
        public int SpecId { get; set; }
        public MaterialSpec Spec { get; set; }
        public int ItemId { get; set; }
    }

    /// <summary>A chemistry or mechanical value of an EN 10204 certificate, with the limits and the verdict the server computed from the specification.</summary>
    public class CertificateValue
    {
        public int Id { get; set; }
        public int CertificateId { get; set; }
        public MaterialCertificate Certificate { get; set; }
        public int SortOrder { get; set; }
        public CertValueKind Kind { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal Value { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public CertRowStatus Status { get; set; }
    }
}
