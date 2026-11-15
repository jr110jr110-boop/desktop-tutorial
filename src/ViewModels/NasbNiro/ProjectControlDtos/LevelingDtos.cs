using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.ProjectControlDtos
{
    // Project control priority 4, phase 4c: the load of the resources over all projects (histogram), the over-allocation alerts and the levelling proposals. No money anywhere in these records.

    public class PcResourceLoadQuery
    {
        public int? ResourceId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }

        /// <summary>"day" (default) or "week" (a week starts on Saturday).</summary>
        public string Bucket { get; set; }

        /// <summary>Also load the plans that are still being made (draft, submitted, returned): they replace the approved plan of their contract in the picture and are flagged as drafts.</summary>
        public bool IncludeOpen { get; set; }
    }

    public class PcLoadItemDto
    {
        public int ContractId { get; set; }
        public string ContractCode { get; set; }
        public int ActivityId { get; set; }
        public string ActivityCode { get; set; }
        public string ActivityTitle { get; set; }
        public decimal Load { get; set; }

        /// <summary>The load comes from a plan that is not approved yet («پیش‌نویس»).</summary>
        public bool IsDraft { get; set; }
    }

    public class PcLoadBucketDto
    {
        /// <summary>First and last day of the bucket (ISO dates; the front writes them in Shamsi).</summary>
        public DateTime Start { get; set; }
        public DateTime End { get; set; }

        /// <summary>The load of the bucket in the unit of the resource (hours for an hour resource), the part of it that comes from draft plans, the capacity and the over-allocation (the sum of the daily excess).</summary>
        public decimal Load { get; set; }
        public decimal DraftLoad { get; set; }
        public decimal Capacity { get; set; }
        public decimal Over { get; set; }

        /// <summary>Priority 4d: the hours that the work orders (production) need on the work centre linked to the resource («بار تولید»), a layer apart from <see cref="Load"/>; it counts toward <see cref="Over"/>. Zero without a link.</summary>
        public decimal ProductionLoad { get; set; }
        public List<PcLoadItemDto> Items { get; set; } = new List<PcLoadItemDto>();
    }

    public class PcResourceLoadRowDto
    {
        public int ResourceId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public PcResourceKind Kind { get; set; }
        public PcResourceUnit Unit { get; set; }
        public decimal Capacity { get; set; }

        /// <summary>Priority 4d: the work centre the machine resource is linked to (its capacity and calendar come from it); null for any other resource.</summary>
        public int? WorkCenterId { get; set; }
        public decimal PeakLoad { get; set; }
        public decimal TotalOver { get; set; }
        public List<PcLoadBucketDto> Buckets { get; set; } = new List<PcLoadBucketDto>();
    }

    public class PcResourceLoadDto
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string Bucket { get; set; }
        public bool IncludeOpen { get; set; }
        public List<PcResourceLoadRowDto> Resources { get; set; } = new List<PcResourceLoadRowDto>();
    }

    public class PcResourceOverloadQuery : PaginationParams
    {
        public int? ResourceId { get; set; }
        public PcOverloadStatus? Status { get; set; }
    }

    public class PcResourceOverloadDto
    {
        public int Id { get; set; }
        public int ResourceId { get; set; }
        public string ResourceCode { get; set; }
        public string ResourceTitle { get; set; }
        public PcResourceUnit ResourceUnit { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public decimal PeakLoad { get; set; }
        public decimal PeakCapacity { get; set; }
        public PcOverloadStatus Status { get; set; }
        public DateTimeOffset DetectedAt { get; set; }
        public DateTimeOffset? ResolvedAt { get; set; }
        public int? AcceptedByUserId { get; set; }
        public string AcceptedByName { get; set; }
        public DateTimeOffset? AcceptedAt { get; set; }
        public string AcceptReason { get; set; }
        public string RowVersion { get; set; }

        /// <summary>For the caller: null when the caller may accept this over-allocation now, else the Persian reason (the same rule the action enforces).</summary>
        public string AcceptBlockedReason { get; set; }

        /// <summary>The proposals of this over-allocation, newest first (at most five).</summary>
        public List<PcLevelingProposalDto> Proposals { get; set; } = new List<PcLevelingProposalDto>();
    }

    public class PcOverloadAcceptDto
    {
        public int Id { get; set; }
        public string Reason { get; set; }
        public string RowVersion { get; set; }
    }

    public class PcLevelingProposalQuery : PaginationParams
    {
        public int? OverloadId { get; set; }
        public int? ContractId { get; set; }
        public PcLevelingStatus? Status { get; set; }
    }

    public class PcLevelingProposalDto
    {
        public int Id { get; set; }
        public int OverloadId { get; set; }
        public int ContractId { get; set; }
        public string ContractCode { get; set; }
        public int ActivityId { get; set; }
        public string ActivityCode { get; set; }
        public string ActivityTitle { get; set; }
        public PcLevelingKind Kind { get; set; }
        public DateTime CurrentStart { get; set; }
        public DateTime CurrentFinish { get; set; }
        public DateTime ProposedStart { get; set; }
        public DateTime ProposedFinish { get; set; }
        public int ShiftDays { get; set; }
        public int CriticalImpactDays { get; set; }
        public bool NeedsChangeRequest { get; set; }
        public string Note { get; set; }
        public PcLevelingStatus Status { get; set; }
        public bool PreparedByAgent { get; set; }

        /// <summary>Applied by the planner and not yet confirmed by the second person («در انتظار تأیید نفر دوم»).</summary>
        public bool PendingSecondPerson { get; set; }
        public bool Reverted { get; set; }
        public int? ApplierUserId { get; set; }
        public string ApplierName { get; set; }
        public DateTimeOffset? AppliedAt { get; set; }
        public int? ConfirmedByUserId { get; set; }
        public string ConfirmedByName { get; set; }
        public DateTimeOffset? ConfirmedAt { get; set; }
        public int? RejectedByUserId { get; set; }
        public string RejectedByName { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public string RejectReason { get; set; }
        public DateTimeOffset CreateDate { get; set; }
        public string RowVersion { get; set; }

        /// <summary>For the caller: null when the action is allowed now, else the Persian reason (the rules the actions enforce, the lock of the contract and applier != confirmer included).</summary>
        public string ApplyBlockedReason { get; set; }
        public string RejectBlockedReason { get; set; }
        public string ConfirmBlockedReason { get; set; }

        /// <summary>«بستن بدون برگرداندن»: null when the caller may close an applied change whose revert is impossible.</summary>
        public string CloseBlockedReason { get; set; }
    }

    /// <summary>action: apply | reject | confirm | refuse; reject and refuse need a reason.</summary>
    public class PcLevelingActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Reason { get; set; }
        public string RowVersion { get; set; }
    }
}
