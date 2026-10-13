using System.Collections.Generic;
using Entities.Model.NasbNiro.Contracts;

namespace Entities.Model.NasbNiro.Providers
{
    public class Provider : BaseEntity
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public decimal EvaluationScore { get; set; }
        public string ForContract { get; set; }
        public string Services { get; set; }

        public int? CategoryId { get; set; }
        public ProviderCategory Category { get; set; }

        public virtual ICollection<ProviderContact> ProviderContacts { get; set; }
        public virtual ICollection<ProviderAccount> ProviderAccounts { get; set; }
        public virtual ICollection<Inquiry> Inquiries { get; set; }

    }
}
