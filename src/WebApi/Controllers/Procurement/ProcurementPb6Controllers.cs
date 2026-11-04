using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Entities.Model.NasbNiro.Procurement;
using Services.NasbNiro.ContractsSystem;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.ProcurementDtos;
using WebApi.Extensions;
using WebApi.Filters;

namespace WebApi.Controllers
{
    // PB6: return and revision of requests, archive, the bridge from legacy buy orders, supplier contracts (addenda, guarantees, payment terms, print) and the procurement reports.
    // The role of each write is checked in the services.
    [ApiController]
    [Authorize]
    [Route("api/v1/procurement")]
    [RolesCheckedInService]
    public class ProcurementPb6Controller : ControllerBase
    {
        private readonly IProcurementService _service;
        private readonly ISupplierContractService _contracts;
        private readonly IProcurementReportsService _reports;

        public ProcurementPb6Controller(IProcurementService service, ISupplierContractService contracts, IProcurementReportsService reports)
        {
            _service = service;
            _contracts = contracts;
            _reports = reports;
        }

        // request return and new version
        [HttpPost("requests/return")]
        public async Task<IActionResult> ReturnRequestAsync([FromBody] ProcurementActionDto dto, CancellationToken ct) => Ok(await _service.ReturnRequestAsync(dto, ct));

        [HttpPost("requests/revise")]
        public async Task<IActionResult> ReviseRequestAsync([FromBody] ProcurementActionDto dto, CancellationToken ct) => Ok(await _service.ReviseRequestAsync(dto, ct));

        [HttpGet("requests/{id:int}/chain")]
        [Authorize(Roles = ProcurementRoles.RequestReaders)]
        public async Task<IActionResult> RequestChainAsync(int id, CancellationToken ct) => Ok(await _service.GetRequestChainAsync(id, ct));

