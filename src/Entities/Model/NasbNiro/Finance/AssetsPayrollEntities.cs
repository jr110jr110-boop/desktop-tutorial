using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.Finance
{
    // Release F2: Sepidar outbox (tax and insurance only), fixed assets, payroll and insurance. Tables start with "Fn".
    // Statutory figures (minimum wage, allowances, insurance ceiling, tax brackets, depreciation rates) are NOT seeded:
    // they live in dated rate tables, and an operation that needs a missing value fails with a clear message.
    // Payroll data is confidential: only the roles PAYROLL, FINANCEMANAGER and CEO may read it.

    // ------------------------------------------------------------------------------------------ Sepidar outbox

    public enum FnOutboxKind { SalesInvoicesTaxpayer = 1, VatSeason = 2, PayrollInsuranceList = 3, PayrollTaxList = 4 }

    public enum FnOutboxStatus { Draft = 0, Ready = 1, Exported = 2, Sent = 3, Accepted = 4, Rejected = 5, Superseded = 6 }

    /// <summary>A package of tax or insurance data for one period. An exported package is never edited; a new package supersedes it.</summary>
    public class FnOutboxPackage
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public FnOutboxKind Kind { get; set; }
        /// <summary>Persian year.</summary>
        public int Year { get; set; }
        public int? Month { get; set; }
        public int? Season { get; set; }
        public int? WorkshopId { get; set; }
        public FnOutboxStatus Status { get; set; }
        public int Version { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        /// <summary>SHA-256 of the payload (hex).</summary>
        public string ContentHash { get; set; }
        /// <summary>The tables of the package as JSON (table name to rows).</summary>
        public string PayloadJson { get; set; }
        public int RowCount { get; set; }
        public Guid? FileDocumentId { get; set; }
        public int? SupersededByPackageId { get; set; }
        public string ExternalReference { get; set; }
        public string Error { get; set; }
        public string Note { get; set; }
        /// <summary>0 = created by the monthly background job.</summary>
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public DateTimeOffset? ExportedAt { get; set; }
        public DateTimeOffset? SentAt { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ fixed assets

    public enum FnDepreciationMethod { StraightLine = 1, DecliningBalance = 2 }

    public enum FnDepreciationStart { MonthOfUse = 1, NextMonth = 2 }

    public class FnAssetClass
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        /// <summary>Asset account (201xx / 203xx), subsidiary code.</summary>
        public string AssetAccountCode { get; set; }
        /// <summary>Accumulated depreciation account (202xx / 204xx), subsidiary code.</summary>
        public string AccumulatedAccountCode { get; set; }
        public FnDepreciationMethod Method { get; set; }
        /// <summary>Annual rate in percent; empty until finance sets it (nothing is invented).</summary>
        public decimal? RatePercent { get; set; }
        /// <summary>Straight line: useful life in years (alternative to the rate).</summary>
        public decimal? UsefulLifeYears { get; set; }
        public FnDepreciationStart StartRule { get; set; }
        /// <summary>Idle assets depreciate at this percent of the normal amount (★ 30).</summary>
        public decimal IdleRatePercent { get; set; }
        /// <summary>The reduced idle rate applies after this many idle months (★ 6).</summary>
        public int IdleAfterMonths { get; set; }
        public decimal SalvagePercent { get; set; }
        public string Source { get; set; }
        public bool Verify { get; set; }
        public bool IsIntangible { get; set; }
        public bool IsActive { get; set; } = true;
        public string Jurisdiction { get; set; }
    }

    public enum FnAssetStatus { Draft = 0, InUse = 1, Idle = 2, UnderConstruction = 3, Disposed = 4 }

    public enum FnAssetSource { Manual = 1, SupplierInvoiceLine = 2, Receipt = 3, Opening = 4 }

    public class FnAsset
    {
        public int Id { get; set; }
        /// <summary>Everyone who edited the draft (",5,7,"): the checker or approver must be neither the creator nor any editor.</summary>
        public string EditorUserIds { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public int? ClassId { get; set; }
        public FnAssetClass Class { get; set; }
        public string Serial { get; set; }
        public string Maker { get; set; }
        public string Location { get; set; }
        public int? CustodianEmployeeId { get; set; }
        public string CustodianTitle { get; set; }
        public int? CostCenterId { get; set; }
        /// <summary>Link to the legacy machine list.</summary>
        public int? MachineId { get; set; }
        public DateTime? AcquisitionDate { get; set; }
        public DateTime? InUseDate { get; set; }
        public decimal Cost { get; set; }
        public decimal SalvageValue { get; set; }
        public FnAssetSource Source { get; set; }
        public int? SourceId { get; set; }
        public string SourceNumber { get; set; }
        /// <summary>The account credited when the asset is approved (where the cost sits now). Empty for opening assets.</summary>
        public string OffsetAccountCode { get; set; }
        /// <summary>Accumulated depreciation brought from the old books at go-live.</summary>
        public decimal OpeningAccumulated { get; set; }
        /// <summary>Accumulated depreciation to date (opening included).</summary>
        public decimal AccumulatedDepreciation { get; set; }
        /// <summary>First day of the last Persian month already depreciated.</summary>
        public DateTime? DepreciatedThrough { get; set; }
        /// <summary>Declining balance: accumulated depreciation at the start of <see cref="YearStartYear"/>.</summary>
        public decimal YearStartAccumulated { get; set; }
        public int? YearStartYear { get; set; }
        public FnAssetStatus Status { get; set; }
        public DateTime? DisposedAt { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public ICollection<FnAssetHistory> History { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public enum FnAssetHistoryKind { Acquired = 1, Commissioned = 2, Transfer = 3, IdleFrom = 4, IdleTo = 5, Improvement = 6, Revaluation = 7, Disposal = 8, Edited = 9 }

    public class FnAssetHistory
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public FnAsset Asset { get; set; }
        public DateTime Date { get; set; }
        public FnAssetHistoryKind Kind { get; set; }
        public decimal Amount { get; set; }
        public string Note { get; set; }
        public int UserId { get; set; }
        public DateTimeOffset At { get; set; }
    }

    public enum FnRunStatus { Draft = 0, Approved = 1 }

    public class FnDepreciationRun
    {
        public int Id { get; set; }
        /// <summary>Everyone who edited the draft (",5,7,"): the checker or approver must be neither the creator nor any editor.</summary>
        public string EditorUserIds { get; set; }
        public string Number { get; set; }
        public int PeriodId { get; set; }
        public FnPeriod Period { get; set; }
        public int FiscalYearId { get; set; }
        public FnRunStatus Status { get; set; }
        public decimal TotalAmount { get; set; }
        public int AssetCount { get; set; }
        /// <summary>0 = drafted by the monthly background job.</summary>
        public int PreparedByUserId { get; set; }
        public DateTimeOffset PreparedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? AccountingEventId { get; set; }
        public ICollection<FnDepreciationLine> Lines { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class FnDepreciationLine
    {
        public int Id { get; set; }
        public int RunId { get; set; }
        public FnDepreciationRun Run { get; set; }
        public int AssetId { get; set; }
        public FnAsset Asset { get; set; }
        public int ClassId { get; set; }
        public int? CostCenterId { get; set; }
        /// <summary>Number of months included (more than one when earlier months were not run).</summary>
        public int Months { get; set; }
        public int IdleMonths { get; set; }
        public decimal Amount { get; set; }
        public decimal BookValueBefore { get; set; }
        public decimal AccumulatedAfter { get; set; }
        public DateTime ThroughMonth { get; set; }
        public int YearStartYearAfter { get; set; }
        public decimal YearStartAccumulatedAfter { get; set; }
    }

    public enum FnDisposalKind { Sale = 1, Scrap = 2 }

    /// <summary>How the proceeds of a disposal are collected.</summary>
    public enum FnProceedsMethod { None = 0, Cash = 1, Bank = 2, Receivable = 3 }

    public class FnAssetDisposal
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int AssetId { get; set; }
        public FnAsset Asset { get; set; }
        public DateTime Date { get; set; }
        public FnDisposalKind Kind { get; set; }
        public FnDocStatus Status { get; set; }
        /// <summary>The sale price without VAT.</summary>
        public decimal ProceedsAmount { get; set; }
        /// <summary>VAT on the sale (F3b): output VAT, received from the buyer together with the price.</summary>
        public decimal VatPercent { get; set; }
        public decimal VatAmount { get; set; }
        public FnProceedsMethod ProceedsMethod { get; set; }
        public int? CashBoxId { get; set; }
        public int? BankAccountId { get; set; }
        public int? CustomerId { get; set; }
        public decimal CatchUpDepreciation { get; set; }
        public decimal AccumulatedAtDisposal { get; set; }
        public decimal BookValue { get; set; }
        /// <summary>Positive = gain, negative = loss.</summary>
        public decimal GainLoss { get; set; }
        public string Note { get; set; }
        public int PreparedByUserId { get; set; }
        public DateTimeOffset PreparedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ payroll masters

    public enum FnEmploymentType { Permanent = 1, Contract = 2, Hourly = 3, Daily = 4 }

    public class FnInsuranceWorkshop
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string EmployerName { get; set; }
        public string Address { get; set; }
        public string Branch { get; set; }
        public bool IsContractor { get; set; }
        public string ContractRow { get; set; }
        public string RateSet { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>Confidential: readable only by PAYROLL, FINANCEMANAGER and CEO.</summary>
    public class FnEmployee
    {
        public int Id { get; set; }
        public string PersonnelCode { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string FatherName { get; set; }
        public string NationalCode { get; set; }
        public DateTime? BirthDate { get; set; }
        public string BirthPlace { get; set; }
        /// <summary>1 = male, 2 = female.</summary>
        public int Gender { get; set; }
        /// <summary>1 = single, 2 = married.</summary>
        public int MaritalStatus { get; set; }
        public int ChildrenCount { get; set; }
        public string InsuranceNumber { get; set; }
        public string InsuranceJobCode { get; set; }
        public int? WorkshopId { get; set; }
        public FnInsuranceWorkshop Workshop { get; set; }
        public DateTime HireDate { get; set; }
        public DateTime? EndDate { get; set; }
        public FnEmploymentType EmploymentType { get; set; }
        public string JobTitle { get; set; }
        public string Unit { get; set; }
        public int? CostCenterId { get; set; }
        public int? DefaultWorkOrderId { get; set; }
        public string BankIban { get; set; }
        /// <summary>Contract daily wage (rial).</summary>
        public decimal DailyWage { get; set; }
        /// <summary>Comma separated exemption flags (for example Disabled, Veteran); each needs a TaxExemption{Flag}Monthly rate.</summary>
        public string ExemptionFlags { get; set; }
        public bool HardAndHarmful { get; set; }
        public int? UserId { get; set; }
        public int? QualifiedPersonId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTimeOffset CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public enum FnLoanKind { Loan = 1, Advance = 2 }

    public class FnEmployeeLoan
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public FnEmployee Employee { get; set; }
        public FnLoanKind Kind { get; set; }
        public string Title { get; set; }
        public decimal Principal { get; set; }
        public decimal InstallmentAmount { get; set; }
        public decimal Balance { get; set; }
        /// <summary>The treasury payment that paid the loan or advance out (null until it is paid).</summary>
        public int? DisbursementPaymentId { get; set; }
        public int FirstYear { get; set; }
        public int FirstMonth { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTimeOffset CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public enum FnPayrollItemKind { Earning = 1, Deduction = 2, EmployerContribution = 3, Info = 4, Provision = 5 }

    public enum FnPayrollCalc
    {
        FixedMonthly = 1, PerDayWorked = 2, PerHour = 3, PercentOfItems = 4, Formula = 5, Input = 6, LoanInstallment = 7, IncomeTax = 8,
    }

    public class FnPayrollItem
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public FnPayrollItemKind Kind { get; set; }
        public bool Insurable { get; set; }
        public bool Taxable { get; set; }
        public FnPayrollCalc Calculation { get; set; }
        /// <summary>Rate table key (fixed amounts, per-day rates).</summary>
        public string RateKey { get; set; }
        /// <summary>PerHour: OVERTIME, NIGHT or HOLIDAY. LoanInstallment: Loan or Advance.</summary>
        public string Variant { get; set; }
        /// <summary>PercentOfItems: comma separated item codes.</summary>
        public string ItemCodes { get; set; }
        /// <summary>Formula: restricted expression (item codes, built-in variables, RATE_Key, + - * / ( ), min, max, round).</summary>
        public string Formula { get; set; }
        public decimal Factor { get; set; }
        public decimal Factor2 { get; set; }
        /// <summary>FixedMonthly: multiply by paid days / 30.</summary>
        public bool ProRata { get; set; }
        /// <summary>Empty, Married, HasChildren or HardAndHarmful: the item applies only when true.</summary>
        public string Condition { get; set; }
        /// <summary>The amount is deducted from the taxable base (employee social security).</summary>
        public bool ReducesTaxable { get; set; }
        /// <summary>Earning: counts toward the severance accrual base (SEVERANCE_BASE). When no item is flagged the whole insurable pay is the base (the old behaviour).</summary>
        public bool SeveranceBase { get; set; }
        /// <summary>Deduction / contribution / provision: the liability role credited. Earning: when set (a provision role) the earning is paid from that provision instead of being expensed.</summary>
        public string AccountRole { get; set; }
        public int Order { get; set; }
        public bool IsActive { get; set; } = true;
        public string Jurisdiction { get; set; }
        public string Source { get; set; }
        public bool Verify { get; set; }
    }

    /// <summary>One value of the payroll rate table. Year 0 = valid for every year (long-standing ratios); a year row wins.</summary>
    public class FnPayrollRateTable
    {
        public int Id { get; set; }
        public int Year { get; set; }
        public string Key { get; set; }
        public decimal Value { get; set; }
        public string Source { get; set; }
        public bool Verify { get; set; }
        public string Note { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }
    }

    /// <summary>Yearly income tax bracket on annual taxable income (rial).</summary>
    public class FnTaxBracket
    {
        public int Id { get; set; }
        public int Year { get; set; }
        public decimal FromAmount { get; set; }
        public decimal? ToAmount { get; set; }
        public decimal RatePercent { get; set; }
    }

    // ------------------------------------------------------------------------------------------ attendance and leave

    public class FnAttendanceSheet
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public FnEmployee Employee { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal DaysWorked { get; set; }
        public decimal OvertimeHours { get; set; }
        public decimal NightHours { get; set; }
        public decimal HolidayHours { get; set; }
        /// <summary>0 = none, 1 = two-shift, 2 = three-shift, 3 = night shift.</summary>
        public int ShiftType { get; set; }
        public decimal PaidLeaveDays { get; set; }
        /// <summary>Approved sick leave days (insurance pays them); not taken from the annual leave balance.</summary>
        public decimal SickLeaveDays { get; set; }
        public decimal UnpaidLeaveDays { get; set; }
        public decimal AbsenceDays { get; set; }
        public decimal MissionDays { get; set; }
        /// <summary>Inputs of the Input items as JSON (item code to amount).</summary>
        public string InputsJson { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public int UpdatedByUserId { get; set; }
        public ICollection<FnAttendanceWorkOrder> WorkOrders { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class FnAttendanceWorkOrder
    {
        public int Id { get; set; }
        public int SheetId { get; set; }
        public FnAttendanceSheet Sheet { get; set; }
        public int WorkOrderId { get; set; }
        public decimal Hours { get; set; }
    }

    public class FnLeaveBalance
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public int Year { get; set; }
        public decimal CarriedForward { get; set; }
        public decimal Entitled { get; set; }
        public decimal Used { get; set; }
        /// <summary>The last month (1..12) whose entitlement and use were added.</summary>
        public int LastMonth { get; set; }
    }

    // ------------------------------------------------------------------------------------------ payroll runs and payslips

    public enum FnPayrollStatus { Draft = 0, Calculated = 1, Approved = 2, Posted = 3, Paid = 4 }

    public class FnPayrollRun
    {
        public int Id { get; set; }
        /// <summary>Everyone who edited the draft (",5,7,"): the checker or approver must be neither the creator nor any editor.</summary>
        public string EditorUserIds { get; set; }
        public string Number { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public int? WorkshopId { get; set; }
        public FnPayrollStatus Status { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        /// <summary>The user who calculated last; cannot approve.</summary>
        public int PreparedByUserId { get; set; }
        public DateTimeOffset? CalculatedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? PostedByUserId { get; set; }
        public DateTimeOffset? PostedAt { get; set; }
        public DateTimeOffset? PaidAt { get; set; }
        public int EmployeeCount { get; set; }
        public decimal TotalGross { get; set; }
        public decimal TotalInsurable { get; set; }
        public decimal TotalTaxable { get; set; }
        public decimal TotalEmployeeInsurance { get; set; }
        public decimal TotalEmployerInsurance { get; set; }
        public decimal TotalTax { get; set; }
        public decimal TotalOtherDeductions { get; set; }
        public decimal TotalNet { get; set; }
        public decimal TotalProvisions { get; set; }
        public int? AccountingEventId { get; set; }
        public string Note { get; set; }
        public ICollection<FnPayslip> Payslips { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>Confidential: person-level salary data.</summary>
    public class FnPayslip
    {
        public int Id { get; set; }
        public int RunId { get; set; }
        public FnPayrollRun Run { get; set; }
        public int EmployeeId { get; set; }
        public FnEmployee Employee { get; set; }
        public int? WorkshopId { get; set; }
        public int? CostCenterId { get; set; }
        public decimal DaysPaid { get; set; }
        public decimal GrossEarnings { get; set; }
        public decimal InsurableTotal { get; set; }
        public decimal TaxableTotal { get; set; }
        public decimal EmployeeInsurance { get; set; }
        public decimal EmployerInsurance { get; set; }
        public decimal Tax { get; set; }
        public decimal OtherDeductions { get; set; }
        public decimal Provisions { get; set; }
        public decimal Net { get; set; }
        public ICollection<FnPayslipLine> Lines { get; set; }
        public ICollection<FnPayslipAllocation> Allocations { get; set; }
    }

    public class FnPayslipLine
    {
        public int Id { get; set; }
        public int PayslipId { get; set; }
        public FnPayslip Payslip { get; set; }
        public int RowNumber { get; set; }
        public string ItemCode { get; set; }
        public string Title { get; set; }
        public FnPayrollItemKind Kind { get; set; }
        public decimal Base { get; set; }
        public decimal Amount { get; set; }
        public bool Insurable { get; set; }
        public bool Taxable { get; set; }
        /// <summary>The liability role credited (snapshot of the item).</summary>
        public string AccountRole { get; set; }
    }

    /// <summary>How the cost of a payslip is charged to work orders (share of 1).</summary>
    public class FnPayslipAllocation
    {
        public int Id { get; set; }
        public int PayslipId { get; set; }
        public FnPayslip Payslip { get; set; }
        public int WorkOrderId { get; set; }
        public decimal Share { get; set; }
    }
}
