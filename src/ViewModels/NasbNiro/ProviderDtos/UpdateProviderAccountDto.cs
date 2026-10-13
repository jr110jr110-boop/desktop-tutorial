using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace ViewModels.NasbNiro.ProviderDtos
{
    public class UpdateProviderAccountDto : ProviderAccountDtoBase
    {

        [Required]
        public int Id { get; set; }

    }
}