        // archive
        [HttpGet("archive")]
        [Authorize(Roles = ProcurementRoles.Archivers + "," + ProcurementRoles.ReportReaders)]
        public async Task<IActionResult> ArchiveAsync([FromQuery] ArchiveQuery query, CancellationToken ct) => Ok((await _service.GetArchiveAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("archive")]
        public async Task<IActionResult> ArchiveDocumentAsync([FromBody] ArchiveDto dto, CancellationToken ct) => Ok(await _service.ArchiveAsync(dto, ct));

        [HttpPost("archive/restore")]
        public async Task<IActionResult> RestoreDocumentAsync([FromBody] ArchiveDto dto, CancellationToken ct) => Ok(await _service.RestoreAsync(dto, ct));

        // the bridge from the frozen legacy buy orders
        [HttpGet("legacy-buy-orders")]
        [Authorize(Roles = ProcurementRoles.Planning + "," + ProcurementRoles.Commercial + "," + ProcurementRoles.Management)]
        public async Task<IActionResult> LegacyBuyOrdersAsync([FromQuery] bool openOnly, CancellationToken ct) => Ok(await _service.GetLegacyBuyOrdersAsync(openOnly, ct));

        [HttpPost("legacy-buy-orders/convert")]
        public async Task<IActionResult> ConvertBuyOrderAsync([FromBody] ConvertBuyOrderDto dto, CancellationToken ct) => Ok(await _service.ConvertBuyOrderAsync(dto, ct));

        // addenda
        [HttpGet("addenda")]
        [Authorize(Roles = SupplierContractRoles.Readers)]
        public async Task<IActionResult> AddendaAsync([FromQuery] int? purchaseOrderId, CancellationToken ct) => Ok(await _contracts.GetAddendaAsync(purchaseOrderId, ct));

        [HttpPost("addenda")]
        public async Task<IActionResult> SaveAddendumAsync([FromBody] SaveAddendumDto dto, CancellationToken ct) => Ok(await _contracts.SaveAddendumAsync(dto, ct));

        [HttpPost("addenda/action")]
        public async Task<IActionResult> AddendumActionAsync([FromBody] ProcurementActionDto dto, CancellationToken ct) => Ok(await _contracts.AddendumActionAsync(dto, ct));

        // guarantees
        [HttpGet("guarantees")]
        [Authorize(Roles = SupplierContractRoles.Readers)]
        public async Task<IActionResult> GuaranteesAsync([FromQuery] int? providerId, [FromQuery] int? purchaseOrderId, [FromQuery] SupplierGuaranteeStatus? status, [FromQuery] bool expiringOnly, CancellationToken ct) =>
            Ok(await _contracts.GetGuaranteesAsync(providerId, purchaseOrderId, status, expiringOnly, ct));

        [HttpPost("guarantees")]
        public async Task<IActionResult> SaveGuaranteeAsync([FromBody] SaveGuaranteeDto dto, CancellationToken ct) => Ok(await _contracts.SaveGuaranteeAsync(dto, ct));

        [HttpPost("guarantees/action")]
        public async Task<IActionResult> GuaranteeActionAsync([FromBody] ProcurementActionDto dto, CancellationToken ct) => Ok(await _contracts.GuaranteeActionAsync(dto, ct));

        [HttpPost("guarantees/run-alerts")]
        [Authorize(Roles = SupplierContractRoles.Registrars + ",PMO")]
        public async Task<IActionResult> RunGuaranteeAlertsAsync(CancellationToken ct) => Ok(await _contracts.RunGuaranteeAlertsAsync(ct));

        // payment terms (30 / 30 / 30 / 10)
        [HttpGet("orders/{id:int}/payment-terms")]
        [Authorize(Roles = SupplierContractRoles.Readers)]
        public async Task<IActionResult> PaymentTermsAsync(int id, CancellationToken ct) => Ok(await _contracts.GetPaymentTermsAsync(id, ct));

        [HttpPost("payment-terms")]
        public async Task<IActionResult> SavePaymentTermsAsync([FromBody] SavePaymentTermsDto dto, CancellationToken ct) => Ok(await _contracts.SavePaymentTermsAsync(dto, ct));

        [HttpPost("payment-terms/confirm")]
        public async Task<IActionResult> ConfirmTermAsync([FromBody] ProcurementActionDto dto, CancellationToken ct) => Ok(await _contracts.ConfirmTermConditionAsync(dto, ct));

        [HttpPost("payment-terms/request-payment")]
        public async Task<IActionResult> RequestTermPaymentAsync([FromBody] RequestTermPaymentDto dto, CancellationToken ct) => Ok(await _contracts.RequestTermPaymentAsync(dto, ct));

        // FO-IP-040-04
        [HttpGet("orders/{id:int}/contract-print")]
        [Authorize(Roles = SupplierContractRoles.Readers)]
        public async Task<IActionResult> ContractPrintAsync(int id, CancellationToken ct) => Ok(await _contracts.GetContractPrintAsync(id, ct));

        // reports
        [HttpGet("reports/catalog")]
        [Authorize(Roles = ProcurementRoles.ReportReaders)]
        public IActionResult ReportCatalog() => Ok(_reports.GetCatalog());

        [HttpGet("reports")]
        [Authorize(Roles = ProcurementRoles.ReportReaders)]
        public async Task<IActionResult> ReportAsync([FromQuery] ProcurementReportQuery query, CancellationToken ct) => Ok(await _reports.GetReportAsync(query, ct));

        [HttpGet("reports/export")]
        [Authorize(Roles = ProcurementRoles.ReportReaders)]
        public async Task<IActionResult> ExportAsync([FromQuery] ProcurementReportQuery query, CancellationToken ct)
        {
            var result = await _reports.ExportAsync(query, ct);
            if (!result.Success || result.Data == null) return BadRequest(result);
            return File(result.Data, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "procurement-" + (query?.Report ?? "report") + ".xlsx");
        }
    }

    // PB6: the contracts system (پیوست ۶)
    [ApiController]
    [Authorize]
    [Route("api/v1/contracts-system")]
    [RolesCheckedInService]
    public class ContractsSystemController : ControllerBase
    {
        private readonly IContractsSystemService _service;
        public ContractsSystemController(IContractsSystemService service) { _service = service; }

        [HttpGet("deals")]
        [Authorize(Roles = CtRoles.Readers)]
        public async Task<IActionResult> DealsAsync([FromQuery] FlowQuery query, CancellationToken ct) => Ok(await _service.GetDealsAsync(query, ct));

        [HttpGet("deals/{id:int}")]
        [Authorize(Roles = CtRoles.Readers)]
        public async Task<IActionResult> DealAsync(int id, CancellationToken ct) => Ok(await _service.GetDealAsync(id, ct));

        [HttpPost("deals")]
        public async Task<IActionResult> SaveDealAsync([FromBody] SaveDealRequestDto dto, CancellationToken ct) => Ok(await _service.SaveDealAsync(dto, ct));

        [HttpPost("deals/action")]
        public async Task<IActionResult> DealActionAsync([FromBody] FlowActionDto dto, CancellationToken ct) => Ok(await _service.DealActionAsync(dto, ct));

