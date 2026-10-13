using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.AssemblyDtos
{
    public class UpdateAssemblySetPredecessorDto
    {
        [Required]
        public int Id { get; set; }

        [Required]
        public int PredecessorId { get; set; }
    }
}
