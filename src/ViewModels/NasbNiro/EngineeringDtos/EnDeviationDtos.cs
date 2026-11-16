using System;

namespace ViewModels.NasbNiro.EngineeringDtos
{
    public class EnDeviationQuery
    {
        /// <summary>0 requested, 1 manager approved, 2 approved, 3 rejected, 4 void.</summary>
        public int? Status { get; set; }
        public int? CaseId { get; set; }
        public int? ProductId { get; set; }
        public bool OnlyOpen { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    /// <summary>A deviation from a requirement of a standard: decided by the head of engineering and then the CEO (two distinct persons).</summary>
    public class EnDeviationDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int Kind { get; set; }
        public string KindText { get; set; }
        public string TargetType { get; set; }
        public int TargetId { get; set; }
        public int? CaseId { get; set; }
        public string CaseNumber { get; set; }
        public int? ProductId { get; set; }
        public string ProductCode { get; set; }
        public string StandardCode { get; set; }
        public string StandardClause { get; set; }
        public string Requirement { get; set; }
        public string Instruction { get; set; }
        public string RequestedByName { get; set; }
        public string RequestedAtText { get; set; }
        public string ManagerApprovedByName { get; set; }
        public string ManagerApprovedAtText { get; set; }
        public string CeoApprovedByName { get; set; }
        public string CeoApprovedAtText { get; set; }
        public string RejectedByName { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }
        public string DecisionNote { get; set; }
        public int WaitingHours { get; set; }
        public string RowVersion { get; set; }
        public bool CanDecide { get; set; }
        public string DecideBlockedReason { get; set; }
    }

    public class EnDeviationDecisionDto
    {
        public int Id { get; set; }

        /// <summary>approve or reject.</summary>
        public string Decision { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }
}
