using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Production;

namespace ViewModels.NasbNiro.ProductionDtos
{
    // Production release P2 part C. ISO dates in JSON; "...Text" is Persian (Shamsi).

    // ------------------------------------------------------------------------------------------ checklists

    public class ChecklistItemDto
    {
        public int? Id { get; set; }
        public int Sequence { get; set; }
        public string Text { get; set; }
        public MfChecklistItemKind Kind { get; set; }
        public decimal? Nominal { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public string Unit { get; set; }
        public bool Mandatory { get; set; } = true;

        /// <summary>P2 part E: the code the probe cycle or the gauge reports (e.g. "D1").</summary>
        public string Characteristic { get; set; }

        /// <summary>P2 part E: the value comes only from a measuring device; a typed value is ignored.</summary>
        public bool DeviceOnly { get; set; }
    }

    public class ChecklistTemplateDto
    {
        public int? Id { get; set; }
        public int OperationTemplateId { get; set; }
        public string OperationTitle { get; set; }
        public string Title { get; set; }
        public bool RequiredToFinish { get; set; } = true;
        public bool IsActive { get; set; } = true;
        public List<ChecklistItemDto> Items { get; set; } = new List<ChecklistItemDto>();
        public string RowVersion { get; set; }
    }

    public class ChecklistAnswerDto
    {
        public int ItemId { get; set; }
        public bool? YesNo { get; set; }
        public decimal? Value { get; set; }
        public string Text { get; set; }

        /// <summary>Read only: the rule of the item applied to the answer.</summary>
        public bool Passed { get; set; }

        /// <summary>Read only (P2 part E): the device measurement the value came from.</summary>
        public long? MeasurementId { get; set; }
    }

    /// <summary>The checklist of an operation: the items of its step and the answers given so far.</summary>
    public class OperationChecklistDto
    {
        public int OperationId { get; set; }
        public bool HasTemplate { get; set; }
        public ChecklistTemplateDto Template { get; set; }
        public int? ResultId { get; set; }
        public MfChecklistStatus? Status { get; set; }
        public bool AllPassed { get; set; }
        public List<ChecklistAnswerDto> Answers { get; set; } = new List<ChecklistAnswerDto>();
        public string FilledByName { get; set; }
        public string SubmittedAtText { get; set; }
        public string ReviewedByName { get; set; }
        public string ReviewedAtText { get; set; }
        public string ReviewNote { get; set; }

        /// <summary>save, submit, approve, reject.</summary>
        public List<string> AllowedActions { get; set; } = new List<string>();

        public string RowVersion { get; set; }
    }

    public class SaveChecklistAnswersDto
    {
        public int OperationId { get; set; }
        public List<ChecklistAnswerDto> Answers { get; set; } = new List<ChecklistAnswerDto>();

        /// <summary>Submit after saving.</summary>
        public bool Submit { get; set; }

        public string RowVersion { get; set; }
    }

    public class ChecklistReviewDto
    {
        public int OperationId { get; set; }

        /// <summary>approve or reject (note required).</summary>
        public string Action { get; set; }

        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ staff performance events

    public class HrEventQuery : MfPageQuery
    {
        public int? EmployeeId { get; set; }
        public HrEventKind? Kind { get; set; }
        public HrEventStatus? Status { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }

        /// <summary>Only the caller's own events (any signed-in user linked to a personnel file).</summary>
        public bool? Mine { get; set; }
    }

    public class HrEventDto
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public HrEventKind Kind { get; set; }
        public DateTime EventDate { get; set; }
        public string EventDateText { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int? Score { get; set; }
        public int? NcrId { get; set; }
        public int? OperationId { get; set; }
        public HrEventStatus Status { get; set; }
        public string CreatedByName { get; set; }
        public string CreatedAtText { get; set; }
        public string DecidedByName { get; set; }
        public string DecidedAtText { get; set; }
        public string DecisionNote { get; set; }
        public List<string> AllowedActions { get; set; } = new List<string>();
        public string RowVersion { get; set; }
    }

    public class SaveHrEventDto
    {
        public int? Id { get; set; }
        public int EmployeeId { get; set; }
        public HrEventKind Kind { get; set; }
        public DateTime EventDate { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int? Score { get; set; }
        public int? NcrId { get; set; }
        public int? OperationId { get; set; }
        public string RowVersion { get; set; }
    }

    public class HrEventActionDto
    {
        public int Id { get; set; }

        /// <summary>submit, approve, reject (note required), delete (a draft, by its writer).</summary>
        public string Action { get; set; }

        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>Approved events of one employee in a period.</summary>
    public class HrEmployeeSummaryDto
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public int Evaluations { get; set; }
        public decimal? AverageScore { get; set; }
        public int Disciplinary { get; set; }
        public int QualityFeedback { get; set; }
        public int Goals { get; set; }
        public int Commendations { get; set; }
    }

    // ------------------------------------------------------------------------------------------ cameras

    public class CameraDto
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public int? MachineId { get; set; }
        public string MachineTitle { get; set; }
        public int? WorkCenterId { get; set; }
        public string WorkCenterCode { get; set; }
        public string SnapshotUrl { get; set; }
        public string StreamUrl { get; set; }
        public string CredentialKey { get; set; }
        public bool HasStream { get; set; }
        public bool IsActive { get; set; } = true;
        public string RowVersion { get; set; }
    }

    /// <summary>A picture or a stream the API passes on from a camera.</summary>
    public class CameraMediaDto
    {
        public string ContentType { get; set; }
        public byte[] Bytes { get; set; }
        public System.IO.Stream Stream { get; set; }
        public System.Net.Http.HttpResponseMessage Response { get; set; }
    }
}
