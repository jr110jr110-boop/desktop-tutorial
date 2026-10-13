using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public class UpdateProcessActualStartDto
    {
        [Required]
        public int Id { get; set; }

        //public DateTimeOffset? OptionalActualStartDate { get; set; }
    }
}
