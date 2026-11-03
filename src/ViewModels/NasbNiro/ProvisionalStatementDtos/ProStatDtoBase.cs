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
        // R1a2 (M8): nullable so that the answer can leave the amounts out for a signed-in user who is not allowed to see them (a missing value in a request is saved as 0, as before)
        public decimal? Price { get; set; }
        public decimal? AcceptedPrice { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
    }
}
