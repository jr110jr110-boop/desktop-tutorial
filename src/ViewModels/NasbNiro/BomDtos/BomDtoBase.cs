using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.BomDtos
{
    public abstract class BomDtoBase
    {
        [Required]
        public int PositionNumber { get; set; }

        [Required]
        public string Title { get; set; }

        [Required]
        public int Count { get; set; }

        [Required]
        public string Unit { get; set; }

        [Required]
        public string Material { get; set; }

        [Required]
        public string Standard { get; set; }

        [Required]
        public string Diagonal { get; set; }
        public string DiagonalUnit { get; set; }

        [Required]
        public string Length { get; set; }
        public string LengthUnit { get; set; }

        [Required]
        public string Width { get; set; }
        public string WidthUnit { get; set; }

        [Required]
        public string Height { get; set; }
        public string HeightUnit { get; set; }

        [Required]
        public decimal NotWetWeight { get; set; }
        public decimal NotWetWeightUnit { get; set; }

        [Required]
        public decimal WetWeight { get; set; }
        public decimal WetWeightUnit { get; set; }

        public string Description { get; set; }

        public int Sort { get; set; }

        [Required]
        public int AssemblyId { get; set; }
    }
}
