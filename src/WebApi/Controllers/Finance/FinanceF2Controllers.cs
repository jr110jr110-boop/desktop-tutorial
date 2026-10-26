using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Finance;
using ViewModels.NasbNiro.FinanceDtos;
using WebApi.Extensions;
using WebApi.Filters;

namespace WebApi.Controllers
{
    // Release F2: fixed assets, payroll and insurance, and the Sepidar outbox. Role and segregation checks per step are in the services;
    // payroll data is confidential (PAYROLL, FINANCEMANAGER, CEO only).
    internal static class F2Files
    {
        public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/finance/assets")]
    [RolesCheckedInService]
    public class AssetsController : ControllerBase
    {
        private readonly IAssetService _assets;

        public AssetsController(IAssetService assets) { _assets = assets; }

        [HttpGet("classes")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ClassesAsync(CancellationToken ct) => Ok(await _assets.GetClassesAsync(ct));

        [HttpPost("classes")]
        public async Task<IActionResult> SaveClassAsync([FromBody] SaveAssetClassDto dto, CancellationToken ct) => Ok(await _assets.SaveClassAsync(dto, ct));

        [HttpGet]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> AssetsAsync([FromQuery] AssetQuery query, CancellationToken ct) => Ok((await _assets.GetAssetsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("{id:int}")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> AssetAsync(int id, CancellationToken ct) => Ok(await _assets.GetAssetAsync(id, ct));

        [HttpPost]
        public async Task<IActionResult> SaveAssetAsync([FromBody] SaveAssetDto dto, CancellationToken ct) => Ok(await _assets.SaveAssetAsync(dto, ct));

        [HttpPost("action")]
        public async Task<IActionResult> AssetActionAsync([FromBody] AssetActionDto dto, CancellationToken ct) => Ok(await _assets.AssetActionAsync(dto, ct));

        [HttpGet("depreciation/runs")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> RunsAsync([FromQuery] DepreciationRunQuery query, CancellationToken ct) => Ok((await _assets.GetDepreciationRunsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("depreciation/runs/{id:int}")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> RunAsync(int id, CancellationToken ct) => Ok(await _assets.GetDepreciationRunAsync(id, ct));

        /// <summary>Calculates (or recalculates) the draft depreciation of a fiscal period. A different person approves it.</summary>
        [HttpPost("depreciation/prepare/{periodId:int}")]
        public async Task<IActionResult> PrepareAsync(int periodId, CancellationToken ct) => Ok(await _assets.PrepareDepreciationAsync(periodId, ct));

        [HttpPost("depreciation/action")]
        public async Task<IActionResult> DepreciationActionAsync([FromBody] DepreciationActionDto dto, CancellationToken ct) => Ok(await _assets.DepreciationActionAsync(dto, ct));

        [HttpGet("disposals")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> DisposalsAsync([FromQuery] DisposalQuery query, CancellationToken ct) => Ok((await _assets.GetDisposalsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("disposals")]
        public async Task<IActionResult> SaveDisposalAsync([FromBody] SaveDisposalDto dto, CancellationToken ct) => Ok(await _assets.SaveDisposalAsync(dto, ct));

        [HttpPost("disposals/action")]
        public async Task<IActionResult> DisposalActionAsync([FromBody] DocActionDto dto, CancellationToken ct) => Ok(await _assets.DisposalActionAsync(dto, ct));

        [HttpGet("reports/depreciation-schedule")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ScheduleAsync([FromQuery] int year, CancellationToken ct) => Ok(await _assets.GetDepreciationScheduleAsync(year, ct));

        [HttpGet("reports/movement")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> MovementAsync([FromQuery] int year, CancellationToken ct) => Ok(await _assets.GetMovementAsync(year, ct));

        [HttpGet("reports/handover")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> HandoverAsync([FromQuery] int? employeeId, [FromQuery] string custodian, CancellationToken ct) => Ok(await _assets.GetHandoverAsync(employeeId, custodian, ct));

        [HttpGet("setup-check")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> SetupCheckAsync(CancellationToken ct) => Ok(await _assets.SetupCheckAsync(ct));
    }

    [ApiController]
    [Authorize(Roles = FinanceRoles.PayrollReaders)]
    [Route("api/v1/finance/payroll")]
    public class PayrollController : ControllerBase
    {
        private readonly IPayrollMasterService _master;
        private readonly IPayrollRunService _runs;

        public PayrollController(IPayrollMasterService master, IPayrollRunService runs)
        {
            _master = master;
            _runs = runs;
        }

        [HttpGet("setup")]
        public async Task<IActionResult> SetupAsync([FromQuery] int year, CancellationToken ct) => Ok(await _master.GetSetupAsync(year, ct));

        [HttpGet("workshops")]
        public async Task<IActionResult> WorkshopsAsync(CancellationToken ct) => Ok(await _master.GetWorkshopsAsync(ct));

        [HttpPost("workshops")]
        public async Task<IActionResult> SaveWorkshopAsync([FromBody] SaveWorkshopDto dto, CancellationToken ct) => Ok(await _master.SaveWorkshopAsync(dto, ct));

        [HttpGet("employees")]
        public async Task<IActionResult> EmployeesAsync([FromQuery] EmployeeQuery query, CancellationToken ct) => Ok((await _master.GetEmployeesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("employees/{id:int}")]
        public async Task<IActionResult> EmployeeAsync(int id, CancellationToken ct) => Ok(await _master.GetEmployeeAsync(id, ct));

        [HttpPost("employees")]
        public async Task<IActionResult> SaveEmployeeAsync([FromBody] SaveEmployeeDto dto, CancellationToken ct) => Ok(await _master.SaveEmployeeAsync(dto, ct));

        [HttpGet("loans")]
        public async Task<IActionResult> LoansAsync([FromQuery] LoanQuery query, CancellationToken ct) => Ok((await _master.GetLoansAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("loans")]
        public async Task<IActionResult> SaveLoanAsync([FromBody] SaveLoanDto dto, CancellationToken ct) => Ok(await _master.SaveLoanAsync(dto, ct));

        [HttpGet("items")]
        public async Task<IActionResult> ItemsAsync(CancellationToken ct) => Ok(await _master.GetItemsAsync(ct));

        [HttpPost("items")]
        public async Task<IActionResult> SaveItemAsync([FromBody] SavePayrollItemDto dto, CancellationToken ct) => Ok(await _master.SaveItemAsync(dto, ct));

        [HttpGet("rates")]
        public async Task<IActionResult> RatesAsync([FromQuery] RateQuery query, CancellationToken ct) => Ok(await _master.GetRatesAsync(query, ct));

        [HttpPost("rates")]
        public async Task<IActionResult> SaveRateAsync([FromBody] SavePayrollRateDto dto, CancellationToken ct) => Ok(await _master.SaveRateAsync(dto, ct));

        [HttpGet("tax-brackets")]
        public async Task<IActionResult> BracketsAsync([FromQuery] int year, CancellationToken ct) => Ok(await _master.GetBracketsAsync(year, ct));

        [HttpPost("tax-brackets")]
        public async Task<IActionResult> SaveBracketsAsync([FromBody] SaveTaxBracketsDto dto, CancellationToken ct) => Ok(await _master.SaveBracketsAsync(dto, ct));

        [HttpGet("attendance")]
        public async Task<IActionResult> AttendanceAsync([FromQuery] AttendanceQuery query, CancellationToken ct) => Ok((await _master.GetAttendanceAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("attendance")]
        public async Task<IActionResult> SaveAttendanceAsync([FromBody] SaveAttendanceDto dto, CancellationToken ct) => Ok(await _master.SaveAttendanceAsync(dto, ct));

        [HttpPost("attendance/import")]
        public async Task<IActionResult> ImportAttendanceAsync([FromBody] ImportAttendanceDto dto, CancellationToken ct) => Ok(await _master.ImportAttendanceAsync(dto, ct));

        [HttpGet("leave-balances")]
        public async Task<IActionResult> LeaveBalancesAsync([FromQuery] int year, CancellationToken ct) => Ok(await _master.GetLeaveBalancesAsync(year, ct));

        [HttpGet("runs")]
        public async Task<IActionResult> RunsAsync([FromQuery] PayrollRunQuery query, CancellationToken ct) => Ok((await _runs.GetRunsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("runs/{id:int}")]
        public async Task<IActionResult> RunAsync(int id, CancellationToken ct) => Ok(await _runs.GetRunAsync(id, ct));

        [HttpPost("runs")]
        public async Task<IActionResult> CreateRunAsync([FromBody] CreatePayrollRunDto dto, CancellationToken ct) => Ok(await _runs.CreateRunAsync(dto, ct));

        [HttpPost("runs/{id:int}/calculate")]
        public async Task<IActionResult> CalculateAsync(int id, CancellationToken ct) => Ok(await _runs.CalculateAsync(id, ct));

        [HttpPost("runs/{id:int}/approve")]
        public async Task<IActionResult> ApproveAsync(int id, CancellationToken ct) => Ok(await _runs.ApproveAsync(id, ct));

        [HttpPost("runs/{id:int}/post")]
        public async Task<IActionResult> PostAsync(int id, CancellationToken ct) => Ok(await _runs.PostAsync(id, ct));

        [HttpDelete("runs/{id:int}")]
        public async Task<IActionResult> DeleteRunAsync(int id, CancellationToken ct) => Ok(await _runs.DeleteRunAsync(id, ct));

        [HttpGet("payslips")]
        public async Task<IActionResult> PayslipsAsync([FromQuery] PayslipQuery query, CancellationToken ct) => Ok((await _runs.GetPayslipsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("payslips/{id:int}")]
        public async Task<IActionResult> PayslipAsync(int id, CancellationToken ct) => Ok(await _runs.GetPayslipAsync(id, ct));

        [HttpGet("reports/insurance-list")]
        public async Task<IActionResult> InsuranceListAsync([FromQuery] int year, [FromQuery] int month, [FromQuery] int? workshopId, CancellationToken ct) =>
            Ok(await _runs.GetInsuranceListAsync(year, month, workshopId, ct));

        [HttpGet("reports/tax-list")]
        public async Task<IActionResult> TaxListAsync([FromQuery] int year, [FromQuery] int month, CancellationToken ct) => Ok(await _runs.GetTaxListAsync(year, month, ct));

        [HttpGet("reports/employee-year")]
        public async Task<IActionResult> EmployeeYearAsync([FromQuery] int employeeId, [FromQuery] int year, CancellationToken ct) => Ok(await _runs.GetEmployeeYearSummaryAsync(employeeId, year, ct));

        [HttpGet("runs/{id:int}/bank-transfer")]
        public async Task<IActionResult> BankTransferAsync(int id, CancellationToken ct) => Ok(await _runs.GetBankTransferAsync(id, ct));

        [HttpGet("runs/{id:int}/bank-transfer/file")]
        public async Task<IActionResult> BankTransferFileAsync(int id, CancellationToken ct)
        {
            var result = await _runs.GetBankTransferFileAsync(id, ct);
            if (!result.Success) return Ok(result);
            return File(result.Data, F2Files.Xlsx, $"payroll-transfer-{id}.xlsx");
        }
    }

    /// <summary>The employee directory every signed-in user may read: name, job title and unit only.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/finance/employees")]
    public class EmployeeDirectoryController : ControllerBase
    {
        private readonly IPayrollMasterService _master;

        public EmployeeDirectoryController(IPayrollMasterService master) { _master = master; }

        [HttpGet("directory")]
        public async Task<IActionResult> DirectoryAsync([FromQuery] string contains, CancellationToken ct) => Ok(await _master.GetDirectoryAsync(contains, ct));
    }

    [ApiController]
    [Authorize(Roles = FinanceRoles.OutboxReaders + "," + FinanceRoles.PayrollReaders)]
    [Route("api/v1/finance/sepidar")]
    public class SepidarOutboxController : ControllerBase
    {
        private readonly ISepidarOutboxService _outbox;

        public SepidarOutboxController(ISepidarOutboxService outbox) { _outbox = outbox; }

        [HttpGet("packages")]
        public async Task<IActionResult> PackagesAsync([FromQuery] OutboxQuery query, CancellationToken ct) => Ok((await _outbox.GetPackagesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("packages/{id:int}")]
        public async Task<IActionResult> PackageAsync(int id, CancellationToken ct) => Ok(await _outbox.GetPackageAsync(id, ct));

        [HttpPost("generate")]
        public async Task<IActionResult> GenerateAsync([FromBody] GenerateOutboxDto dto, CancellationToken ct) => Ok(await _outbox.GenerateAsync(dto, ct));

        [HttpPost("action")]
        public async Task<IActionResult> ActionAsync([FromBody] OutboxActionDto dto, CancellationToken ct) => Ok(await _outbox.ActionAsync(dto, ct));

        [HttpGet("packages/{id:int}/export")]
        public async Task<IActionResult> ExportAsync(int id, CancellationToken ct)
        {
            var result = await _outbox.ExportAsync(id, ct);
            if (!result.Success) return Ok(result);
            return File(result.Data, F2Files.Xlsx, $"sepidar-package-{id}.xlsx");
        }

        [HttpGet("summary")]
        public async Task<IActionResult> SummaryAsync(CancellationToken ct) => Ok(await _outbox.GetSummaryAsync(ct));
    }
}
