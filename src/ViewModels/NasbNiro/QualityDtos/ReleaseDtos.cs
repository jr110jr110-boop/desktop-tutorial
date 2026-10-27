using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Quality;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.QualityDtos
{
    public class ReleaseCheckDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public bool Passed { get; set; }
        public string Detail { get; set; }
    }

    public class ReleaseNoteListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public ReleaseNoteStatus Status { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public string CertificateOfConformityNumber { get; set; }
    }

    public class ReleaseNoteDto : ReleaseNoteListDto
    {
        public int ContractId { get; set; }
        public string ItemsText { get; set; }
        public string Note { get; set; }
        public string CancelReason { get; set; }
        public string ReleasedByName { get; set; }
        public string DraftedByName { get; set; }
        public string RowVersion { get; set; }

        /// <summary>The products, quantities and serial numbers this note releases: a customer delivery of the work order may only carry these.</summary>
        public List<ReleaseNoteLineDto> Lines { get; set; } = new List<ReleaseNoteLineDto>();

        /// <summary>Computed now while the note is a draft; the stored snapshot once it is released.</summary>
        public List<ReleaseCheckDto> Checks { get; set; } = new List<ReleaseCheckDto>();
        public bool CanRelease { get; set; }
    }

    public class ReleaseNoteQuery : PaginationParams
    {
        public int? WorkOrderId { get; set; }
        public ReleaseNoteStatus? Status { get; set; }
        public string Contains { get; set; }
    }

    public class SaveReleaseNoteDto
    {
        public int? Id { get; set; }
        public int WorkOrderId { get; set; }
        public string ItemsText { get; set; }
        public string Note { get; set; }

        /// <summary>At least one line: the item, the quantity and, for a serial item, the serial numbers.</summary>
        public List<SaveReleaseNoteLineDto> Lines { get; set; } = new List<SaveReleaseNoteLineDto>();
    }

    public class SaveReleaseNoteLineDto
    {
        public int ItemId { get; set; }
        public decimal Quantity { get; set; }
        public List<string> SerialNumbers { get; set; } = new List<string>();
    }

    public class ReleaseNoteLineDto
    {
        public int RowNumber { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public List<string> SerialNumbers { get; set; } = new List<string>();
    }

    public class ReleaseNoteActionDto
    {
        public int Id { get; set; }

        /// <summary>release, cancel</summary>
        public string Action { get; set; }
        public string Reason { get; set; }
    }

    public class ReleaseChecksDto
    {
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public bool CanRelease { get; set; }
        public List<ReleaseCheckDto> Checks { get; set; } = new List<ReleaseCheckDto>();
    }

    public class DataBookEntryDto
    {
        public string SourceKind { get; set; }
        public int? SourceId { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string Status { get; set; }
        public Guid? DocumentId { get; set; }
        public bool Mandatory { get; set; }
        public bool Missing { get; set; }
        /// <summary>True when the client must receive this entry (client point on its ITP activity, ITP, NCR list, release note).</summary>
        public bool ClientRelevant { get; set; }
    }

    public class DataBookSectionDto
    {
        public int No { get; set; }
        public string Name { get; set; }
        public List<DataBookEntryDto> Entries { get; set; } = new List<DataBookEntryDto>();
    }

    public class DataBookDto
    {
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int MissingCount { get; set; }
        /// <summary>company (every entry, internal book) or client (only what the client receives).</summary>
        public string Variant { get; set; } = "company";
        public List<DataBookSectionDto> Sections { get; set; } = new List<DataBookSectionDto>();
    }
}
