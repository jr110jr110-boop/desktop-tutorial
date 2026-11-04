using System;
using System.Collections.Generic;

namespace ViewModels.NasbNiro.DashboardDtos
{
    // Package P-B7b: read-only management dashboards (EPM). One generic widget schema (kpis, charts, tables) is rendered by one front component; every widget carries the route it drills down to.

    public class DashKpiDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public decimal? Value { get; set; }
        public string Unit { get; set; }

        /// <summary>ok | warn | bad | info (how the front colours the figure).</summary>
        public string Tone { get; set; } = "info";
        public string Link { get; set; }

        /// <summary>The figure is money and the caller has no price access: the value is empty.</summary>
        public bool Masked { get; set; }
    }

    public class DashSeriesDto
    {
        public string Name { get; set; }
        public List<decimal?> Values { get; set; } = new List<decimal?>();
    }

    public class DashChartDto
    {
        public string Key { get; set; }
        public string Title { get; set; }

        /// <summary>bar | line | donut</summary>
        public string Kind { get; set; } = "bar";
        public List<string> Labels { get; set; } = new List<string>();
        public List<DashSeriesDto> Series { get; set; } = new List<DashSeriesDto>();
        public string Link { get; set; }
        public bool Masked { get; set; }
    }

    public class DashTableDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public List<string> Columns { get; set; } = new List<string>();
        public List<List<string>> Rows { get; set; } = new List<List<string>>();

        /// <summary>The route of each row (same order as the rows; null when a row has no page).</summary>
        public List<string> RowLinks { get; set; } = new List<string>();
        public string Link { get; set; }
    }

    public class DashboardDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public DateTimeOffset GeneratedAt { get; set; }
        public bool PricesMasked { get; set; }
        public int? ContractId { get; set; }
        public List<DashKpiDto> Kpis { get; set; } = new List<DashKpiDto>();
        public List<DashChartDto> Charts { get; set; } = new List<DashChartDto>();
        public List<DashTableDto> Tables { get; set; } = new List<DashTableDto>();
    }

    public class DashboardIndexDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool NeedsContract { get; set; }
    }

    // ------------------------------------------------------------------------------------------ portfolio EVM (R15)

    public class PortfolioEvmRowDto
    {
        public int ContractId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public decimal? Bac { get; set; }
        public decimal? Pv { get; set; }
        public decimal? Ev { get; set; }
        public decimal? Ac { get; set; }
        public decimal? Cpi { get; set; }
        public decimal? Spi { get; set; }
        public decimal? EacTypical { get; set; }
        public string Tone { get; set; }
    }

    public class PortfolioEvmDto
    {
        public DateTime AsOf { get; set; }
        public bool PricesMasked { get; set; }
        public int Projects { get; set; }

        /// <summary>Portfolio CPI = sum EV / sum AC and SPI = sum EV / sum PV (cost weighted, not the mean of the project indices).</summary>
        public decimal? Cpi { get; set; }
        public decimal? Spi { get; set; }
        public decimal? Bac { get; set; }
        public decimal? Pv { get; set; }
        public decimal? Ev { get; set; }
        public decimal? Ac { get; set; }
        public decimal? EacTypical { get; set; }
        public List<PortfolioEvmRowDto> Rows { get; set; } = new List<PortfolioEvmRowDto>();
    }

    // ------------------------------------------------------------------------------------------ commitments forecast (R14, R18)

    public class CommitmentsQuery
    {
        public int? ContractId { get; set; }

        /// <summary>week | month</summary>
        public string Period { get; set; } = "week";

        /// <summary>How many periods ahead (default 8 weeks / 6 months, at most 52).</summary>
        public int? Count { get; set; }
        public DateTime? From { get; set; }
    }

    public class CommitmentBucketDto
    {
        public string Label { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public decimal OpenOrders { get; set; }
        public decimal Inquiries { get; set; }
        public decimal Statements { get; set; }
        public decimal Total { get; set; }
    }

    public class CommitmentItemDto
    {
        /// <summary>order | inquiry | statement</summary>
        public string Source { get; set; }
        public string Number { get; set; }
        public int? ContractId { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public string Link { get; set; }
    }

    public class CommitmentsForecastDto
    {
        public string Period { get; set; }
        public DateTime From { get; set; }
        public int? ContractId { get; set; }
        public bool PricesMasked { get; set; }
        public decimal Overdue { get; set; }
        public decimal OpenOrdersTotal { get; set; }
        public decimal InquiriesTotal { get; set; }
        public decimal StatementsTotal { get; set; }
        public decimal Total { get; set; }
        public List<CommitmentBucketDto> Buckets { get; set; } = new List<CommitmentBucketDto>();
        public List<CommitmentItemDto> Items { get; set; } = new List<CommitmentItemDto>();
    }

    // ------------------------------------------------------------------------------------------ delay causes (R19)

    public class DelayCauseQuery
    {
        public int? ContractId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string CauseCode { get; set; }
    }

    public class DelayCauseRowDto
    {
        public string CauseCode { get; set; }
        public string Cause { get; set; }
        public int LateLines { get; set; }
        public int Activities { get; set; }
        public int Reports { get; set; }
        public int OpenIssues { get; set; }
        public int ClosedIssues { get; set; }
    }

    public class DelayResponsibleRowDto
    {
        public string CauseCode { get; set; }
        public int? PositionId { get; set; }
        public string Responsible { get; set; }
        public int OpenIssues { get; set; }
        public int ClosedIssues { get; set; }
        public int OverdueIssues { get; set; }
    }

    public class DelayCauseReportDto
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int? ContractId { get; set; }
        public int TotalLateLines { get; set; }
        public List<DelayCauseRowDto> ByCause { get; set; } = new List<DelayCauseRowDto>();
        public List<DelayResponsibleRowDto> ByResponsible { get; set; } = new List<DelayResponsibleRowDto>();
    }
}
