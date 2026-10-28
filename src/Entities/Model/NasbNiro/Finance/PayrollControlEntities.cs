using System;

namespace Entities.Model.NasbNiro.Finance
{
    // Release R1f: maker-checker controls on the payroll inputs. The maker proposes, a second person decides, and the value is used only after the approval:
    // employee master changes, wage history, corrections of locked attendance sheets and opening leave balances.
    // Payroll data is confidential: only PAYROLL, FINANCEMANAGER and CEO read the wage-bearing tables; the correction of an attendance sheet carries no money and is read by HR.

    /// <summary>Decision state of a request. Pending until a second person approves or rejects it.</summary>
    public enum FnChangeStatus { Pending = 0, Approved = 1, Rejected = 2 }

    public enum FnEmployeeChangeKind { Create = 1, Update = 2 }

    /// <summary>
    /// A request to create an employee or to change one of the guarded fields (DailyWage, ExemptionFlags, ChildrenCount, HardAndHarmful, BankIban).
    /// The employee row keeps the old values until a second PAYROLL / FINANCEMANAGER user approves; a payroll run never sees a pending value.
    /// </summary>
    public class FnEmployeeChange
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }

        /// <summary>Lets a new employee and the request that approves him be saved in one transaction.</summary>
        public FnEmployee Employee { get; set; }
        public FnEmployeeChangeKind Kind { get; set; }
        public FnChangeStatus Status { get; set; }

        /// <summary>JSON of the proposed values of the guarded fields (confidential).</summary>
        public string ProposedJson { get; set; }

        /// <summary>The first day of the Persian month the new daily wage applies from; null when the wage is not part of the change.</summary>
        public DateTime? WageEffectiveFrom { get; set; }

        public string Reason { get; set; }
        public int ProposedByUserId { get; set; }
        public DateTimeOffset ProposedAt { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }
    }

    /// <summary>
    /// The approved daily wage of an employee from a month on. The payroll run reads the wage effective at its month (the latest row whose EffectiveFrom is not after the first day of the month);
    /// FnEmployee.DailyWage stays as a cached copy of the current value. A row exists only after the approval of the change that created it.
    /// </summary>
    public class FnEmployeeWage
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }

        /// <summary>The first day of a Persian month.</summary>
        public DateTime EffectiveFrom { get; set; }

        public decimal DailyWage { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ChangeId { get; set; }

        /// <summary>True for the row written from the cached DailyWage when the history of an existing employee started.</summary>
        public bool IsBaseline { get; set; }
    }

    /// <summary>
    /// A request of payroll to change the attendance numbers of a sheet that the HR close locked. HR (never the proposer) approves or rejects it; the approval writes the numbers to the sheet.
    /// </summary>
    public class FnAttendanceCorrection
    {
        public int Id { get; set; }
        public int SheetId { get; set; }
        public int EmployeeId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }

        /// <summary>JSON of the proposed attendance numbers (days, hours, shift type, leave, absence, mission). No money.</summary>
        public string ProposedJson { get; set; }

        /// <summary>JSON of the numbers the sheet held when the request was made.</summary>
        public string CurrentJson { get; set; }

        public string Reason { get; set; }
        public FnChangeStatus Status { get; set; }
        public int ProposedByUserId { get; set; }
        public DateTimeOffset ProposedAt { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }
    }

    /// <summary>
    /// The leave balance of an employee as of the start of a month (carried days). PAYROLL enters it, FINANCEMANAGER approves it; the approval re-bases the balance row of the year
    /// so that the accrual continues from that month. This replaces whatever the system had accrued before.
    /// </summary>
    public class FnLeaveOpening
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }

        /// <summary>Persian year of the balance.</summary>
        public int Year { get; set; }

        /// <summary>The balance is as of the start of this month (1..12); the accrual continues from it.</summary>
        public int Month { get; set; }

        public decimal CarriedDays { get; set; }
        public string Note { get; set; }
        public FnChangeStatus Status { get; set; }
        public int EnteredByUserId { get; set; }
        public DateTimeOffset EnteredAt { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }
    }
}
