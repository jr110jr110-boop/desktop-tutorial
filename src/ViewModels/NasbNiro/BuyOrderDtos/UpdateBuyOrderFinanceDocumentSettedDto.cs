using System;
using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public class UpdateBuyOrderFinanceDocumentSettedDto
    {
        [Required]
        public int Id { get; set; }

        public DateTimeOffset? OptionalFinanceDocDate { get; set; }


        [Required]
        public string FinanceType { get; set; }

        [Required]
        public string FinanceNumber { get; set; }

        [Required]
        public decimal FinancePrice { get; set; }
    }
}
