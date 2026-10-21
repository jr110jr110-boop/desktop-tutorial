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
}
