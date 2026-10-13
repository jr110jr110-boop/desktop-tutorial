using System;
using System.Collections.Generic;
using System.Text;

namespace Entities.Model.NasbNiro.Providers
{
    public class ProviderAccount : BaseEntity
    {
        public string Name { get; set; }
        public string Bank { get; set; }
        public string No { get; set; }
        public string SHABA { get; set; }
        public string Card { get; set; }

        public virtual Provider Provider { get; set; }
        public int ProviderId { get; set; }
    }
}
