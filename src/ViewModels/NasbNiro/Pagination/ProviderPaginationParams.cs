namespace ViewModels.NasbNiro.Pagination
{
    public class ProviderPaginationParams : BaseEntityPaginationParams
    {
        public string Contains { get; set; }
        public int? CategoryId { get; set; }
    }
}
