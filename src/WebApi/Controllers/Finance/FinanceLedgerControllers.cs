using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Finance;
using ViewModels.NasbNiro.FinanceDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // General ledger (release F1): fiscal years, chart of accounts, vouchers and reports. Role and segregation checks per step are in the services.
    [ApiController]
    [Authorize]
    [Route("api/v1/finance/gl")]
    public class GlController : ControllerBase
    {
        private readonly IGlService _gl;
        private readonly ILedgerReportService _reports;

        public GlController(IGlService gl, ILedgerReportService reports)
        {
            _gl = gl;
            _reports = reports;
        }

        [HttpGet("years")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> YearsAsync([FromQuery] int? legalEntityId, CancellationToken ct) => Ok(await _gl.GetYearsAsync(legalEntityId, ct));

        [HttpPost("years/ensure")]
        public async Task<IActionResult> EnsureYearAsync([FromBody] EnsureYearDto dto, CancellationToken ct) => Ok(await _gl.EnsureYearAsync(dto, ct));

        [HttpPost("periods/action")]
        public async Task<IActionResult> PeriodActionAsync([FromBody] PeriodActionDto dto, CancellationToken ct) => Ok(await _gl.PeriodActionAsync(dto, ct));

        [HttpPost("years/{id:int}/close")]
        public async Task<IActionResult> CloseYearAsync(int id, [FromBody] CloseYearDto dto, CancellationToken ct) => Ok(await _gl.CloseYearAsync(id, dto ?? new CloseYearDto(), ct));

        [HttpGet("accounts")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> AccountsAsync([FromQuery] AccountQuery query, CancellationToken ct) => Ok((await _gl.GetAccountsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("accounts")]
        public async Task<IActionResult> SaveAccountAsync([FromBody] SaveAccountDto dto, CancellationToken ct) => Ok(await _gl.SaveAccountAsync(dto, ct));

        [HttpDelete("accounts/{id:int}")]
        public async Task<IActionResult> DeleteAccountAsync(int id, CancellationToken ct) => Ok(await _gl.DeleteAccountAsync(id, ct));

        [HttpGet("details")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> DetailsAsync([FromQuery] DetailAccountQuery query, CancellationToken ct) => Ok((await _gl.GetDetailsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("details")]
        public async Task<IActionResult> SaveDetailAsync([FromBody] SaveDetailAccountDto dto, CancellationToken ct) => Ok(await _gl.SaveDetailAsync(dto, ct));

        [HttpGet("vouchers")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> VouchersAsync([FromQuery] VoucherQuery query, CancellationToken ct) => Ok((await _gl.GetVouchersAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("vouchers/{id:int}")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> VoucherAsync(int id, CancellationToken ct) => Ok(await _gl.GetVoucherAsync(id, ct));

        [HttpPost("vouchers")]
        public async Task<IActionResult> SaveVoucherAsync([FromBody] SaveVoucherDto dto, CancellationToken ct) => Ok(await _gl.SaveVoucherAsync(dto, ct));

        [HttpDelete("vouchers/{id:int}")]
        public async Task<IActionResult> DeleteVoucherAsync(int id, CancellationToken ct) => Ok(await _gl.DeleteVoucherAsync(id, ct));

        [HttpPost("vouchers/action")]
        public async Task<IActionResult> VoucherActionAsync([FromBody] VoucherActionDto dto, CancellationToken ct) => Ok(await _gl.VoucherActionAsync(dto, ct));

        [HttpPost("vouchers/finalize-up-to")]
        public async Task<IActionResult> FinalizeUpToAsync([FromBody] FinalizeUpToDto dto, CancellationToken ct) => Ok(await _gl.FinalizeUpToAsync(dto, ct));

        /// <summary>Turns the pending accounting events into Auto vouchers (also done by the background job).</summary>
        [HttpPost("post-events")]
        public async Task<IActionResult> PostEventsAsync(CancellationToken ct) => Ok(await _gl.PostEventsAsync(ct));

        [HttpPost("opening/import")]
        public async Task<IActionResult> ImportOpeningAsync([FromBody] OpeningImportDto dto, CancellationToken ct) => Ok(await _gl.ImportOpeningAsync(dto, ct));

        /// <summary>Lists what is missing before the ledger can be used (fiscal year, chart, role mapping, VAT rate).</summary>
        [HttpGet("setup-check")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> SetupCheckAsync(CancellationToken ct) => Ok(await _gl.SetupCheckAsync(ct));

        // reports
        [HttpGet("reports/journal")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> JournalAsync([FromQuery] LedgerReportQuery query, CancellationToken ct) => Ok((await _reports.JournalAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("reports/general-ledger")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> GeneralLedgerAsync([FromQuery] LedgerReportQuery query, CancellationToken ct) => Ok(await _reports.GeneralLedgerAsync(query, ct));

        [HttpGet("reports/trial-balance")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> TrialBalanceAsync([FromQuery] LedgerReportQuery query, CancellationToken ct) => Ok(await _reports.TrialBalanceAsync(query, ct));

        [HttpGet("reports/account-review")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> AccountReviewAsync([FromQuery] LedgerReportQuery query, CancellationToken ct) => Ok(await _reports.AccountReviewAsync(query, ct));

        [HttpGet("reports/balance-sheet")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> BalanceSheetAsync([FromQuery] LedgerReportQuery query, CancellationToken ct) => Ok(await _reports.BalanceSheetAsync(query, ct));

        [HttpGet("reports/income-statement")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> IncomeStatementAsync([FromQuery] LedgerReportQuery query, CancellationToken ct) => Ok(await _reports.IncomeStatementAsync(query, ct));

        [HttpGet("reports/project-profitability")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ProjectProfitabilityAsync([FromQuery] LedgerReportQuery query, CancellationToken ct) =>
            Ok((await _reports.ProjectProfitabilityAsync(query, ct)).ToPaginatedResultSafe());

        /// <summary>Sub-ledgers against the ledger: inventory, GRNI, payables, bank; unposted events and old non-final vouchers.</summary>
        [HttpGet("reports/reconciliation")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ReconciliationAsync([FromQuery] int oldVoucherDays = 30, CancellationToken ct = default) => Ok(await _reports.ReconcileAsync(oldVoucherDays, ct));

        /// <summary>CSV (UTF-8 with BOM) of a report: journal, general-ledger, account-review, trial-balance, balance-sheet, income-statement, project-profitability, reconciliation.</summary>
        [HttpGet("reports/export/{report}")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ExportAsync(string report, [FromQuery] LedgerReportQuery query, CancellationToken ct)
        {
            var (content, fileName, error) = await _reports.ExportAsync(report, query ?? new LedgerReportQuery(), ct);
            if (error != null) return Ok(Common.Results.ResponseDtoHelper.BadRequest<bool>(error));
            return File(content, "text/csv; charset=utf-8", fileName);
        }
    }

    // Treasury: bank accounts, cash boxes, petty cash, payments, receipts, cheques, guarantees, statements and reconciliation.
    [ApiController]
    [Authorize]
    [Route("api/v1/finance/treasury")]
    public class TreasuryController : ControllerBase
    {
        private readonly ITreasuryService _service;
        public TreasuryController(ITreasuryService service) => _service = service;

        [HttpGet("bank-accounts")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> BankAccountsAsync([FromQuery] TreasuryQuery query, CancellationToken ct) => Ok((await _service.GetBankAccountsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("bank-accounts")]
        public async Task<IActionResult> SaveBankAccountAsync([FromBody] SaveBankAccountDto dto, CancellationToken ct) => Ok(await _service.SaveBankAccountAsync(dto, ct));

        [HttpGet("cash-boxes")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> CashBoxesAsync([FromQuery] TreasuryQuery query, CancellationToken ct) => Ok((await _service.GetCashBoxesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("cash-boxes")]
        public async Task<IActionResult> SaveCashBoxAsync([FromBody] SaveCashBoxDto dto, CancellationToken ct) => Ok(await _service.SaveCashBoxAsync(dto, ct));

        [HttpGet("petty-cash")]
        public async Task<IActionResult> PettyCashesAsync([FromQuery] TreasuryQuery query, CancellationToken ct) => Ok((await _service.GetPettyCashesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("petty-cash")]
        public async Task<IActionResult> SavePettyCashAsync([FromBody] SavePettyCashDto dto, CancellationToken ct) => Ok(await _service.SavePettyCashAsync(dto, ct));

        // payments
        [HttpGet("payable-requests")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> PayableRequestsAsync([FromQuery] TreasuryQuery query, CancellationToken ct) => Ok((await _service.GetPayableRequestsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("payments")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> PaymentsAsync([FromQuery] TreasuryQuery query, CancellationToken ct) => Ok((await _service.GetPaymentsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("payments/{id:int}")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> PaymentAsync(int id, CancellationToken ct) => Ok(await _service.GetPaymentAsync(id, ct));

        [HttpPost("payments")]
        public async Task<IActionResult> SavePaymentAsync([FromBody] SavePaymentDto dto, CancellationToken ct) => Ok(await _service.SavePaymentAsync(dto, ct));

        [HttpPost("payments/action")]
        public async Task<IActionResult> PaymentActionAsync([FromBody] DocActionDto dto, CancellationToken ct) => Ok(await _service.PaymentActionAsync(dto, ct));

        // receipts
        [HttpGet("receipts")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ReceiptsAsync([FromQuery] TreasuryQuery query, CancellationToken ct) => Ok((await _service.GetReceiptsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("receipts/{id:int}")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ReceiptAsync(int id, CancellationToken ct) => Ok(await _service.GetReceiptAsync(id, ct));

        [HttpPost("receipts")]
        public async Task<IActionResult> SaveReceiptAsync([FromBody] SaveReceiptDto dto, CancellationToken ct) => Ok(await _service.SaveReceiptAsync(dto, ct));

        [HttpPost("receipts/action")]
        public async Task<IActionResult> ReceiptActionAsync([FromBody] DocActionDto dto, CancellationToken ct) => Ok(await _service.ReceiptActionAsync(dto, ct));

        // cheques
        [HttpGet("received-cheques")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ReceivedChequesAsync([FromQuery] TreasuryQuery query, CancellationToken ct) => Ok((await _service.GetReceivedChequesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("received-cheques/action")]
        public async Task<IActionResult> ReceivedChequeActionAsync([FromBody] DocActionDto dto, CancellationToken ct) => Ok(await _service.ReceivedChequeActionAsync(dto, ct));

        [HttpGet("cheque-books")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ChequeBooksAsync([FromQuery] TreasuryQuery query, CancellationToken ct) => Ok((await _service.GetChequeBooksAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("cheque-books")]
        public async Task<IActionResult> SaveChequeBookAsync([FromBody] SaveChequeBookDto dto, CancellationToken ct) => Ok(await _service.SaveChequeBookAsync(dto, ct));

        [HttpGet("issued-cheques")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> IssuedChequesAsync([FromQuery] IssuedChequeQuery query, CancellationToken ct) => Ok((await _service.GetIssuedChequesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("issued-cheques/action")]
        public async Task<IActionResult> IssuedChequeActionAsync([FromBody] DocActionDto dto, CancellationToken ct) => Ok(await _service.IssuedChequeActionAsync(dto, ct));

        // guarantees
        [HttpGet("guarantees")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> GuaranteesAsync([FromQuery] TreasuryQuery query, CancellationToken ct) => Ok((await _service.GetGuaranteesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("guarantees")]
        public async Task<IActionResult> SaveGuaranteeAsync([FromBody] SaveGuaranteeDto dto, CancellationToken ct) => Ok(await _service.SaveGuaranteeAsync(dto, ct));

        [HttpPost("guarantees/action")]
        public async Task<IActionResult> GuaranteeActionAsync([FromBody] DocActionDto dto, CancellationToken ct) => Ok(await _service.GuaranteeActionAsync(dto, ct));

        // petty cash settlements
        [HttpGet("settlements")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> SettlementsAsync([FromQuery] TreasuryQuery query, CancellationToken ct) => Ok((await _service.GetSettlementsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("settlements")]
        public async Task<IActionResult> SaveSettlementAsync([FromBody] SaveSettlementDto dto, CancellationToken ct) => Ok(await _service.SaveSettlementAsync(dto, ct));

        [HttpPost("settlements/action")]
        public async Task<IActionResult> SettlementActionAsync([FromBody] DocActionDto dto, CancellationToken ct) => Ok(await _service.SettlementActionAsync(dto, ct));

        /// <summary>Receipts bought by the petty cash holder without a purchase order, not yet settled.</summary>
        [HttpGet("petty-cash/{id:int}/candidates")]
        public async Task<IActionResult> PettyCandidatesAsync(int id, [FromQuery] TreasuryQuery query, CancellationToken ct) =>
            Ok((await _service.GetPettyCandidatesAsync(id, query, ct)).ToPaginatedResultSafe());

        // bank statements and reconciliation
        [HttpPost("statements/import")]
        public async Task<IActionResult> ImportStatementAsync([FromBody] ImportStatementDto dto, CancellationToken ct) => Ok(await _service.ImportStatementAsync(dto, ct));

        [HttpGet("statements")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> StatementsAsync([FromQuery] TreasuryQuery query, CancellationToken ct) => Ok((await _service.GetStatementsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("statements/{id:int}")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> StatementAsync(int id, CancellationToken ct) => Ok(await _service.GetStatementAsync(id, ct));

        [HttpPost("statements/{id:int}/auto-match")]
        public async Task<IActionResult> AutoMatchAsync(int id, CancellationToken ct) => Ok(await _service.AutoMatchAsync(id, ct));

        [HttpPost("statements/match")]
        public async Task<IActionResult> MatchAsync([FromBody] StatementMatchDto dto, CancellationToken ct) => Ok(await _service.MatchAsync(dto, ct));

        [HttpPost("reconciliations")]
        public async Task<IActionResult> CreateReconciliationAsync([FromBody] CreateReconciliationDto dto, CancellationToken ct) => Ok(await _service.CreateReconciliationAsync(dto, ct));

        [HttpGet("reconciliations")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> ReconciliationsAsync([FromQuery] TreasuryQuery query, CancellationToken ct) => Ok((await _service.GetReconciliationsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("reconciliations/action")]
        public async Task<IActionResult> ReconciliationActionAsync([FromBody] DocActionDto dto, CancellationToken ct) => Ok(await _service.ReconciliationActionAsync(dto, ct));

        /// <summary>Cash, banks, petty cash, cheques, approved payments, eight weeks forecast and alerts.</summary>
        [HttpGet("cash-position")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> CashPositionAsync(CancellationToken ct) => Ok(await _service.GetCashPositionAsync(ct));
    }

    // Receivables: customers, sales invoices and credit notes, ageing, customer statements.
    [ApiController]
    [Authorize]
    [Route("api/v1/finance/receivables")]
    public class ReceivablesController : ControllerBase
    {
        private readonly IReceivablesService _service;
        public ReceivablesController(IReceivablesService service) => _service = service;

        [HttpGet("customers")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> CustomersAsync([FromQuery] TreasuryQuery query, CancellationToken ct) => Ok((await _service.GetCustomersAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("customers")]
        public async Task<IActionResult> SaveCustomerAsync([FromBody] SaveCustomerDto dto, CancellationToken ct) => Ok(await _service.SaveCustomerAsync(dto, ct));

        [HttpGet("invoices")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> InvoicesAsync([FromQuery] InvoiceQuery query, CancellationToken ct) => Ok((await _service.GetInvoicesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("invoices/{id:int}")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> InvoiceAsync(int id, CancellationToken ct) => Ok(await _service.GetInvoiceAsync(id, ct));

        [HttpPost("invoices")]
        public async Task<IActionResult> SaveInvoiceAsync([FromBody] SaveInvoiceDto dto, CancellationToken ct) => Ok(await _service.SaveInvoiceAsync(dto, ct));

        [HttpPost("invoices/action")]
        public async Task<IActionResult> InvoiceActionAsync([FromBody] DocActionDto dto, CancellationToken ct) => Ok(await _service.InvoiceActionAsync(dto, ct));

        /// <summary>Drafts an invoice for every due contract milestone that has none (also done by the background job).</summary>
        [HttpPost("invoices/draft-from-milestones")]
        public async Task<IActionResult> DraftFromMilestonesAsync(CancellationToken ct) => Ok(await _service.DraftFromMilestonesAsync(ct));

        [HttpGet("ageing")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> AgeingAsync([FromQuery] DateTime? asOf, CancellationToken ct) => Ok(await _service.GetAgeingAsync(asOf, ct));

        [HttpGet("customers/{id:int}/statement")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> StatementAsync(int id, CancellationToken ct) => Ok(await _service.GetCustomerStatementAsync(id, ct));
    }

    // Tax: rate tables (data, never hard-coded), VAT season summary, taxpayer sales list.
    [ApiController]
    [Authorize]
    [Route("api/v1/finance/tax")]
    public class TaxController : ControllerBase
    {
        private readonly ITaxService _service;
        public TaxController(ITaxService service) => _service = service;

        [HttpGet("rates")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> RatesAsync([FromQuery] TreasuryQuery query, CancellationToken ct) => Ok((await _service.GetRatesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("rates")]
        public async Task<IActionResult> SaveRateAsync([FromBody] SaveTaxRateDto dto, CancellationToken ct) => Ok(await _service.SaveRateAsync(dto, ct));

        [HttpGet("vat-season")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> VatSeasonAsync([FromQuery] int year, [FromQuery] int season, CancellationToken ct) => Ok(await _service.GetVatSeasonAsync(year, season, ct));

        [HttpGet("taxpayer-sales")]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> TaxpayerSalesAsync([FromQuery] TreasuryQuery query, CancellationToken ct) => Ok((await _service.GetTaxpayerSalesAsync(query, ct)).ToPaginatedResultSafe());
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/finance/dashboard")]
    public class FinanceDashboardController : ControllerBase
    {
        private readonly IFinanceDashboardService _service;
        public FinanceDashboardController(IFinanceDashboardService service) => _service = service;

        [HttpGet]
        [Authorize(Roles = FinanceRoles.Readers)]
        public async Task<IActionResult> GetAsync(CancellationToken ct) => Ok(await _service.GetAsync(ct));
    }
}
