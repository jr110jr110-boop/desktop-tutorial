using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.ProductBank
{
    // Package A1 (design and development control, ISO 9001 8.3.2 to 8.3.4, ISO 10007 6.5 FCA): the design plan of a product revision, the register of its design inputs, the design reviews
    // (review, verification, validation) with the traceability matrix input -> output -> evidence, the findings of a review as actions, and the gates that rest on a closed verification.
    // Records are never deleted physically: a plan is superseded or voided, an input is replaced or voided, a review is voided before it is closed, a finding is voided with a reason.

    /// <summary>Draft and Submitted are the preparer's; Approved is the plan in force of the product revision; a new version supersedes the old one.</summary>
    public enum DesignPlanStatus { Draft = 0, Submitted = 1, Approved = 2, Superseded = 3, Void = 4 }

    /// <summary>The stages of design and development (ISO 9001 8.3.2): inputs, design, review, verification, validation, release.</summary>
    public enum DesignStageKind { Inputs = 1, Design = 2, DesignReview = 3, Verification = 4, Validation = 5, Release = 6 }

    public enum DesignStageStatus { NotStarted = 0, InProgress = 1, Done = 2, NotRequired = 3 }

    /// <summary>The kinds of design input of ISO 9001 8.3.3 (a to e) plus the customer specification.</summary>
    public enum DesignInputKind { Functional = 1, Performance = 2, Statutory = 3, Standard = 4, PreviousDesign = 5, CustomerSpec = 6, FailureConsequence = 7 }

    /// <summary>Open is in force; Conflict blocks the plan's progress until a second person resolves it; Superseded was replaced by a newer input; Void was withdrawn with a reason.</summary>
    public enum DesignInputStatus { Open = 0, Conflict = 1, Superseded = 2, Void = 3 }

    public enum DesignReviewType { Review = 1, Verification = 2, Validation = 3 }

    /// <summary>Planned (scope being set), Held (the meeting took place, findings and evidence are recorded), Closed (outcome given, never changes), Void (before it was closed).</summary>
    public enum DesignReviewStatus { Planned = 0, Held = 1, Closed = 2, Void = 3 }

    public enum DesignReviewOutcome { None = 0, Accepted = 1, AcceptedWithConditions = 2, Rejected = 3 }

    /// <summary>What a review looks at: a revision of an EDMS drawing or document, a BOM revision, an OPC routing revision.</summary>
    public enum DesignOutputKind { Drawing = 1, Bom = 2, Opc = 3 }

    public enum DesignTraceResult { Pending = 0, Passed = 1, Failed = 2 }

    /// <summary>Open (action not done), Done (the owner says it is done), Closed (a different person checked it), Void (withdrawn with a reason by a third person).</summary>
    public enum DesignFindingStatus { Open = 0, Done = 1, Closed = 2, Void = 3 }

    public enum DesignValidationMethod { None = 0, Test = 1, Trial = 2, Simulation = 3 }
    public enum DesignAdoptionStatus { Requested = 0, Accepted = 1, Void = 2 }

    /// <summary>The design plan of a product revision. A new version copies the plan, is approved by a different person than its preparer and supersedes the old one.</summary>
    public class PbDesignPlan : BaseEntity
    {
        public int ProductId { get; set; }
        public PbProduct Product { get; set; }

        /// <summary>The revision of the product design; the same numbering as the revision of the product's BOM (0, 1, 2 ...).</summary>
        public int ProductRevision { get; set; }

        /// <summary>1, 2, 3 ... one per new version of the plan of the same product revision.</summary>
        public int Version { get; set; }

        /// <summary>DP-1405-00001.</summary>
        public string Number { get; set; }

        public string Title { get; set; }
        public string Scope { get; set; }
        public DesignPlanStatus Status { get; set; }

        /// <summary>The validation of the design is required for this product (ISO 9001 8.3.4 f). Off needs a reason and the approval of QA.</summary>
        public bool ValidationRequired { get; set; } = true;

        public string ValidationWaiverReason { get; set; }

        public int PreparedByUserId { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string ReturnNote { get; set; }

        /// <summary>The inputs of the product revision are complete: stated by a person other than the preparer. Every kind has an input or is declared not applicable with a note.</summary>
        public int? InputsCompleteByUserId { get; set; }
        public DateTimeOffset? InputsCompleteAt { get; set; }
        public string InputsCompleteNote { get; set; }

        /// <summary>Comma list of the input kinds (numbers) that do not apply to this product.</summary>
        public string NotApplicableKinds { get; set; }

        public int? SupersedesPlanId { get; set; }
        public DateTimeOffset? SupersededAt { get; set; }
        public string VoidReason { get; set; }

        /// <summary>SHA-256 of the content (stages, responsibilities, dates, interfaces) at the submission.</summary>
        public string ContentHash { get; set; }

        public ICollection<PbDesignStage> Stages { get; set; } = new List<PbDesignStage>();
        public ICollection<PbDesignInterface> Interfaces { get; set; } = new List<PbDesignInterface>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>One stage of a plan with its responsibility (a role and a person) and its planned dates.</summary>
    public class PbDesignStage : BaseEntity
    {
        public int PlanId { get; set; }
        public PbDesignPlan Plan { get; set; }
        public int Seq { get; set; }
        public DesignStageKind Kind { get; set; }
        public string Title { get; set; }
        public string ResponsibleRole { get; set; }
        public int? ResponsibleUserId { get; set; }
        public DateTime? PlannedStart { get; set; }
        public DateTime? PlannedEnd { get; set; }
        public DesignStageStatus Status { get; set; }
        public DateTime? ActualEnd { get; set; }
        public int? CompletedByUserId { get; set; }
        public string Note { get; set; }
    }

    /// <summary>An interface between the groups that take part in the design (ISO 9001 8.3.2 e): what is handed over, by whom, to whom.</summary>
    public class PbDesignInterface : BaseEntity
    {
        public int PlanId { get; set; }
        public PbDesignPlan Plan { get; set; }
        public string FromGroup { get; set; }
        public string ToGroup { get; set; }
        public string Subject { get; set; }
        public string ResponsibleRole { get; set; }
        public int? ResponsibleUserId { get; set; }
    }

    /// <summary>One design input of a product revision (a requirement the design must meet). Unique code; source and acceptance criterion are required.</summary>
    public class PbDesignInput : BaseEntity
    {
        public int ProductId { get; set; }
        public PbProduct Product { get; set; }
        public int ProductRevision { get; set; }

        /// <summary>DI-1405-00001.</summary>
        public string Code { get; set; }

        public DesignInputKind Kind { get; set; }
        public string Statement { get; set; }
        public string Source { get; set; }
        public string AcceptanceCriterion { get; set; }

        /// <summary>For a standard or code: its designation and edition (ISO 9001:2015, ASME VIII Div.1 2023 ...).</summary>
        public string StandardEdition { get; set; }

        /// <summary>For an input taken from a previous similar design: which design (a plan number, a product code, a document).</summary>
        public string PreviousDesignRef { get; set; }

        /// <summary>Reference to a customer specification or contract: the EDMS document and its revision, or a text reference with its revision.</summary>
        public int? RefDeliverableId { get; set; }
        public int? RefRevisionId { get; set; }
        public string RefText { get; set; }
        public string RefRevisionText { get; set; }

        /// <summary>The potential consequence of a failure of what this input protects (ISO 9001 8.3.3 e).</summary>
        public string FailureConsequence { get; set; }

        public DesignInputStatus Status { get; set; }

        public int? ConflictWithInputId { get; set; }
        public string ConflictNote { get; set; }
        public int? ConflictMarkedByUserId { get; set; }
        public DateTimeOffset? ConflictMarkedAt { get; set; }
        public int? ConflictResolvedByUserId { get; set; }
        public DateTimeOffset? ConflictResolvedAt { get; set; }
        public string ConflictResolution { get; set; }

        public int? SupersedesInputId { get; set; }
        public string VoidReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>A design review, verification or validation of a product revision with its scope (inputs, outputs), participants, findings, traceability and outcome.</summary>
    public class PbDesignReview : BaseEntity
    {
        public int PlanId { get; set; }
        public PbDesignPlan Plan { get; set; }
        public int ProductId { get; set; }
        public int ProductRevision { get; set; }

        /// <summary>DR-1405-00001.</summary>
        public string Number { get; set; }

        public DesignReviewType Type { get; set; }
        public DesignStageKind Stage { get; set; }
        public string Title { get; set; }
        public DateTime? ScheduledDate { get; set; }
        public DesignReviewStatus Status { get; set; }

        public DateTime? HeldDate { get; set; }
        public int? HeldByUserId { get; set; }
        public DateTimeOffset? HeldAt { get; set; }
        public string Minutes { get; set; }

        public DesignReviewOutcome Outcome { get; set; }
        public string OutcomeNote { get; set; }
        public int? ClosedByUserId { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }

        /// <summary>The review this one repeats after a rejection or conditions.</summary>
        public int? PreviousReviewId { get; set; }
        public string VoidReason { get; set; }

        // validation record (type Validation)
        public DesignValidationMethod ValidationMethod { get; set; }

        /// <summary>What was tested, tried or simulated on what (the product or its serial, the test report, the model and tool), and the result.</summary>
        public string ValidationEvidence { get; set; }

        /// <summary>The customer's acceptance of the result (letter, minutes, signed sheet).</summary>
        public string CustomerAcceptanceRef { get; set; }
        public bool CustomerAcceptanceNotApplicable { get; set; }
        public string CustomerAcceptanceNote { get; set; }

        public ICollection<PbDesignReviewParticipant> Participants { get; set; } = new List<PbDesignReviewParticipant>();
        public ICollection<PbDesignReviewInput> Inputs { get; set; } = new List<PbDesignReviewInput>();
        public ICollection<PbDesignReviewOutput> Outputs { get; set; } = new List<PbDesignReviewOutput>();
        public ICollection<PbDesignFinding> Findings { get; set; } = new List<PbDesignFinding>();
        public ICollection<PbDesignTrace> Traces { get; set; } = new List<PbDesignTrace>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class PbDesignReviewParticipant : BaseEntity
    {
        public int ReviewId { get; set; }
        public PbDesignReview Review { get; set; }
        public int UserId { get; set; }

        /// <summary>The function in the review (chair, designer, QA, production ...).</summary>
        public string RoleText { get; set; }

        /// <summary>Computed by the server: the person is not the author of any reviewed output (stored at the meeting for the minutes).</summary>
        public bool Independent { get; set; }
    }

    /// <summary>An input covered by the review (a reference to the register).</summary>
    public class PbDesignReviewInput : BaseEntity
    {
        public int ReviewId { get; set; }
        public PbDesignReview Review { get; set; }
        public int InputId { get; set; }
    }

    /// <summary>An output reviewed: the drawing revision, BOM revision or OPC revision, with what it was at the time (text and content hash of the BOM / OPC).</summary>
    public class PbDesignReviewOutput : BaseEntity
    {
        public int ReviewId { get; set; }
        public PbDesignReview Review { get; set; }
        public DesignOutputKind Kind { get; set; }
        public int? DeliverableId { get; set; }
        public int? RevisionId { get; set; }
        public int? BomHeaderId { get; set; }
        public int? OpcRoutingId { get; set; }
        public string Label { get; set; }
        public string RevisionText { get; set; }
        public string ContentHash { get; set; }
    }

    /// <summary>One cell of the traceability matrix: input x output with the evidence and the judgement of an independent participant.</summary>
    public class PbDesignTrace : BaseEntity
    {
        public int ReviewId { get; set; }
        public PbDesignReview Review { get; set; }
        public int InputId { get; set; }
        public int OutputId { get; set; }
        public DesignTraceResult Result { get; set; }
        public string Evidence { get; set; }

        /// <summary>An EDMS document or a QC record that holds the evidence (free text reference).</summary>
        public string EvidenceRef { get; set; }
        public int? JudgedByUserId { get; set; }
        public DateTimeOffset? JudgedAt { get; set; }
    }

    /// <summary>A finding of a review. It is an action: an owner and a due date; it is closed by a different person than the owner.</summary>
    public class PbDesignFinding : BaseEntity
    {
        public int ReviewId { get; set; }
        public PbDesignReview Review { get; set; }
        public int Seq { get; set; }
        public string Description { get; set; }
        public int OwnerUserId { get; set; }
        public DateTime DueDate { get; set; }
        public DesignFindingStatus Status { get; set; }
        public int RaisedByUserId { get; set; }
        public DateTimeOffset? DoneAt { get; set; }
        public string DoneNote { get; set; }
        public int? ClosedByUserId { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public string CloseNote { get; set; }
        public string VoidReason { get; set; }
    }

    /// <summary>
    /// Fix round 1 (H6): the adoption of an existing design. A BOM revision that was released before the design control started is "legacy, not design-verified"; engineering asks (with a reason and an evidence reference)
    /// and QA, a second person, accepts: the accepted record counts as the verification and validation of that exact BOM content. Never deleted; voided with a reason.
    /// </summary>
    public class PbDesignAdoption : BaseEntity
    {
        public int BomHeaderId { get; set; }
        public int? ProductId { get; set; }
        public int ProductRevision { get; set; }
        public DesignAdoptionStatus Status { get; set; }
        public string Reason { get; set; }
        public string EvidenceRef { get; set; }
        public int RequestedByUserId { get; set; }
        public DateTimeOffset RequestedAt { get; set; }
        public int? AcceptedByUserId { get; set; }
        public DateTimeOffset? AcceptedAt { get; set; }
        public string AcceptNote { get; set; }
        public string VoidReason { get; set; }
        public string ContentHash { get; set; }
        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>Fix round 1 (H6): the day the design control started, recorded once by the first gate decision (a revision released before this day is legacy). One live row.</summary>
    public class PbDesignActivation : BaseEntity
    {
        public DateTime StartDate { get; set; }
        public int? RecordedByUserId { get; set; }
        public string Note { get; set; }
    }
}
