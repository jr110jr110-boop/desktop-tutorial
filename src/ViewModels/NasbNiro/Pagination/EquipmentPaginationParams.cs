namespace ViewModels.NasbNiro.Pagination
{
    public class EquipmentPaginationParams : BaseEntityPaginationParams
    {
        public string Contains { get; set; }
        public bool IncludeTypes { get; set; } = false;
    }
}
