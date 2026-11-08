using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.OpsData;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.OpsDataDtos
{
    /// <summary>A finding as the form, the list and the reports show it.</summary>
    public class OpsFindingDto
    {
        public int? Id { get; set; }
        public OpsUnit Unit { get; set; }
        public string UnitTitle { get; set; }
        public string DocumentType { get; set; }
        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; }
        public int? LineId { get; set; }
        public int? RowNumber { get; set; }
        public int? ItemId { get; set; }
        public string ItemTitle { get; set; }
        public string RuleCode { get; set; }
        public string RuleTitle { get; set; }
        public OpsSeverity Severity { get; set; }
        public OpsSource Source { get; set; }
        public OpsFindingStatus Status { get; set; }
        public string StatusTitle { get; set; }
        public string Message { get; set; }
        public decimal? EnteredValue { get; set; }
        public decimal? ReferenceValue { get; set; }
        public decimal? DeviationPercent { get; set; }
        public bool IsMoney { get; set; }
        public bool Masked { get; set; }
        public DateTimeOffset? DetectedAt { get; set; }
        public string DetectedAtText { get; set; }

        /// <summary>Block findings: the exception that lets the confirmation pass (or is waiting for the supervisor).</summary>
        public int? ExceptionId { get; set; }
        public OpsExceptionStatus? ExceptionStatus { get; set; }
        public string EnteredByName { get; set; }
        public DateTimeOffset? ResolvedAt { get; set; }
        public string ResolvedAtText { get; set; }
        public string ResolvedByName { get; set; }
        public string ResolutionNote { get; set; }
        public int? CorrectiveDocumentId { get; set; }
        public string Link { get; set; }
    }

    public class OpsFindingQuery : PaginationParams
    {
        public OpsUnit? Unit { get; set; }
        public string Rule { get; set; }
        public OpsSeverity? Severity { get; set; }
        public OpsSource? Source { get; set; }

        /// <summary>open = Open; resolved = Resolved, Verified; superseded; all (default open).</summary>
        public string State { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string DocumentType { get; set; }
        public int? DocumentId { get; set; }
    }

    public class OpsExceptionDto
    {
        public int Id { get; set; }
        public OpsUnit Unit { get; set; }
        public string UnitTitle { get; set; }
        public string DocumentType { get; set; }
        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; }
        public int? LineId { get; set; }
        public int? RowNumber { get; set; }
        public string RuleCode { get; set; }
        public string RuleTitle { get; set; }
        public decimal? EnteredValue { get; set; }
        public decimal? ReferenceValue { get; set; }
        public decimal? DeviationPercent { get; set; }
        public bool IsMoney { get; set; }
        public bool Masked { get; set; }
        public string Message { get; set; }
        public string Justification { get; set; }
        public int RequestedByUserId { get; set; }
        public string RequestedByName { get; set; }
        public DateTimeOffset RequestedAt { get; set; }
        public string RequestedAtText { get; set; }
        public OpsExceptionStatus Status { get; set; }
        public string StatusTitle { get; set; }
        public string DecidedByName { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecidedAtText { get; set; }
        public string DecisionReason { get; set; }
        public DateTimeOffset? UsedAt { get; set; }
        public string Link { get; set; }

        /// <summary>The caller may decide this exception (supervisor of the unit, not the requester, not the maker of the document).</summary>
        public bool CanDecide { get; set; }
    }

    public class OpsExceptionQuery : PaginationParams
    {
        public OpsUnit? Unit { get; set; }
        public OpsExceptionStatus? Status { get; set; }
        public string DocumentType { get; set; }
        public int? DocumentId { get; set; }

        /// <summary>Only the exceptions waiting for the caller's decision.</summary>
        public bool WaitingForMe { get; set; }
    }

    public class RequestOpsExceptionDto
    {
        /// <summary>The blocking finding the gate stored when it refused the entry.</summary>
        public int FindingId { get; set; }
        public string Justification { get; set; }
    }

    public class DecideOpsExceptionDto
    {
        public int Id { get; set; }
        public bool Approve { get; set; }
        public string Reason { get; set; }
    }

    public class ResolveOpsFindingDto
    {
        public int Id { get; set; }

        /// <summary>corrected (resolved with a corrective document) or verified (the data is right).</summary>
        public string Resolution { get; set; }
        public int? CorrectiveDocumentId { get; set; }
        public string Reason { get; set; }
    }

    public class OpsEvaluateQuery
    {
        public string DocumentType { get; set; }
        public int DocumentId { get; set; }
    }

    public class OpsRuleDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string Family { get; set; }
        public OpsUnit Unit { get; set; }
        public bool Enabled { get; set; }
        public decimal Warn { get; set; }
        public decimal Block { get; set; }
        public decimal MinHistory { get; set; }
        public string Basis { get; set; }
    }

    public class OpsCountDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public int Count { get; set; }
    }

    /// <summary>The counts the widgets, the data-quality page and the CEO brief show.</summary>
    public class OpsSummaryDto
    {
        public DateTime AsOf { get; set; }
        public int OpenFindings { get; set; }
        public int OpenBlocks { get; set; }
        public int OpenWarnings { get; set; }
        public int ExceptionsThisWeek { get; set; }
        public int PendingExceptions { get; set; }
        public int ResolvedThisWeek { get; set; }
        public List<OpsCountDto> ByUnit { get; set; } = new List<OpsCountDto>();
        public List<OpsCountDto> ByRule { get; set; } = new List<OpsCountDto>();
        public List<OpsCountDto> TopRules { get; set; } = new List<OpsCountDto>();
        public string Link { get; set; }
    }
}
