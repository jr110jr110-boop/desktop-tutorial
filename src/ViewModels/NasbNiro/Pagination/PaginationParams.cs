namespace ViewModels.NasbNiro.Pagination
{
    public class PaginationParams
    {
        public int PageNumber { get; set; } = 1;

        private const int maxPageSize = 200;
        private int _pageSize = 0;

        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (value <= 0) _pageSize = 0;
                else _pageSize = (value > maxPageSize) ? maxPageSize : value;
            }
        }
    }
}
