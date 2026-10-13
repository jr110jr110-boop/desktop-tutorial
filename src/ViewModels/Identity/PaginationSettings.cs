using System;

namespace ViewModels.Identity
{
    /// <summary>
    /// Paging information for the identity list view models.
    /// Replaces cloudscribe.Web.Pagination.PaginationSettings (that package targets ASP.NET Core 2.x).
    /// </summary>
    public class PaginationSettings
    {
        public long TotalItems { get; set; }
        public int ItemsPerPage { get; set; } = 10;
        public int CurrentPage { get; set; } = 1;
        public int MaxPagerItems { get; set; } = 10;

        public int TotalPages =>
            ItemsPerPage <= 0 ? 0 : (int)Math.Ceiling(TotalItems / (double)ItemsPerPage);
    }
}
