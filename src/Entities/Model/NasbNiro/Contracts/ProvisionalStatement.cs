using System;

namespace Entities.Model.NasbNiro.Contracts
{
    public class ProvisionalStatement : BaseEntity
    {
        public int ContractId { get; set; }
        public int Number { get; set; }
        public DateTimeOffset Date { get; set; }
        public decimal Price { get; set; }
        public decimal AcceptedPrice { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
    }
}
