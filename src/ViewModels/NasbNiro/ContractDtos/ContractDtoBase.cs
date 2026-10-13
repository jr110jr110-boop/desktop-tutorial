using System;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;

namespace ViewModels.NasbNiro.ContractDtos
{
    public abstract class ContractDtoBase
    {
        [Required]
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
        [Required]
        public ContractStatus Status { get; set; }
        public string ForContract { get; set; }
        public string Services { get; set; }
    }
}
