using System;

namespace ViewModels.NasbNiro.ReportsDtos
{
    public class PriceReport
    {
        public decimal? Actual { get; set; }
        public decimal? Planned { get; set; }
        public DateTimeOffset? Date { get; set; }
    }
}
