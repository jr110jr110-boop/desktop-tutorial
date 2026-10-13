using System;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public class ResponseProcessFileStatusDto
    {
        public int BomId { get; set; }
        public int ProcessId { get; set; }
        public int AssemblyId { get; set; }
        public string BomTitle { get; set; }
        public string ProcessTitle { get; set; }
        public string AssemblyTitle { get; set; }
        public int Id { get; set; }
        public Guid DocumentId { get; set; }
        public string Description { get; set; }
        public int Major { get; set; }
        public int Minor { get; set; }
        public int UserId { get; set; }
        public string UserDispalyName { get; set; }
        public DateTime RegDate { get; set; }
    }
}
