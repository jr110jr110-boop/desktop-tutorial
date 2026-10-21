using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Finance;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.FinanceDtos
{
    // Finance release F3, section 3: foreign currency.

    public class CurrencyDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public int Decimals { get; set; }
        public bool IsBase { get; set; }
    }

    public class ExchangeRateDto
    {
        public int Id { get; set; }
        public string CurrencyCode { get; set; }
        public DateTime Date { get; set; }
        public decimal? Buy { get; set; }
        public decimal? Sell { get; set; }
        public decimal Average { get; set; }
        public FnRateSource Source { get; set; }
        public FnRateType RateType { get; set; }
        public string Note { get; set; }
    }

    public class SaveExchangeRateDto
    {
        public string CurrencyCode { get; set; }
        public DateTime Date { get; set; }
        public decimal? Buy { get; set; }
        public decimal? Sell { get; set; }
        /// <summary>Optional: the mean of buy and sell when empty.</summary>
        public decimal? Average { get; set; }
        public FnRateSource Source { get; set; } = FnRateSource.Manual;
        public FnRateType RateType { get; set; } = FnRateType.Official;
        public string Note { get; set; }
    }

    public class RateQuery : PaginationParams
    {
        public string CurrencyCode { get; set; }
        public FnRateType? RateType { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }

    public class FxRevaluationRequestDto
    {
        public int PeriodId { get; set; }
        public bool DryRun { get; set; }
        /// <summary>Default: the setting Fx.AutoReverse.</summary>
        public bool? Reverse { get; set; }
        /// <summary>Default: the setting Fx.RateType.</summary>
        public FnRateType? RateType { get; set; }
    }

    public class FxRevaluationLineDto
    {
        public string AccountCode { get; set; }
        public string DetailCode { get; set; }
        public string CurrencyCode { get; set; }
        public decimal CurrencyBalance { get; set; }
        public decimal BookBalance { get; set; }
        public decimal Rate { get; set; }
        public decimal NewBalance { get; set; }
        public decimal Difference { get; set; }
    }

    public class FxRevaluationDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int PeriodId { get; set; }
        public int LegalEntityId { get; set; }
        public bool DryRun { get; set; }
        public decimal TotalGain { get; set; }
        public decimal TotalLoss { get; set; }
        public int? VoucherId { get; set; }
        public int? ReversalVoucherId { get; set; }
        public string Warning { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public List<FxRevaluationLineDto> Lines { get; set; } = new List<FxRevaluationLineDto>();
    }

    public class FxSettlementRequestDto
    {
        public int? LegalEntityId { get; set; }
        public DateTime Date { get; set; }
        /// <summary>Receivable or Payable.</summary>
        public string Direction { get; set; }
        /// <summary>The receivable or payable account (a foreign-currency account).</summary>
        public string AccountCode { get; set; }
        public string DetailCode { get; set; }
        /// <summary>The bank or cash account the settlement is paid from / received in (rial).</summary>
        public string CounterAccountCode { get; set; }
        public string CounterDetailCode { get; set; }
        public string CurrencyCode { get; set; }
        public decimal CurrencyAmount { get; set; }
        public decimal BookRate { get; set; }
        public decimal SettleRate { get; set; }
        public string Description { get; set; }
    }

    public class FxSettlementDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public DateTime Date { get; set; }
        public string Direction { get; set; }
        public string CurrencyCode { get; set; }
        public decimal CurrencyAmount { get; set; }
        public decimal BookRate { get; set; }
        public decimal SettleRate { get; set; }
        public decimal BookBase { get; set; }
        public decimal SettledBase { get; set; }
        public decimal Difference { get; set; }
        public int VoucherId { get; set; }
    }

    public class FxExposureRowDto
    {
        public string CurrencyCode { get; set; }
        /// <summary>Open foreign receivables (currency units).</summary>
        public decimal Receivables { get; set; }
        public decimal Payables { get; set; }
        public decimal Net { get; set; }
        public decimal BaseReceivables { get; set; }
        public decimal BasePayables { get; set; }
        /// <summary>Open letters of credit (not yet paid), currency units.</summary>
        public decimal OpenLetterOfCredit { get; set; }
        public decimal NetWithLetterOfCredit { get; set; }
    }

    public class FxExposureDto
    {
        public DateTime AsOf { get; set; }
        public List<FxExposureRowDto> Rows { get; set; } = new List<FxExposureRowDto>();
    }

    public class LetterOfCreditDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public string LcNumber { get; set; }
        public int ProviderId { get; set; }
        public int BankAccountId { get; set; }
        public string CurrencyCode { get; set; }
        public decimal Amount { get; set; }
        public DateTime OpenDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public decimal MarginPercent { get; set; }
        public decimal MarginPaid { get; set; }
        public decimal FeesPaid { get; set; }
        public FnLcStatus Status { get; set; }
        public int? PurchaseOrderId { get; set; }
        public string ForeignInvoiceRef { get; set; }
        public decimal? SettlementRate { get; set; }
        public decimal? SettledBase { get; set; }
        public DateTime? PaidDate { get; set; }
        public string Note { get; set; }
        public List<LcPostingDto> Postings { get; set; } = new List<LcPostingDto>();
    }

    public class LcPostingDto
    {
        public FnLcPostingKind Kind { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public int VoucherId { get; set; }
        public string Note { get; set; }
    }

    public class SaveLetterOfCreditDto
    {
        public int? Id { get; set; }
        public string LcNumber { get; set; }
        public int ProviderId { get; set; }
        public int BankAccountId { get; set; }
        public string CurrencyCode { get; set; }
        public decimal Amount { get; set; }
        public DateTime OpenDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public decimal MarginPercent { get; set; }
        public int? PurchaseOrderId { get; set; }
        public string Note { get; set; }
    }

    public class LcActionDto
    {
        public int Id { get; set; }
        /// <summary>margin, fee, documents, pay, expire, cancel</summary>
        public string Action { get; set; }
        public DateTime? Date { get; set; }
        /// <summary>Margin deposit or fee, rial.</summary>
        public decimal Amount { get; set; }
        /// <summary>Settlement rate (rial per currency unit), for pay.</summary>
        public decimal? Rate { get; set; }
        public string ForeignInvoiceRef { get; set; }
        public string Note { get; set; }
    }
}
