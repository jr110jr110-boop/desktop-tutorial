using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.AfterSales;
using ViewModels.NasbNiro.ServiceDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // After-sales service (release S1 part B). Role checks per action (segregation of duties) are in the services; rule violations answer 200 with success=false.

    /// <summary>Settings, the SLA table, the installed base and the service requests.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/service")]
    public class ServiceRequestsController : ControllerBase
    {
        private readonly ISvRequestService _requests;
        private readonly ISvUnitService _units;

        public ServiceRequestsController(ISvRequestService requests, ISvUnitService units)
        {
            _requests = requests;
            _units = units;
        }

        [HttpGet("settings")]
        public async Task<IActionResult> SettingsAsync(CancellationToken ct) => Ok(await _requests.GetSettingsAsync(ct));

        [HttpPost("settings")]
        public async Task<IActionResult> SaveSettingAsync([FromBody] SvSaveSettingDto dto, CancellationToken ct) => Ok(await _requests.SaveSettingAsync(dto, ct));

        [HttpGet("sla-rules")]
        public async Task<IActionResult> SlaRulesAsync(CancellationToken ct) => Ok(await _requests.GetSlaRulesAsync(ct));

        [HttpPost("sla-rules")]
        public async Task<IActionResult> SaveSlaRuleAsync([FromBody] SvSaveSlaRuleDto dto, CancellationToken ct) => Ok(await _requests.SaveSlaRuleAsync(dto, ct));

        [HttpGet("units")]
        public async Task<IActionResult> UnitsAsync([FromQuery] SvUnitQuery query, CancellationToken ct) => Ok((await _units.GetUnitsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("units/{id:int}")]
        public async Task<IActionResult> UnitAsync(int id, [FromQuery] DateTime? asOf, CancellationToken ct) => Ok(await _units.GetUnitAsync(id, asOf, ct));

        [HttpGet("units/{id:int}/as-built")]
        public async Task<IActionResult> AsBuiltAsync(int id, CancellationToken ct) => Ok(await _units.GetAsBuiltAsync(id, ct));

        [HttpPost("units")]
        public async Task<IActionResult> SaveUnitAsync([FromBody] SvSaveUnitDto dto, CancellationToken ct) => Ok(await _units.SaveUnitAsync(dto, ct));

        [HttpPost("units/action")]
        public async Task<IActionResult> UnitActionAsync([FromBody] SvUnitActionDto dto, CancellationToken ct) => Ok(await _units.UnitActionAsync(dto, ct));

        [HttpGet("requests")]
        public async Task<IActionResult> RequestsAsync([FromQuery] SvRequestQuery query, CancellationToken ct) => Ok((await _requests.GetRequestsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("requests/{id:int}")]
        public async Task<IActionResult> RequestAsync(int id, [FromQuery] DateTime? asOf, CancellationToken ct) => Ok(await _requests.GetRequestAsync(id, asOf, ct));

        [HttpPost("requests")]
        public async Task<IActionResult> SaveRequestAsync([FromBody] SvSaveRequestDto dto, CancellationToken ct) => Ok(await _requests.SaveRequestAsync(dto, ct));

        [HttpPost("requests/action")]
        public async Task<IActionResult> RequestActionAsync([FromBody] SvRequestActionDto dto, CancellationToken ct) => Ok(await _requests.RequestActionAsync(dto, ct));
    }

    /// <summary>Service orders: planning, time, parts, measurements, the warranty decision, closing and the invoice of billable work.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/service/orders")]
    public class ServiceOrdersController : ControllerBase
    {
        private readonly ISvOrderService _orders;

        public ServiceOrdersController(ISvOrderService orders)
        {
            _orders = orders;
        }

        [HttpGet]
        public async Task<IActionResult> OrdersAsync([FromQuery] SvOrderQuery query, CancellationToken ct) => Ok((await _orders.GetOrdersAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("{id:int}")]
        public async Task<IActionResult> OrderAsync(int id, CancellationToken ct) => Ok(await _orders.GetOrderAsync(id, ct));

        [HttpPost]
        public async Task<IActionResult> SaveOrderAsync([FromBody] SvSaveOrderDto dto, CancellationToken ct) => Ok(await _orders.SaveOrderAsync(dto, ct));

        [HttpPost("action")]
        public async Task<IActionResult> OrderActionAsync([FromBody] SvOrderActionDto dto, CancellationToken ct) => Ok(await _orders.OrderActionAsync(dto, ct));

        [HttpPost("time-entries")]
        public async Task<IActionResult> SaveTimeEntryAsync([FromBody] SvSaveTimeEntryDto dto, CancellationToken ct) => Ok(await _orders.SaveTimeEntryAsync(dto, ct));

        [HttpPost("time-entries/remove")]
        public async Task<IActionResult> RemoveTimeEntryAsync([FromBody] SvRemoveRowDto dto, CancellationToken ct) => Ok(await _orders.RemoveTimeEntryAsync(dto, ct));

        [HttpPost("measurements")]
        public async Task<IActionResult> SaveMeasurementAsync([FromBody] SvSaveMeasurementDto dto, CancellationToken ct) => Ok(await _orders.SaveMeasurementAsync(dto, ct));

        [HttpPost("measurements/remove")]
        public async Task<IActionResult> RemoveMeasurementAsync([FromBody] SvRemoveRowDto dto, CancellationToken ct) => Ok(await _orders.RemoveMeasurementAsync(dto, ct));

        [HttpPost("decision")]
        public async Task<IActionResult> DecideAsync([FromBody] SvDecisionDto dto, CancellationToken ct) => Ok(await _orders.DecideAsync(dto, ct));

        [HttpPost("goodwill-request")]
        public async Task<IActionResult> GoodwillAsync([FromBody] SvGoodwillRequestDto dto, CancellationToken ct) => Ok(await _orders.RequestGoodwillAsync(dto, ct));

        [HttpPost("draft-invoice")]
        public async Task<IActionResult> DraftInvoiceAsync([FromBody] SvDraftInvoiceDto dto, CancellationToken ct) => Ok(await _orders.DraftInvoiceAsync(dto, ct));
    }

    /// <summary>Service agreements (LTSA), the jobs, satisfaction surveys, the service dashboard and the KPIs.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/service")]
    public class ServiceAgreementsController : ControllerBase
    {
        private readonly ISvContractService _contracts;
        private readonly ISvSurveyService _surveys;
        private readonly ISvDashboardService _dashboard;
        private readonly ISvLabourService _labour;

        public ServiceAgreementsController(ISvContractService contracts, ISvSurveyService surveys, ISvDashboardService dashboard, ISvLabourService labour)
        {
            _labour = labour;
            _contracts = contracts;
            _surveys = surveys;
            _dashboard = dashboard;
        }

        [HttpGet("contracts")]
        public async Task<IActionResult> ContractsAsync([FromQuery] SvContractQuery query, CancellationToken ct) => Ok((await _contracts.GetContractsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("contracts/{id:int}")]
        public async Task<IActionResult> ContractAsync(int id, CancellationToken ct) => Ok(await _contracts.GetContractAsync(id, ct));

        [HttpPost("contracts")]
        public async Task<IActionResult> SaveContractAsync([FromBody] SvSaveContractDto dto, CancellationToken ct) => Ok(await _contracts.SaveContractAsync(dto, ct));

        [HttpPost("contracts/action")]
        public async Task<IActionResult> ContractActionAsync([FromBody] SvContractActionDto dto, CancellationToken ct) => Ok(await _contracts.ContractActionAsync(dto, ct));

        [HttpPost("jobs/run")]
        public async Task<IActionResult> RunJobsAsync([FromBody] SvJobsRunDto dto, CancellationToken ct) => Ok(await _contracts.RunJobsAsync(dto, ct));

        /// <summary>The monthly aggregate posting of the protected labour (finance manager, CEO): one accounting event per ended Persian month, without order numbers.</summary>
        [HttpPost("labour/aggregate")]
        public async Task<IActionResult> AggregateLabourAsync([FromBody] SvJobsRunDto dto, CancellationToken ct) => Ok(await _labour.RunAsync(dto, ct));

        [HttpGet("surveys")]
        public async Task<IActionResult> SurveysAsync([FromQuery] SlSurveyQuery query, CancellationToken ct) => Ok((await _surveys.GetSurveysAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("surveys/{id:int}")]
        public async Task<IActionResult> SurveyAsync(int id, CancellationToken ct) => Ok(await _surveys.GetSurveyAsync(id, ct));

        [HttpPost("surveys/action")]
        public async Task<IActionResult> SurveyActionAsync([FromBody] SlSurveyActionDto dto, CancellationToken ct) => Ok(await _surveys.SurveyActionAsync(dto, ct));

        [HttpPost("surveys/answer")]
        public async Task<IActionResult> SurveyAnswerAsync([FromBody] SlSurveyAnswerSaveDto dto, CancellationToken ct) => Ok(await _surveys.AnswerAsync(dto, ct));

        [HttpGet("dashboard")]
        public async Task<IActionResult> DashboardAsync([FromQuery] DateTime? asOf, CancellationToken ct) => Ok(await _dashboard.GetAsync(asOf, ct));

        [HttpGet("kpis")]
        public async Task<IActionResult> KpisAsync([FromQuery] SvKpiQuery query, CancellationToken ct) => Ok(await _dashboard.GetKpisAsync(query, ct));
    }
}
