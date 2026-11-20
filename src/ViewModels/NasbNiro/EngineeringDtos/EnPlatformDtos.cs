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

        /// <summary>The standard of the dossier (a platform has one dossier per standard); empty on the virtual row of a product that has no dossier yet.</summary>
        public string ReferenceCode { get; set; }
        public string StandardBasis { get; set; }

        /// <summary>The product has no dossier: the engineers must record the standard of the data sheet it was built for (step ENG.PLATFORM.STANDARD).</summary>
        public bool NativeStandardMissing { get; set; }
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
        public string StandardBasis { get; set; }

        /// <summary>The data sheet file of the original project the standard was recorded from; null for a dossier made without a file (by the case flow or before E1d).</summary>
        public Guid? StandardAttachmentId { get; set; }

        /// <summary>The other dossiers of the same product (one per standard).</summary>
        public List<EnDossierRefDto> OtherDossiers { get; set; } = new List<EnDossierRefDto>();
        public bool NativeStandardMissing { get; set; }
        public bool CanRecordStandard { get; set; }
        public string RecordStandardBlockedReason { get; set; }
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

    public class EnDossierRefDto
    {
        public int Id { get; set; }
        public string ReferenceCode { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }
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

    /// <summary>Action: native-standard (record the standard of the original data sheet; creates the dossier), assess (build or refresh from the approved list of its standard), complete (the engineer says it is complete), confirm (the manager), return (the manager; optional LineId is set to missing).</summary>
    public class EnDossierActionDto
    {
        public int ProductId { get; set; }

        /// <summary>The standard (Code) of the dossier: a platform has one dossier per standard. For native-standard it is the standard of the data sheet the platform was built for; omitted only when the product has exactly one dossier.</summary>
        public string ReferenceCode { get; set; }

        /// <summary>For native-standard: where the standard is known from (data sheet number and clause), at least 5 characters.</summary>
        public string StandardBasis { get; set; }

        /// <summary>For native-standard: the uploaded data sheet file of the original project (REQUIRED from a person; same name as the case DTO).</summary>
        public Guid? StandardAttachmentId { get; set; }
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
