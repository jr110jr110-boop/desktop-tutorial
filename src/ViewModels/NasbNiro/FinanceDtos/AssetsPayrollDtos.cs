using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Finance;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.FinanceDtos
{
    // Release F2: Sepidar outbox, fixed assets, payroll and insurance.

    // ============================================================================================ set-up check of the optional modules

    public class SetupModuleDto
    {
        /// <summary>Assets or Payroll.</summary>
        public string Module { get; set; }
        public bool Ok { get; set; }
        /// <summary>Missing rate keys, tax brackets or asset class rates (names only, never values).</summary>
        public List<string> Missing { get; set; } = new List<string>();
        public string Message { get; set; }
    }

    // ============================================================================================ Sepidar outbox

    public class OutboxQuery : PaginationParams
    {
        public FnOutboxKind? Kind { get; set; }
        public FnOutboxStatus? Status { get; set; }
        public int? Year { get; set; }
        public int? Month { get; set; }
    }

    public class OutboxTableInfoDto
    {
        public string Name { get; set; }
        public int Rows { get; set; }
    }

    public class OutboxPackageDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public FnOutboxKind Kind { get; set; }
        public int Year { get; set; }
        public int? Month { get; set; }
        public int? Season { get; set; }
        public int? WorkshopId { get; set; }
        public FnOutboxStatus Status { get; set; }
        public int Version { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public string ContentHash { get; set; }
        public int RowCount { get; set; }
        public List<OutboxTableInfoDto> Tables { get; set; } = new List<OutboxTableInfoDto>();
        public int? SupersededByPackageId { get; set; }
        public string ExternalReference { get; set; }
        public string Error { get; set; }
        public string Note { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public DateTimeOffset? ExportedAt { get; set; }
        public DateTimeOffset? SentAt { get; set; }
    }

    public class GenerateOutboxDto
    {
        public FnOutboxKind Kind { get; set; }
        public int Year { get; set; }
        public int? Month { get; set; }
        public int? Season { get; set; }
        public int? WorkshopId { get; set; }
        public string Note { get; set; }
    }

    public class OutboxActionDto
    {
        public int Id { get; set; }
        /// <summary>approve, send, mark-sent, mark-accepted, mark-rejected</summary>
        public string Action { get; set; }
        public string Note { get; set; }
        public string Reference { get; set; }
    }

    public class OutboxSummaryDto
    {
        public int Pending { get; set; }
        public int ReadyToSend { get; set; }
        public int Failed { get; set; }
        public DateTimeOffset? LastSentAt { get; set; }
    }

    // ============================================================================================ fixed assets

    public class AssetClassDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string AssetAccountCode { get; set; }
        public string AccumulatedAccountCode { get; set; }
        public FnDepreciationMethod Method { get; set; }
        public decimal? RatePercent { get; set; }
        public decimal? UsefulLifeYears { get; set; }
        public FnDepreciationStart StartRule { get; set; }
        public decimal IdleRatePercent { get; set; }
        public int IdleAfterMonths { get; set; }
        public decimal SalvagePercent { get; set; }
        public string Source { get; set; }
        public bool Verify { get; set; }
        public bool IsIntangible { get; set; }
        public bool IsActive { get; set; }
        /// <summary>False until finance has entered the rate or the useful life.</summary>
        public bool HasRate { get; set; }
    }

    public class SaveAssetClassDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string AssetAccountCode { get; set; }
        public string AccumulatedAccountCode { get; set; }
        public FnDepreciationMethod Method { get; set; }
        public decimal? RatePercent { get; set; }
        public decimal? UsefulLifeYears { get; set; }
        public FnDepreciationStart? StartRule { get; set; }
        public decimal? IdleRatePercent { get; set; }
        public int? IdleAfterMonths { get; set; }
        public decimal? SalvagePercent { get; set; }
        public string Source { get; set; }
        public bool Verify { get; set; }
        public bool IsIntangible { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class AssetQuery : PaginationParams
    {
        public FnAssetStatus? Status { get; set; }
        public int? ClassId { get; set; }
        public int? CostCenterId { get; set; }
        public int? CustodianEmployeeId { get; set; }
        public string Contains { get; set; }
    }

    public class DepreciationRunQuery : PaginationParams
    {
        public int? Year { get; set; }
        public FnRunStatus? Status { get; set; }
    }

    public class DisposalQuery : PaginationParams
    {
        public FnDocStatus? Status { get; set; }
        public int? AssetId { get; set; }
    }

    public class AssetHistoryDto
    {
        public DateTime Date { get; set; }
        public FnAssetHistoryKind Kind { get; set; }
        public decimal Amount { get; set; }
        public string Note { get; set; }
        public string UserName { get; set; }
    }

    public class AssetDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public int? ClassId { get; set; }
        public string ClassCode { get; set; }
        public string ClassTitle { get; set; }
        public string Serial { get; set; }
        public string Maker { get; set; }
        public string Location { get; set; }
        public int? CustodianEmployeeId { get; set; }
        public string CustodianTitle { get; set; }
        public int? CostCenterId { get; set; }
        public string CostCenterCode { get; set; }
        public int? MachineId { get; set; }
        public DateTime? AcquisitionDate { get; set; }
        public DateTime? InUseDate { get; set; }
        public decimal Cost { get; set; }
        public decimal SalvageValue { get; set; }
        public FnAssetSource Source { get; set; }
        public int? SourceId { get; set; }
        public string SourceNumber { get; set; }
        public string OffsetAccountCode { get; set; }
        public decimal OpeningAccumulated { get; set; }
        public decimal AccumulatedDepreciation { get; set; }
        public decimal BookValue { get; set; }
        public DateTime? DepreciatedThrough { get; set; }
        public FnAssetStatus Status { get; set; }
        public DateTime? DisposedAt { get; set; }
        public string CreatorName { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public List<AssetHistoryDto> History { get; set; } = new List<AssetHistoryDto>();
    }

    public class SaveAssetDto
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public int? ClassId { get; set; }
        public string Serial { get; set; }
        public string Maker { get; set; }
        public string Location { get; set; }
        public int? CustodianEmployeeId { get; set; }
        public string CustodianTitle { get; set; }
        public int? CostCenterId { get; set; }
        public int? MachineId { get; set; }
        public DateTime? AcquisitionDate { get; set; }
        public DateTime? InUseDate { get; set; }
        public decimal Cost { get; set; }
        public decimal? SalvageValue { get; set; }
        public FnAssetSource Source { get; set; } = FnAssetSource.Manual;
        public string SourceNumber { get; set; }
        /// <summary>Account credited at approval (where the cost sits now); not used for opening assets.</summary>
        public string OffsetAccountCode { get; set; }
        /// <summary>Opening assets: accumulated depreciation of the old books.</summary>
        public decimal? OpeningAccumulated { get; set; }
        /// <summary>Opening assets: any date inside the last Persian month already depreciated in the old books.</summary>
        public DateTime? OpeningDepreciatedThrough { get; set; }
    }

    public class AssetActionDto
    {
        public int Id { get; set; }
        /// <summary>approve, commission, idle-from, idle-to, transfer, improve</summary>
        public string Action { get; set; }
        public DateTime? Date { get; set; }
        public decimal? Amount { get; set; }
        public string Note { get; set; }
        public string OffsetAccountCode { get; set; }
        public int? CostCenterId { get; set; }
        public int? CustodianEmployeeId { get; set; }
        public string CustodianTitle { get; set; }
        public string Location { get; set; }
    }

    public class DepreciationLineDto
    {
        public int AssetId { get; set; }
        public string AssetCode { get; set; }
        public string AssetTitle { get; set; }
        public string ClassCode { get; set; }
        public int? CostCenterId { get; set; }
        public int Months { get; set; }
        public int IdleMonths { get; set; }
        public decimal Amount { get; set; }
        public decimal BookValueBefore { get; set; }
        public decimal AccumulatedAfter { get; set; }
    }

    public class DepreciationRunDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int PeriodId { get; set; }
        public string PeriodTitle { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public FnRunStatus Status { get; set; }
        public decimal TotalAmount { get; set; }
        public int AssetCount { get; set; }
        public string PreparedByName { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset PreparedAt { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public List<DepreciationLineDto> Lines { get; set; } = new List<DepreciationLineDto>();
    }

    public class DepreciationActionDto
    {
        public int Id { get; set; }
        /// <summary>approve, delete</summary>
        public string Action { get; set; }
        public string Note { get; set; }
    }

    public class SaveDisposalDto
    {
        public int? Id { get; set; }
        public int AssetId { get; set; }
        public DateTime Date { get; set; }
        public FnDisposalKind Kind { get; set; }
        public decimal ProceedsAmount { get; set; }
        /// <summary>VAT percent on a sale (price is without VAT). Empty = none; ApplyStandardVat takes the VAT rate of the date from the tax rates.</summary>
        public decimal? VatPercent { get; set; }
        public bool ApplyStandardVat { get; set; }
        public FnProceedsMethod ProceedsMethod { get; set; }
        public int? CashBoxId { get; set; }
        public int? BankAccountId { get; set; }
        public int? CustomerId { get; set; }
        public string Note { get; set; }
    }

    public class DisposalDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int AssetId { get; set; }
        public string AssetCode { get; set; }
        public string AssetTitle { get; set; }
        public DateTime Date { get; set; }
        public FnDisposalKind Kind { get; set; }
        public FnDocStatus Status { get; set; }
        public decimal Cost { get; set; }
        public decimal ProceedsAmount { get; set; }
        public decimal VatPercent { get; set; }
        public decimal VatAmount { get; set; }
        public FnProceedsMethod ProceedsMethod { get; set; }
        public decimal CatchUpDepreciation { get; set; }
        public decimal AccumulatedAtDisposal { get; set; }
        public decimal BookValue { get; set; }
        public decimal GainLoss { get; set; }
        public string Note { get; set; }
        public string PreparedByName { get; set; }
        public string ApprovedByName { get; set; }
    }

    public class DepreciationScheduleRowDto
    {
        public int AssetId { get; set; }
        public string AssetCode { get; set; }
        public string AssetTitle { get; set; }
        public string ClassCode { get; set; }
        /// <summary>Depreciation per Persian month 1..12 of the year (approved runs only).</summary>
        public List<decimal> Months { get; set; } = new List<decimal>();
        public decimal Total { get; set; }
    }

    public class DepreciationScheduleDto
    {
        public int Year { get; set; }
        public List<DepreciationScheduleRowDto> Assets { get; set; } = new List<DepreciationScheduleRowDto>();
        public List<DepreciationScheduleRowDto> Classes { get; set; } = new List<DepreciationScheduleRowDto>();
        public decimal Total { get; set; }
    }

    public class AssetMovementRowDto
    {
        public string ClassCode { get; set; }
        public string ClassTitle { get; set; }
        public decimal OpeningCost { get; set; }
        public decimal Additions { get; set; }
        public decimal Disposals { get; set; }
        public decimal ClosingCost { get; set; }
        public decimal OpeningAccumulated { get; set; }
        public decimal Depreciation { get; set; }
        public decimal DisposalAccumulated { get; set; }
        public decimal ClosingAccumulated { get; set; }
        public decimal NetBookValue { get; set; }
    }

    public class AssetMovementDto
    {
        public int Year { get; set; }
        public List<AssetMovementRowDto> Rows { get; set; } = new List<AssetMovementRowDto>();
        public AssetMovementRowDto Total { get; set; } = new AssetMovementRowDto();
    }

    public class AssetHandoverDto
    {
        public int? EmployeeId { get; set; }
        public string CustodianName { get; set; }
        public string CustodianTitle { get; set; }
        public DateTime PrintedOn { get; set; }
        public List<AssetDto> Assets { get; set; } = new List<AssetDto>();
    }

    // ============================================================================================ payroll masters (confidential)

    public class WorkshopDto
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
        public bool IsActive { get; set; }
    }

    public class SaveWorkshopDto
    {
        public int? Id { get; set; }
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

    public class EmployeeQuery : PaginationParams
    {
        public int? WorkshopId { get; set; }
        public int? CostCenterId { get; set; }
        public bool? IsActive { get; set; }
        public string Contains { get; set; }
    }

    public class EmployeeDto
    {
        public int Id { get; set; }
        public string PersonnelCode { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string FatherName { get; set; }
        public string NationalCode { get; set; }
        public DateTime? BirthDate { get; set; }
        public string BirthPlace { get; set; }
        public int Gender { get; set; }
        public int MaritalStatus { get; set; }
        public int ChildrenCount { get; set; }
        public string InsuranceNumber { get; set; }
        public string InsuranceJobCode { get; set; }
        public int? WorkshopId { get; set; }
        public string WorkshopName { get; set; }
        public DateTime HireDate { get; set; }
        public DateTime? EndDate { get; set; }
        public FnEmploymentType EmploymentType { get; set; }
        public string JobTitle { get; set; }
        public string Unit { get; set; }
        public int? CostCenterId { get; set; }
        public int? DefaultWorkOrderId { get; set; }
        public string BankIban { get; set; }
        public decimal DailyWage { get; set; }
        public string ExemptionFlags { get; set; }
        public bool HardAndHarmful { get; set; }
        public int? UserId { get; set; }
        public int? QualifiedPersonId { get; set; }
        public bool IsActive { get; set; }
    }

    public class SaveEmployeeDto
    {
        public int? Id { get; set; }
        public string PersonnelCode { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string FatherName { get; set; }
        public string NationalCode { get; set; }
        public DateTime? BirthDate { get; set; }
        public string BirthPlace { get; set; }
        public int Gender { get; set; } = 1;
        public int MaritalStatus { get; set; } = 1;
        public int ChildrenCount { get; set; }
        public string InsuranceNumber { get; set; }
        public string InsuranceJobCode { get; set; }
        public int? WorkshopId { get; set; }
        public DateTime HireDate { get; set; }
        public DateTime? EndDate { get; set; }
        public FnEmploymentType EmploymentType { get; set; } = FnEmploymentType.Permanent;
        public string JobTitle { get; set; }
        public string Unit { get; set; }
        public int? CostCenterId { get; set; }
        public int? DefaultWorkOrderId { get; set; }
        public string BankIban { get; set; }
        public decimal DailyWage { get; set; }
        public string ExemptionFlags { get; set; }
        public bool HardAndHarmful { get; set; }
        public int? UserId { get; set; }
        public int? QualifiedPersonId { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>What other modules may see of an employee: name and job title only.</summary>
    public class EmployeeDirectoryDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string JobTitle { get; set; }
        public string Unit { get; set; }
    }

    public class LoanQuery : PaginationParams
    {
        public int? EmployeeId { get; set; }
        public bool? IsActive { get; set; }
    }

    public class LoanDto
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string PersonnelCode { get; set; }
        public FnLoanKind Kind { get; set; }
        public string Title { get; set; }
        public decimal Principal { get; set; }
        public decimal InstallmentAmount { get; set; }
        public decimal Balance { get; set; }
        public int FirstYear { get; set; }
        public int FirstMonth { get; set; }
        public bool IsActive { get; set; }
    }

    public class SaveLoanDto
    {
        public int? Id { get; set; }
        public int EmployeeId { get; set; }
        public FnLoanKind Kind { get; set; } = FnLoanKind.Loan;
        public string Title { get; set; }
        public decimal Principal { get; set; }
        public decimal InstallmentAmount { get; set; }
        public int FirstYear { get; set; }
        public int FirstMonth { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class PayrollItemDto
    {
        public bool SeveranceBase { get; set; }
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public FnPayrollItemKind Kind { get; set; }
        public bool Insurable { get; set; }
        public bool Taxable { get; set; }
        public FnPayrollCalc Calculation { get; set; }
        public string RateKey { get; set; }
        public string Variant { get; set; }
        public string ItemCodes { get; set; }
        public string Formula { get; set; }
        public decimal Factor { get; set; }
        public decimal Factor2 { get; set; }
        public bool ProRata { get; set; }
        public string Condition { get; set; }
        public bool ReducesTaxable { get; set; }
        public string AccountRole { get; set; }
        public int Order { get; set; }
        public bool IsActive { get; set; }
        public string Source { get; set; }
        public bool Verify { get; set; }
    }

    public class SavePayrollItemDto
    {
        public bool SeveranceBase { get; set; }
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public FnPayrollItemKind Kind { get; set; }
        public bool Insurable { get; set; }
        public bool Taxable { get; set; }
        public FnPayrollCalc Calculation { get; set; }
        public string RateKey { get; set; }
        public string Variant { get; set; }
        public string ItemCodes { get; set; }
        public string Formula { get; set; }
        public decimal Factor { get; set; } = 1;
        public decimal Factor2 { get; set; }
        public bool ProRata { get; set; }
        public string Condition { get; set; }
        public bool ReducesTaxable { get; set; }
        public string AccountRole { get; set; }
        public int Order { get; set; }
        public bool IsActive { get; set; } = true;
        public string Source { get; set; }
        public bool Verify { get; set; }
    }

    public class RateQuery : PaginationParams
    {
        public int? Year { get; set; }
        public string Key { get; set; }
    }

    public class PayrollRateDto
    {
        public int Id { get; set; }
        public int Year { get; set; }
        public string Key { get; set; }
        public decimal Value { get; set; }
        public string Source { get; set; }
        public bool Verify { get; set; }
        public string Note { get; set; }
    }

    public class SavePayrollRateDto
    {
        public int Year { get; set; }
        public string Key { get; set; }
        public decimal Value { get; set; }
        public string Source { get; set; }
        public bool Verify { get; set; }
        public string Note { get; set; }
    }

    public class TaxBracketDto
    {
        public decimal FromAmount { get; set; }
        public decimal? ToAmount { get; set; }
        public decimal RatePercent { get; set; }
    }

    public class SaveTaxBracketsDto
    {
        public int Year { get; set; }
        public List<TaxBracketDto> Brackets { get; set; } = new List<TaxBracketDto>();
    }

    public class PayrollSetupDto
    {
        public int Year { get; set; }
        public bool Ok { get; set; }
        public List<string> MissingRateKeys { get; set; } = new List<string>();
        public bool TaxBracketsMissing { get; set; }
        public List<string> Items { get; set; } = new List<string>();
    }

    // ============================================================================================ attendance

    public class AttendanceQuery : PaginationParams
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int? EmployeeId { get; set; }
    }

    public class AttendanceWorkOrderDto
    {
        public int WorkOrderId { get; set; }
        public decimal Hours { get; set; }
    }

    public class AttendanceDto
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string PersonnelCode { get; set; }
        public string EmployeeName { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal DaysWorked { get; set; }
        public decimal OvertimeHours { get; set; }
        public decimal NightHours { get; set; }
        public decimal HolidayHours { get; set; }
        public int ShiftType { get; set; }
        public decimal PaidLeaveDays { get; set; }
        public decimal SickLeaveDays { get; set; }
        public decimal UnpaidLeaveDays { get; set; }
        public decimal AbsenceDays { get; set; }
        public decimal MissionDays { get; set; }
        public Dictionary<string, decimal> Inputs { get; set; } = new Dictionary<string, decimal>();
        public List<AttendanceWorkOrderDto> WorkOrders { get; set; } = new List<AttendanceWorkOrderDto>();
    }

    public class SaveAttendanceDto
    {
        public int? EmployeeId { get; set; }
        /// <summary>Alternative to EmployeeId (used by imports from attendance devices or Excel).</summary>
        public string PersonnelCode { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal DaysWorked { get; set; }
        public decimal OvertimeHours { get; set; }
        public decimal NightHours { get; set; }
        public decimal HolidayHours { get; set; }
        public int ShiftType { get; set; }
        public decimal PaidLeaveDays { get; set; }
        public decimal UnpaidLeaveDays { get; set; }
        public decimal AbsenceDays { get; set; }
        public decimal MissionDays { get; set; }
        public Dictionary<string, decimal> Inputs { get; set; } = new Dictionary<string, decimal>();
        public List<AttendanceWorkOrderDto> WorkOrders { get; set; } = new List<AttendanceWorkOrderDto>();
    }

    public class ImportAttendanceDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public List<SaveAttendanceDto> Rows { get; set; } = new List<SaveAttendanceDto>();
    }

    public class ImportAttendanceResultDto
    {
        public int Imported { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    // ============================================================================================ payroll runs and payslips (confidential)

    public class PayrollRunQuery : PaginationParams
    {
        public int? Year { get; set; }
        public int? Month { get; set; }
        public FnPayrollStatus? Status { get; set; }
    }

    public class CreatePayrollRunDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int? WorkshopId { get; set; }
        public string Note { get; set; }
    }

    public class PayrollRunDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public int? WorkshopId { get; set; }
        public FnPayrollStatus Status { get; set; }
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
        public string CreatorName { get; set; }
        public string PreparedByName { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? CalculatedAt { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public DateTimeOffset? PostedAt { get; set; }
        public DateTimeOffset? PaidAt { get; set; }
        public int? AccountingEventId { get; set; }
        public string Note { get; set; }
    }

    public class PayslipQuery : PaginationParams
    {
        public int? RunId { get; set; }
        public int? EmployeeId { get; set; }
        public int? Year { get; set; }
        public int? Month { get; set; }
    }

    public class PayslipLineDto
    {
        public int RowNumber { get; set; }
        public string ItemCode { get; set; }
        public string Title { get; set; }
        public FnPayrollItemKind Kind { get; set; }
        public decimal Base { get; set; }
        public decimal Amount { get; set; }
        public bool Insurable { get; set; }
        public bool Taxable { get; set; }
    }

    public class PayslipDto
    {
        public int Id { get; set; }
        public int RunId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public FnPayrollStatus RunStatus { get; set; }
        public int EmployeeId { get; set; }
        public string PersonnelCode { get; set; }
        public string EmployeeName { get; set; }
        public string JobTitle { get; set; }
        public string Unit { get; set; }
        public string NationalCode { get; set; }
        public string InsuranceNumber { get; set; }
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
        public List<PayslipLineDto> Lines { get; set; } = new List<PayslipLineDto>();
    }

    public class PayrollCalcResultDto
    {
        public PayrollRunDto Run { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class InsuranceListRowDto
    {
        public string PersonnelCode { get; set; }
        public string InsuranceNumber { get; set; }
        public string NationalCode { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string FatherName { get; set; }
        public DateTime? BirthDate { get; set; }
        public string JobCode { get; set; }
        public decimal DaysWorked { get; set; }
        public decimal DailyWage { get; set; }
        public decimal MonthlyWage { get; set; }
        public decimal Benefits { get; set; }
        public decimal InsurableTotal { get; set; }
        /// <summary>The insurable total after the ceiling (days x daily ceiling): the base of the shares.</summary>
        public decimal InsurableCapped { get; set; }
        public decimal EmployeeShare { get; set; }
        public decimal EmployerShare { get; set; }
        public decimal Unemployment { get; set; }
        public decimal HardAndHarmful { get; set; }
    }

    public class InsuranceListDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int? WorkshopId { get; set; }
        public string WorkshopCode { get; set; }
        public string WorkshopName { get; set; }
        public int Employees { get; set; }
        public decimal TotalInsurable { get; set; }
        public decimal TotalEmployeeShare { get; set; }
        public decimal TotalEmployerShare { get; set; }
        public decimal TotalUnemployment { get; set; }
        public List<InsuranceListRowDto> Rows { get; set; } = new List<InsuranceListRowDto>();
    }

    public class TaxListRowDto
    {
        public string PersonnelCode { get; set; }
        public string NationalCode { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public decimal TaxableTotal { get; set; }
        public decimal EmployeeInsurance { get; set; }
        public decimal Tax { get; set; }
    }

    public class TaxListDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int Employees { get; set; }
        public decimal TotalTaxable { get; set; }
        public decimal TotalTax { get; set; }
        public List<TaxListRowDto> Rows { get; set; } = new List<TaxListRowDto>();
    }

    public class EmployeeYearSummaryDto
    {
        public int EmployeeId { get; set; }
        public string PersonnelCode { get; set; }
        public string EmployeeName { get; set; }
        public int Year { get; set; }
        public int Months { get; set; }
        public decimal Gross { get; set; }
        public decimal EmployeeInsurance { get; set; }
        public decimal Tax { get; set; }
        public decimal OtherDeductions { get; set; }
        public decimal Net { get; set; }
    }

    public class BankTransferRowDto
    {
        public string Iban { get; set; }
        public decimal Amount { get; set; }
        public string Name { get; set; }
        public string PersonnelCode { get; set; }
    }

    public class LeaveBalanceDto
    {
        public int EmployeeId { get; set; }
        public string PersonnelCode { get; set; }
        public int Year { get; set; }
        public decimal CarriedForward { get; set; }
        public decimal Entitled { get; set; }
        public decimal Used { get; set; }
        public decimal Balance { get; set; }
    }
}
