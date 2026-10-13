using Entities.Model.NasbNiro.Contracts;
using System.Collections.Generic;
using ViewModels.Constants;
using ViewModels.NasbNiro.DocumentDtos;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public class ResponseProcessCardBoardDto
    {
        public int WorkOrderId { get; set; }
        public string WorkOrderTitle { get; set; }

        public int AssemblyId { get; set; }
        public string AssemblyTitle { get; set; }
        public int BomCount { get; set; }
        public int BomId { get; set; }
        public string BomTitle { get; set; }

        public string ExitCommodityCode { get; set; }
        public string EnterCommodityCode { get; set; }
        public int MapDuration { get; set; }
        public int MasterDuration { get; set; }
        public int ProcessDuration { get; set; }
        public int Sort { get; set; }
        public decimal ProgramPrice { get; set; }
        public string Title { get; set; }
        public int Id { get; set; }
        public bool Deleted { get; set; }
        public bool NeedsMasterAcceptance => this.MasterDuration != 0;

        //public ResponseFileFinalizeableDto[] Files { get; set; }
        public List<ResponseProcessFileDto> Files { get; set; }
        public string UserName { get; set; }
        public int UserId { get; set; }

        public int TotalDuration { get => this.MapDuration + this.MasterDuration + this.ProcessDuration; }

        public string TypeDescription { get; protected set; }
        private ProcessType _type;
        public ProcessType Type
        {
            get => _type;
            set
            {
                _type = value;
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
        public ResponseProcessCardBoardDto()
        {
            Files = new List<ResponseProcessFileDto>();
        }
    }
}
