namespace ViewModels.NasbNiro.EquipmentProcessDtos
{
    public class EquipmentProcessUpdateResponseDto : ResponseEquipmentProcessDto
    {
        public decimal BomWeightPercent { get; set; }
        public decimal AssemblyWeightPercent { get; set; }
    }
}
