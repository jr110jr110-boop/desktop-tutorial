using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

using Entities.Identity;

namespace Entities.Model.NasbNiro.Contracts
{
    public class ProcessEntity : BaseEntity
    {
        public string ExitCommodityCode { get; set; }
        public string EnterCommodityCode { get; set; }
        public string DocumentCode { get; set; }
        public int WorkflowId { get; set; }
        public int MachineId { get; set; }
        public int MapDuration { get; set; }
        public int MasterDuration { get; set; }
        public int ProcessDuration { get; set; }
        public int GetTotalDuration()
        {
            return MapDuration + MasterDuration + ProcessDuration;
        }

        public ProcessType Type { get; set; }
        public ProcessStatus ProcessStatus { get; protected set; }
        public int Sort { get; set; }
        public DateTimeOffset? ProgramStartDate { get; set; }
        public DateTimeOffset? ProgramEndDate { get; set; }
        public DateTimeOffset? ActualStartDate { get; set; }
        public DateTimeOffset? ActualEndDate { get; set; }
        public DateTimeOffset? DocAcceptDate { get; set; }
        public DateTimeOffset? MasterAcceptDate { get; set; }
        public decimal ProgramPrice { get; set; }
        public decimal WeightPercent { get; set; }
        public string Title { get; set; }

        [NotMapped]
        public decimal ProgressLevel
        {
            get
            {
                switch (ProcessStatus)
                {
                    case ProcessStatus.NewProcess: return 0.0M;
                    case ProcessStatus.ProcessIsCompleted: return 100.0M;
                    default: return 50.0M;
                }
            }
        }

        public Bom Bom { get; set; }
        public int BomId { get; set; }

        public int WorkflowPositionId { get; set; }
        public int WorkflowUserId { get; set; }

        public IList<ProcessFile> ProcessFiles { get; set; }
        public IList<BuyOrderProcesses> BuyOrderProcesses { get; set; }

        public void PmoAccepted()
        {
            this.ActualStartDate = DateTimeOffset.UtcNow;
            this.ProcessStatus = ProcessStatus.PmoAccepted;
            this.ActualStartDate = DateTime.Now;
        }

        public bool IsNewProcess()
        {
            return this.ProcessStatus == ProcessStatus.NewProcess;
        }

        public bool HasPmoAccepted()
        {
            return this.ProcessStatus == ProcessStatus.PmoAccepted;
        }

        public bool AreDocumentsAdded()
        {
            return this.ProcessStatus == ProcessStatus.DocumentsAdded;
        }

        public bool HasGotReadyBefore()
        {
            return this.ProcessStatus >= ProcessStatus.ReadyToGo;
        }

        public void GetReadyToGo()
        {
            this.ProcessStatus = ProcessStatus.ReadyToGo;
        }

        public void UpdateMasterAcceptDate()
        {
            this.MasterAcceptDate = DateTimeOffset.UtcNow;
            GetReadyToGo();
        }

        public void EngineeringAcceptDouments()
        {

            this.ProcessStatus = this.MasterDuration == 0 ?
                ProcessStatus.ReadyToGo :
                ProcessStatus.MasterShouldAccept;

            this.DocAcceptDate = DateTimeOffset.UtcNow;
        }

        public void EngineeringNotAcceptDouments()
        {
            this.ProcessStatus = ProcessStatus.PmoAccepted;
        }

        public void DocumentsAdded()
        {
            this.ProcessStatus = ProcessStatus.DocumentsAdded;
        }

        public bool CanAttachOrDetachDocuments()
        {
            return this.ProcessStatus == ProcessStatus.DocumentsAdded ||
                this.ProcessStatus == ProcessStatus.EngineeringWorkflowStart;
        }

        public bool CanAddToBuyOrder() =>
            this.ProcessStatus == ProcessStatus.ReadyToGo;

        public void AddedToBuyOrder()
        {
            this.ProcessStatus = ProcessStatus.BuyOrderHasBeenAdded;
        }

        public void UpdateStatus(ProcessStatus processStatus)
        {
            this.ProcessStatus = processStatus;
        }

        public void Completed()
        {
            this.ActualEndDate = DateTimeOffset.UtcNow;
            this.ProcessStatus = ProcessStatus.ProcessIsCompleted;
        }

        public void SetWorkflowPositionId(int workflowPositionId,int workflowUserId)
        {
            this.WorkflowPositionId = workflowPositionId;
            this.WorkflowUserId = workflowUserId;
        }

        public void EngineerWorkflowStart()
        {
            this.ProcessStatus = ProcessStatus.EngineeringWorkflowStart;
        }

        public bool CanPmoAccept()
        {
            return this.ProcessStatus == ProcessStatus.NewProcess;
        }

        public bool CanBeAssignedToUser()
        {
            return this.ProcessStatus == ProcessStatus.PmoAccepted;
        }

        public void WorkflowStart()
        {
            this.ProcessStatus = ProcessStatus.EngineeringWorkflowStart;
        }

        public bool CanChangeUser()
        {
            return this.ProcessStatus == ProcessStatus.PmoAccepted ||
                this.ProcessStatus == ProcessStatus.EngineeringWorkflowStart;
        }

        public bool CanMasterAccept()
        {
            return ProcessStatus == ProcessStatus.MasterShouldAccept;
        }

        public bool CanEngineeringAcceptDocument()
        {
            return ProcessStatus == ProcessStatus.EngineerAcceptedDocuments;
        }

        public bool CanEngineerAcceptDocument()
        {
            return ProcessStatus == ProcessStatus.DocumentsAdded ||
                ProcessStatus == ProcessStatus.EngineeringWorkflowStart;
        }

        public void EngineerAcceptDouments()
        {
            ProcessStatus = ProcessStatus.EngineerAcceptedDocuments;
        }
    }
}
