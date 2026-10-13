using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public class UpdateProcessMasterAcceptDto
    {
        [Required]
        public int Id { get; set; }

        //public DateTimeOffset? OptionalMasterAcceptDate { get; set; }
    }
}
