using System;

namespace ViewModels.NasbNiro.EquipmentProcessDtos
{
    public class ResponseEquipmentProcessFileStatusDto
    {
        public int BomId { get; set; }
        public int ProcessId { get; set; }
        public int AssemblyId { get; set; }
        public string BomTitle { get; set; }
        public string ProcessTitle { get; set; }
        public string AssemblyTitle { get; set; }
        public Guid Id { get; set; }
        public bool IsFinalized { get; set; }
    }
}
