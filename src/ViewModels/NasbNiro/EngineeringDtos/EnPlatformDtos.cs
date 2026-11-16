using System;
using System.Collections.Generic;

namespace ViewModels.NasbNiro.EngineeringDtos
{
    public class EnPlatformQuery
    {
        /// <summary>0 not assessed, 1 incomplete, 2 completing, 3 complete.</summary>
        public int? Status { get; set; }
        public string Contains { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    public class EnPlatformListDto
    {
        public int ProductId { get; set; }
        public string ProductCode { get; set; }
        public string ProductTitle { get; set; }
        public int? DossierId { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }
        public int RequiredCount { get; set; }
        public int AvailableCount { get; set; }
        public int MissingCount { get; set; }
        public int TenderStageMissingCount { get; set; }
        public int ReferenceRevision { get; set; }
        public bool Stale { get; set; }
        public string ConfirmedAtText { get; set; }
    }

    public class EnDossierLineDto
    {
        public int Id { get; set; }
        public int Seq { get; set; }
        public string DocumentType { get; set; }
        public string Discipline { get; set; }
        public string Title { get; set; }
        public bool Required { get; set; }
        public bool TenderStage { get; set; }
        public int State { get; set; }
        public string StateText { get; set; }
        public Guid? DocumentId { get; set; }
        public int? ControlledDocumentId { get; set; }
        public string Revision { get; set; }
        public string NotApplicableReason { get; set; }
        public bool SystemProven { get; set; }
        public string SuppliedByName { get; set; }
        public string SuppliedAtText { get; set; }
        public string RowVersion { get; set; }
    }

    public class EnDossierDto
    {
        public int Id { get; set; }
        public string ReferenceCode { get; set; }
        public bool CanAssign { get; set; }
        public string AssignBlockedReason { get; set; }
        public List<string> ApprovedCodes { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; }
        public string ProductTitle { get; set; }
        public int? ReferenceListId { get; set; }
        public int ReferenceRevision { get; set; }
        public string ReferenceTitle { get; set; }
        public bool Stale { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }
        public int RequiredCount { get; set; }
        public int AvailableCount { get; set; }
        public int MissingCount { get; set; }
        public int TenderStageMissingCount { get; set; }
        public string AssessedAtText { get; set; }
        public string ConfirmedByName { get; set; }
        public string ConfirmedAtText { get; set; }
        public string Note { get; set; }
        public bool TouchedByMe { get; set; }
        public List<EnDossierLineDto> Lines { get; set; } = new List<EnDossierLineDto>();
        public string RowVersion { get; set; }

        public bool CanAssess { get; set; }
        public string AssessBlockedReason { get; set; }
        public bool CanSupply { get; set; }
        public string SupplyBlockedReason { get; set; }
        public bool CanComplete { get; set; }
        public string CompleteBlockedReason { get; set; }
        public bool CanConfirm { get; set; }
        public string ConfirmBlockedReason { get; set; }
        public bool CanReturn { get; set; }
        public string ReturnBlockedReason { get; set; }
    }

    /// <summary>Supplies one line of a dossier: a file, a controlled document, or "not applicable" with a reason.</summary>
    public class EnSupplyLineDto
    {
        public int LineId { get; set; }
        public Guid? DocumentId { get; set; }
        public int? ControlledDocumentId { get; set; }
        public string Revision { get; set; }
        public bool NotApplicable { get; set; }
        public string NotApplicableReason { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>Action: assess (create or refresh from the current reference list), complete (the engineer says it is complete), confirm (the manager), return (the manager; optional LineId is set to missing).</summary>
    public class EnDossierActionDto
    {
        public int ProductId { get; set; }

        /// <summary>For the action assign: the Code of the reference list (the standard basis) this platform is assessed against.</summary>
        public string ReferenceCode { get; set; }
        public string Action { get; set; }
        public int? LineId { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    public class EnPlatformSummaryDto
    {
        public int Products { get; set; }
        public int NotAssessed { get; set; }
        public int Incomplete { get; set; }
        public int Completing { get; set; }
        public int Complete { get; set; }
        public int Stale { get; set; }
        public bool HasApprovedReference { get; set; }
        public int ReferenceRevision { get; set; }
    }
}
