using Entities.Identity;
using System;

namespace Entities.Model.NasbNiro.Contracts
{
    public class ProcessFile
    {
        public int Id { get; set; }
        public Guid DocumentId { get; set; }
        public int ProcessId { get; set; }
        public string Description { get; set; }
        public int Major { get; set; }
        public int Minor { get; set; }
        public int UserId { get; set; }
        public virtual User User { get; set; }
        public int Issue { get; set; }
        public DateTime RegDate { get; set; }
        public ProcessEntity Process { get; set; }
    }
}