using System;
using System.Collections.Generic;

namespace ViewModels.NasbNiro.EngineeringDtos
{
    public class EnCountDto
    {
        public int Key { get; set; }
        public string Text { get; set; }
        public int Count { get; set; }
    }

    public class EnPlatformCountDto
    {
        public int Status { get; set; }
        public string StatusText { get; set; }
        public int Count { get; set; }

        /// <summary>The required documents still missing in the dossiers of this status.</summary>
        public int MissingRequired { get; set; }
    }

    /// <summary>The picture of the engineering control for the engineering manager, the PMO and the CEO. Counts only.</summary>
    public class EnOverviewDto
    {
        public bool ControlStarted { get; set; }
        public string ControlStartText { get; set; }
        public List<EnCountDto> CasesByStatus { get; set; } = new List<EnCountDto>();
        public List<EnCountDto> CasesByClassification { get; set; } = new List<EnCountDto>();
        public List<EnPlatformCountDto> PlatformsByDossierStatus { get; set; } = new List<EnPlatformCountDto>();
        public int PlatformsTotal { get; set; }
        public int PlatformsIncomplete { get; set; }
        public int OpenClarifications { get; set; }

        /// <summary>Handovers that wait for a gate: Draft (engineering), Submitted (PMO accept), Accepted (PMO release).</summary>
        public List<EnCountDto> HandoversWaiting { get; set; } = new List<EnCountDto>();

        public int OpenDebts { get; set; }
        public int OverdueDebts { get; set; }
        public int WaiversAwaitingCeo { get; set; }
        public int OldestWaiverWaitingHours { get; set; }
        public int DistributionsUnacknowledged { get; set; }

        /// <summary>Early warning: controlled cases whose documents are at risk, and those of them that are Awarded or later (the CEO is told).</summary>
        public int CasesAtDocumentRisk { get; set; }
        public int AwardedCasesAtDocumentRisk { get; set; }

        /// <summary>Deviations from a standard that wait for the head of engineering or the CEO (never expire; a permanent alarm), and those together with the emergency waivers that wait for the CEO.</summary>
        public int DeviationsAwaiting { get; set; }
        public int StandardsDeviationsOpen { get; set; }

        /// <summary>Of the deviations awaiting: those the head of engineering has approved (waiting for the CEO) and those not yet decided by him; hours since the oldest request of the open ones (0 when none), and since the oldest open registration proposal.</summary>
        public int DeviationsAwaitingCeo { get; set; }
        public int DeviationsAwaitingManager { get; set; }
        public int OldestDeviationWaitingHours { get; set; }
        public int OldestRegistrationWaitingHours { get; set; }
        public int StandardGapsOpen { get; set; }

        /// <summary>Questions of the units to the PMO that wait for an answer.</summary>
        public int InfoRequestsOpen { get; set; }

        /// <summary>New designs proposed as platforms that wait for the CEO.</summary>
        public int RegistrationsOpen { get; set; }

        /// <summary>Engineering tasks that wait for a person (or failed) more than 72 OFFICE hours; filled for the engineering manager and the CEO only (employer rule 14). The CEO is told in the inbox; nothing is decided by it.</summary>
        public int StalledTasksCount { get; set; }
        public List<EnStalledTaskDto> StalledTasks { get; set; } = new List<EnStalledTaskDto>();
    }

    public class EnStalledTaskDto
    {
        public int TaskId { get; set; }
        public string Title { get; set; }

        /// <summary>The roles the task waits for, in Persian.</summary>
        public string WaitingRolesText { get; set; }
        public bool Failed { get; set; }

        /// <summary>The due instant of the task (for a failed agent task: failure time + 72 office hours).</summary>
        public DateTimeOffset? DueAt { get; set; }
        public string DueAtText { get; set; }
        public string Link { get; set; }
    }
}
