using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Finance;

namespace ViewModels.NasbNiro.FinanceDtos
{
    public class FnFacilityLimitDto
    {
        public int? Id { get; set; }
        public string BankName { get; set; }
        public decimal LimitAmount { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public string Note { get; set; }
        public decimal Utilised { get; set; }
        public decimal Available { get; set; }
    }

    public class FnScheduleRowDto
    {
        public DateTime DueDate { get; set; }
        public decimal Principal { get; set; }
        public decimal Interest { get; set; }
    }

    public class FnSaveLoanDto
    {
        public int? Id { get; set; }
        public string BankName { get; set; }
        public FnFacilityType FacilityType { get; set; } = FnFacilityType.Loan;
        public string ContractNo { get; set; }
        public decimal Amount { get; set; }
        public string CurrencyCode { get; set; }
        public DateTime StartDate { get; set; }
        public decimal RatePercent { get; set; }
        public FnScheduleKind ScheduleKind { get; set; } = FnScheduleKind.Annuity;
        public int InstallmentCount { get; set; }
        public decimal FeeAmount { get; set; }
        public string Collateral { get; set; }
        public int? FacilityLimitId { get; set; }
        public int? BankAccountId { get; set; }
        /// <summary>Only for the Custom schedule.</summary>
        public List<FnScheduleRowDto> CustomRows { get; set; } = new List<FnScheduleRowDto>();
    }

    public class FnInstallmentDto
    {
        public int Id { get; set; }
        public int Sequence { get; set; }
        public DateTime DueDate { get; set; }
        public decimal Principal { get; set; }
        public decimal Interest { get; set; }
        public decimal Total { get; set; }
        public int Status { get; set; }
        public DateTime? PaidDate { get; set; }
        public decimal PenaltyPaid { get; set; }
        public int? VoucherId { get; set; }
    }

    public class FnLoanDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public string BankName { get; set; }
        public FnFacilityType FacilityType { get; set; }
        public string ContractNo { get; set; }
        public decimal Amount { get; set; }
        public string CurrencyCode { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal RatePercent { get; set; }
        public FnScheduleKind ScheduleKind { get; set; }
        public int InstallmentCount { get; set; }
        public decimal FeeAmount { get; set; }
        public string Collateral { get; set; }
        public FnLoanStatus Status { get; set; }
        public int? FacilityLimitId { get; set; }
        public int? BankAccountId { get; set; }
        public int? DisbursementVoucherId { get; set; }
        public decimal TotalInterest { get; set; }
        public decimal OutstandingPrincipal { get; set; }
        public decimal AccruedInterestPayable { get; set; }
        public List<FnInstallmentDto> Installments { get; set; } = new List<FnInstallmentDto>();
    }

    public class FnLoanActionDto
    {
        public int Id { get; set; }
        /// <summary>disburse, cancel.</summary>
        public string Action { get; set; }
        public DateTime? Date { get; set; }
        public int? BankAccountId { get; set; }
        /// <summary>Foreign-currency loan: rials per one unit of the loan currency on the disbursement date (required).</summary>
        public decimal? Rate { get; set; }
    }

    public class FnCurrentPortionRowDto
    {
        public int LoanId { get; set; }
        public string LoanNumber { get; set; }
        public decimal CurrentPortion { get; set; }
        public decimal AlreadyReclassified { get; set; }
        public decimal Change { get; set; }
    }

    public class FnCurrentPortionDto
    {
        public int PeriodId { get; set; }
        public decimal Total { get; set; }
        public int? VoucherId { get; set; }
        public List<FnCurrentPortionRowDto> Rows { get; set; } = new List<FnCurrentPortionRowDto>();
    }

    public class FnLoanPayDto
    {
        public int LoanId { get; set; }
        public int Sequence { get; set; }
        public DateTime? Date { get; set; }
        public int? BankAccountId { get; set; }
        public decimal Penalty { get; set; }
        /// <summary>Foreign-currency loan: rials per one unit of the loan currency on the payment date (required); the realised difference is booked automatically.</summary>
        public decimal? Rate { get; set; }
    }

    public class FnLoanAccrualDto
    {
        public int Id { get; set; }
        public int LoanId { get; set; }
        public string LoanNumber { get; set; }
        public int PeriodId { get; set; }
        public decimal Amount { get; set; }
        public int Status { get; set; }
        public int PreparedByUserId { get; set; }
        public int? ApprovedByUserId { get; set; }
        public int? VoucherId { get; set; }
    }

    public class FnUtilisationDto
    {
        public decimal TotalLimit { get; set; }
        public decimal TotalUtilised { get; set; }
        public decimal TotalAvailable { get; set; }
        public List<FnFacilityLimitDto> Facilities { get; set; } = new List<FnFacilityLimitDto>();
    }
}
