using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Sales;
using ViewModels.NasbNiro.SalesDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // Sales (release S1 part A). Role checks per action (segregation of duties) are in the services; rule violations answer 200 with success=false.

    /// <summary>Customers, contacts, sites, activities, the link of legacy contracts and tenders, settings and reason codes.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/sales")]
    public class SalesCrmController : ControllerBase
    {
        private readonly ISlCrmService _crm;

        public SalesCrmController(ISlCrmService crm)
        {
            _crm = crm;
        }

        [HttpGet("customers")]
        public async Task<IActionResult> CustomersAsync([FromQuery] SlCustomerQuery query, CancellationToken ct) => Ok((await _crm.GetCustomersAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("customers/{id:int}")]
        public async Task<IActionResult> CustomerAsync(int id, CancellationToken ct) => Ok(await _crm.GetCustomerAsync(id, ct));

        [HttpPost("customers")]
        public async Task<IActionResult> SaveCustomerAsync([FromBody] SlSaveCustomerDto dto, CancellationToken ct) => Ok(await _crm.SaveCustomerAsync(dto, ct));

        [HttpPost("customers/credit")]
        public async Task<IActionResult> SetCreditAsync([FromBody] SlSetCreditDto dto, CancellationToken ct) => Ok(await _crm.SetCreditAsync(dto, ct));

        [HttpGet("contacts")]
        public async Task<IActionResult> ContactsAsync([FromQuery] SlContactQuery query, CancellationToken ct) => Ok((await _crm.GetContactsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("contacts")]
        public async Task<IActionResult> SaveContactAsync([FromBody] SlSaveContactDto dto, CancellationToken ct) => Ok(await _crm.SaveContactAsync(dto, ct));

        [HttpGet("sites")]
        public async Task<IActionResult> SitesAsync([FromQuery] SlSiteQuery query, CancellationToken ct) => Ok((await _crm.GetSitesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("sites")]
        public async Task<IActionResult> SaveSiteAsync([FromBody] SlSaveSiteDto dto, CancellationToken ct) => Ok(await _crm.SaveSiteAsync(dto, ct));

        [HttpGet("activities")]
        public async Task<IActionResult> ActivitiesAsync([FromQuery] SlActivityQuery query, CancellationToken ct) => Ok((await _crm.GetActivitiesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("activities")]
        public async Task<IActionResult> SaveActivityAsync([FromBody] SlSaveActivityDto dto, CancellationToken ct) => Ok(await _crm.SaveActivityAsync(dto, ct));

        [HttpPost("customers/link-legacy")]
        public async Task<IActionResult> LinkLegacyAsync(CancellationToken ct) => Ok(await _crm.LinkLegacyAsync(ct));

        [HttpPost("customers/link")]
        public async Task<IActionResult> LinkAsync([FromBody] SlManualLinkDto dto, CancellationToken ct) => Ok(await _crm.LinkManualAsync(dto, ct));

        [HttpGet("customers/links")]
        public async Task<IActionResult> LinksAsync([FromQuery] SlLinkQuery query, CancellationToken ct) => Ok((await _crm.GetLinksAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("customers/unlinked")]
        public async Task<IActionResult> UnlinkedAsync(CancellationToken ct) => Ok(await _crm.GetUnlinkedAsync(ct));

        [HttpGet("settings")]
        public async Task<IActionResult> SettingsAsync(CancellationToken ct) => Ok(await _crm.GetSettingsAsync(ct));

        [HttpPost("settings")]
        public async Task<IActionResult> SaveSettingAsync([FromBody] SlSaveSettingDto dto, CancellationToken ct) => Ok(await _crm.SaveSettingAsync(dto, ct));

        [HttpGet("reason-codes")]
        public async Task<IActionResult> ReasonCodesAsync([FromQuery] SlReasonCodeQuery query, CancellationToken ct) => Ok((await _crm.GetReasonCodesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("reason-codes")]
        public async Task<IActionResult> SaveReasonCodeAsync([FromBody] SlSaveReasonCodeDto dto, CancellationToken ct) => Ok(await _crm.SaveReasonCodeAsync(dto, ct));
    }

    /// <summary>Inquiries, the bid / no-bid gate, estimates and proposals.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/sales")]
    public class SalesPipelineController : ControllerBase
    {
        private readonly ISlPipelineService _pipeline;
        private readonly ISlEstimateService _estimates;
        private readonly ISlProposalService _proposals;

        public SalesPipelineController(ISlPipelineService pipeline, ISlEstimateService estimates, ISlProposalService proposals)
        {
            _pipeline = pipeline;
            _estimates = estimates;
            _proposals = proposals;
        }

        [HttpGet("inquiries")]
        public async Task<IActionResult> InquiriesAsync([FromQuery] SlInquiryQuery query, CancellationToken ct) => Ok((await _pipeline.GetInquiriesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("inquiries/{id:int}")]
        public async Task<IActionResult> InquiryAsync(int id, CancellationToken ct) => Ok(await _pipeline.GetInquiryAsync(id, ct));

        [HttpPost("inquiries")]
        public async Task<IActionResult> SaveInquiryAsync([FromBody] SlSaveInquiryDto dto, CancellationToken ct) => Ok(await _pipeline.SaveInquiryAsync(dto, ct));

        [HttpPost("inquiries/action")]
        public async Task<IActionResult> InquiryActionAsync([FromBody] SlInquiryActionDto dto, CancellationToken ct) => Ok(await _pipeline.InquiryActionAsync(dto, ct));

        [HttpGet("bid-decisions")]
        public async Task<IActionResult> BidDecisionsAsync([FromQuery] SlBidDecisionQuery query, CancellationToken ct) => Ok((await _pipeline.GetBidDecisionsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("bid-decisions/{inquiryId:int}")]
        public async Task<IActionResult> BidDecisionAsync(int inquiryId, CancellationToken ct) => Ok(await _pipeline.GetBidDecisionAsync(inquiryId, ct));

        [HttpPost("bid-decisions")]
        public async Task<IActionResult> SaveBidDecisionAsync([FromBody] SlSaveBidDecisionDto dto, CancellationToken ct) => Ok(await _pipeline.SaveBidDecisionAsync(dto, ct));

        [HttpPost("bid-decisions/submit")]
        public async Task<IActionResult> SubmitBidDecisionAsync([FromBody] SlBidActionDto dto, CancellationToken ct) => Ok(await _pipeline.SubmitBidDecisionAsync(dto, ct));

        [HttpPost("bid-decisions/decide")]
        public async Task<IActionResult> DecideBidAsync([FromBody] SlBidActionDto dto, CancellationToken ct) => Ok(await _pipeline.DecideBidAsync(dto, ct));

        [HttpGet("estimates")]
        public async Task<IActionResult> EstimatesAsync([FromQuery] SlEstimateQuery query, CancellationToken ct) => Ok((await _estimates.GetEstimatesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("estimates/{id:int}")]
        public async Task<IActionResult> EstimateAsync(int id, CancellationToken ct) => Ok(await _estimates.GetEstimateAsync(id, ct));

        [HttpPost("estimates")]
        public async Task<IActionResult> SaveEstimateAsync([FromBody] SlSaveEstimateDto dto, CancellationToken ct) => Ok(await _estimates.SaveEstimateAsync(dto, ct));

        [HttpPost("estimates/from-equipment")]
        public async Task<IActionResult> FromEquipmentAsync([FromBody] SlEstimateFromEquipmentDto dto, CancellationToken ct) => Ok(await _estimates.FromEquipmentAsync(dto, ct));

        [HttpPost("estimates/action")]
        public async Task<IActionResult> EstimateActionAsync([FromBody] SlEstimateActionDto dto, CancellationToken ct) => Ok(await _estimates.EstimateActionAsync(dto, ct));

        [HttpGet("proposals")]
        public async Task<IActionResult> ProposalsAsync([FromQuery] SlProposalQuery query, CancellationToken ct) => Ok((await _proposals.GetProposalsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("proposals/{id:int}")]
        public async Task<IActionResult> ProposalAsync(int id, CancellationToken ct) => Ok(await _proposals.GetProposalAsync(id, ct));

        [HttpPost("proposals")]
        public async Task<IActionResult> SaveProposalAsync([FromBody] SlSaveProposalDto dto, CancellationToken ct) => Ok(await _proposals.SaveProposalAsync(dto, ct));

        [HttpPost("proposals/action")]
        public async Task<IActionResult> ProposalActionAsync([FromBody] SlProposalActionDto dto, CancellationToken ct) => Ok(await _proposals.ProposalActionAsync(dto, ct));

        [HttpPost("proposals/result")]
        public async Task<IActionResult> ResultAsync([FromBody] SlProposalResultDto dto, CancellationToken ct) => Ok(await _proposals.ResultAsync(dto, ct));

        [HttpGet("statistics/win-loss")]
        public async Task<IActionResult> WinLossAsync([FromQuery] SlWinLossQuery query, CancellationToken ct) => Ok(await _proposals.GetWinLossAsync(query, ct));
    }

    /// <summary>Price lists, quotations, sales orders and the sales dashboard.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/sales")]
    public class SalesOrdersController : ControllerBase
    {
        private readonly ISlOrderService _orders;
        private readonly ISlDashboardService _dashboard;

        public SalesOrdersController(ISlOrderService orders, ISlDashboardService dashboard)
        {
            _orders = orders;
            _dashboard = dashboard;
        }

        [HttpGet("price-lists")]
        public async Task<IActionResult> PriceListsAsync([FromQuery] SlPriceListQuery query, CancellationToken ct) => Ok((await _orders.GetPriceListsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("price-lists/{id:int}")]
        public async Task<IActionResult> PriceListAsync(int id, CancellationToken ct) => Ok(await _orders.GetPriceListAsync(id, ct));

        [HttpPost("price-lists")]
        public async Task<IActionResult> SavePriceListAsync([FromBody] SlSavePriceListDto dto, CancellationToken ct) => Ok(await _orders.SavePriceListAsync(dto, ct));

        [HttpPost("price-lists/action")]
        public async Task<IActionResult> PriceListActionAsync([FromBody] SlPriceListActionDto dto, CancellationToken ct) => Ok(await _orders.PriceListActionAsync(dto, ct));

        [HttpPost("price-lists/suggest")]
        public async Task<IActionResult> SuggestAsync([FromBody] SlPriceSuggestDto dto, CancellationToken ct) => Ok(await _orders.SuggestPricesAsync(dto, ct));

        [HttpGet("quotations")]
        public async Task<IActionResult> QuotationsAsync([FromQuery] SlQuotationQuery query, CancellationToken ct) => Ok((await _orders.GetQuotationsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("quotations/{id:int}")]
        public async Task<IActionResult> QuotationAsync(int id, CancellationToken ct) => Ok(await _orders.GetQuotationAsync(id, ct));

        [HttpPost("quotations")]
        public async Task<IActionResult> SaveQuotationAsync([FromBody] SlSaveQuotationDto dto, CancellationToken ct) => Ok(await _orders.SaveQuotationAsync(dto, ct));

        [HttpPost("quotations/action")]
        public async Task<IActionResult> QuotationActionAsync([FromBody] SlQuotationActionDto dto, CancellationToken ct) => Ok(await _orders.QuotationActionAsync(dto, ct));

        [HttpPost("quotations/to-order")]
        public async Task<IActionResult> QuotationToOrderAsync([FromBody] SlQuotationToOrderDto dto, CancellationToken ct) => Ok(await _orders.QuotationToOrderAsync(dto, ct));

        [HttpGet("orders")]
        public async Task<IActionResult> OrdersAsync([FromQuery] SlOrderQuery query, CancellationToken ct) => Ok((await _orders.GetOrdersAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("orders/{id:int}")]
        public async Task<IActionResult> OrderAsync(int id, CancellationToken ct) => Ok(await _orders.GetOrderAsync(id, ct));

        [HttpPost("orders")]
        public async Task<IActionResult> SaveOrderAsync([FromBody] SlSaveOrderDto dto, CancellationToken ct) => Ok(await _orders.SaveOrderAsync(dto, ct));

        [HttpPost("orders/action")]
        public async Task<IActionResult> OrderActionAsync([FromBody] SlOrderActionDto dto, CancellationToken ct) => Ok(await _orders.OrderActionAsync(dto, ct));

        [HttpPost("orders/credit-approve")]
        public async Task<IActionResult> CreditApproveAsync([FromBody] SlCreditApproveDto dto, CancellationToken ct) => Ok(await _orders.CreditApproveAsync(dto, ct));

        [HttpPost("orders/draft-invoice")]
        public async Task<IActionResult> DraftInvoiceAsync([FromBody] SlDraftInvoiceDto dto, CancellationToken ct) => Ok(await _orders.DraftInvoiceAsync(dto, ct));

        [HttpGet("dashboard")]
        public async Task<IActionResult> DashboardAsync([FromQuery] DateTime? asOf, CancellationToken ct) => Ok(await _dashboard.GetAsync(asOf, ct));
    }
}
