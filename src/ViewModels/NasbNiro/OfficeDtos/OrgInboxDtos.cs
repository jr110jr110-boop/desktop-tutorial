using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Office;

namespace ViewModels.NasbNiro.OfficeDtos
{
    /// <summary>Paging and a free-text filter shared by the office list queries.</summary>
    public class OfficePageQuery
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public string Contains { get; set; }
    }

    // ------------------------------------------------------------------------------------------ organisation

    public class UnitDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public int? ParentId { get; set; }
        public string ParentTitle { get; set; }
        public int? ManagerPositionId { get; set; }
        public string ManagerPositionTitle { get; set; }
        public bool IsActive { get; set; }
        public int PositionCount { get; set; }
    }

    public class SaveUnitDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public int? ParentId { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class PositionQuery : OfficePageQuery
    {
        public int? UnitId { get; set; }
        public int? UserId { get; set; }
        public bool? SignersOnly { get; set; }
    }

    public class PositionDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public int UnitId { get; set; }
        public string UnitTitle { get; set; }
        public int? UserId { get; set; }
        public string UserName { get; set; }
        public int? EmployeeId { get; set; }
        public bool IsManager { get; set; }
        public bool CanSign { get; set; }
        public bool IsActive { get; set; }
        public bool HolderHasSignature { get; set; }
    }

    public class SavePositionDto
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public int UnitId { get; set; }
        public int? UserId { get; set; }
        public int? EmployeeId { get; set; }
        public bool IsManager { get; set; }
        public bool CanSign { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class DelegationQuery : OfficePageQuery
    {
        public int? PositionId { get; set; }
        public int? DeputyUserId { get; set; }
        public bool? CurrentOnly { get; set; }
    }

    public class DelegationDto
    {
        public int Id { get; set; }
        public int PositionId { get; set; }
        public string PositionTitle { get; set; }
        public int DeputyUserId { get; set; }
        public string DeputyName { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public string FromDateText { get; set; }
        public string ToDateText { get; set; }
        public bool IsActive { get; set; }
        public bool Current { get; set; }
        public string Reason { get; set; }

        /// <summary>0 approved, 1 pending (waits for the holder or the CEO), 2 rejected. Only an approved, active delegation in its dates gives access.</summary>
        public Entities.Model.NasbNiro.Office.OfDelegationStatus Status { get; set; }
        public string CreatedByName { get; set; }
        public string DecidedByName { get; set; }
        public string DecidedAtText { get; set; }
    }

    public class SaveDelegationDto
    {
        public int PositionId { get; set; }
        public int DeputyUserId { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public string Reason { get; set; }
    }

    public class OrgNodeDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public int? ManagerPositionId { get; set; }
        public List<PositionDto> Positions { get; set; } = new List<PositionDto>();
        public List<OrgNodeDto> Children { get; set; } = new List<OrgNodeDto>();
    }

    public class MyPositionDto
    {
        public int PositionId { get; set; }
        public string Title { get; set; }
        public string UnitTitle { get; set; }
        public bool OnBehalf { get; set; }
        public bool CanSign { get; set; }
        public bool IsManager { get; set; }
    }

    public class MyOfficeDto
    {
        public int UserId { get; set; }
        public string UserName { get; set; }
        public List<MyPositionDto> Positions { get; set; } = new List<MyPositionDto>();
        public bool HasSignature { get; set; }
        public Guid? SignatureDocumentId { get; set; }
        public int? EmployeeId { get; set; }
    }

    public class SaveSignatureDto
    {
        public Guid DocumentId { get; set; }
    }

    // ------------------------------------------------------------------------------------------ inbox

    public class InboxQuery : OfficePageQuery
    {
        /// <summary>Default: items that are still open (Unread, Read).</summary>
        public OfInboxStatus? Status { get; set; }
        public bool IncludeClosed { get; set; }
        public OfInboxKind? Kind { get; set; }
        public string SourceModule { get; set; }
        public string SourceKey { get; set; }
    }

    public class InboxItemDto
    {
        public long Id { get; set; }
        public OfInboxKind Kind { get; set; }
        public string Title { get; set; }
        public string SourceModule { get; set; }
        public string SourceType { get; set; }
        public int? SourceId { get; set; }
        public string SourceKey { get; set; }
        public string Link { get; set; }
        public DateTime? DueDate { get; set; }
        public string DueDateText { get; set; }
        public bool Overdue { get; set; }
        public OfUrgency Priority { get; set; }
        public OfInboxStatus Status { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string CreatedAtText { get; set; }
        public DateTimeOffset? ReadAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public int? RecipientPositionId { get; set; }
        public string RecipientPositionTitle { get; set; }
        public string RecipientRole { get; set; }
        public string ClosedReason { get; set; }
    }

    public class InboxCountsDto
    {
        public int Unread { get; set; }
        public int Open { get; set; }
        public int Overdue { get; set; }
        public Dictionary<string, int> ByKind { get; set; } = new Dictionary<string, int>();
    }

    public class InboxActionDto
    {
        public long Id { get; set; }

        /// <summary>read, unread or done (done is allowed for tasks, alerts and information items only).</summary>
        public string Action { get; set; }
    }
}
