using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public class UpdateProcessActualEndDto
    {
        [Required]
        public int Id { get; set; }

        //public DateTimeOffset? OptionalActualEndDate { get; set; }
    }
}
