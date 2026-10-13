using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.EquipmentBomDtos
{
    public abstract class EquipmentBomDtoBase
    {
        [Required]
        public int EquipmentAssemblyId { get; set; }

        [Required]
        public int PositionNumber { get; set; }

        [Required]
        public string Title { get; set; }

        [Required]
        public int Count { get; set; }

        public string Unit { get; set; }

        public string Material { get; set; }

        public string Standard { get; set; }

        public string Diagonal { get; set; }
        public string DiagonalUnit { get; set; }

        public string Length { get; set; }
        public string LengthUnit { get; set; }

        public string Width { get; set; }
        public string WidthUnit { get; set; }

        public string Height { get; set; }
        public string HeightUnit { get; set; }

        public decimal NotWetWeight { get; set; }
        public decimal NotWetWeightUnit { get; set; }

        public decimal WetWeight { get; set; }
        public decimal WetWeightUnit { get; set; }

        public string Description { get; set; }

        public int? PredecessorId { get; set; }
        public int Sort { get; set; }
    }
}
