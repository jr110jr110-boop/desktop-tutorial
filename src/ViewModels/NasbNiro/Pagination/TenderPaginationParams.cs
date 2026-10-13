using System;

namespace ViewModels.NasbNiro.Pagination
{
    public class TenderPaginationParams : BaseEntityPaginationParams
    {
        public string Contains { get; set; }

        public DateTimeOffset? FromDate { get; set; }
        public DateTimeOffset? ToDate { get; set; }
    }
}
