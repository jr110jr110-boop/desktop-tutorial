using System;
using System.Collections.Generic;

namespace ViewModels.NasbNiro.FinanceDtos
{
    public class FnStatementQuery
    {
        public int? FiscalYearId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        /// <summary>Include Draft and Checked vouchers (default: final only, like the other ledger reports).</summary>
        public bool IncludeDraft { get; set; }
    }

    public class FnCashFlowLineDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Section { get; set; }
        public decimal Amount { get; set; }
        public List<string> Accounts { get; set; } = new List<string>();
    }

    public class FnCashFlowDto
    {
        public int FiscalYearId { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public decimal NetProfit { get; set; }
        public decimal Operating { get; set; }
        public decimal Investing { get; set; }
        public decimal Financing { get; set; }
        public decimal NetChange { get; set; }
        public decimal OpeningCash { get; set; }
        public decimal ClosingCash { get; set; }
        /// <summary>The closing cash of the statement equals the cash accounts of the ledger.</summary>
        public bool Ties { get; set; }
        public List<FnCashFlowLineDto> Lines { get; set; } = new List<FnCashFlowLineDto>();
    }

    public class FnEquityRowDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public decimal Opening { get; set; }
        public decimal Movement { get; set; }
        public decimal Closing { get; set; }
    }

    public class FnEquityDto
    {
        public int FiscalYearId { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public decimal NetProfit { get; set; }
        public decimal OpeningEquity { get; set; }
        public decimal ClosingEquity { get; set; }
        /// <summary>Total assets minus total liabilities at the end date.</summary>
        public decimal BalanceSheetEquity { get; set; }
        public bool Ties { get; set; }
        public List<FnEquityRowDto> Rows { get; set; } = new List<FnEquityRowDto>();
    }

    public class FnRatioDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public decimal? Value { get; set; }
        public string Formula { get; set; }
    }

    public class FnRatiosDto
    {
        public int FiscalYearId { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int Days { get; set; }
        public decimal Revenue { get; set; }
        public decimal CostOfSales { get; set; }
        public decimal NetProfit { get; set; }
        public List<FnRatioDto> Ratios { get; set; } = new List<FnRatioDto>();
    }

    public class FnDrillRowDto
    {
        public int VoucherId { get; set; }
        public int? Number { get; set; }
        public int TemporaryNumber { get; set; }
        public DateTime Date { get; set; }
        public string Description { get; set; }
        public string AccountCode { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string SourceType { get; set; }
        public string SourceNumber { get; set; }
    }

    public class FnDrillDto
    {
        public string AccountPrefix { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public List<FnDrillRowDto> Rows { get; set; } = new List<FnDrillRowDto>();
    }

    public class FnSaveViewDto
    {
        public int? Id { get; set; }
        public string ReportKey { get; set; }
        public string Title { get; set; }
        public string FiltersJson { get; set; }
        public string ColumnsJson { get; set; }
        public bool Shared { get; set; }
    }

    public class FnViewDto
    {
        public int Id { get; set; }
        public string ReportKey { get; set; }
        public string Title { get; set; }
        public string FiltersJson { get; set; }
        public string ColumnsJson { get; set; }
        public bool Shared { get; set; }
        public int OwnerUserId { get; set; }
    }
}
