using Entities.Model.NasbNiro.Providers;
using System;

namespace Entities.Model.NasbNiro.Contracts
{
    public class Inquiry : BaseEntity
    {
        public virtual Provider Provider { get; set; }
        public int ProviderId { get; set; }

        public virtual BuyOrder BuyOrder { get; set; }
        public int BuyOrderId { get; set; }

        public int Count { get; set; }
        public decimal Price { get; set; }
        public bool IsAccept { get; set; }
        public int TechnicalScore { get; set; }
        public Guid TechnicalDoc { get; set; }
        public Guid FinancialDoc { get; set; }
    }
}
