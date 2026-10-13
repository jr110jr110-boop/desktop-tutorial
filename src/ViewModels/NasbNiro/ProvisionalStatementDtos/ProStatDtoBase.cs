using System;
using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.ProvisionalStatementDtos
{
    public class ProStatDtoBase
    {
        [Required]
        public int ContractId { get; set; }

        public int Number { get; set; }
        public DateTimeOffset Date { get; set; }
        public decimal Price { get; set; }
        public decimal AcceptedPrice { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
    }
}
