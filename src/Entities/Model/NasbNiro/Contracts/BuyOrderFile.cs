using System;

namespace Entities.Model.NasbNiro.Contracts
{
    public class BuyOrderFile
    {
        protected BuyOrderFile()
        {

        }

        public BuyOrderFile(Guid documentId, int buyOrderId)
        {
            DocumentId = documentId;
            BuyOrderId = buyOrderId;
        }

        public virtual Document Document { get; protected set; }
        public Guid DocumentId { get; protected set; }

        public virtual BuyOrder BuyOrder { get; protected set; }
        public int BuyOrderId { get; protected set; }
    }
}
