using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Finance;
using ViewModels.NasbNiro.FinanceDtos;

namespace WebApi.Controllers
{
    // Finance release F3b: direct cash flow, bank guarantees, clearance and retention release, planned cost, supplier notes and statements,
    // confirmation letters, comparatives and notes. The role checks per step are in the service.

    [ApiController]
    [Authorize]
    [Route("api/v1/finance/completeness")]
    public class FinanceCompletenessController : ControllerBase
    {
        private readonly IFinanceCompletenessService _s;
        public FinanceCompletenessController(IFinanceCompletenessService s) { _s = s; }

        [HttpGet("cash-flow-direct")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> DirectCashFlowAsync([FromQuery] FnStatementQuery query, CancellationToken ct) => Ok(await _s.DirectCashFlowAsync(query, ct));

        [HttpGet("comparative")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ComparativeAsync([FromQuery] string kind, [FromQuery] int? fiscalYearId, [FromQuery] bool includeDraft, CancellationToken ct) => Ok(await _s.ComparativeAsync(kind, fiscalYearId, includeDraft, ct));

        [HttpGet("notes")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> NotesAsync([FromQuery] int? fiscalYearId, CancellationToken ct) => Ok(await _s.NotesAsync(fiscalYearId, ct));

        // bank guarantees
        [HttpGet("guarantees")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> GuaranteesAsync([FromQuery] int? direction, CancellationToken ct) => Ok(await _s.GetBankGuaranteesAsync(direction, ct));

        [HttpPost("guarantees")]
        public async Task<IActionResult> SaveGuaranteeAsync([FromBody] FnSaveBankGuaranteeDto dto, CancellationToken ct) => Ok(await _s.SaveBankGuaranteeAsync(dto, ct));

        [HttpPost("guarantees/action")]
        public async Task<IActionResult> GuaranteeActionAsync([FromBody] FnGuaranteeActionDto dto, CancellationToken ct) => Ok(await _s.BankGuaranteeActionAsync(dto, ct));

        [HttpPost("guarantees/limits")]
        public async Task<IActionResult> GuaranteeLimitAsync([FromBody] FnGuaranteeLimitDto dto, CancellationToken ct) => Ok(await _s.SetGuaranteeLimitAsync(dto, ct));

        [HttpGet("guarantees/exposure")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ExposureAsync([FromQuery] DateTime? asOf, CancellationToken ct) => Ok(await _s.GuaranteeExposureAsync(asOf, ct));

        // contractor insurance clearance and performance retention
        [HttpGet("clearances")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ClearancesAsync([FromQuery] int? providerId, [FromQuery] DateTime? asOf, CancellationToken ct) => Ok(await _s.GetClearancesAsync(providerId, asOf, ct));

        [HttpPost("clearances")]
        public async Task<IActionResult> SaveClearanceAsync([FromBody] FnSaveClearanceDto dto, CancellationToken ct) => Ok(await _s.SaveClearanceAsync(dto, ct));

        [HttpGet("retention/status")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> RetentionStatusAsync([FromQuery] int providerId, [FromQuery] int? contractId, [FromQuery] DateTime? asOf, CancellationToken ct) => Ok(await _s.RetentionStatusAsync(providerId, contractId, asOf, ct));

        [HttpPost("retention/release")]
        public async Task<IActionResult> ReleaseRetentionAsync([FromBody] FnRetentionReleaseRequestDto dto, CancellationToken ct) => Ok(await _s.ReleaseRetentionAsync(dto, ct));

        // planned versus actual
        [HttpPost("estimates")]
        public async Task<IActionResult> SaveEstimateAsync([FromBody] FnSaveEstimateDto dto, CancellationToken ct) => Ok(await _s.SaveEstimateAsync(dto, ct));

        [HttpGet("work-orders/{workOrderId:int}/planned-vs-actual")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> PlannedVsActualAsync(int workOrderId, CancellationToken ct) => Ok(await _s.PlannedVsActualAsync(workOrderId, ct));

        // supplier credit / debit notes and statements
        [HttpGet("supplier-notes")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> SupplierNotesAsync([FromQuery] int? providerId, CancellationToken ct) => Ok(await _s.GetSupplierNotesAsync(providerId, ct));

        [HttpPost("supplier-notes")]
        public async Task<IActionResult> SaveSupplierNoteAsync([FromBody] FnSupplierNoteInputDto dto, CancellationToken ct) => Ok(await _s.SaveSupplierNoteAsync(dto, ct));

        [HttpPost("supplier-notes/action")]
        public async Task<IActionResult> SupplierNoteActionAsync([FromBody] DocActionDto dto, CancellationToken ct) => Ok(await _s.SupplierNoteActionAsync(dto, ct));

        [HttpPost("supplier-statements")]
        public async Task<IActionResult> ReconcileStatementAsync([FromBody] FnSupplierStatementInputDto dto, CancellationToken ct) => Ok(await _s.ReconcileSupplierStatementAsync(dto, ct));

        [HttpGet("supplier-statements/{id:int}")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> StatementAsync(int id, CancellationToken ct) => Ok(await _s.GetSupplierStatementAsync(id, ct));

        // customer confirmation letters
        [HttpGet("confirmations")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ConfirmationsAsync([FromQuery] int? customerId, CancellationToken ct) => Ok(await _s.GetConfirmationsAsync(customerId, ct));

        [HttpPost("confirmations")]
        public async Task<IActionResult> IssueConfirmationAsync([FromBody] FnConfirmationInputDto dto, CancellationToken ct) => Ok(await _s.IssueConfirmationAsync(dto, ct));

        [HttpPost("confirmations/respond")]
        public async Task<IActionResult> RespondConfirmationAsync([FromBody] FnConfirmationRespondDto dto, CancellationToken ct) => Ok(await _s.RespondConfirmationAsync(dto, ct));
    }

    /// <summary>Individual salary data: payroll roles only (the auditor and the other finance roles get 403).</summary>
    [ApiController]
    [Authorize(Roles = FinanceRoles.PayrollReaders)]
    [Route("api/v1/finance/payroll-settlements")]
    public class FinancePayrollSettlementsController : ControllerBase
    {
        private readonly IFinanceCompletenessService _s;
        public FinancePayrollSettlementsController(IFinanceCompletenessService s) { _s = s; }

        [HttpGet("terminations")]
        public async Task<IActionResult> TerminationsAsync(CancellationToken ct) => Ok(await _s.GetTerminationsAsync(ct));

        [HttpPost("terminations")]
        [Authorize(Roles = FinanceRoles.PayrollUsers)]
        public async Task<IActionResult> SaveTerminationAsync([FromBody] FnTerminationInputDto dto, CancellationToken ct) => Ok(await _s.SaveTerminationAsync(dto, ct));

        [HttpPost("terminations/action")]
        [Authorize(Roles = FinanceRoles.PayrollUsers)]
        public async Task<IActionResult> TerminationActionAsync([FromBody] DocActionDto dto, CancellationToken ct) => Ok(await _s.TerminationActionAsync(dto, ct));

        [HttpGet("bonus-runs")]
        public async Task<IActionResult> BonusRunsAsync(CancellationToken ct) => Ok(await _s.GetBonusRunsAsync(ct));

        [HttpPost("bonus-runs")]
        [Authorize(Roles = FinanceRoles.PayrollUsers)]
        public async Task<IActionResult> SaveBonusRunAsync([FromBody] FnBonusInputDto dto, CancellationToken ct) => Ok(await _s.SaveBonusRunAsync(dto, ct));

        [HttpPost("bonus-runs/action")]
        [Authorize(Roles = FinanceRoles.PayrollUsers)]
        public async Task<IActionResult> BonusRunActionAsync([FromBody] DocActionDto dto, CancellationToken ct) => Ok(await _s.BonusRunActionAsync(dto, ct));

        [HttpGet("tax-reconciliation")]
        public async Task<IActionResult> TaxReconciliationAsync([FromQuery] int year, CancellationToken ct) => Ok(await _s.PayrollTaxReconciliationAsync(year, ct));
    }
}
