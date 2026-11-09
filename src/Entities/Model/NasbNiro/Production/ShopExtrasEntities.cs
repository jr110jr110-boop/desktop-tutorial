using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.Production
{
    // Production release P2 part C: in-process checklists of the routing steps, performance events of the staff, live view of the shop cameras. Numbers are stored; never renumber.

    public enum MfChecklistItemKind { YesNo = 1, Measure = 2, Text = 3 }

    public enum MfChecklistStatus { Draft = 0, Submitted = 1, Approved = 2, Rejected = 3 }

    /// <summary>The in-process checklist of one routing step (OperationTemplate): filled at the terminal for every operation of that step, reviewed by QC or the supervisor.</summary>
    public class MfChecklistTemplate : BaseEntity
    {
        public int OperationTemplateId { get; set; }
        public string Title { get; set; }

        /// <summary>The operation does not finish on the terminal before its checklist is submitted with every item passing.</summary>
        public bool RequiredToFinish { get; set; } = true;

        public bool IsActive { get; set; } = true;
        public ICollection<MfChecklistItem> Items { get; set; } = new List<MfChecklistItem>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class MfChecklistItem
    {
        public int Id { get; set; }
        public int TemplateId { get; set; }
        public int Sequence { get; set; }
        public string Text { get; set; }
        public MfChecklistItemKind Kind { get; set; }
        public decimal? Nominal { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public string Unit { get; set; }
        public bool Mandatory { get; set; } = true;

        /// <summary>A removed item stays for the answers given to it.</summary>
        public bool IsActive { get; set; } = true;
    }

    /// <summary>The filled checklist of one operation.</summary>
    public class MfChecklistResult : BaseEntity
    {
        public int OperationId { get; set; }
        public int TemplateId { get; set; }
        public MfChecklistStatus Status { get; set; }
        public bool AllPassed { get; set; }
        public int? FilledByUserId { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? ReviewedByUserId { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
        public string ReviewNote { get; set; }
        public ICollection<MfChecklistAnswer> Answers { get; set; } = new List<MfChecklistAnswer>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class MfChecklistAnswer
    {
        public int Id { get; set; }
        public int ResultId { get; set; }
        public int ItemId { get; set; }
        public bool? YesNo { get; set; }
        public decimal? Value { get; set; }
        public string Text { get; set; }
        public bool Passed { get; set; }
    }

    // ------------------------------------------------------------------------------------------ staff performance events

    public enum HrEventKind { Evaluation = 1, Disciplinary = 2, QualityFeedback = 3, Goal = 4, Commendation = 5 }

    public enum HrEventStatus { Draft = 0, Submitted = 1, Approved = 2, Rejected = 3 }

    /// <summary>A performance event of an employee (evaluation, disciplinary note, quality feedback, goal, commendation): written by a supervisor, approved by another person.</summary>
    public class HrPerformanceEvent : BaseEntity
    {
        public int EmployeeId { get; set; }
        public HrEventKind Kind { get; set; }
        public DateTime EventDate { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }

        /// <summary>1..5 for an evaluation (null for the other kinds).</summary>
        public int? Score { get; set; }

        /// <summary>The NCR or production operation the feedback is about (optional).</summary>
        public int? NcrId { get; set; }

        public int? OperationId { get; set; }
        public HrEventStatus Status { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ shop cameras

    /// <summary>An IP camera of the shop on the LAN. The server fetches its pictures (the browsers never reach the camera); the login is a key name in the server settings.</summary>
    public class MfCamera : BaseEntity
    {
        public string Title { get; set; }
        public int? MachineId { get; set; }
        public int? WorkCenterId { get; set; }

        /// <summary>http(s) address of one JPEG picture on the LAN (for example the ISAPI snapshot of the camera).</summary>
        public string SnapshotUrl { get; set; }

        /// <summary>http(s) address of the MJPEG stream on the LAN (optional; without it the page refreshes the snapshot).</summary>
        public string StreamUrl { get; set; }

        /// <summary>Name of the credential in the server settings (Dnc:Credentials:&lt;key&gt;:User / Password).</summary>
        public string CredentialKey { get; set; }

        public bool IsActive { get; set; } = true;

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}