        [HttpGet("tenders")]
        [Authorize(Roles = CtRoles.Readers)]
        public async Task<IActionResult> TendersAsync([FromQuery] FlowQuery query, CancellationToken ct) => Ok(await _service.GetTendersAsync(query, ct));

        [HttpGet("tenders/{id:int}")]
        [Authorize(Roles = CtRoles.Readers)]
        public async Task<IActionResult> TenderAsync(int id, CancellationToken ct) => Ok(await _service.GetTenderAsync(id, ct));

        [HttpPost("tenders")]
        public async Task<IActionResult> SaveTenderAsync([FromBody] SaveTenderRunDto dto, CancellationToken ct) => Ok(await _service.SaveTenderAsync(dto, ct));

        [HttpPost("tenders/select-winner")]
        public async Task<IActionResult> SelectWinnerAsync([FromBody] SelectWinnerDto dto, CancellationToken ct) => Ok(await _service.SelectWinnerAsync(dto, ct));

        [HttpPost("tenders/action")]
        public async Task<IActionResult> TenderActionAsync([FromBody] FlowActionDto dto, CancellationToken ct) => Ok(await _service.TenderActionAsync(dto, ct));

        [HttpGet("drafts")]
        [Authorize(Roles = CtRoles.Readers)]
        public async Task<IActionResult> DraftsAsync([FromQuery] FlowQuery query, CancellationToken ct) => Ok(await _service.GetDraftsAsync(query, ct));

        [HttpGet("drafts/{id:int}")]
        [Authorize(Roles = CtRoles.Readers)]
        public async Task<IActionResult> DraftAsync(int id, CancellationToken ct) => Ok(await _service.GetDraftAsync(id, ct));

        [HttpGet("drafts/{id:int}/print")]
        [Authorize(Roles = CtRoles.Readers)]
        public async Task<IActionResult> DraftPrintAsync(int id, CancellationToken ct) => Ok(await _service.GetDraftPrintAsync(id, ct));

        [HttpPost("drafts")]
        public async Task<IActionResult> SaveDraftAsync([FromBody] SaveContractDraftDto dto, CancellationToken ct) => Ok(await _service.SaveDraftAsync(dto, ct));

        [HttpPost("drafts/action")]
        public async Task<IActionResult> DraftActionAsync([FromBody] FlowActionDto dto, CancellationToken ct) => Ok(await _service.DraftActionAsync(dto, ct));

        [HttpGet("changes")]
        [Authorize(Roles = CtRoles.Readers)]
        public async Task<IActionResult> ChangesAsync([FromQuery] FlowQuery query, CancellationToken ct) => Ok(await _service.GetChangesAsync(query, ct));

        [HttpGet("mapping")]
        [Authorize(Roles = CtRoles.Readers)]
        public IActionResult Mapping() => Ok(_service.GetMapping());
    }

    // PB6: progress statements (پیوست ۷), legal rates, CBS, cash flow
    [ApiController]
    [Authorize]
    [Route("api/v1/progress-statements")]
    [RolesCheckedInService]
    public class ProgressStatementsController : ControllerBase
    {
        private readonly IProgressStatementService _service;
        public ProgressStatementsController(IProgressStatementService service) { _service = service; }

        [HttpGet]
        [Authorize(Roles = PsRoles.Readers)]
        public async Task<IActionResult> ListAsync([FromQuery] FlowQuery query, CancellationToken ct) => Ok(await _service.GetStatementsAsync(query, ct));

        [HttpGet("{id:int}")]
        [Authorize(Roles = PsRoles.Readers)]
        public async Task<IActionResult> GetAsync(int id, CancellationToken ct) => Ok(await _service.GetStatementAsync(id, ct));

        [HttpGet("{id:int}/print")]
        [Authorize(Roles = PsRoles.Readers)]
        public async Task<IActionResult> PrintAsync(int id, CancellationToken ct) => Ok(await _service.GetPrintAsync(id, ct));

        [HttpPost]
        public async Task<IActionResult> SaveAsync([FromBody] SaveStatementDto dto, CancellationToken ct) => Ok(await _service.SaveStatementAsync(dto, ct));

        [HttpPost("action")]
        public async Task<IActionResult> ActionAsync([FromBody] FlowActionDto dto, CancellationToken ct) => Ok(await _service.StatementActionAsync(dto, ct));

        [HttpPost("payment")]
        public async Task<IActionResult> PaymentAsync([FromBody] StatementPaymentDto dto, CancellationToken ct) => Ok(await _service.RecordPaymentAsync(dto, ct));

