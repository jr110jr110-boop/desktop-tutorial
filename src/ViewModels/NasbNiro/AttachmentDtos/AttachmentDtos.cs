using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Attachments;
using Microsoft.AspNetCore.Http;

namespace ViewModels.NasbNiro.AttachmentDtos
{
    /// <summary>The form of an upload (multipart): the file and what it is.</summary>
    public class UploadAttachmentForm
    {
        public AttachmentOwnerType OwnerType { get; set; }
        public int OwnerId { get; set; }
        public AttachmentKind Kind { get; set; }
        public string Title { get; set; }
        public string Revision { get; set; }

        /// <summary>The attachment (same owner) this file replaces; it stays in the history, marked as superseded.</summary>
        public int? SupersedesId { get; set; }

        public IFormFile File { get; set; }
    }

    public class VoidAttachmentDto
    {
        public int Id { get; set; }
        public string Reason { get; set; }
    }

    public class AttachmentDto
    {
        /// <summary>0 for a legacy single-file column (quote, invoice, inspection report, receipt weighbridge photo): shown, never changed here.</summary>
        public int Id { get; set; }
        public AttachmentOwnerType OwnerType { get; set; }
        public int OwnerId { get; set; }
        public string OwnerTitle { get; set; }
        public string OwnerNumber { get; set; }
        public Guid DocumentId { get; set; }
        public AttachmentKind Kind { get; set; }
        public string KindTitle { get; set; }
        public string Title { get; set; }
        public string Revision { get; set; }
        public string FileName { get; set; }
        public long SizeBytes { get; set; }
        public int UploadedByUserId { get; set; }
        public string UploadedByName { get; set; }
        public DateTimeOffset UploadedAt { get; set; }
        public bool Voided { get; set; }
        public string VoidedByName { get; set; }
        public DateTimeOffset? VoidedAt { get; set; }
        public string VoidReason { get; set; }
        public int? SupersededById { get; set; }
        /// <summary>Not voided and not superseded: the file in force.</summary>
        public bool Current { get; set; }

        /// <summary>This row is a link to a document of another record (a drawing linked to a purchase request).</summary>
        public bool IsLink { get; set; }

        /// <summary>The document it was linked from was voided or replaced: it is not in force any more and is shown as obsolete.</summary>
        public bool LinkObsolete { get; set; }
        /// <summary>The owner is approved (or otherwise locked): the file can only be voided or superseded.</summary>
        public bool Locked { get; set; }
        public bool Legacy { get; set; }
        public bool CanRemove { get; set; }
        public bool CanVoid { get; set; }
        public bool CanSupersede { get; set; }
    }

    /// <summary>The technical documents of a purchase request, shown read-only on a downstream record (link, never a copy).</summary>
    public class PropagatedAttachmentsDto
    {
        public AttachmentOwnerType SourceType { get; set; }
        public int SourceId { get; set; }
        public string SourceNumber { get; set; }
        public string SourceTitle { get; set; }
        public List<AttachmentDto> Items { get; set; } = new List<AttachmentDto>();
    }

    public class AttachmentPanelDto
    {
        public AttachmentOwnerType OwnerType { get; set; }
        public int OwnerId { get; set; }
        public string OwnerTitle { get; set; }
        public string OwnerNumber { get; set; }
        public bool Locked { get; set; }
        /// <summary>The caller may add files (and void / supersede) on this record.</summary>
        public bool CanWrite { get; set; }
        public List<AttachmentDto> Items { get; set; } = new List<AttachmentDto>();
        /// <summary>«مدارک فنی درخواست»: empty on records that are not downstream of a purchase request.</summary>
        public List<PropagatedAttachmentsDto> Propagated { get; set; } = new List<PropagatedAttachmentsDto>();
        public long MaxBytes { get; set; }
        public List<string> AllowedExtensions { get; set; } = new List<string>();
    }

    // ------------------------------------------------------------------------------------------ request dossier (پرونده درخواست)

    public class DossierRecordDto
    {
        /// <summary>The owner type of the record. For the petty-cash step it is the settlement LINE type but <see cref="Id"/> is the SETTLEMENT id (its files hang on the lines): never open an attachment panel from this pair.</summary>
        public AttachmentOwnerType Type { get; set; }
        public int Id { get; set; }
        public string Number { get; set; }
        public string Status { get; set; }
        public DateTime? Date { get; set; }
        public string CreatedByName { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string Description { get; set; }
        /// <summary>Null for a caller who may not see prices (QC, warehouse, shop floor).</summary>
        public decimal? Amount { get; set; }
        public string Route { get; set; }
        /// <summary>A payment request waiting for the approval of this caller: finance approves it from the dossier without paper.</summary>
        public bool CanApprovePayment { get; set; }
        public List<AttachmentDto> Attachments { get; set; } = new List<AttachmentDto>();
    }

    public class DossierStepDto
    {
        public int Order { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public List<DossierRecordDto> Records { get; set; } = new List<DossierRecordDto>();
    }

    public class DossierDto
    {
        public string Query { get; set; }
        public string FoundAs { get; set; }
        public int StepCount { get; set; }
        public int RecordCount { get; set; }
        public int AttachmentCount { get; set; }
        /// <summary>Steps of the chain that exist but that the caller may not read: counted, never described.</summary>
        public int HiddenStepCount { get; set; }
        public string HiddenStepsText { get; set; }
        /// <summary>More than one record carries the number: the caller picks one and asks again with its kind.</summary>
        public List<DossierChoiceDto> Choices { get; set; } = new List<DossierChoiceDto>();
        public DateTimeOffset GeneratedAt { get; set; }
        public string GeneratedBy { get; set; }
        public List<DossierStepDto> Steps { get; set; } = new List<DossierStepDto>();
    }

    public class DossierChoiceDto
    {
        /// <summary>The value to send as <c>kind</c>: an owner type name (PurchaseOrder ...) or SupplierInvoiceNumber.</summary>
        public string Kind { get; set; }
        public string Title { get; set; }
        public string Number { get; set; }
        public string Description { get; set; }
    }
}
