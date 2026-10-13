using System;

namespace Entities.Model.NasbNiro.Tenders
{
    public class TenderFile
    {
        public Guid DocumentId { get; set; }
        public Document Document { get; set; }
        public int TenderId { get; set; }
        public virtual Tender Tender { get; set; }
    }
}
