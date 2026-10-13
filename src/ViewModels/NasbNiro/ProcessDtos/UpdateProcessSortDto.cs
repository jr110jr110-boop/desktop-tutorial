using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public class UpdateProcessSortDto
    {
        [Required]
        public int Id1 { get; set; }

        [Required]
        public int Id2 { get; set; }
    }
}
