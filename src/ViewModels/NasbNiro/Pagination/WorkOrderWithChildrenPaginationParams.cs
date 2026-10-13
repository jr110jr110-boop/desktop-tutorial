namespace ViewModels.NasbNiro.Pagination
{
    public class WorkOrderWithChildrenPaginationParams : BaseEntityPaginationParams
    {
        public string WorkOrderTitle { get; set; }
        public string AssemblyTitle { get; set; }
        public string BomTitle { get; set; }
        public string ProcessTitle { get; set; }
    }
}
