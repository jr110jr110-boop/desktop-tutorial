using System;

namespace ViewModels.NasbNiro.ReportsDtos
{
    public class WeightPercentReport
    {
        public decimal? Actual { get; set; }
        public decimal? Planned { get; set; }
        public DateTimeOffset? Date { get; set; }
    }
}
