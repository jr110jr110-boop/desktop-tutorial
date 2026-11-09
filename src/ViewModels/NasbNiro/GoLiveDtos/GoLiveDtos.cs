using System;
using System.Collections.Generic;
using ViewModels.NasbNiro.QualityDtos;

namespace ViewModels.NasbNiro.GoLiveDtos
{
    // Package G. Every class name starts with Gl so that the Swagger schema ids stay unique.

    public class GlCharAdoptionDto
    {
        public int Id { get; set; }
        public int DeliverableId { get; set; }
        public int RevisionId { get; set; }
        public string DocumentNumber { get; set; }
        public string RevisionCode { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public int? ListId { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }
        public string Reason { get; set; }
        public string EvidenceRef { get; set; }
        public int RequestedByUserId { get; set; }
        public string RequestedByName { get; set; }
        public DateTimeOffset RequestedAt { get; set; }
        public string AcceptedByName { get; set; }
        public DateTimeOffset? AcceptedAt { get; set; }
        public string AcceptNote { get; set; }
        public string VoidReason { get; set; }
        public bool CanAccept { get; set; }
        public bool CanVoid { get; set; }
        public string RowVersion { get; set; }
    }

    public class GlCharAdoptionCandidateDto
    {
        public int DeliverableId { get; set; }
        public int RevisionId { get; set; }
        public string DocumentNumber { get; set; }
        public string Title { get; set; }
        public string RevisionCode { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public bool Legacy { get; set; }
        public bool HasList { get; set; }
        public int? AdoptionId { get; set; }
    }

    public class GlSaveCharAdoptionDto
    {
        public int RevisionId { get; set; }
        public string Reason { get; set; }
        public string EvidenceRef { get; set; }

        /// <summary>The reason of an empty list (a drawing with nothing measurable), when Items is empty.</summary>
        public string EmptyListReason { get; set; }
        public List<SaveCharLineDto> Items { get; set; } = new List<SaveCharLineDto>();
    }

    public class GlAdoptionActionDto
    {
        public int Id { get; set; }

