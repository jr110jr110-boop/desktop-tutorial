namespace ViewModels.NasbNiro.EngineeringDtos
{
    public class EnRegistrationQuery
    {
        public int? CaseId { get; set; }
        public int? Status { get; set; }
        public bool OnlyOpen { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    /// <summary>The head of engineering proposes that the product of a New-design case becomes a platform against a standard.</summary>
    public class EnRegistrationProposeDto
    {
        public int CaseId { get; set; }
        public int ProductId { get; set; }
        public string ReferenceCode { get; set; }

        /// <summary>The data sheet number and clause the standard is known from.</summary>
        public string StandardBasis { get; set; }

        /// <summary>Why this design should be a platform (at least 30 characters).</summary>
        public string Rationale { get; set; }
    }

    /// <summary>decision: approve or reject (the CEO); withdraw (the proposer, while it waits).</summary>
    public class EnRegistrationDecisionDto
    {
        public int Id { get; set; }
        public string Decision { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    public class EnRegistrationDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int CaseId { get; set; }
        public string CaseNumber { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; }
        public string ReferenceCode { get; set; }
        public string StandardBasis { get; set; }
        public string Rationale { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }
        public string ProposedByName { get; set; }
        public string ProposedAtText { get; set; }
        public string DecidedByName { get; set; }
        public string DecidedAtText { get; set; }
        public string DecisionNote { get; set; }
        public int? DossierId { get; set; }
        public int WaitingHours { get; set; }
        public string RowVersion { get; set; }
        public bool CanDecide { get; set; }
        public string DecideBlockedReason { get; set; }
        public bool CanWithdraw { get; set; }
        public string WithdrawBlockedReason { get; set; }
    }
}
