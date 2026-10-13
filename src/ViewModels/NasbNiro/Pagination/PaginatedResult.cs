using System.Collections.Generic;

namespace ViewModels.NasbNiro.Pagination
{
    public sealed class PaginatedResult<T>
    {
        public IEnumerable<T> Data { get; set; }

        public int CurrentPage { get; set; }
        public int ItemsPerPage { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
    }
}
