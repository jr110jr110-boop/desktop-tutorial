using System;

namespace ViewModels.NasbNiro.ReportsDtos
{
    public class GetProgressReportDto
    {
        public int Id { get; set; }
        public DateTimeOffset ToDate { get; set; } = DateTimeOffset.UtcNow;
    }
}
