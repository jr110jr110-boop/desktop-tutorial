using System;
using Entities.Model.NasbNiro.Contracts;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public class ProcessProgressLevelDto
    {
        public int ProcessId { get; set; }
        public string Title { get; set; }
        public decimal ProgressLevel { get; set; }
        public ProcessStatus Status { get; set; }
        public DateTimeOffset? ProgramStartDate { get; set; }
        public DateTimeOffset? ProgramEndDate { get; set; }
        public DateTimeOffset? ActualStartDate { get; set; }
        public DateTimeOffset? ActualEndDate { get; set; }
    }
}