        [HttpPost("counterparty")]
        public async Task<IActionResult> CounterpartyAsync([FromBody] StatementCounterpartyDto dto, CancellationToken ct) => Ok(await _service.SaveCounterpartyAsync(dto, ct));

        [HttpGet("legal-rates")]
        [Authorize(Roles = PsRoles.Readers)]
        public async Task<IActionResult> RatesAsync(CancellationToken ct) => Ok(await _service.GetRatesAsync(ct));

        [HttpPost("legal-rates")]
        public async Task<IActionResult> SaveRateAsync([FromBody] SaveLegalRateDto dto, CancellationToken ct) => Ok(await _service.SaveRateAsync(dto, ct));

        [HttpPost("legal-rates/action")]
        public async Task<IActionResult> RateActionAsync([FromBody] FlowActionDto dto, CancellationToken ct) => Ok(await _service.RateActionAsync(dto, ct));

        [HttpGet("cbs")]
        [Authorize(Roles = PsRoles.Readers)]
        public async Task<IActionResult> CbsAsync([FromQuery] int? purchaseOrderId, [FromQuery] int? contractId, CancellationToken ct) => Ok(await _service.GetCbsAsync(purchaseOrderId, contractId, ct));

        [HttpPost("cbs")]
        public async Task<IActionResult> SaveCbsAsync([FromBody] SaveCbsDto dto, CancellationToken ct) => Ok(await _service.SaveCbsAsync(dto, ct));

        [HttpGet("cash-flow")]
        [Authorize(Roles = PsRoles.Readers)]
        public async Task<IActionResult> CashFlowAsync([FromQuery] int? purchaseOrderId, [FromQuery] int? contractId, CancellationToken ct) => Ok(await _service.GetCashFlowAsync(purchaseOrderId, contractId, ct));

        [HttpPost("cash-flow")]
        public async Task<IActionResult> SaveCashPlanAsync([FromBody] SaveCashPlanDto dto, CancellationToken ct) => Ok(await _service.SaveCashPlanAsync(dto, ct));

        [HttpGet("analytics")]
        [Authorize(Roles = PsRoles.Readers)]
        public async Task<IActionResult> AnalyticsAsync([FromQuery] FlowQuery query, CancellationToken ct) => Ok(await _service.GetAnalyticsAsync(query, ct));
    }

    // PB6: the petty-cash flow (request, top-up, summary FO-AC-005-07 and its forms)
    [ApiController]
    [Authorize]
    [Route("api/v1/petty-cash-flow")]
    [RolesCheckedInService]
    public class PettyCashFlowController : ControllerBase
    {
        private readonly IPettyCashFlowService _service;
        public PettyCashFlowController(IPettyCashFlowService service) { _service = service; }

        [HttpGet("requests")]
        [Authorize]
        public async Task<IActionResult> RequestsAsync([FromQuery] FlowQuery query, CancellationToken ct) => Ok(await _service.GetRequestsAsync(query, ct));

        [HttpPost("requests")]
        public async Task<IActionResult> SaveRequestAsync([FromBody] SavePettyRequestDto dto, CancellationToken ct) => Ok(await _service.SaveRequestAsync(dto, ct));

        [HttpPost("requests/action")]
        public async Task<IActionResult> RequestActionAsync([FromBody] FlowActionDto dto, CancellationToken ct) => Ok(await _service.RequestActionAsync(dto, ct));

        [HttpGet("summaries")]
        [Authorize]
        public async Task<IActionResult> SummariesAsync([FromQuery] FlowQuery query, CancellationToken ct) => Ok(await _service.GetSummariesAsync(query, ct));

        [HttpGet("summaries/{id:int}")]
        [Authorize]
        public async Task<IActionResult> SummaryAsync(int id, CancellationToken ct) => Ok(await _service.GetSummaryAsync(id, ct));

        [HttpPost("summaries/preview")]
        public async Task<IActionResult> PreviewAsync([FromBody] SavePettySummaryDto dto, CancellationToken ct) => Ok(await _service.PreviewSummaryAsync(dto, ct));

        [HttpPost("summaries")]
        public async Task<IActionResult> SaveSummaryAsync([FromBody] SavePettySummaryDto dto, CancellationToken ct) => Ok(await _service.SaveSummaryAsync(dto, ct));

        [HttpPost("summaries/action")]
        public async Task<IActionResult> SummaryActionAsync([FromBody] FlowActionDto dto, CancellationToken ct) => Ok(await _service.SummaryActionAsync(dto, ct));
    }
}
