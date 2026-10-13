using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Entities.Model.NasbNiro.Contracts
{
    public class AssemblyEntity : BaseEntity
    {
        public string Title { get; set; }
        public string Code { get; set; }
        public AssemblyStatus Status { get; protected set; }

        public int AssemblerDuration { get; set; }

        public DateTimeOffset? StartDate { get; set; }
        public DateTimeOffset? EndDate { get; set; }
        public DateTimeOffset? ActualStartDate { get; set; }
        public DateTimeOffset? ActualEndDate { get; set; }

        public int? PredecessorId { get; set; }
        public AssemblyEntity Predecessor { get; set; }
        public ICollection<AssemblyEntity> Children { get; set; }

        [JsonIgnore]
        public virtual WorkOrder WorkOrder { get; set; }
        public int WorkOrderId { get; set; }

        [JsonIgnore]
        public virtual ICollection<Bom> Boms { get; set; }

        public bool IsAssembligStarted()
        {
            return this.Status != AssemblyStatus.NewAssembly;
        }

        public void StartAssembling()
        {
            this.ActualStartDate = DateTimeOffset.UtcNow;
            this.Status = AssemblyStatus.ReadyToAssemble;
        }

        public bool CanAssemblerAssemble()
        {
            return this.Status == AssemblyStatus.ReadyToAssemble ||
                this.Status == AssemblyStatus.QcRejectToAssemble;
        }

        public void AssemblerAssembled()
        {
            this.Status = AssemblyStatus.AssemblerAssembled;
        }

        public void UpdateStatus(AssemblyStatus status)
        {
            this.Status = status;
        }

        public bool CanQcRejectToAssembleOrAcceptToFinalAssemble()
        {
            return this.Status == AssemblyStatus.AssemblerAssembled;
        }

        public void QcAcceptToFinalAssemble()
        {
            this.Status = AssemblyStatus.ReadyToFinalAssemble;
        }

        public void QcRejectToAssemble()
        {
            this.Status = AssemblyStatus.QcRejectToAssemble;
        }

        public bool CanAssemblerFinalAssemble()
        {
            return this.Status == AssemblyStatus.ReadyToFinalAssemble ||
                 this.Status == AssemblyStatus.QcRejectToFinalAssemble;
        }

        public void AssemblerFinalAssembled()
        {
            this.Status = AssemblyStatus.AssemblerFinalAssembled;
        }

        public bool CanQcRejectToFinalAssembleOrAcceptToFinalAcceptance()
        {
            return this.Status == AssemblyStatus.AssemblerFinalAssembled;
        }

        public void QcRejectToFinalAssemble()
        {
            this.Status = AssemblyStatus.QcRejectToFinalAssemble;
        }

        public void QcAcceptToFinalAcceptance()
        {
            this.Status = AssemblyStatus.PendingFinalAcceptance;
        }

        public bool CanPmoAcceptToCompleted()
        {
            return this.Status == AssemblyStatus.PendingFinalAcceptance;
        }

        public void PmoAcceptToCompleted()
        {
            this.ActualEndDate = DateTimeOffset.UtcNow;
            this.Status = AssemblyStatus.IsCompleted;
        }

        public void UpdatePredecessor(AssemblyEntity predecessorAssembly)
        {
            this.Predecessor = predecessorAssembly;
        }

        public void RemovePredecessor()
        {
            if (PredecessorId.HasValue)
            {
                PredecessorId = null;
            }
        }
    }
}
