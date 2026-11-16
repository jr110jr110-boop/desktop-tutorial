using System.Collections.Generic;

namespace ViewModels.NasbNiro.EngineeringDtos
{
    public class EnGapQuery
    {
        /// <summary>0 draft, 1 confirmed, 2 void.</summary>
        public int? Status { get; set; }
        public int? CaseId { get; set; }
        public bool OnlyOpen { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    public class EnGapLineDto
    {
        public int Seq { get; set; }

        /// <summary>0 same, 1 added by the new standard, 2 removed, 3 required flag changed.</summary>
        public int Kind { get; set; }
        public string KindText { get; set; }
        public string DocumentType { get; set; }
        public string Discipline { get; set; }
        public string Title { get; set; }
        public bool BaseRequired { get; set; }
        public bool TargetRequired { get; set; }
        public int? ReusableFromDossierLineId { get; set; }
    }

    /// <summary>The standard-gap report: what changes in the document set of a platform when the order is under another standard.</summary>
    public class EnGapDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int CaseId { get; set; }
        public string CaseNumber { get; set; }
        public int? ProductId { get; set; }
        public string ProductCode { get; set; }
        public string BaseCode { get; set; }
        public string TargetCode { get; set; }
        public int BaseRevision { get; set; }
        public int TargetRevision { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }
        public int AddedCount { get; set; }
        public int RemovedCount { get; set; }
        public int RequiredChangedCount { get; set; }
        public int ReusableCount { get; set; }
        public string Summary { get; set; }
        public string EngineeringImpactNote { get; set; }
        public string ConfirmedByName { get; set; }
        public string ConfirmedAtText { get; set; }
        public string RowVersion { get; set; }
        public bool CanConfirm { get; set; }
        public string ConfirmBlockedReason { get; set; }
        public List<EnGapLineDto> Lines { get; set; } = new List<EnGapLineDto>();
    }

    /// <summary>The short form on the case and the handover.</summary>
    public class EnGapSummaryDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }
        public string Summary { get; set; }
    }

    public class EnGapConfirmDto
    {
        public int Id { get; set; }

        /// <summary>The effect on materials and design as judged by engineering (at least 30 characters).</summary>
        public string EngineeringImpactNote { get; set; }
        public string RowVersion { get; set; }
    }
}
