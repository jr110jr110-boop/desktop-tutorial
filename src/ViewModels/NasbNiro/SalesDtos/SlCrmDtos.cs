using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Sales;

namespace ViewModels.NasbNiro.SalesDtos
{
    // Sales (release S1 part A). Every class name starts with "Sl" because swagger needs unique DTO names project-wide.
    // Dates are ISO in JSON; every "...Text" field is a Persian (Shamsi) date for people to read. Money is in rials.

    public class SlPageQuery
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public string Contains { get; set; }
    }

    // ------------------------------------------------------------------------------------------ customers

    public class SlCustomerQuery : SlPageQuery
    {
        public SlCustomerStatus? Status { get; set; }
        public SlCustomerType? Type { get; set; }
        public int? OwnerUserId { get; set; }
    }

    public class SlCustomerDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string EconomicCode { get; set; }
        public string NationalId { get; set; }
        public string PostalCode { get; set; }
        public string Address { get; set; }
        public string Phone { get; set; }
        public string EmployerName { get; set; }
        public bool IsActive { get; set; }
        public int? ProfileId { get; set; }
        public SlCustomerType? Type { get; set; }
        public SlIndustry? Industry { get; set; }
        public string Country { get; set; }
        public string City { get; set; }
        public SlCustomerStatus Status { get; set; }
        public int? OwnerUserId { get; set; }
        public string OwnerName { get; set; }
        public decimal? CreditLimitRial { get; set; }
        public int? PaymentTermDays { get; set; }
        public string Note { get; set; }
        public int ContactCount { get; set; }
        public int SiteCount { get; set; }
        public string RowVersion { get; set; }
    }

    public class SlSaveCustomerDto
    {
        public int? Id { get; set; }
        public string Name { get; set; }
        public string EconomicCode { get; set; }
        public string NationalId { get; set; }
        public string PostalCode { get; set; }
        public string Address { get; set; }
        public string Phone { get; set; }
        public string EmployerName { get; set; }
        public SlCustomerType? Type { get; set; }
        public SlIndustry? Industry { get; set; }
        public string Country { get; set; }
        public string City { get; set; }
        public SlCustomerStatus Status { get; set; } = SlCustomerStatus.Prospect;
        public int? OwnerUserId { get; set; }
        public int? PaymentTermDays { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>The credit limit of a customer in rials; finance sets it.</summary>
    public class SlSetCreditDto
    {
        public int CustomerId { get; set; }
        public decimal? CreditLimitRial { get; set; }
    }

    public class SlContactQuery : SlPageQuery
    {
        public int? CustomerId { get; set; }
    }

    public class SlContactDto
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string FullName { get; set; }
        public string JobTitle { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsActive { get; set; }
    }

    public class SlSaveContactDto
    {
        public int? Id { get; set; }
        public int CustomerId { get; set; }
        public string FullName { get; set; }
        public string JobTitle { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class SlSiteQuery : SlPageQuery
    {
        public int? CustomerId { get; set; }
    }

    public class SlSiteDto
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string Title { get; set; }
        public string Address { get; set; }
        public string Country { get; set; }
        public string CoordinatesText { get; set; }
    }

    public class SlSaveSiteDto
    {
        public int? Id { get; set; }
        public int CustomerId { get; set; }
        public string Title { get; set; }
        public string Address { get; set; }
        public string Country { get; set; }
        public string CoordinatesText { get; set; }
    }

    public class SlActivityQuery : SlPageQuery
    {
        public int? CustomerId { get; set; }
        public int? InquiryId { get; set; }
        public int? ProposalId { get; set; }
    }

    public class SlActivityDto
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int? ContactId { get; set; }
        public SlActivityKind Kind { get; set; }
        public DateTime ActivityDate { get; set; }
        public string ActivityDateText { get; set; }
        public string Subject { get; set; }
        public string Notes { get; set; }
        public DateTime? NextActionDate { get; set; }
        public string NextActionDateText { get; set; }
        public int? InquiryId { get; set; }
        public int? ProposalId { get; set; }
        public int? ServiceRequestId { get; set; }
        public int OwnerUserId { get; set; }
        public string OwnerName { get; set; }
    }

    public class SlSaveActivityDto
    {
        public int? Id { get; set; }
        public int CustomerId { get; set; }
        public int? ContactId { get; set; }
        public SlActivityKind Kind { get; set; }
        public DateTime? ActivityDate { get; set; }
        public string Subject { get; set; }
        public string Notes { get; set; }
        public DateTime? NextActionDate { get; set; }
        public int? InquiryId { get; set; }
        public int? ProposalId { get; set; }
        public int? ServiceRequestId { get; set; }
    }

    // ------------------------------------------------------------------------------------------ legacy link

    public class SlLinkSourceDto
    {
        public SlLinkSource SourceType { get; set; }
        public int SourceId { get; set; }
        public string Text { get; set; }
    }

    public class SlLinkLegacyResultDto
    {
        public int LinkedContracts { get; set; }
        public int LinkedTenders { get; set; }
        public int AlreadyLinked { get; set; }
        public List<SlLinkSourceDto> Unmatched { get; set; } = new List<SlLinkSourceDto>();
    }

    /// <summary>A person links one contract or tender to a customer by hand (what the exact match could not do).</summary>
    public class SlManualLinkDto
    {
        public SlLinkSource SourceType { get; set; }
        public int SourceId { get; set; }
        public int CustomerId { get; set; }
    }

    public class SlLinkDto
    {
        public int Id { get; set; }
        public SlLinkSource SourceType { get; set; }
        public int SourceId { get; set; }
        public string SourceText { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public SlLinkMethod Method { get; set; }
    }

    public class SlLinkQuery : SlPageQuery
    {
        public int? CustomerId { get; set; }
        public SlLinkSource? SourceType { get; set; }
    }

    // ------------------------------------------------------------------------------------------ settings, reasons

    public class SlSettingDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public decimal? Value { get; set; }
        public string Note { get; set; }
    }

    public class SlSaveSettingDto
    {
        public string Key { get; set; }
        public decimal? Value { get; set; }
    }

    public class SlReasonCodeQuery : SlPageQuery
    {
        public SlReasonKind? Kind { get; set; }
        public bool? IsActive { get; set; }
    }

    public class SlReasonCodeDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public SlReasonKind Kind { get; set; }
        public bool IsActive { get; set; }
    }

    public class SlSaveReasonCodeDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public SlReasonKind Kind { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
