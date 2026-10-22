using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Finance;
using ViewModels.NasbNiro.FinanceDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // Finance release F3: completeness against Sepidar / Hamkaran class systems. Role checks per step are in the services.

    [ApiController]
    [Authorize]
    [Route("api/v1/finance/audit")]
    public class FinanceAuditController : ControllerBase
    {
        private readonly IGlControlsService _controls;
        public FinanceAuditController(IGlControlsService controls) { _controls = controls; }

        /// <summary>The append-only audit trail (FINANCEMANAGER, CEO, AUDITOR).</summary>
        [HttpGet]
        [Authorize(Roles = FinanceRoles.AuditReaders)]
        public async Task<IActionResult> AuditAsync([FromQuery] AuditQuery query, CancellationToken ct) => Ok((await _controls.AuditAsync(query, ct)).ToPaginatedResultSafe());
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/finance/gl")]
    public class GlControlsController : ControllerBase
    {
        private readonly IGlControlsService _controls;
        public GlControlsController(IGlControlsService controls) { _controls = controls; }

        [HttpGet("close-checklist/{periodId:int}")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ChecklistAsync(int periodId, CancellationToken ct) => Ok(await _controls.GetChecklistAsync(periodId, ct));

        [HttpPost("close-checklist/{periodId:int}/override")]
        public async Task<IActionResult> OverrideAsync(int periodId, [FromBody] ChecklistOverrideDto dto, CancellationToken ct) => Ok(await _controls.OverrideChecklistAsync(periodId, dto, ct));

        [HttpGet("settings")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> SettingsAsync(CancellationToken ct) => Ok(await _controls.GetSettingsAsync(ct));

        [HttpPost("settings")]
        public async Task<IActionResult> SaveSettingAsync([FromBody] SaveFinanceSettingDto dto, CancellationToken ct) => Ok(await _controls.SaveSettingAsync(dto, ct));

        [HttpGet("vouchers/{voucherId:int}/attachments")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> AttachmentsAsync(int voucherId, CancellationToken ct) => Ok(await _controls.GetAttachmentsAsync(voucherId, ct));

        [HttpPost("attachments")]
        public async Task<IActionResult> AddAttachmentAsync([FromBody] AddAttachmentDto dto, CancellationToken ct) => Ok(await _controls.AddAttachmentAsync(dto, ct));

        [HttpDelete("attachments/{id:int}")]
        public async Task<IActionResult> RemoveAttachmentAsync(int id, CancellationToken ct) => Ok(await _controls.RemoveAttachmentAsync(id, ct));

        [HttpGet("attachment-rules")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> RulesAsync(CancellationToken ct) => Ok(await _controls.GetRulesAsync(ct));

        [HttpPost("attachment-rules")]
        public async Task<IActionResult> SaveRuleAsync([FromBody] SaveAttachmentRuleDto dto, CancellationToken ct) => Ok(await _controls.SaveRuleAsync(dto, ct));

        [HttpGet("approval-thresholds")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ThresholdsAsync(CancellationToken ct) => Ok(await _controls.GetThresholdsAsync(ct));

        [HttpPost("approval-thresholds")]
        public async Task<IActionResult> SaveThresholdAsync([FromBody] SaveApprovalThresholdDto dto, CancellationToken ct) => Ok(await _controls.SaveThresholdAsync(dto, ct));

        [HttpPost("vouchers/copy")]
        public async Task<IActionResult> CopyAsync([FromBody] VoucherIdDto dto, CancellationToken ct) => Ok(await _controls.CopyVoucherAsync(dto, ct));

        [HttpGet("templates")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> TemplatesAsync(CancellationToken ct) => Ok(await _controls.GetTemplatesAsync(ct));

        [HttpPost("templates")]
        public async Task<IActionResult> SaveTemplateAsync([FromBody] SaveVoucherTemplateDto dto, CancellationToken ct) => Ok(await _controls.SaveTemplateAsync(dto, ct));

        [HttpPost("templates/generate")]
        public async Task<IActionResult> GenerateTemplatesAsync([FromBody] GenerateTemplatesDto dto, CancellationToken ct) => Ok(await _controls.GenerateTemplatesAsync(dto, ct));

        [HttpPost("vouchers/mass-import")]
        public async Task<IActionResult> MassImportAsync([FromBody] MassVoucherImportDto dto, CancellationToken ct) => Ok(await _controls.MassImportAsync(dto, ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/finance/entities")]
    public class LegalEntitiesController : ControllerBase
    {
        private readonly ILegalEntityService _entities;
        public LegalEntitiesController(ILegalEntityService entities) { _entities = entities; }

        [HttpGet]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> EntitiesAsync(CancellationToken ct) => Ok(await _entities.GetEntitiesAsync(ct));

        [HttpPost]
        public async Task<IActionResult> SaveEntityAsync([FromBody] SaveLegalEntityDto dto, CancellationToken ct) => Ok(await _entities.SaveEntityAsync(dto, ct));

        [HttpPost("default")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> SetDefaultAsync([FromBody] SetDefaultEntityDto dto, CancellationToken ct) => Ok(await _entities.SetDefaultEntityAsync(dto, ct));

        [HttpGet("pairs")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> PairsAsync(CancellationToken ct) => Ok(await _entities.GetPairsAsync(ct));

        [HttpPost("pairs")]
        public async Task<IActionResult> SavePairAsync([FromBody] SaveIntercompanyPairDto dto, CancellationToken ct) => Ok(await _entities.SavePairAsync(dto, ct));

        [HttpPost("intercompany/post")]
        public async Task<IActionResult> PostIntercompanyAsync([FromBody] IntercompanyPostDto dto, CancellationToken ct) => Ok(await _entities.PostIntercompanyAsync(dto, ct));

        [HttpGet("consolidation")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ConsolidationAsync([FromQuery] ConsolidationQuery query, CancellationToken ct) => Ok(await _entities.ConsolidationAsync(query ?? new ConsolidationQuery(), ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/finance/migration")]
    public class FinanceMigrationController : ControllerBase
    {
        private readonly IMigrationService _migration;
        public FinanceMigrationController(IMigrationService migration) { _migration = migration; }

        [HttpPost("accounts")]
        public async Task<IActionResult> AccountsAsync([FromBody] SepidarAccountImportDto dto, [FromQuery] bool commit, CancellationToken ct) => Ok(await _migration.ImportAccountsAsync(dto, commit, ct));

        [HttpPost("details")]
        public async Task<IActionResult> DetailsAsync([FromBody] SepidarDetailImportDto dto, [FromQuery] bool commit, CancellationToken ct) => Ok(await _migration.ImportDetailsAsync(dto, commit, ct));

        [HttpPost("opening")]
        public async Task<IActionResult> OpeningAsync([FromBody] SepidarOpeningImportDto dto, [FromQuery] bool commit, CancellationToken ct) => Ok(await _migration.ImportOpeningBalancesAsync(dto, commit, ct));

        [HttpPost("imported-years")]
        public async Task<IActionResult> ImportedYearAsync([FromBody] CreateImportedYearDto dto, CancellationToken ct) => Ok(await _migration.CreateImportedYearAsync(dto, ct));

        [HttpPost("vouchers/import")]
        public async Task<IActionResult> HistoricalAsync([FromBody] HistoricalImportDto dto, CancellationToken ct) => Ok(await _migration.ImportHistoricalAsync(dto, ct));

        [HttpPost("parallel-run/compare")]
        public async Task<IActionResult> CompareAsync([FromBody] ParallelCompareDto dto, CancellationToken ct) => Ok(await _migration.CompareAsync(dto, ct));

        [HttpGet("parallel-run")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> RunsAsync([FromQuery] int? periodId, CancellationToken ct) => Ok(await _migration.GetRunsAsync(periodId, ct));

        [HttpGet("parallel-run/status")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> StatusAsync([FromQuery] int? legalEntityId, CancellationToken ct) => Ok(await _migration.ParallelStatusAsync(legalEntityId, ct));

        [HttpGet("parallel-run/{id:int}")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> RunAsync(int id, CancellationToken ct) => Ok(await _migration.GetRunAsync(id, ct));

        [HttpPost("parallel-run/{id:int}/explain")]
        public async Task<IActionResult> ExplainAsync(int id, [FromBody] ExplainDifferenceDto dto, CancellationToken ct) => Ok(await _migration.ExplainAsync(id, dto, ct));

        [HttpPost("parallel-run/{id:int}/sign-off")]
        public async Task<IActionResult> SignOffAsync(int id, [FromBody] SignOffDto dto, CancellationToken ct) => Ok(await _migration.SignOffAsync(id, dto, ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/finance/sepidar-export")]
    public class SepidarFullExportController : ControllerBase
    {
        private readonly ISepidarFullExportService _export;
        public SepidarFullExportController(ISepidarFullExportService export) { _export = export; }

        [HttpGet("status")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> StatusAsync(CancellationToken ct) => Ok(await _export.StatusAsync(ct));

        [HttpGet("layout")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> LayoutAsync(CancellationToken ct) => Ok(await _export.GetLayoutAsync(ct));

        [HttpPost("layout")]
        public async Task<IActionResult> SaveLayoutAsync([FromBody] SaveExportLayoutDto dto, CancellationToken ct) => Ok(await _export.SaveLayoutAsync(dto, ct));

        [HttpGet("account-mapping")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> AccountMappingAsync([FromQuery] bool onlyUnmapped, CancellationToken ct) => Ok(await _export.GetAccountMappingAsync(onlyUnmapped, ct));

        [HttpPost("account-mapping")]
        public async Task<IActionResult> SaveAccountMappingAsync([FromBody] SaveExportMappingDto dto, CancellationToken ct) => Ok(await _export.SaveAccountMappingAsync(dto, ct));

        [HttpGet("detail-mapping")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> DetailMappingAsync([FromQuery] bool onlyUnmapped, CancellationToken ct) => Ok(await _export.GetDetailMappingAsync(onlyUnmapped, ct));

        [HttpPost("detail-mapping")]
        public async Task<IActionResult> SaveDetailMappingAsync([FromBody] SaveExportMappingDto dto, CancellationToken ct) => Ok(await _export.SaveDetailMappingAsync(dto, ct));

        [HttpGet("preview/{periodId:int}")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> PreviewAsync(int periodId, CancellationToken ct) => Ok(await _export.PreviewAsync(periodId, ct));

        [HttpPost("export")]
        public async Task<IActionResult> ExportAsync([FromBody] ExportPeriodDto dto, CancellationToken ct) => Ok(await _export.ExportAsync(dto, ct));

        [HttpGet("exports")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ExportsAsync([FromQuery] int? periodId, CancellationToken ct) => Ok(await _export.GetExportsAsync(periodId, ct));

        [HttpGet("exports/{id:int}/file")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> FileAsync(int id, CancellationToken ct)
        {
            var result = await _export.DownloadAsync(id, ct);
            if (!result.Success) return Ok(result);
            return File(result.Data, "text/csv; charset=utf-8", $"sepidar-vouchers-{id}.csv");
        }
    }
    // F3 section 3: foreign currency
    [ApiController]
    [Authorize]
    [Route("api/v1/finance/fx")]
    public class FxController : ControllerBase
    {
        private readonly IFxService _fx;
        public FxController(IFxService fx) { _fx = fx; }

        [HttpGet("currencies")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> CurrenciesAsync(CancellationToken ct) => Ok(await _fx.GetCurrenciesAsync(ct));

        [HttpPost("currencies")]
        public async Task<IActionResult> SaveCurrencyAsync([FromBody] CurrencyDto dto, CancellationToken ct) => Ok(await _fx.SaveCurrencyAsync(dto, ct));

        [HttpGet("rates")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> RatesAsync([FromQuery] FxRateQuery query, CancellationToken ct) => Ok((await _fx.GetRatesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("rates")]
        public async Task<IActionResult> SaveRateAsync([FromBody] SaveExchangeRateDto dto, CancellationToken ct) => Ok(await _fx.SaveRateAsync(dto, ct));

        [HttpPost("revaluation")]
        public async Task<IActionResult> RevaluationAsync([FromBody] FxRevaluationRequestDto dto, CancellationToken ct) => Ok(await _fx.RevaluationAsync(dto, ct));

        [HttpGet("revaluations")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> RevaluationsAsync([FromQuery] int? periodId, CancellationToken ct) => Ok(await _fx.GetRevaluationsAsync(periodId, ct));

        [HttpPost("settlement")]
        public async Task<IActionResult> SettleAsync([FromBody] FxSettlementRequestDto dto, CancellationToken ct) => Ok(await _fx.SettleAsync(dto, ct));

        [HttpGet("exposure")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ExposureAsync([FromQuery] DateTime? asOf, CancellationToken ct) => Ok(await _fx.ExposureAsync(asOf, ct));

        [HttpGet("letters-of-credit")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> LcsAsync(CancellationToken ct) => Ok(await _fx.GetLettersOfCreditAsync(ct));

        [HttpGet("letters-of-credit/{id:int}")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> LcAsync(int id, CancellationToken ct) => Ok(await _fx.GetLetterOfCreditAsync(id, ct));

        [HttpPost("letters-of-credit")]
        public async Task<IActionResult> SaveLcAsync([FromBody] SaveLetterOfCreditDto dto, CancellationToken ct) => Ok(await _fx.SaveLetterOfCreditAsync(dto, ct));

        [HttpPost("letters-of-credit/action")]
        public async Task<IActionResult> LcActionAsync([FromBody] LcActionDto dto, CancellationToken ct) => Ok(await _fx.LetterOfCreditActionAsync(dto, ct));
    }
}

namespace WebApi.Controllers
{
    // F3 section 4: budgeting and commitment control
    [ApiController]
    [Authorize]
    [Route("api/v1/finance/budgets")]
    public class BudgetsController : ControllerBase
    {
        private readonly IBudgetService _budgets;
        public BudgetsController(IBudgetService budgets) { _budgets = budgets; }

        [HttpGet]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ListAsync([FromQuery] int? fiscalYearId, CancellationToken ct) => Ok(await _budgets.GetBudgetsAsync(fiscalYearId, ct));

        [HttpGet("{id:int}")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> GetAsync(int id, CancellationToken ct) => Ok(await _budgets.GetBudgetAsync(id, ct));

        [HttpPost]
        public async Task<IActionResult> SaveAsync([FromBody] FnSaveBudgetDto dto, CancellationToken ct) => Ok(await _budgets.SaveBudgetAsync(dto, ct));

        [HttpPost("import")]
        public async Task<IActionResult> ImportAsync([FromBody] FnBudgetImportDto dto, CancellationToken ct) => Ok(await _budgets.ImportAsync(dto, ct));

        [HttpGet("{id:int}/export")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ExportAsync(int id, CancellationToken ct)
        {
            var r = await _budgets.ExportAsync(id, ct);
            if (!r.Success) return Ok(r);
            return File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(r.Data)).ToArray(), "text/csv; charset=utf-8", "budget-" + id + ".csv");
        }

        [HttpPost("{id:int}/approve")]
        public async Task<IActionResult> ApproveAsync(int id, CancellationToken ct) => Ok(await _budgets.ApproveAsync(id, ct));

        [HttpPost("{id:int}/revise")]
        public async Task<IActionResult> ReviseAsync(int id, CancellationToken ct) => Ok(await _budgets.ReviseAsync(id, ct));

        [HttpGet("report")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ReportAsync([FromQuery] FnBudgetReportQuery query, CancellationToken ct) => Ok(await _budgets.ReportAsync(query, ct));

        /// <summary>Read-only helper for the procurement gates: the decision stays with the human.</summary>
        [HttpPost("commitment-check")]
        public async Task<IActionResult> CheckAsync([FromBody] FnCommitmentCheckRequest request, CancellationToken ct) => Ok(await _budgets.CheckAsync(request, ct));

        [HttpGet("commitment-check/purchase-request/{id:int}")]
        public async Task<IActionResult> CheckPurchaseRequestAsync(int id, [FromQuery] string overrideReason, CancellationToken ct) => Ok(await _budgets.CheckPurchaseRequestAsync(id, overrideReason, ct));
    }

    // F3 section 5: costing completeness
    [ApiController]
    [Authorize]
    [Route("api/v1/finance/costing")]
    public class CostingCompletenessController : ControllerBase
    {
        private readonly ICostingCompletenessService _costing;
        public CostingCompletenessController(ICostingCompletenessService costing) { _costing = costing; }

        [HttpGet("overhead-rates")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> RatesAsync(CancellationToken ct) => Ok(await _costing.GetOverheadRatesAsync(ct));

        [HttpPost("overhead-rates")]
        public async Task<IActionResult> SaveRateAsync([FromBody] FnOverheadRateDto dto, CancellationToken ct) => Ok(await _costing.SaveOverheadRateAsync(dto, ct));

        [HttpGet("allocation-keys")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> KeysAsync(CancellationToken ct) => Ok(await _costing.GetAllocationKeysAsync(ct));

        [HttpPost("allocation-keys")]
        public async Task<IActionResult> SaveKeyAsync([FromBody] FnAllocationKeyDto dto, CancellationToken ct) => Ok(await _costing.SaveAllocationKeyAsync(dto, ct));

        [HttpGet("workorder-bases/{periodId:int}")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> BasesAsync(int periodId, CancellationToken ct) => Ok(await _costing.GetWorkOrderBasesAsync(periodId, ct));

        [HttpPost("workorder-bases")]
        public async Task<IActionResult> SaveBaseAsync([FromBody] FnWorkOrderBaseDto dto, CancellationToken ct) => Ok(await _costing.SaveWorkOrderBaseAsync(dto, ct));

        [HttpPost("allocate/{periodId:int}")]
        public async Task<IActionResult> AllocateAsync(int periodId, [FromBody] FnAllocateRequest request, CancellationToken ct) => Ok(await _costing.AllocateAsync(periodId, request, ct));

        [HttpGet("workorders/{id:int}/cost-sheet")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> CostSheetAsync(int id, CancellationToken ct) => Ok(await _costing.CostSheetAsync(id, ct));

        [HttpPost("workorders/{id:int}/close")]
        public async Task<IActionResult> CloseAsync(int id, [FromBody] FnWorkOrderCloseRequest request, CancellationToken ct) => Ok(await _costing.CloseWorkOrderAsync(id, request, ct));

        [HttpPost("workorders/{id:int}/reopen")]
        public async Task<IActionResult> ReopenAsync(int id, [FromBody] FnWorkOrderCloseRequest request, CancellationToken ct) => Ok(await _costing.ReopenWorkOrderAsync(id, request, ct));

        [HttpGet("standard-costs")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> StandardAsync(CancellationToken ct) => Ok(await _costing.GetStandardCostsAsync(ct));

        [HttpPost("standard-costs")]
        public async Task<IActionResult> SaveStandardAsync([FromBody] FnStandardCostDto dto, CancellationToken ct) => Ok(await _costing.SaveStandardCostAsync(dto, ct));
    }

    // F3 section 5: revenue recognition of construction contracts (percentage of completion)
    [ApiController]
    [Authorize]
    [Route("api/v1/finance/receivables/revenue-recognition")]
    public class RevenueRecognitionController : ControllerBase
    {
        private readonly ICostingCompletenessService _costing;
        public RevenueRecognitionController(ICostingCompletenessService costing) { _costing = costing; }

        [HttpGet("contracts")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ContractsAsync(CancellationToken ct) => Ok(await _costing.GetContractRevenueAsync(ct));

        [HttpPost("contracts")]
        public async Task<IActionResult> SaveContractAsync([FromBody] FnContractRevenueDto dto, CancellationToken ct) => Ok(await _costing.SaveContractRevenueAsync(dto, ct));

        [HttpPost("{periodId:int}")]
        public async Task<IActionResult> RunAsync(int periodId, [FromBody] FnRevenueRunRequest request, CancellationToken ct) => Ok(await _costing.RevenueRecognitionAsync(periodId, request, ct));
    }

    // F3 section 5b: deferrals, accrual reversals and provisions
    [ApiController]
    [Authorize]
    [Route("api/v1/finance/deferrals")]
    public class DeferralsController : ControllerBase
    {
        private readonly IDeferralService _deferrals;
        public DeferralsController(IDeferralService deferrals) { _deferrals = deferrals; }

        [HttpGet]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ListAsync(CancellationToken ct) => Ok(await _deferrals.GetDeferralsAsync(ct));

        [HttpGet("{id:int}")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> GetAsync(int id, CancellationToken ct) => Ok(await _deferrals.GetDeferralAsync(id, ct));

        [HttpPost]
        public async Task<IActionResult> SaveAsync([FromBody] FnSaveDeferralDto dto, CancellationToken ct) => Ok(await _deferrals.SaveDeferralAsync(dto, ct));

        [HttpPost("run/{periodId:int}")]
        public async Task<IActionResult> RunAsync(int periodId, CancellationToken ct) => Ok(await _deferrals.RunAmortisationAsync(periodId, ct));

        [HttpPost("entries/{id:int}/approve")]
        public async Task<IActionResult> ApproveAsync(int id, CancellationToken ct) => Ok(await _deferrals.ApproveEntryAsync(id, ct));

        [HttpPost("{id:int}/cancel")]
        public async Task<IActionResult> CancelAsync(int id, CancellationToken ct) => Ok(await _deferrals.CancelDeferralAsync(id, ct));

        [HttpPost("reversing/run/{periodId:int}")]
        public async Task<IActionResult> ReversingAsync(int periodId, CancellationToken ct) => Ok(await _deferrals.RunReversingAsync(periodId, ct));

        [HttpGet("provisions")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ProvisionsAsync([FromQuery] DateTime? asOf, CancellationToken ct) => Ok(await _deferrals.GetProvisionsAsync(asOf, ct));

        [HttpPost("provisions")]
        public async Task<IActionResult> SaveProvisionAsync([FromBody] FnSaveProvisionDto dto, CancellationToken ct) => Ok(await _deferrals.SaveProvisionAsync(dto, ct));

        [HttpPost("provisions/movement")]
        public async Task<IActionResult> MovementAsync([FromBody] FnProvisionMovementDto dto, CancellationToken ct) => Ok(await _deferrals.ProvisionMovementAsync(dto, ct));
    }

    // F3 section 6: loans, facilities and guarantees
    [ApiController]
    [Authorize]
    [Route("api/v1/finance/loans")]
    public class LoansController : ControllerBase
    {
        private readonly ILoanService _loans;
        public LoansController(ILoanService loans) { _loans = loans; }

        [HttpGet]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ListAsync(CancellationToken ct) => Ok(await _loans.GetLoansAsync(ct));

        [HttpGet("{id:int}")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> GetAsync(int id, CancellationToken ct) => Ok(await _loans.GetLoanAsync(id, ct));

        [HttpPost]
        public async Task<IActionResult> SaveAsync([FromBody] FnSaveLoanDto dto, CancellationToken ct) => Ok(await _loans.SaveLoanAsync(dto, ct));

        [HttpPost("action")]
        public async Task<IActionResult> ActionAsync([FromBody] FnLoanActionDto dto, CancellationToken ct) => Ok(await _loans.LoanActionAsync(dto, ct));

        [HttpPost("pay")]
        public async Task<IActionResult> PayAsync([FromBody] FnLoanPayDto dto, CancellationToken ct) => Ok(await _loans.PayInstallmentAsync(dto, ct));

        [HttpPost("accrual/run/{periodId:int}")]
        public async Task<IActionResult> AccrualAsync(int periodId, CancellationToken ct) => Ok(await _loans.RunAccrualAsync(periodId, ct));

        [HttpPost("accrual/{id:int}/approve")]
        public async Task<IActionResult> ApproveAccrualAsync(int id, CancellationToken ct) => Ok(await _loans.ApproveAccrualAsync(id, ct));

        [HttpPost("limits")]
        public async Task<IActionResult> LimitAsync([FromBody] FnFacilityLimitDto dto, CancellationToken ct) => Ok(await _loans.SaveFacilityLimitAsync(dto, ct));

        [HttpGet("utilisation")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> UtilisationAsync(CancellationToken ct) => Ok(await _loans.UtilisationAsync(ct));
    }

    // F3 section 8: statements and analysis
    [ApiController]
    [Authorize]
    [Route("api/v1/finance/statements")]
    public class StatementsController : ControllerBase
    {
        private readonly IStatementsService _statements;
        public StatementsController(IStatementsService statements) { _statements = statements; }

        [HttpGet("cash-flow")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> CashFlowAsync([FromQuery] FnStatementQuery q, CancellationToken ct) => Ok(await _statements.CashFlowAsync(q, ct));

        [HttpGet("equity-changes")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> EquityAsync([FromQuery] FnStatementQuery q, CancellationToken ct) => Ok(await _statements.EquityChangesAsync(q, ct));

        [HttpGet("ratios")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> RatiosAsync([FromQuery] FnStatementQuery q, CancellationToken ct) => Ok(await _statements.RatiosAsync(q, ct));

        [HttpGet("drill")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> DrillAsync([FromQuery] string account, [FromQuery] FnStatementQuery q, CancellationToken ct) => Ok(await _statements.DrillDownAsync(account, q, ct));

        [HttpGet("views")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ViewsAsync(CancellationToken ct) => Ok(await _statements.GetViewsAsync(ct));

        [HttpPost("views")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> SaveViewAsync([FromBody] FnSaveViewDto dto, CancellationToken ct) => Ok(await _statements.SaveViewAsync(dto, ct));
    }

    // F3 section 7: tax books
    [ApiController]
    [Authorize]
    [Route("api/v1/finance/tax")]
    public class TaxCompletenessController : ControllerBase
    {
        private readonly ITaxCompletenessService _tax;
        public TaxCompletenessController(ITaxCompletenessService tax) { _tax = tax; }

        [HttpGet("vat-season/detail")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> VatDetailAsync([FromQuery] int year, [FromQuery] int season, CancellationToken ct) => Ok(await _tax.VatDetailAsync(year, season, ct));

        [HttpGet("adjustments")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> AdjustmentsAsync([FromQuery] int fiscalYearId, CancellationToken ct) => Ok(await _tax.GetAdjustmentsAsync(fiscalYearId, ct));

        [HttpPost("adjustments")]
        public async Task<IActionResult> SaveAdjustmentAsync([FromBody] FnSaveTaxAdjustmentDto dto, CancellationToken ct) => Ok(await _tax.SaveAdjustmentAsync(dto, ct));

        [HttpDelete("adjustments/{id:int}")]
        public async Task<IActionResult> DeleteAdjustmentAsync(int id, CancellationToken ct) => Ok(await _tax.DeleteAdjustmentAsync(id, ct));

        [HttpGet("taxable-profit")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> TaxableProfitAsync([FromQuery] FnTaxableProfitQuery q, CancellationToken ct) => Ok(await _tax.TaxableProfitAsync(q, ct));

        [HttpPost("taxable-profit/provision")]
        public async Task<IActionResult> ProvisionAsync([FromBody] FnTaxableProfitQuery q, CancellationToken ct) { if (q != null) q.CreateProvision = true; return Ok(await _tax.TaxableProfitAsync(q, ct)); }
    }

    // F3 section 10: receivables and payables control
    [ApiController]
    [Authorize]
    [Route("api/v1/finance/collections")]
    public class CollectionsController : ControllerBase
    {
        private readonly ICollectionsService _c;
        public CollectionsController(ICollectionsService c) { _c = c; }

        [HttpGet("bad-debt")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> BadDebtAsync([FromQuery] FnBadDebtQuery q, CancellationToken ct) { if (q != null) q.Post = false; return Ok(await _c.BadDebtAsync(q, ct)); }

        [HttpPost("bad-debt")]
        public async Task<IActionResult> PostBadDebtAsync([FromBody] FnBadDebtQuery q, CancellationToken ct) { if (q != null) q.Post = true; return Ok(await _c.BadDebtAsync(q, ct)); }

        [HttpGet("holds")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> HoldsAsync(CancellationToken ct) => Ok(await _c.GetCreditHoldsAsync(ct));

        [HttpPost("holds")]
        public async Task<IActionResult> SetHoldAsync([FromBody] FnSetHoldDto dto, CancellationToken ct) => Ok(await _c.SetCreditHoldAsync(dto, ct));

        [HttpGet("dunning")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> DunningAsync(CancellationToken ct) => Ok(await _c.GetDunningAsync(ct));

        [HttpPost("dunning/run")]
        public async Task<IActionResult> RunDunningAsync([FromQuery] DateTime? asOf, CancellationToken ct) => Ok(await _c.RunDunningAsync(asOf, ct));

        [HttpPost("dunning/{id:int}/approve")]
        public async Task<IActionResult> ApproveDunningAsync(int id, CancellationToken ct) => Ok(await _c.ApproveDunningAsync(id, ct));

        [HttpGet("payment-proposals")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ProposalsAsync(CancellationToken ct) => Ok(await _c.GetProposalsAsync(ct));

        [HttpGet("payment-proposals/{id:int}")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ProposalAsync(int id, CancellationToken ct) => Ok(await _c.GetProposalAsync(id, ct));

        [HttpPost("payment-proposals")]
        public async Task<IActionResult> CreateProposalAsync([FromBody] FnProposalQuery q, CancellationToken ct) => Ok(await _c.CreateProposalAsync(q, ct));

        [HttpPost("payment-proposals/{id:int}/approve")]
        public async Task<IActionResult> ApproveProposalAsync(int id, CancellationToken ct) => Ok(await _c.ApproveProposalAsync(id, ct));
    }
}
