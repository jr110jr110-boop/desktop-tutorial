using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.ProviderDtos
{
    public class UpdateProviderContactDto : ProviderContactDtoBase
    {
        [Required]
        public int Id { get; set; }
    }
}
