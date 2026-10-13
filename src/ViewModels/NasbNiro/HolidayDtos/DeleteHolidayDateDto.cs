using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.HolidayDtos
{
    public class DeleteHolidayDateDto
    {
        [Required]
        public int Id { get; set; }
    }
}
