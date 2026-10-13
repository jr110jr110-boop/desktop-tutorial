using System;
using System.Collections.Generic;

namespace Entities.Model.NasbNiro.Tenders
{
    public class Tender : BaseEntity
    {
        public string Code { get; set; }
        public string PmrNo { get; set; }
        public string Description { get; set; }
        public string Count { get; set; }
        public string Company { get; set; }
        public string SubCompany { get; set; }
        public DateTimeOffset ReciveDate { get; set; }
        public DateTimeOffset TenderDate { get; set; }
        public DateTimeOffset? TechSendDate { get; set; }
        public DateTimeOffset? FinanceSendDate { get; set; }
        public string CallResult { get; set; }
        public string CustomerResult { get; set; }
        public string Result { get; set; }
        public string CustomerName { get; set; }
        public string CustomerRole { get; set; }
        public string CustomerTel { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerMobile { get; set; }
        
        public virtual ICollection<TenderFile> TenderFiles { get; set; }
        public virtual ICollection<TenderGuaranty> TenderGuaranties { get; set; }
    }
}
