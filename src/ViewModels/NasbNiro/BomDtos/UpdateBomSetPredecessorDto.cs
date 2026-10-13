using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.BomDtos
{
    public class UpdateBomSetPredecessorDto
    {
        [Required]
        public int BomId { get; set; }

        [Required]
        public int PredecessorId { get; set; }
    }
}
