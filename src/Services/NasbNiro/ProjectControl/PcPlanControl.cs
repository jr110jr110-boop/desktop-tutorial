using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>
    /// R1c (H1, integrated change control, PMBOK 7 / ISO 21500): while a contract has a current (approved) baseline, the plan is the approved plan.
    /// Activities, dependencies, WBS nodes, weights and the cost lines are read-only through the normal endpoints; they change only when a change request
    /// whose scenario describes the change is approved (see <see cref="PcScenario"/>).
    /// </summary>
    internal static class PcPlanLock
    {
        public const string Refusal = "پس از تایید خط مبنا، برنامه (فعالیت‌ها، وابستگی‌ها، گره‌های WBS، وزن‌ها و ردیف‌های هزینه) فقط از مسیر «درخواست تغییر» عوض می‌شود: درخواست تغییر با سناریوی افزودن، اصلاح یا حذف ثبت کنید و پس از تایید، خط مبنای تازه ساخته می‌شود.";

        /// <summary>True when the contract has a current approved baseline.</summary>
        public static Task<bool> IsLockedAsync(IUnitOfWork uow, int contractId, CancellationToken cancellationToken) =>
            uow.Set<PcContractTerms>().AsNoTracking().AnyAsync(t => t.ContractId == contractId && !t.Deleted && t.CurrentBaselineId != null, cancellationToken);

        /// <summary>The fields of an activity that belong to the approved plan (scope, structure, schedule, cost, weight). Titles, wiring to other modules and the responsible unit are not among them.</summary>
        public static bool ActivityPlanChanged(PcActivity a, PcActivityDto d, string code)
        {
            return (code != null && a.Code != code) || a.WbsNodeId != d.WbsNodeId || a.SourceId != d.SourceId || a.WorkOrderId != d.WorkOrderId || a.DurationDays != d.DurationDays
                || a.CalendarId != d.CalendarId || a.Phase != d.Phase || a.ConstraintType != d.ConstraintType || a.ConstraintDate?.Date != d.ConstraintDate?.Date
                || a.Weight != d.Weight || a.BudgetAtCompletion != d.BudgetAtCompletion || a.ProgressMethod != d.ProgressMethod
                || a.SourceType != (Enum.IsDefined(typeof(PcSourceType), d.SourceType) ? d.SourceType : PcSourceType.Manual);
        }

        /// <summary>The fields of a WBS node that belong to the approved plan; the title, the dictionary and the responsible unit are not among them.</summary>
        public static bool WbsPlanChanged(PcWbsNode n, PcWbsNodeDto d, string code)
        {
            return n.ParentId != d.ParentId || n.Code != code || n.NodeType != d.NodeType || n.Phase != d.Phase || n.WorkOrderId != d.WorkOrderId || n.AssemblyId != d.AssemblyId
                || n.BomId != d.BomId || n.ProcessId != d.ProcessId || n.Weight != d.Weight || n.BudgetAmount != d.BudgetAmount || n.Sort != d.Sort;
        }

        /// <summary>True when something a reviewer reads (not only the plan fields) changed; the approval of the node is then given again.</summary>
        public static bool WbsContentChanged(PcWbsNode n, PcWbsNodeDto d, string code, string title, string dictionary, string unit) =>
            WbsPlanChanged(n, d, code) || n.Title != title || n.Dictionary != dictionary || n.ResponsibleUnit != unit;
    }

    /// <summary>R1c (H1): the schedule of the contract today and with the scenario of a change request, for the impact analysis (nothing is written).</summary>
    public sealed class PcScenarioPreview
    {
        /// <summary>The Persian error (wrong scenario, a cycle, no dates); null when the preview was computed.</summary>
        public string Error { get; set; }
        public CpmResult Before { get; set; }
        public CpmResult After { get; set; }

        /// <summary>The codes of the activities in the result; an activity the scenario adds has a negative id (see <see cref="PcScenario.TempId"/>).</summary>
        public Dictionary<int, string> Codes { get; set; } = new Dictionary<int, string>();

        /// <summary>The activities of the contract by id (for the critical-path and purchase counts).</summary>
        public Dictionary<int, PcActivity> Activities { get; set; } = new Dictionary<int, PcActivity>();
    }

    /// <summary>R1c (H1): what applying the scenario of an approved change request left behind: the tracked activities (with the dates of the plan run) and their shares in the project.</summary>
    public sealed class PcScenarioOutcome
    {
        /// <summary>The Persian error; null when the plan was changed. Nothing is saved when an error is returned.</summary>
        public string Error { get; set; }

        /// <summary>The activities of the plan after the change (not deleted), tracked, with early dates.</summary>
        public List<PcActivity> Activities { get; set; } = new List<PcActivity>();

        /// <summary>The share of every activity in the whole project (0-1), from the WBS tree and the weights after the change.</summary>
        public Dictionary<int, decimal> EffectiveWeights { get; set; } = new Dictionary<int, decimal>();

        /// <summary>What the application did besides the scenario (for example an activity outside the baseline that was taken out of the plan).</summary>
        public List<string> Notes { get; set; } = new List<string>();
    }

    /// <summary>R1c (H1): the checks and the arithmetic of a change-request scenario (add, modify, remove activities), apart from the database.</summary>
    internal static class PcScenario
    {
        public const int MaxDuration = 3650;

        /// <summary>The activity is not started, so it can leave the plan (a started activity keeps its history: reduce its duration or budget instead).</summary>
        public static bool CanBeRemoved(PcActivity a) => a.Status == PcActivityStatus.NotStarted && !a.ActualStart.HasValue && a.PercentCache <= 0m;

        /// <summary>The Persian error of the first wrong item, or null.</summary>
        public static string Validate(IList<PcChangeScenarioItemDto> items, IList<PcActivity> activities, ISet<int> wbsNodeIds, ISet<int> workOrderIds, ISet<int> calendarIds)
        {
            if (items == null || items.Count == 0) return null;
            Dictionary<int, PcActivity> byId = activities.ToDictionary(a => a.Id);
            var touched = new HashSet<int>();
            var addedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingCodes = new HashSet<string>(activities.Select(a => a.Code), StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < items.Count; i++)
            {
                PcChangeScenarioItemDto s = items[i];
                string at = "سناریو، ردیف " + (i + 1).ToString(CultureInfo.InvariantCulture) + ": ";
                if (!Enum.IsDefined(typeof(PcScenarioOperation), s.Operation)) return at + "عملیات نامعتبر است.";
                if (s.Operation == PcScenarioOperation.Add)
                {
                    string code = PcText.Clean(s.Code);
                    if (code == null || code.Length > 60) return at + "کد فعالیت جدید لازم است (حداکثر ۶۰ نویسه).";
                    if (PcText.Clean(s.Title) == null) return at + "عنوان فعالیت جدید لازم است.";
                    if (existingCodes.Contains(code) || !addedCodes.Add(code)) return at + "کد فعالیت جدید «" + code + "» تکراری است.";
                    if (s.DurationDays < 0 || s.DurationDays > MaxDuration) return at + "مدت فعالیت باید بین ۰ و ۳۶۵۰ روز کاری باشد.";
                    if (s.BudgetAtCompletion < 0m) return at + "بودجه منفی نیست.";
                    if (s.Weight < 0m || s.Weight > 100m) return at + "وزن باید بین ۰ و ۱۰۰ باشد.";
                    if (s.ProgressMethod != PcProgressMethod.Manual && s.ProgressMethod != PcProgressMethod.Contractor && s.ProgressMethod != PcProgressMethod.Tasks) return at + "فعالیت جدید با روش پیشرفت «دستی»، «پیمانکار» یا «وظایف» افزوده می‌شود؛ روش‌های دیگر پس از تایید با اتصال به رویدادهای سیستم تنظیم می‌شوند.";
                    if (s.WbsNodeId.HasValue && !wbsNodeIds.Contains(s.WbsNodeId.Value)) return at + "گره WBS متعلق به این قرارداد نیست.";
                    if (s.WorkOrderId.HasValue && !workOrderIds.Contains(s.WorkOrderId.Value)) return at + "دستور کار متعلق به این قرارداد نیست.";
                    if (s.CalendarId.HasValue && !calendarIds.Contains(s.CalendarId.Value)) return at + "تقویم پیدا نشد.";
                    if (s.Phase.HasValue && !Enum.IsDefined(typeof(PcPhase), s.Phase.Value)) return at + "فاز نامعتبر است.";
                    continue;
                }
                if (!byId.TryGetValue(s.ActivityId, out PcActivity a)) return at + "فعالیت متعلق به این قرارداد نیست.";
                if (!touched.Add(s.ActivityId)) return at + "فعالیت «" + a.Code + "» بیش از یک بار در سناریو آمده است.";
                if (s.Operation == PcScenarioOperation.Remove)
                {
                    if (!CanBeRemoved(a)) return at + "فعالیت «" + a.Code + "» شروع شده و حذف نمی‌شود؛ مدت یا بودجه آن را کم کنید.";
                    continue;
                }
                if (a.DurationDays + s.DurationDelta < 0 || a.DurationDays + s.DurationDelta > MaxDuration) return at + "مدت فعالیت «" + a.Code + "» پس از تغییر خارج از ۰ تا ۳۶۵۰ روز کاری است.";
                if (a.BudgetAtCompletion + s.BudgetDelta < 0m) return at + "بودجه فعالیت «" + a.Code + "» پس از تغییر منفی می‌شود.";
                if (s.NewWeight.HasValue && (s.NewWeight.Value < 0m || s.NewWeight.Value > 100m)) return at + "وزن باید بین ۰ و ۱۰۰ باشد.";
            }
            // the links of an added activity: an existing activity that stays, or the code of another added one
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Operation != PcScenarioOperation.Add) continue;
                string at = "سناریو، ردیف " + (i + 1).ToString(CultureInfo.InvariantCulture) + ": ";
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (PcScenarioLinkDto l in items[i].Links ?? new List<PcScenarioLinkDto>())
                {
                    if (!Enum.IsDefined(typeof(PcDependencyType), l.Type)) return at + "نوع وابستگی نامعتبر است.";
                    if (l.LagDays < -3650 || l.LagDays > 3650) return at + "تاخیر/تقدم نامعتبر است.";
                    string other;
                    if (l.OtherActivityId.HasValue)
                    {
                        if (!byId.ContainsKey(l.OtherActivityId.Value)) return at + "فعالیت وابسته متعلق به این قرارداد نیست.";
                        if (items.Any(x => x.Operation == PcScenarioOperation.Remove && x.ActivityId == l.OtherActivityId.Value)) return at + "فعالیت وابسته در همین سناریو حذف می‌شود.";
                        other = "#" + l.OtherActivityId.Value.ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        string c = PcText.Clean(l.OtherCode);
                        if (c == null || !items.Any(x => x.Operation == PcScenarioOperation.Add && string.Equals(PcText.Clean(x.Code), c, StringComparison.OrdinalIgnoreCase))) return at + "وابستگی باید به فعالیتی از قرارداد یا به کد فعالیت دیگری که همین سناریو اضافه می‌کند اشاره کند.";
                        if (string.Equals(c, PcText.Clean(items[i].Code), StringComparison.OrdinalIgnoreCase)) return at + "فعالیت نمی‌تواند به خودش وابسته باشد.";
                        other = c;
                    }
                    if (!seen.Add(other + "|" + l.OtherIsPredecessor)) return at + "وابستگی تکراری است.";
                }
            }
            return null;
        }

        /// <summary>The change of the budget that the scenario brings: the deltas, plus the budgets of the added activities, minus the budgets of the removed ones.</summary>
        public static decimal CostDelta(IList<PcChangeScenarioItemDto> items, IDictionary<int, PcActivity> byId)
        {
            decimal sum = 0m;
            foreach (PcChangeScenarioItemDto s in items ?? new List<PcChangeScenarioItemDto>())
            {
                if (s.Operation == PcScenarioOperation.Add) sum += s.BudgetAtCompletion;
                else if (s.Operation == PcScenarioOperation.Remove) sum -= byId.TryGetValue(s.ActivityId, out PcActivity a) ? a.BudgetAtCompletion : 0m;
                else sum += s.BudgetDelta;
            }
            return sum;
        }

        /// <summary>The temporary node id of the added item with this index inside the preview network (never an id of a real activity).</summary>
        public static int TempId(int index) => -(index + 1);

        /// <summary>The roles of the units that the scenario touches (for the change board).</summary>
        public static IEnumerable<string> UnitsOf(IList<PcChangeScenarioItemDto> items, IDictionary<int, PcActivity> byId)
        {
            foreach (PcChangeScenarioItemDto s in items ?? new List<PcChangeScenarioItemDto>())
            {
                string unit = s.Operation == PcScenarioOperation.Add ? s.ResponsibleUnit : byId.TryGetValue(s.ActivityId, out PcActivity a) ? a.ResponsibleUnit : null;
                if (!string.IsNullOrWhiteSpace(unit)) yield return unit.Trim().ToUpperInvariant();
            }
        }
    }
}
