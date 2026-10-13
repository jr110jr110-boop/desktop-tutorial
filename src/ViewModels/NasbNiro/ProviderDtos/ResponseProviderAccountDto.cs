using System;
using System.Collections.Generic;
using System.Text;

namespace ViewModels.NasbNiro.ProviderDtos
{
    public class ResponseProviderAccountDto : ProviderAccountDtoBase
    {
        public int Id { get; set; }
        public bool Deleted { get; set; }
    }
}
