using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.ProviderDtos
{
    public class UpdateProviderDto : ProviderDtoBase
    {
        [Required]
        public int Id { get; set; }
    }
}
