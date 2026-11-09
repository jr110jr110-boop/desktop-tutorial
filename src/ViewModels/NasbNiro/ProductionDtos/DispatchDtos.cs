using System;
using System.Collections.Generic;

namespace ViewModels.NasbNiro.ProductionDtos
{
    // Production release P2 part D: the production AI seat (MF-DISPATCH) and the shop-floor overview. Read-only: nothing here changes an order, an operation or a plan.
    // ISO dates in JSON; every "...Text" is Persian (Shamsi).

    /// <summary>One operation in the dispatch queue of a work centre, ranked by the seat.</summary>
    public class DispatchItemDto
    {
        public int Rank { get; set; }
        public int OperationId { get; set; }
        public int OrderId { get; set; }
        public string OrderNumber { get; set; }
        public int Sequence { get; set; }

        /// <summary>Order number / sequence.</summary>
        public string Name { get; set; }

        public string Title { get; set; }
        public string ItemTitle { get; set; }

        /// <summary>running (on the machine now), ready (may start now) or upcoming (an earlier step is still open).</summary>
        public string Stage { get; set; }

        public decimal HoursLeft { get; set; }
        public DateTime? NeedDate { get; set; }
        public string NeedDateText { get; set; }
        public DateTime? ScheduledStart { get; set; }
        public string ScheduledStartText { get; set; }

        /// <summary>Working days left until the need date divided by the working days the rest of the order needs; below 1 = late unless it moves up. Null without a need date.</summary>
        public decimal? CriticalRatio { get; set; }

        /// <summary>Working days to spare (negative = late).</summary>
        public decimal? SlackDays { get; set; }

        public bool KitShortage { get; set; }

        /// <summary>The CNC program of the step is approved and confirmed on a machine of the work centre (null: the step has no CNC program or it was not checked).</summary>
        public bool? ProgramReady { get; set; }

        public string ProgramNote { get; set; }

        /// <summary>late-risk, at-risk, on-time or no-date.</summary>
        public string Risk { get; set; }

        /// <summary>Why it stands at this place (Persian).</summary>
        public string Reason { get; set; }
    }

    public class DispatchCenterDto
    {
        public int WorkCenterId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }

        /// <summary>Hours the open orders need here in the horizon (no capacity limit).</summary>
        public decimal DemandHours { get; set; }

        /// <summary>Hours the centre can produce in the horizon (calendar, shifts, efficiency, utilisation, downtime).</summary>
        public decimal CapacityHours { get; set; }

        public decimal? LoadPercent { get; set; }

        /// <summary>Hours of the steps that may start now.</summary>
        public decimal ReadyHours { get; set; }

        public int ReadyCount { get; set; }
        public int RunningCount { get; set; }
        public int LateRiskCount { get; set; }

        /// <summary>bottleneck (over capacity), busy (above the busy threshold), normal or idle.</summary>
        public string Level { get; set; }

        /// <summary>The share of the last 7 days the machines of the centre really ran (live monitoring; null without monitoring).</summary>
        public decimal? MeasuredRunPercent { get; set; }

        public List<string> Machines { get; set; } = new List<string>();
        public List<DispatchItemDto> Queue { get; set; } = new List<DispatchItemDto>();
    }

    /// <summary>One concrete proposal of the seat. A person decides; the seat never changes the plan.</summary>
    public class DispatchSuggestionDto
    {
        /// <summary>bottleneck, late-risk, machine-down, machine-idle, kit, program, overdue.</summary>
        public string Kind { get; set; }

        /// <summary>1 = urgent, 2 = important, 3 = note.</summary>
        public int Severity { get; set; }

        public string Title { get; set; }
        public string Text { get; set; }

        /// <summary>The page that shows the subject (front-end route).</summary>
        public string Link { get; set; }

        public int? WorkCenterId { get; set; }
        public int? OperationId { get; set; }
        public int? MachineId { get; set; }
    }

    public class DispatchPlanDto
    {
        public DateTime AsOf { get; set; }
        public string AsOfText { get; set; }
        public string GeneratedAtText { get; set; }
        public int HorizonDays { get; set; }
        public int OpenOrders { get; set; }
        public int LateOrders { get; set; }
        public int LateRiskOperations { get; set; }

        /// <summary>The work centre with the highest load, when it is over the busy threshold.</summary>
        public string BottleneckText { get; set; }

        public List<DispatchCenterDto> Centers { get; set; } = new List<DispatchCenterDto>();
        public List<DispatchSuggestionDto> Suggestions { get; set; } = new List<DispatchSuggestionDto>();

        /// <summary>The seat explains its method in one paragraph (shown under the plan).</summary>
        public string Method { get; set; }
    }

    public class AiDispatchQuery
    {
        public int? WorkCenterId { get; set; }

        /// <summary>Days ahead for the load (default 10, 1..60).</summary>
        public int? HorizonDays { get; set; }
    }

    /// <summary>The numbers at the top of the shop-floor overview.</summary>
    public class FloorTileDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public decimal Value { get; set; }
        public string ValueText { get; set; }
        public string Hint { get; set; }

        /// <summary>good, warn, bad or neutral.</summary>
        public string Tone { get; set; }

        public string Link { get; set; }
    }

    /// <summary>One thing waiting for a person (an approval, a review, an alarm).</summary>
    public class FloorWaitingDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public int Count { get; set; }
        public string Link { get; set; }
        public string Tone { get; set; }
    }

    public class FloorOverviewDto
    {
        public string AsOfText { get; set; }
        public string GreetingText { get; set; }
        public List<FloorTileDto> Tiles { get; set; } = new List<FloorTileDto>();
        public LiveBoardDto Live { get; set; }
        public List<FloorWaitingDto> Waiting { get; set; } = new List<FloorWaitingDto>();
        public List<DispatchSuggestionDto> Suggestions { get; set; } = new List<DispatchSuggestionDto>();
        public List<DispatchCenterDto> Centers { get; set; } = new List<DispatchCenterDto>();
        public string BottleneckText { get; set; }

        /// <summary>The request came from the company VPN (remote access): the page is read-only there.</summary>
        public bool Remote { get; set; }
    }

    public class RemoteStatusDto
    {
        public bool Enabled { get; set; }
        public bool Remote { get; set; }
        public bool ReadOnly { get; set; }
        public bool Allowed { get; set; }
        public string Text { get; set; }
    }
}
