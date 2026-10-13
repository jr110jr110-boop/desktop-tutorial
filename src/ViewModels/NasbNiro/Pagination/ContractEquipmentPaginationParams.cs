namespace ViewModels.NasbNiro.Pagination
{
    public class ContractEquipmentPaginationParams : PaginationParams
    {
        public int? Count { get; set; }
        public int? ContractId { get; set; }
        public int? EquipmentId { get; set; }
    }
}
