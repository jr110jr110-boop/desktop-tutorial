using Entities.Model.NasbNiro.Contracts;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public class AttachfileToProcessDto
    {
        public Guid DocumentId { get; set; }
        public int ProcessId { get; set; }
        public string Description { get; set; }
        public int Major { get; set; }
        public int Minor { get; set; }
        public ProcessFileType UploadCondition { get; set; }
    }
}
