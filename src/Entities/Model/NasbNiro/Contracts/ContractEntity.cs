using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.ContractEquipments;

namespace Entities.Model.NasbNiro.Contracts
{
    public class ContractEntity : BaseEntity
    {
        public string Code { get; set; }
        public string SubCode { get; set; }
        public string TenderCode { get; set; }
        public string EmployerOrderNo { get; set; }
        public string EmployerContractNo { get; set; }
        public string EmployerProjectNo { get; set; }
        public string InternalContractNo { get; set; }
        public string InternalProjectNo { get; set; }
        public DateTimeOffset ContractDate { get; set; }
        public DateTimeOffset AnnounceDate { get; set; }
        public DateTimeOffset StartDate { get; set; }
        public DateTimeOffset PayDate { get; set; }
        public Decimal? Price { get; set; }
        public int? Duration { get; set; }
        public DateTimeOffset ActualStartDate { get; set; }
        public string TPA { get; set; }
        public string Site { get; set; }
        public string Type { get; set; }
        public string Title { get; set; }
        public DateTimeOffset TempDate { get; set; }
        public DateTimeOffset DeliverDate { get; set; }
        public DateTimeOffset GurantyDate { get; set; }
        public DateTimeOffset EndDate { get; set; }
        public string Employer { get; set; }
        public string SubEmployer { get; set; }
        public string Master { get; set; }
        //Project Charter File Id
        public Guid? ProjectCharterFileId { get; set; }
        public Document ProjectCharterFile { get; set; }
        public ContractStatus Status { get; set; }
        public bool ForProvider { get; set; }

        public string ForContract { get; set; }
        public string Services { get; set; }

        public virtual ICollection<ContractFile> ContractFiles { get; set; }
        public virtual ICollection<ContractEquipment> ContractEquipments { get; set; }
        public virtual ICollection<WorkOrder> WorkOrders { get; set; }
        public virtual ICollection<ProvisionalStatement> ProvisionalStatements { get; set; }
        public virtual ICollection<ContractGuaranty> ContractGuaranties { get; set; }
    }
}
