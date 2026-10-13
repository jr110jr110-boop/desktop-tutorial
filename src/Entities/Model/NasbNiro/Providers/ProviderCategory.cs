using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Entities.Model.NasbNiro.Providers
{
    public class ProviderCategory
    {
        [Key]
        public int Id { get; set; }
        public string   Category { get; set; }
    }
}
