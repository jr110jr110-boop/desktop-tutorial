using System;
using System.Collections.Generic;
using System.Text;

namespace ViewModels.NasbNiro.ProviderDtos
{
    public class ProviderAccountDtoBase
    {
        public string Name { get; set; }
        public string Bank { get; set; }
        public string No { get; set; }
        public string SHABA { get; set; }
        public string Card { get; set; }

        public int ProviderId { get; set; }
    }
}
