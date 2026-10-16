using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.ProcurementDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // Procurement: purchase request -> inquiry and quotes -> selection (committee) -> purchase order -> receipt / service acceptance
    // -> supplier invoice (three-way match) -> payment request. Role checks per step are in the services (segregation of duties).
    [ApiController]
    [Authorize]
    [Route("api/v1/procurement")]
    public class ProcurementController : ControllerBase
    {
        private const string Readers = ProcurementRoles.Readers + ",ASSEMBLER,ENGINEER";
        private readonly IProcurementService _service;
        private readonly IPayablesService _payables;

        public ProcurementController(IProcurementService service, IPayablesService payables)
        {
            _service = service;
            _payables = payables;
        }

        [HttpGet("settings")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> SettingsAsync(CancellationToken ct) => Ok(await _service.GetSettingsAsync(ct));

        [HttpPost("settings")]
        public async Task<IActionResult> SaveSettingAsync([FromBody] ProcurementSettingDto dto, CancellationToken ct) => Ok(await _service.SaveSettingAsync(dto, ct));

        // purchase requests
        [HttpGet("requests")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> RequestsAsync([FromQuery] PurchaseRequestQuery query, CancellationToken ct) =>
            Ok((await _service.GetRequestsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("requests/{id:int}")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> RequestAsync(int id, CancellationToken ct) => Ok(await _service.GetRequestAsync(id, ct));

        [HttpPost("requests")]
        [Authorize(Roles = ProcurementRoles.Requesters)]
        public async Task<IActionResult> SaveRequestAsync([FromBody] SavePurchaseRequestDto dto, CancellationToken ct) => Ok(await _service.SaveRequestAsync(dto, ct));

        [HttpPost("requests/action")]
        public async Task<IActionResult> RequestActionAsync([FromBody] ProcurementActionDto dto, CancellationToken ct) => Ok(await _service.RequestActionAsync(dto, ct));

        // inquiries and quotes
        [HttpGet("inquiries")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> InquiriesAsync([FromQuery] int? purchaseRequestId, CancellationToken ct) => Ok(await _service.GetInquiriesAsync(purchaseRequestId, ct));

        [HttpGet("inquiries/{id:int}")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> InquiryAsync(int id, CancellationToken ct) => Ok(await _service.GetInquiryAsync(id, ct));

        [HttpPost("inquiries")]
        public async Task<IActionResult> SaveInquiryAsync([FromBody] SaveInquiryDto dto, CancellationToken ct) => Ok(await _service.SaveInquiryAsync(dto, ct));

        [HttpPost("inquiries/select")]
        public async Task<IActionResult> SelectQuoteAsync([FromBody] SelectQuoteDto dto, CancellationToken ct) => Ok(await _service.SelectQuoteAsync(dto, ct));

        [HttpPost("inquiries/action")]
        public async Task<IActionResult> InquiryActionAsync([FromBody] ProcurementActionDto dto, CancellationToken ct) => Ok(await _service.InquiryActionAsync(dto, ct));

        // purchase orders
        [HttpGet("orders")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> OrdersAsync([FromQuery] PurchaseOrderQuery query, CancellationToken ct) =>
            Ok((await _service.GetOrdersAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("orders/{id:int}")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> OrderAsync(int id, CancellationToken ct) => Ok(await _service.GetOrderAsync(id, ct));

        [HttpPost("orders")]
        public async Task<IActionResult> SaveOrderAsync([FromBody] SavePurchaseOrderDto dto, CancellationToken ct) => Ok(await _service.SaveOrderAsync(dto, ct));

        [HttpPost("orders/from-inquiry/{inquiryId:int}")]
        public async Task<IActionResult> FromInquiryAsync(int inquiryId, CancellationToken ct) => Ok(await _service.CreateOrderFromInquiryAsync(inquiryId, ct));

        [HttpPost("orders/action")]
        public async Task<IActionResult> OrderActionAsync([FromBody] ProcurementActionDto dto, CancellationToken ct) => Ok(await _service.OrderActionAsync(dto, ct));

        [HttpPost("orders/accept-service")]
        public async Task<IActionResult> AcceptServiceAsync([FromBody] SaveServiceAcceptanceDto dto, CancellationToken ct) => Ok(await _service.AcceptServiceAsync(dto, ct));

        // supplier invoices
        [HttpGet("invoices")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> InvoicesAsync([FromQuery] ProcurementListQuery query, CancellationToken ct) =>
            Ok((await _payables.GetInvoicesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("invoices/{id:int}")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> InvoiceAsync(int id, CancellationToken ct) => Ok(await _payables.GetInvoiceAsync(id, ct));

        [HttpPost("invoices")]
        public async Task<IActionResult> SaveInvoiceAsync([FromBody] SaveSupplierInvoiceDto dto, CancellationToken ct) => Ok(await _payables.SaveInvoiceAsync(dto, ct));

        [HttpPost("invoices/action")]
        public async Task<IActionResult> InvoiceActionAsync([FromBody] ProcurementActionDto dto, CancellationToken ct) => Ok(await _payables.InvoiceActionAsync(dto, ct));

        // payment requests
        [HttpGet("payments")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> PaymentsAsync([FromQuery] ProcurementListQuery query, CancellationToken ct) =>
            Ok((await _payables.GetPaymentsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("payments")]
        public async Task<IActionResult> SavePaymentAsync([FromBody] SavePaymentRequestDto dto, CancellationToken ct) => Ok(await _payables.SavePaymentAsync(dto, ct));

        [HttpPost("payments/action")]
        public async Task<IActionResult> PaymentActionAsync([FromBody] ProcurementActionDto dto, CancellationToken ct) => Ok(await _payables.PaymentActionAsync(dto, ct));

        // approved vendor list and evaluations
        [HttpGet("suppliers")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> SuppliersAsync([FromQuery] string contains, CancellationToken ct) => Ok(await _payables.GetSuppliersAsync(contains, ct));

        [HttpPost("suppliers")]
        public async Task<IActionResult> SaveSupplierAsync([FromBody] SaveSupplierProfileDto dto, CancellationToken ct) => Ok(await _payables.SaveSupplierAsync(dto, ct));

        [HttpGet("suppliers/{providerId:int}/evaluations")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> EvaluationsAsync(int providerId, CancellationToken ct) => Ok(await _payables.GetEvaluationsAsync(providerId, ct));

        [HttpPost("suppliers/evaluate")]
        public async Task<IActionResult> EvaluateAsync([FromBody] SaveSupplierEvaluationDto dto, CancellationToken ct) => Ok(await _payables.EvaluateAsync(dto, ct));
    }
}
