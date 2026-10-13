using System;

namespace Entities.Model.NasbNiro.Contracts
{
    public class ContractFile
    {
        public virtual Document Document { get; set; }
        public Guid DocumentId { get; set; }

        public virtual ContractEntity Contract { get; set; }
        public int ContractId { get; set; }
    }
}
