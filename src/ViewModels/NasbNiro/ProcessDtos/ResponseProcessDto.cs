using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.Constants;
using ViewModels.NasbNiro.DocumentDtos;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public class ResponseProcessDto : ProcessDtoBase
    {
        public int Id { get; set; }
        public bool Deleted { get; set; }
        public bool NeedsMasterAcceptance => this.MasterDuration != 0;
        public int UserId { get; set; }
        public int BomCount { get; set; }
        public DateTimeOffset? ProgramStartDate { get; set; }
        public DateTimeOffset? ProgramEndDate { get; set; }
        public DateTimeOffset? ActualStartDate { get; set; }
        public DateTimeOffset? ActualEndDate { get; set; }
        public DateTimeOffset? DocAcceptDate { get; set; }
        public DateTimeOffset? MasterAcceptDate { get; set; }

        public IEnumerable<ResponseProcessFileDto> Files { get; set; }
        public string UserName { get; set; }

        public string TypeDescription { get; protected set; }
        public override ProcessType Type
        {
            get => base.Type;
            set
            {
                base.Type = value;
                TypeDescription = ProcessTypeDescriptions.GetProcessTypeDescription(value);
            }
        }

        public string ProcessStatusDescription { get; protected set; }
        private ProcessStatus _processStatus;
        public ProcessStatus ProcessStatus
        {
            get => this._processStatus;
            set
            {
                this._processStatus = value;
                ProcessStatusDescription = ProcessStatusDescriptions.GetStatusDescription(value);
            }
        }
    }
}
