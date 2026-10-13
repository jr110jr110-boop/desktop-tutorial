using System;

namespace ViewModels.NasbNiro.ReportsDtos
{
    public class ResponseProgressReportDto
    {
        public int Id { get; set; }
        public string WorkOrderTitle { get; set; }
        public int? RemainingDays { get; set; }
        public DateTimeOffset? PlannedStartDate { get; set; }
        public DateTimeOffset? ActualStartDate { get; set; }
        public DateTimeOffset? PlannedEndDate { get; set; }
        public DateTimeOffset? ActualEndDate { get; set; }
        public DateTimeOffset ReportDate { get; set; }
        public decimal? PlannedPrice { get; set; }
        public decimal? ActualPrice { get; set; }
        public decimal? PlannedWeightPercent { get; set; }
        public decimal? ActualWeightPercent { get; set; }
        public PriceReport[] Prices { get; set; }
        public WeightPercentReport[] WeightPercents { get; set; }
    }
}
