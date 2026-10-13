namespace ViewModels.NasbNiro.ReportsDtos
{
    public class MaterialBalanceDto
    {
        public int BomId { get; set; }
        public string BomTitle { get; set; }
        public int NeededCount { get; set; }
        public int ExistingCount { get; set; }
        public string ApiError { get; set; }
    }
}
