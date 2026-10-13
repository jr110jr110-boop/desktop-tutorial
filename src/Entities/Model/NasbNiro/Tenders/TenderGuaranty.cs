using System;
using System.Collections.Generic;
using System.Text;

namespace Entities.Model.NasbNiro.Tenders
{
    public class TenderGuaranty : BaseEntity
    {
        public string Title { get; set; }
        public string Bank { get; set; }
        public string No { get; set; }
        public DateTime? RegDate { get; set; }
        public string Price { get; set; }
        public string Deposite { get; set; }
        public string ChequeNo { get; set; }
        public DateTime? SentDate { get; set; }
        public DateTime? CancelDate { get; set; }
        public string Extention { get; set; }
        public DateTime? DueDate { get; set; }
        public string Description { get; set; }
        public int TenderId { get; set; }
        public virtual Tender Tender { get; set; }
    }
}
