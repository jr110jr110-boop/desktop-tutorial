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
    }
}
