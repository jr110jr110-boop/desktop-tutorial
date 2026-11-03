using System;
using System.Collections.Generic;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.FinanceDtos
{
    // Release R1f: maker-checker controls on the payroll inputs (employee changes, wage history, loan approval, opening leave balances).

    /// <summary>The employee fields a payroll run reads that are guarded by a second approval.</summary>
    public class GuardedFieldsDto
    {
        public decimal DailyWage { get; set; }
        public string ExemptionFlags { get; set; }
        public int ChildrenCount { get; set; }
        public bool HardAndHarmful { get; set; }
        public string BankIban { get; set; }
    }

    public class EmployeeChangeDto
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string PersonnelCode { get; set; }
        public string EmployeeName { get; set; }

        /// <summary>Create or Update.</summary>
        public string Kind { get; set; }

        /// <summary>Pending, Approved or Rejected.</summary>
        public string Status { get; set; }
        public GuardedFieldsDto Current { get; set; }
        public GuardedFieldsDto Proposed { get; set; }

        /// <summary>The first day of the Persian month the proposed daily wage applies from.</summary>
        public DateTime? WageEffectiveFrom { get; set; }
        public string Reason { get; set; }
        public string ProposedByName { get; set; }
        public DateTimeOffset ProposedAt { get; set; }
        public string DecidedByName { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }
    }

    public class EmployeeChangeQuery : PaginationParams
    {
        public int? EmployeeId { get; set; }

        /// <summary>0 = Pending, 1 = Approved, 2 = Rejected.</summary>
        public int? Status { get; set; }
    }

    public class EmployeeChangeActionDto
    {
        public int Id { get; set; }

        /// <summary>approve or reject.</summary>
        public string Action { get; set; }

        /// <summary>Required for a rejection.</summary>
        public string Note { get; set; }
    }

    public class EmployeeWageDto
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public decimal DailyWage { get; set; }
        public bool IsBaseline { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
    }

    public class LoanActionDto
    {
        public int Id { get; set; }

        /// <summary>approve or reject (FINANCEMANAGER, never the creator).</summary>
        public string Action { get; set; }

        /// <summary>Required for a rejection.</summary>
        public string Note { get; set; }
    }

    public class LeaveOpeningDto
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string PersonnelCode { get; set; }
        public string EmployeeName { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal CarriedDays { get; set; }
        public string Note { get; set; }

        /// <summary>Pending, Approved or Rejected.</summary>
        public string Status { get; set; }
        public string EnteredByName { get; set; }
        public DateTimeOffset EnteredAt { get; set; }
        public string DecidedByName { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }
    }

    public class SaveLeaveOpeningDto
    {
        public int? EmployeeId { get; set; }

        /// <summary>Alternative to EmployeeId (used by imports).</summary>
        public string PersonnelCode { get; set; }

        /// <summary>Persian year of the balance.</summary>
        public int Year { get; set; }

        /// <summary>The balance is as of the start of this month; the accrual continues from it.</summary>
        public int Month { get; set; }
        public decimal CarriedDays { get; set; }
        public string Note { get; set; }
    }

    public class ImportLeaveOpeningDto
    {
        public List<SaveLeaveOpeningDto> Rows { get; set; } = new List<SaveLeaveOpeningDto>();
    }

    public class ImportLeaveOpeningResultDto
    {
        public int Imported { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class LeaveOpeningQuery : PaginationParams
    {
        public int? Year { get; set; }
        public int? EmployeeId { get; set; }

        /// <summary>0 = Pending, 1 = Approved, 2 = Rejected.</summary>
        public int? Status { get; set; }
    }

    public class LeaveOpeningActionDto
    {
        public int Id { get; set; }

        /// <summary>approve or reject (FINANCEMANAGER).</summary>
        public string Action { get; set; }

        /// <summary>Required for a rejection.</summary>
        public string Note { get; set; }
    }
}
