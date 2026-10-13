namespace ViewModels.NasbNiro.Pagination
{
    public class UserPaginationParams : BaseEntityPaginationParams
    {
        public string Contains { get; set; }
        public string Role { get; set; }
        public int? Id { get; set; }
    }
}
