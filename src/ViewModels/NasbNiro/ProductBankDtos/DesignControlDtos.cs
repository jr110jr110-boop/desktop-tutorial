using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.ProductBank;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.ProductBankDtos
{
    // Package A1 (design and development control). Names start with "Design" so that they never clash with another DTO in the Swagger schema ids.

    // ------------------------------------------------------------------------------------------ plan

    public class DesignPlanQuery : PaginationParams
    {
        public int? ProductId { get; set; }
        public int? ProductRevision { get; set; }
        public DesignPlanStatus? Status { get; set; }
        public string Contains { get; set; }
    }

    public class DesignPlanListDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; }
        public string ProductTitle { get; set; }
        public int ProductRevision { get; set; }
        public string ProductRevisionText { get; set; }
        public int Version { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public DesignPlanStatus Status { get; set; }
        public bool ValidationRequired { get; set; }
        public DateTimeOffset CreateDate { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
    }

    public class DesignStageDto
    {
        public int Id { get; set; }
        public int Seq { get; set; }
        public DesignStageKind Kind { get; set; }
        public string KindText { get; set; }
        public string Title { get; set; }
        public string ResponsibleRole { get; set; }
        public int? ResponsibleUserId { get; set; }
        public string ResponsibleName { get; set; }
        public DateTime? PlannedStart { get; set; }
        public DateTime? PlannedEnd { get; set; }
        public DesignStageStatus Status { get; set; }
        public string StatusText { get; set; }
        public DateTime? ActualEnd { get; set; }
        public string Note { get; set; }

        /// <summary>The planned end is past and the stage is not done.</summary>
        public bool Late { get; set; }
    }

    public class DesignInterfaceDto
    {
        public int Id { get; set; }
        public string FromGroup { get; set; }
        public string ToGroup { get; set; }
        public string Subject { get; set; }
        public string ResponsibleRole { get; set; }
        public int? ResponsibleUserId { get; set; }
        public string ResponsibleName { get; set; }
    }

    public class DesignPlanVersionDto
    {
        public int Id { get; set; }
        public int Version { get; set; }
        public string Number { get; set; }
        public DesignPlanStatus Status { get; set; }
        public DateTimeOffset CreateDate { get; set; }
    }

    public class DesignKindCountDto
    {
        public DesignInputKind Kind { get; set; }
        public string KindText { get; set; }
        public int Total { get; set; }
        public int Conflicts { get; set; }
        public bool NotApplicable { get; set; }
    }

    public class DesignPlanDto : DesignPlanListDto
    {
        public string Scope { get; set; }
        public string ValidationWaiverReason { get; set; }
        public int PreparedByUserId { get; set; }
        public string PreparedByName { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public string ApprovedByName { get; set; }
        public string ReturnNote { get; set; }
        public int? InputsCompleteByUserId { get; set; }
        public string InputsCompleteByName { get; set; }
        public DateTimeOffset? InputsCompleteAt { get; set; }
        public string InputsCompleteNote { get; set; }
        public List<int> NotApplicableKinds { get; set; } = new List<int>();
        public int? SupersedesPlanId { get; set; }
        public string VoidReason { get; set; }
        public string ContentHash { get; set; }
        public string RowVersion { get; set; }
        public List<DesignStageDto> Stages { get; set; } = new List<DesignStageDto>();
        public List<DesignInterfaceDto> Interfaces { get; set; } = new List<DesignInterfaceDto>();
        public List<DesignPlanVersionDto> Versions { get; set; } = new List<DesignPlanVersionDto>();
        public List<DesignKindCountDto> Kinds { get; set; } = new List<DesignKindCountDto>();

        /// <summary>Kinds of input that have no input and are not declared not applicable (Persian).</summary>
        public List<string> MissingKinds { get; set; } = new List<string>();
        public int ConflictCount { get; set; }
        public bool CanEdit { get; set; }
        public bool CanSubmit { get; set; }
        public bool CanApprove { get; set; }
        public bool CanDeclareInputsComplete { get; set; }
        public bool CanNewVersion { get; set; }
    }

    public class SaveDesignStageDto
    {
        public int? Id { get; set; }
        public DesignStageKind Kind { get; set; }
        public string ResponsibleRole { get; set; }
        public int? ResponsibleUserId { get; set; }
        public DateTime? PlannedStart { get; set; }
        public DateTime? PlannedEnd { get; set; }
        public string Note { get; set; }
    }

    public class SaveDesignInterfaceDto
    {
        public int? Id { get; set; }
        public string FromGroup { get; set; }
        public string ToGroup { get; set; }
        public string Subject { get; set; }
        public string ResponsibleRole { get; set; }
        public int? ResponsibleUserId { get; set; }
    }

    public class SaveDesignPlanDto
    {
        public int? Id { get; set; }
        public int ProductId { get; set; }
        public int ProductRevision { get; set; }
        public string Title { get; set; }
        public string Scope { get; set; }
        public bool ValidationRequired { get; set; } = true;
        public string ValidationWaiverReason { get; set; }
        public List<SaveDesignStageDto> Stages { get; set; } = new List<SaveDesignStageDto>();
        public List<SaveDesignInterfaceDto> Interfaces { get; set; } = new List<SaveDesignInterfaceDto>();
        public string RowVersion { get; set; }
    }

    /// <summary>Action on a plan: submit, return (note), approve, new-version, void (note), inputs-complete (note, not applicable kinds).</summary>
    public class DesignPlanActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
        public List<int> NotApplicableKinds { get; set; } = new List<int>();
    }

    /// <summary>Action on a stage of a plan: start, complete, not-required (validation only).</summary>
    public class DesignStageActionDto
    {
        public int PlanId { get; set; }
        public int StageId { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
    }

    // ------------------------------------------------------------------------------------------ inputs

    public class DesignInputQuery : PaginationParams
    {
        public int? ProductId { get; set; }
        public int? ProductRevision { get; set; }
        public DesignInputKind? Kind { get; set; }
        public DesignInputStatus? Status { get; set; }
        public bool IncludeInactive { get; set; }
        public string Contains { get; set; }
    }

    public class DesignInputDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; }
        public int ProductRevision { get; set; }
        public string Code { get; set; }
        public DesignInputKind Kind { get; set; }
        public string KindText { get; set; }
        public string Statement { get; set; }
        public string Source { get; set; }
        public string AcceptanceCriterion { get; set; }
        public string StandardEdition { get; set; }
        public string PreviousDesignRef { get; set; }
        public int? RefDeliverableId { get; set; }
        public int? RefRevisionId { get; set; }
        public string RefDocumentText { get; set; }
        public string RefText { get; set; }
        public string RefRevisionText { get; set; }
        public string FailureConsequence { get; set; }
        public DesignInputStatus Status { get; set; }
        public string StatusText { get; set; }
        public int? ConflictWithInputId { get; set; }
        public string ConflictWithCode { get; set; }
        public string ConflictNote { get; set; }
        public int? ConflictMarkedByUserId { get; set; }
        public string ConflictMarkedByName { get; set; }
        public DateTimeOffset? ConflictMarkedAt { get; set; }
        public string ConflictResolution { get; set; }
        public int? ConflictResolvedByUserId { get; set; }
        public string ConflictResolvedByName { get; set; }
        public DateTimeOffset? ConflictResolvedAt { get; set; }
        public int? SupersedesInputId { get; set; }
        public string VoidReason { get; set; }
        public int CreatorUserId { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset CreateDate { get; set; }

        /// <summary>A review that is not void already refers to the input: a change is then a replacement (a new input), not an edit.</summary>
        public bool UsedByReview { get; set; }
        public string RowVersion { get; set; }
    }

    public class SaveDesignInputDto
    {
        public int? Id { get; set; }
        public int ProductId { get; set; }
        public int ProductRevision { get; set; }
        public DesignInputKind Kind { get; set; }
        public string Statement { get; set; }
        public string Source { get; set; }
        public string AcceptanceCriterion { get; set; }
        public string StandardEdition { get; set; }
        public string PreviousDesignRef { get; set; }
        public int? RefDeliverableId { get; set; }
        public int? RefRevisionId { get; set; }
        public string RefText { get; set; }
        public string RefRevisionText { get; set; }
        public string FailureConsequence { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>Action on an input: conflict (note, withInputId), resolve (note), void (note).</summary>
    public class DesignInputActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
        public int? WithInputId { get; set; }
    }

    public class DesignInputExcelRequestDto
    {
        public int ProductId { get; set; }
        public int ProductRevision { get; set; }
        public bool Preview { get; set; } = true;
    }

    public class DesignInputExcelRowDto
    {
        public int Row { get; set; }
        public string Statement { get; set; }
        public bool Ok { get; set; }
        public string Message { get; set; }
        public string Code { get; set; }
    }

    public class DesignInputExcelResultDto
    {
        public bool Preview { get; set; }
        public int RowCount { get; set; }
        public int ValidRows { get; set; }
        public int ErrorRows { get; set; }
        public int Created { get; set; }
        public List<DesignInputExcelRowDto> Rows { get; set; } = new List<DesignInputExcelRowDto>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    // ------------------------------------------------------------------------------------------ reviews

    public class DesignReviewQuery : PaginationParams
    {
        public int? ProductId { get; set; }
        public int? ProductRevision { get; set; }
        public int? PlanId { get; set; }
        public DesignReviewType? Type { get; set; }
        public DesignReviewStatus? Status { get; set; }
        public int? BomHeaderId { get; set; }
        public string Contains { get; set; }
    }

    public class DesignReviewListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; }
        public int ProductRevision { get; set; }
        public int PlanId { get; set; }
        public string PlanNumber { get; set; }
        public DesignReviewType Type { get; set; }
        public string TypeText { get; set; }
        public string Title { get; set; }
        public DesignReviewStatus Status { get; set; }
        public DesignReviewOutcome Outcome { get; set; }
        public DateTime? ScheduledDate { get; set; }
        public DateTime? HeldDate { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public int OpenFindings { get; set; }
        public DateTimeOffset CreateDate { get; set; }
    }

    public class DesignParticipantDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; }
        public string RoleText { get; set; }

        /// <summary>Computed on the server: not the author of any reviewed output.</summary>
        public bool Independent { get; set; }
    }

    public class DesignOutputDto
    {
        public int Id { get; set; }
        public DesignOutputKind Kind { get; set; }
        public string KindText { get; set; }
        public int? DeliverableId { get; set; }
        public int? RevisionId { get; set; }
        public int? BomHeaderId { get; set; }
        public int? OpcRoutingId { get; set; }
        public string Label { get; set; }
        public string RevisionText { get; set; }
        public string ContentHash { get; set; }

        /// <summary>The content of a BOM / OPC revision is still what the review saw.</summary>
        public bool Unchanged { get; set; } = true;
        public List<string> AuthorNames { get; set; } = new List<string>();
    }

    public class DesignFindingDto
    {
        public int Id { get; set; }
        public int Seq { get; set; }
        public string Description { get; set; }
        public int OwnerUserId { get; set; }
        public string OwnerName { get; set; }
        public DateTime DueDate { get; set; }
        public DesignFindingStatus Status { get; set; }
        public string StatusText { get; set; }
        public int RaisedByUserId { get; set; }
        public string RaisedByName { get; set; }
        public DateTimeOffset? DoneAt { get; set; }
        public string DoneNote { get; set; }
        public int? ClosedByUserId { get; set; }
        public string ClosedByName { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public string CloseNote { get; set; }
        public string VoidReason { get; set; }
        public bool Late { get; set; }
    }

    public class DesignTraceDto
    {
        public int Id { get; set; }
        public int InputId { get; set; }
        public string InputCode { get; set; }
        public int OutputId { get; set; }
        public DesignTraceResult Result { get; set; }
        public string Evidence { get; set; }
        public string EvidenceRef { get; set; }
        public int? JudgedByUserId { get; set; }
        public string JudgedByName { get; set; }
        public DateTimeOffset? JudgedAt { get; set; }
    }

    /// <summary>An input the review cannot rely on yet, with the reason (Persian).</summary>
    public class DesignUncoveredDto
    {
        public int InputId { get; set; }
        public string Code { get; set; }
        public string Statement { get; set; }
        public string Reason { get; set; }
    }

    public class DesignReviewInputRowDto
    {
        public int InputId { get; set; }
        public string Code { get; set; }
        public DesignInputKind Kind { get; set; }
        public string KindText { get; set; }
        public string Statement { get; set; }
        public string Source { get; set; }
        public string AcceptanceCriterion { get; set; }
        public DesignInputStatus Status { get; set; }

        /// <summary>The input is covered by a passed acceptance criterion (traceability matrix).</summary>
        public bool Covered { get; set; }
    }

    public class DesignReviewDto : DesignReviewListDto
    {
        public DesignStageKind Stage { get; set; }
        public string PlanTitle { get; set; }
        public bool PlanValidationRequired { get; set; }
        public string Minutes { get; set; }
        public int? HeldByUserId { get; set; }
        public string HeldByName { get; set; }
        public DateTimeOffset? HeldAt { get; set; }
        public string OutcomeNote { get; set; }
        public int? ClosedByUserId { get; set; }
        public string ClosedByName { get; set; }
        public int? PreviousReviewId { get; set; }
        public string VoidReason { get; set; }
        public string CreatedByName { get; set; }
        public int CreatorUserId { get; set; }
        public DesignValidationMethod ValidationMethod { get; set; }
        public string ValidationEvidence { get; set; }
        public string CustomerAcceptanceRef { get; set; }
        public bool CustomerAcceptanceNotApplicable { get; set; }
        public string CustomerAcceptanceNote { get; set; }
        public string RowVersion { get; set; }
        public List<DesignParticipantDto> Participants { get; set; } = new List<DesignParticipantDto>();
        public List<DesignReviewInputRowDto> Inputs { get; set; } = new List<DesignReviewInputRowDto>();
        public List<DesignOutputDto> Outputs { get; set; } = new List<DesignOutputDto>();
        public List<DesignFindingDto> Findings { get; set; } = new List<DesignFindingDto>();
        public List<DesignTraceDto> Traces { get; set; } = new List<DesignTraceDto>();

        /// <summary>Inputs the review does not cover yet and why (verification: every live input; validation: the functional, performance and customer inputs).</summary>
        public List<DesignUncoveredDto> Uncovered { get; set; } = new List<DesignUncoveredDto>();

        /// <summary>What still prevents the review from being closed as Accepted (Persian); empty = it can be closed as Accepted.</summary>
        public List<string> Blockers { get; set; } = new List<string>();
        public int IndependentCount { get; set; }
        public bool CanEdit { get; set; }
        public bool CanHold { get; set; }
        public bool CanClose { get; set; }
        public bool CanJudge { get; set; }
        public bool CanVoid { get; set; }
    }

    public class SaveDesignParticipantDto
    {
        public int UserId { get; set; }
        public string RoleText { get; set; }
    }

    public class SaveDesignOutputDto
    {
        public DesignOutputKind Kind { get; set; }
        public int? RevisionId { get; set; }
        public int? BomHeaderId { get; set; }
        public int? OpcRoutingId { get; set; }
    }

    public class SaveDesignReviewDto
    {
        public int? Id { get; set; }
        public int PlanId { get; set; }
        public DesignReviewType Type { get; set; }
        public string Title { get; set; }
        public DateTime? ScheduledDate { get; set; }
        public List<int> InputIds { get; set; } = new List<int>();
        public List<SaveDesignOutputDto> Outputs { get; set; } = new List<SaveDesignOutputDto>();
        public List<SaveDesignParticipantDto> Participants { get; set; } = new List<SaveDesignParticipantDto>();
        public int? PreviousReviewId { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>Action on a review: hold (heldDate, minutes), close (outcome, note), void (note), repeat (a new planned review with the same scope).</summary>
    public class DesignReviewActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
        public DateTime? HeldDate { get; set; }
        public string Minutes { get; set; }
        public DesignReviewOutcome Outcome { get; set; }
    }

    public class SaveDesignValidationDto
    {
        public int ReviewId { get; set; }
        public DesignValidationMethod Method { get; set; }
        public string Evidence { get; set; }
        public string CustomerAcceptanceRef { get; set; }
        public bool CustomerAcceptanceNotApplicable { get; set; }
        public string CustomerAcceptanceNote { get; set; }
    }

    public class SaveDesignFindingDto
    {
        public int ReviewId { get; set; }
        public int? Id { get; set; }
        public string Description { get; set; }
        public int OwnerUserId { get; set; }
        public DateTime DueDate { get; set; }
    }

    /// <summary>Action on a finding: done (note, by the owner), close (note, by a different person), void (note, by a third person).</summary>
    public class DesignFindingActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
    }

    public class SaveDesignTraceRowDto
    {
        public int InputId { get; set; }
        public int OutputId { get; set; }
        public DesignTraceResult Result { get; set; }
        public string Evidence { get; set; }
        public string EvidenceRef { get; set; }
    }

    public class SaveDesignTraceDto
    {
        public int ReviewId { get; set; }
        public List<SaveDesignTraceRowDto> Rows { get; set; } = new List<SaveDesignTraceRowDto>();
    }

    /// <summary>The printable matrix: rows are inputs, columns are outputs, a cell is the result of the evidence.</summary>
    public class DesignMatrixDto
    {
        public int ReviewId { get; set; }
        public string ReviewNumber { get; set; }
        public string ReviewTitle { get; set; }
        public DesignReviewType Type { get; set; }
        public DesignReviewStatus Status { get; set; }
        public DesignReviewOutcome Outcome { get; set; }
        public string ProductCode { get; set; }
        public string ProductTitle { get; set; }
        public int ProductRevision { get; set; }
        public List<DesignOutputDto> Outputs { get; set; } = new List<DesignOutputDto>();
        public List<DesignMatrixRowDto> Rows { get; set; } = new List<DesignMatrixRowDto>();
        public List<DesignUncoveredDto> Uncovered { get; set; } = new List<DesignUncoveredDto>();
    }

    public class DesignMatrixRowDto
    {
        public DesignReviewInputRowDto Input { get; set; }
        public List<DesignTraceDto> Cells { get; set; } = new List<DesignTraceDto>();
    }

    // ------------------------------------------------------------------------------------------ dossier and gate

    public class DesignGateStatusDto
    {
        public string Subject { get; set; }
        public bool Required { get; set; }
        public bool Passed { get; set; }

        /// <summary>Persian: what is missing (empty when passed or not required).</summary>
        public List<string> Missing { get; set; } = new List<string>();
        public List<DesignReviewListDto> Reviews { get; set; } = new List<DesignReviewListDto>();
    }

    public class DesignDossierBomDto
    {
        public int BomHeaderId { get; set; }
        public string Code { get; set; }
        public int Revision { get; set; }
        public string RevisionText { get; set; }
        public string Status { get; set; }
        public DesignGateStatusDto Verification { get; set; }
        public DesignGateStatusDto Validation { get; set; }
    }

    public class DesignDossierDto
    {
        public int ProductId { get; set; }
        public string ProductCode { get; set; }
        public string ProductTitle { get; set; }
        public bool VerificationRequired { get; set; }
        public bool ValidationRequired { get; set; }
        public List<DesignPlanListDto> Plans { get; set; } = new List<DesignPlanListDto>();
        public List<DesignInputDto> Inputs { get; set; } = new List<DesignInputDto>();
        public List<DesignReviewListDto> Reviews { get; set; } = new List<DesignReviewListDto>();
        public List<DesignDossierBomDto> Boms { get; set; } = new List<DesignDossierBomDto>();
        public int OpenFindings { get; set; }
        public int ConflictCount { get; set; }
    }
}

namespace ViewModels.NasbNiro.QualityDtos
{
    /// <summary>A design review that covers the released configuration (Package A1): part of the as-built record.</summary>
    public class AsBuiltDesignReviewDto
    {
        public string Number { get; set; }
        public string Type { get; set; }
        public string Outcome { get; set; }
        public string PlanNumber { get; set; }
        public string ClosedAt { get; set; }
        public string Covers { get; set; }
    }
}
