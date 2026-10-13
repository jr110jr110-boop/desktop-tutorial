using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Providers;

namespace ViewModels.NasbNiro.ProviderDtos
{
    public abstract class ProviderContactDtoBase
    {
        [Required]
        public int ProviderId { get; set; }

        public string Name { get; set; }

        [Required]
        public string Data { get; set; }

        [Required]
        public virtual ProviderContactType Type { get; set; }

        public string Description { get; set; }
    }
}
