namespace ViewModels.NasbNiro.BomDtos
{
    public class ResponseBomDto : BomDtoBase
    {
        public int Id { get; set; }
        public bool Deleted { get; set; }
        public int? PredecessorId { get; set; }
        public int TotalDuration { get; set; }
        public decimal TotalWeightPercent { get; set; }
    }
}
