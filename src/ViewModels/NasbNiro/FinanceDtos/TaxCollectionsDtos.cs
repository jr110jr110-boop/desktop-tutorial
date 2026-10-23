using System;
using System.Collections.Generic;

namespace ViewModels.NasbNiro.FinanceDtos
{
    public class FnVatDetailRowDto
    {
        public string Side { get; set; }
        public DateTime Date { get; set; }
        public string DocumentNumber { get; set; }
        public string PartyName { get; set; }
        public string EconomicCode { get; set; }
        public decimal NetAmount { get; set; }
        public decimal VatAmount { get; set; }
        public string SourceType { get; set; }
    }

    public class FnVatDetailDto
    {
        public int Year { get; set; }
        public int Season { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public decimal SalesNet { get; set; }
        public decimal SalesVat { get; set; }
        public decimal PurchasesNet { get; set; }
        public decimal PurchasesVat { get; set; }
        /// <summary>Sales VAT minus purchase VAT of the season (equals vat-season).</summary>
        public decimal VatPayable { get; set; }
        /// <summary>Credit carried in from earlier seasons of the same Persian year.</summary>
        public decimal CarryIn { get; set; }
        public decimal NetPayable { get; set; }
        public decimal CarryOut { get; set; }
        public List<FnVatDetailRowDto> Rows { get; set; } = new List<FnVatDetailRowDto>();
    }

    public class FnSaveTaxAdjustmentDto
    {
        public int? Id { get; set; }
        public int FiscalYearId { get; set; }
        public int Kind { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
        public string Source { get; set; }
        public bool Verify { get; set; } = true;
    }

    public class FnTaxAdjustmentDto
    {
        public int Id { get; set; }
        public int FiscalYearId { get; set; }
        public int Kind { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
        public string Source { get; set; }
        public bool Verify { get; set; }
    }

    public class FnTaxableProfitQuery
    {
        public int FiscalYearId { get; set; }
        /// <summary>The corporate tax rate is an input (never seeded); empty = only the taxable profit is computed.</summary>
        public decimal? RatePercent { get; set; }
        /// <summary>Create the draft tax-provision voucher (Dr 80401 / Cr 30401).</summary>
        public bool CreateProvision { get; set; }
    }

    public class FnTaxableProfitDto
    {
        public int FiscalYearId { get; set; }
        public decimal BookProfitBeforeTax { get; set; }
        public decimal Adjustments { get; set; }
        public decimal TaxableProfit { get; set; }
        public decimal? RatePercent { get; set; }
        public decimal TaxAmount { get; set; }
        public int? ProvisionVoucherId { get; set; }
        public List<FnTaxAdjustmentDto> Items { get; set; } = new List<FnTaxAdjustmentDto>();
    }

    public class FnBadDebtQuery
    {
        public DateTime? AsOf { get; set; }
        /// <summary>Provision rates in percent per ageing bucket (inputs, never seeded).</summary>
        public decimal? CurrentRate { get; set; }
        public decimal? Days31To60Rate { get; set; }
        public decimal? Days61To90Rate { get; set; }
        public decimal? Over90Rate { get; set; }
        public bool Post { get; set; }
    }

    public class FnBadDebtBucketDto
    {
        public string Bucket { get; set; }
        public decimal Receivable { get; set; }
        public decimal RatePercent { get; set; }
        public decimal Required { get; set; }
    }

    public class FnBadDebtDto
    {
        public int? RunId { get; set; }
        public string Number { get; set; }
        public DateTime AsOf { get; set; }
        public decimal Required { get; set; }
        public decimal PreviousBalance { get; set; }
        public decimal Change { get; set; }
        public int? VoucherId { get; set; }
        public List<FnBadDebtBucketDto> Buckets { get; set; } = new List<FnBadDebtBucketDto>();
    }

    public class FnSetHoldDto
    {
        public int CustomerId { get; set; }
        public bool Active { get; set; }
        public string Reason { get; set; }
    }

    public class FnCreditHoldDto
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public bool Active { get; set; }
        public string Reason { get; set; }
    }

    public class FnDunningDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public DateTime AsOf { get; set; }
        public string Bucket { get; set; }
        public decimal Amount { get; set; }
        public string Text { get; set; }
        public int Status { get; set; }
    }

    public class FnProposalQuery
    {
        public DateTime? AsOf { get; set; }
        /// <summary>0 or empty = no limit.</summary>
        public decimal? CashLimit { get; set; }
    }

    public class FnProposalLineDto
    {
        public int SupplierInvoiceId { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public string InvoiceNumber { get; set; }
        public DateTime DueDate { get; set; }
        public decimal Amount { get; set; }
        public bool Included { get; set; }
    }

    public class FnProposalDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public DateTime AsOf { get; set; }
        public decimal CashLimit { get; set; }
        public decimal Total { get; set; }
        public int Status { get; set; }
        public List<FnProposalLineDto> Lines { get; set; } = new List<FnProposalLineDto>();
    }
}