        /// <summary>accept, void (characteristic adoption); eng-review, pmo-review, void (configuration adoption).</summary>
        public string Action { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    public class GlConfigAdoptionDto
    {
        public int Id { get; set; }
        public int WorkOrderId { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; }
        public int BomHeaderId { get; set; }
        public string BomCode { get; set; }
        public int BomRevision { get; set; }
        public int? BaselineId { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }
        public string Reason { get; set; }
        public string EvidenceRef { get; set; }
        public string ComparisonNote { get; set; }
        public int LegacyLineCount { get; set; }
        public int BankLineCount { get; set; }
        public string RequestedByName { get; set; }
        public DateTimeOffset RequestedAt { get; set; }
        public string EngReviewerName { get; set; }
        public DateTimeOffset? EngReviewedAt { get; set; }
        public string EngNote { get; set; }
        public string PmoReviewerName { get; set; }
        public DateTimeOffset? PmoReviewedAt { get; set; }
        public string PmoNote { get; set; }
        public string VoidReason { get; set; }
        public bool CanEngReview { get; set; }
        public bool CanPmoReview { get; set; }
        public bool CanVoid { get; set; }
        public string RowVersion { get; set; }
    }

    public class GlSaveConfigAdoptionDto
    {
        public int WorkOrderId { get; set; }
        public int ProductId { get; set; }
        public int BomHeaderId { get; set; }
        public string Reason { get; set; }
        public string EvidenceRef { get; set; }
        public string ComparisonNote { get; set; }
    }

    public class GlConfigCandidateDto
    {
        public int WorkOrderId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>True when the creation date was never recorded (a very old record): the screen says «تاریخ ثبت نشده».</summary>
        public bool CreatedUnknown { get; set; }
        public bool Legacy { get; set; }
        public int LegacyLineCount { get; set; }
        public int? AdoptionId { get; set; }
    }

    public class GlLegacyUseDto
    {
        public int Id { get; set; }
        public int Kind { get; set; }
        public string KindText { get; set; }
        public string ObjectKey { get; set; }
        public string Number { get; set; }
        public string Note { get; set; }
        public string UsedByName { get; set; }
        public DateTimeOffset UsedAt { get; set; }
    }

    public class GlOverviewDto
    {
        public DateTimeOffset? ControlStartAt { get; set; }
        public string ControlStartText { get; set; }
        public int LegacyWorkOrders { get; set; }
        public int LegacyDrawingsWithoutList { get; set; }
        public int OpenCharAdoptions { get; set; }
        public int OpenConfigAdoptions { get; set; }
        public List<GlLegacyUseDto> Uses { get; set; } = new List<GlLegacyUseDto>();
    }

    public class GlRevisionPreCheckDto
    {
        public bool CurrentlyRequired { get; set; }
        public int TotalLines { get; set; }
        public int WouldBreak { get; set; }
        public List<GlRevisionPreCheckRowDto> Rows { get; set; } = new List<GlRevisionPreCheckRowDto>();
        public string Summary { get; set; }
    }

    public class GlRevisionPreCheckRowDto
    {
        public string Source { get; set; }
        public int RecordId { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string Problem { get; set; }
    }

    public class GlPoLineCitationDto
    {
        public int LineId { get; set; }
        public int OrderId { get; set; }
        public string OrderNumber { get; set; }
        public int? ItemId { get; set; }
        public string ItemCode { get; set; }
        public string DrawingNumber { get; set; }
        public string DrawingRevision { get; set; }
        public int? DrawingRevisionId { get; set; }
        public bool Stale { get; set; }
        public string CurrentRevision { get; set; }
        public string ConfirmedRevision { get; set; }
        public string Problem { get; set; }
    }

    public class GlPoUncitedLineDto
    {
        public int LineId { get; set; }
        public int OrderId { get; set; }
        public string OrderNumber { get; set; }
        public int RowNumber { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string Drawings { get; set; }
    }

    public class GlPoConfirmDto
    {
        public int LineId { get; set; }
        public string Reference { get; set; }
        public string Note { get; set; }
    }

    public class GlCeoInboxDto
    {
        public int Total { get; set; }
        public List<GlCeoInboxGroupDto> Groups { get; set; } = new List<GlCeoInboxGroupDto>();
    }

    public class GlCeoInboxGroupDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public int Count { get; set; }
        public int OldestDays { get; set; }

        /// <summary>The page the CEO opens to decide each item (one by one, each with its own reason).</summary>
        public string Route { get; set; }
        public List<GlCeoInboxItemDto> Items { get; set; } = new List<GlCeoInboxItemDto>();
    }

    public class GlCeoInboxItemDto
    {
        public string Ref { get; set; }
        public int RecordId { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public DateTimeOffset? Since { get; set; }
        public int AgeDays { get; set; }
        public string Route { get; set; }
    }

    /// <summary>The multipart form of the stamp call: the print made by prepare, and the two raw RGBA bands drawn by the browser (a uniquely named class: the API description needs one).</summary>
    public class GlPrintStampForm
    {
        public int PrintId { get; set; }
        public Microsoft.AspNetCore.Http.IFormFile HeaderRgba { get; set; }
        public Microsoft.AspNetCore.Http.IFormFile FooterRgba { get; set; }
        public int HeaderWidth { get; set; }
        public int HeaderHeight { get; set; }
        public int FooterWidth { get; set; }
        public int FooterHeight { get; set; }
    }

    public class GlPrintPrepareDto
    {
        public int RevisionId { get; set; }
        public System.Guid? DocumentId { get; set; }

        /// <summary>True: print as an uncontrolled copy even if the revision is in force (the plain download of a reader).</summary>
        public bool Uncontrolled { get; set; }
    }

    public class GlPrintPreparedDto
    {
        public int PrintId { get; set; }
        public bool Controlled { get; set; }
        public int CopyNumber { get; set; }
        public string Header { get; set; }
        public string Footer { get; set; }
        public string FileName { get; set; }
        public System.Guid DocumentId { get; set; }
        public string RevisionCode { get; set; }
        public string DocumentNumber { get; set; }
        public int MaxMegabytes { get; set; }
    }

    public class GlDriftRehearsalDto
    {
        /// <summary>True when the first verification run after the update has not yet taken the newly covered record types as its starting point.</summary>
        public bool BaselineDue { get; set; }
        public System.DateTimeOffset? BaselineAt { get; set; }
        public int TotalDiffering { get; set; }
        public List<GlDriftRehearsalRowDto> Tables { get; set; } = new List<GlDriftRehearsalRowDto>();
    }

    public class GlDriftRehearsalRowDto
    {
        public string EntityType { get; set; }
        public string Label { get; set; }
        public int Differing { get; set; }
        public int PresentAfterDelete { get; set; }
        public int Missing { get; set; }
    }

    public class GlPageDto<T>
    {
        public int Total { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public List<T> Items { get; set; } = new List<T>();
    }

    public class GlCandidateQuery
    {
        public string Search { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
    }
}
