using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Office;

namespace ViewModels.NasbNiro.OfficeDtos
{
    /// <summary>One field of a request form. Type: text, number, date or select.</summary>
    public class RequestFieldDto
    {
        public string Name { get; set; }
        public string Label { get; set; }
        public string Type { get; set; } = "text";
        public bool Required { get; set; }
        public List<string> Options { get; set; } = new List<string>();
    }

    public class RequestTypeDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public List<RequestFieldDto> Fields { get; set; } = new List<RequestFieldDto>();

        /// <summary>Positions that approve in order. Empty: the requester's unit manager approves.</summary>
        public List<int> ApprovalChain { get; set; } = new List<int>();
        public List<string> ApprovalChainTitles { get; set; } = new List<string>();
        public string TargetNote { get; set; }
        public bool IsActive { get; set; }
    }

    public class SaveRequestTypeDto
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public List<RequestFieldDto> Fields { get; set; } = new List<RequestFieldDto>();
        public List<int> ApprovalChain { get; set; } = new List<int>();
        public string TargetNote { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class RequestQuery : OfficePageQuery
    {
        public OfRequestStatus? Status { get; set; }
        public int? TypeId { get; set; }
        public bool? MineOnly { get; set; }
        public bool? WaitingForMe { get; set; }
    }

    public class RequestApprovalDto
    {
        public int Step { get; set; }
        public int PositionId { get; set; }
        public string PositionTitle { get; set; }
        public OfApprovalStatus Status { get; set; }
        public string ActedByName { get; set; }
        public bool OnBehalf { get; set; }
        public string Comment { get; set; }
        public DateTimeOffset? ActedAt { get; set; }
        public string ActedAtText { get; set; }
    }

    public class RequestDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int TypeId { get; set; }
        public string TypeTitle { get; set; }
        public string TargetNote { get; set; }
        public int RequesterUserId { get; set; }
        public string RequesterName { get; set; }
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
        public List<RequestFieldDto> Fields { get; set; } = new List<RequestFieldDto>();
        public OfRequestStatus Status { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public string SubmittedAtText { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string CompletedAtText { get; set; }
        public string Note { get; set; }
        public List<RequestApprovalDto> Approvals { get; set; } = new List<RequestApprovalDto>();
        public List<string> AllowedActions { get; set; } = new List<string>();
        public string RowVersion { get; set; }
    }

    public class SaveRequestDto
    {
        public int? Id { get; set; }
        public int TypeId { get; set; }
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();

        /// <summary>Send for approval at once.</summary>
        public bool Submit { get; set; }
        public string RowVersion { get; set; }
    }

    public class RequestActionDto
    {
        public int Id { get; set; }

        /// <summary>submit, approve, reject, cancel, complete.</summary>
        public string Action { get; set; }
        public string Comment { get; set; }
        public string RowVersion { get; set; }
    }
}
