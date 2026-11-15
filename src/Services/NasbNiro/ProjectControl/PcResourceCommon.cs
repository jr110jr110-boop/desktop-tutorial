using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Production;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>The Persian texts of the resource module (project control priority 4).</summary>
    internal static class PcResourceText
    {
        public const string NotAllowed = PcText.NotAllowed;
        public const string AiDecision = "ثبت و تایید طرح و نرخ منابع با انسان است؛ ایجنت هوش مصنوعی فقط پیش‌نویس طرح را می‌سازد و آن را ارسال یا تایید نمی‌کند.";
        public const string ResourceNotFound = "منبع پیدا نشد.";
        public const string RateNotFound = "نرخ پیدا نشد.";
        public const string PlanNotFound = "طرح منابع پیدا نشد.";
        public const string NoPlan = "برای این قرارداد هنوز طرح منابع ثبت نشده است.";
        public const string ActivityNotFound = "فعالیت متعلق به این قرارداد نیست.";
        public const string NotEditable = "طرح منابع فقط در وضعیت پیش‌نویس یا برگشت‌خورده ویرایش می‌شود.";
        public const string Locked = "پس از تایید خط مبنا، طرح منابع فقط از مسیر «درخواست تغییر» عوض می‌شود: درخواست تغییر را ثبت و تایید کنید و سپس نسخه تازه طرح منابع را باز کنید.";
        public const string OpenPlanExists = "برای این قرارداد یک طرح منابع باز (پیش‌نویس، ارسال‌شده یا برگشت‌خورده) وجود دارد.";
        public const string BaselineNeedsPlan = "برای این قرارداد طرح منابعِ پیش‌نویس، ارسال‌شده یا برگشت‌خورده هست؛ پس از تایید مدیر پروژه (یا برگشت و اصلاح آن) خط مبنا ساخته می‌شود.";
        public const string RatePrivacy = "روی منبع مرتبط با یک فرد نرخ ثبت نمی‌شود (محرمانگی حقوق)؛ همان منبع را به منبع نقش مربوط (مثلا «جوشکار») وصل کنید و نرخ نقش را ثبت کنید.";
        public const string SameUser = PcText.SameUser;
        public const string RowVersionMissing = "برای تایید یا برگشت طرح، نسخه ردیف (rowVersion) همان طرحی که دیده‌اید باید همراه درخواست باشد؛ صفحه را دوباره باز کنید.";
        public const string SensitivityChange = "ظرفیت یا پیوند کارمند منبعی که نرخ یا ردیف خط مبنا دارد، میزان حساسیت هزینه‌ی آن را (هزینه‌ی یک فرد) عوض می‌کند و فقط با نقش‌های حقوق و دستمزد ممکن است.";
        public const string RateDuplicate = "برای این منبع و این تاریخ، نرخ تاییدشده یا در انتظار تایید وجود دارد.";
    }

    /// <summary>The rules of the resource pool and the resource plan that need no database (tested apart).</summary>
    internal static class PcResourceRules
    {
        /// <summary>Hours of a working day when no calendar of the activity or of the project says otherwise.</summary>
        public const decimal DefaultDayHours = 7.5m;

        public const decimal MaxQuantity = 1_000_000_000m;
        public const decimal MaxRate = 100_000_000_000_000m;

        /// <summary>Who creates and edits resources and plans: planning and the PMO (the same roles as the schedule).</summary>
        public const string Writers = ProjectControlRoles.Planning;

        /// <summary>Who reads the pool, the plans and the budget view: the project control readers and the roles that handle the rates.</summary>
        public const string Readers = ProjectControlRoles.Readers + ",FINANCEMANAGER,PAYROLL,AUDITOR";

        /// <summary>Who proposes a standard rate (the maker).</summary>
        public const string RateMakers = "FINANCEMANAGER";

        /// <summary>Who approves a standard rate: a second finance person (the finance manager, or the CEO when there is only one finance manager), see <c>FinanceRoles.ManagersOrCeo</c>.</summary>
        public const string RateApprovers = "FINANCEMANAGER,CEO";

        public static string Clean(string s) => PcText.Clean(s);

        public static string Upper(string s) => PcText.Upper(s);

        /// <summary>A resource whose cost could be derived for one person: a Labour resource of fewer than three units (the group rule of the payroll protection), or linked to an employee. Seen by the payroll roles only (<c>MfPrivacy.SeesPayroll</c>).</summary>
        public static bool IsSensitive(PcResource r) => r != null && IsSensitive(r.Kind, r.Capacity, r.EmployeeId.HasValue || r.RateResourceId.HasValue);

        public static bool IsSensitive(PcResourceKind kind, decimal capacity, bool linkedToPerson) => kind == PcResourceKind.Labour && (capacity < MfLegacyLabour.MinEmployees || linkedToPerson);

        /// <summary>Only a resource that is not linked to an employee carries a rate of its own.</summary>
        public static bool CanCarryRate(PcResource r) => r != null && !r.EmployeeId.HasValue && !r.RateResourceId.HasValue;

        public static PcResourceClass ClassOf(PcResourceKind kind) =>
            kind == PcResourceKind.Labour ? PcResourceClass.Labour
            : kind == PcResourceKind.Material ? PcResourceClass.Material
            : kind == PcResourceKind.Subcontract ? PcResourceClass.Subcontract
            : PcResourceClass.Equipment;

        /// <summary>The hours of a working day: the calendar of the activity, else of the project, else 7.5.</summary>
        public static decimal DayHours(decimal? activityCalendarHours, decimal? projectCalendarHours)
        {
            if (activityCalendarHours.HasValue && activityCalendarHours.Value > 0m) return activityCalendarHours.Value;
            if (projectCalendarHours.HasValue && projectCalendarHours.Value > 0m) return projectCalendarHours.Value;
            return DefaultDayHours;
        }

        public static decimal Round4(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);

        public static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        /// <summary>The persistent text of a person list (",5,7,").</summary>
        public static string AddEditor(string list, int userId)
        {
            string key = "," + userId.ToString(CultureInfo.InvariantCulture) + ",";
            if (string.IsNullOrEmpty(list)) return key;
            return list.Contains(key) ? list : list + userId.ToString(CultureInfo.InvariantCulture) + ",";
        }

        public static bool IsEditor(string list, int userId) => !string.IsNullOrEmpty(list) && list.Contains("," + userId.ToString(CultureInfo.InvariantCulture) + ",");

        /// <summary>
        /// The Persian error of a resource that is not valid, or null. The links are checked for existence by the caller; here the shape: the code, the title, the kind, the unit, the capacity, at most one link that matches the kind,
        /// and the role resource of an employee-linked Labour resource.
        /// </summary>
        public static string ValidateShape(string code, string title, PcResourceKind kind, PcResourceUnit unit, decimal capacity, int? employeeId, int? workCenterId, int? providerId, int? rateResourceId)
        {
            if (code == null || code.Length > 50) return "کد منبع لازم است (حداکثر ۵۰ نویسه).";
            if (title == null || title.Length > 300) return "عنوان منبع لازم است (حداکثر ۳۰۰ نویسه).";
            if (!Enum.IsDefined(typeof(PcResourceKind), kind)) return "نوع منبع نامعتبر است.";
            if (!Enum.IsDefined(typeof(PcResourceUnit), unit)) return "واحد شمارش منبع نامعتبر است.";
            if (capacity <= 0m || capacity > 100000m) return "ظرفیت منبع باید بیشتر از صفر باشد.";
            int links = (employeeId.HasValue ? 1 : 0) + (workCenterId.HasValue ? 1 : 0) + (providerId.HasValue ? 1 : 0);
            if (links > 1) return "یک منبع حداکثر به یک مورد وصل می‌شود: کارمند، مرکز کار یا تامین‌کننده.";
            if (employeeId.HasValue && kind != PcResourceKind.Labour) return "پیوند به کارمند فقط برای منبع نوع نیروی انسانی است.";
            if (workCenterId.HasValue && kind != PcResourceKind.Machine) return "پیوند به مرکز کار فقط برای منبع نوع ماشین است.";
            if (providerId.HasValue && kind != PcResourceKind.Subcontract) return "پیوند به تامین‌کننده فقط برای منبع نوع پیمانکار فرعی است.";
            if (rateResourceId.HasValue && !employeeId.HasValue) return "منبع نقش مبنای نرخ فقط برای منبع نیروی انسانی وصل‌شده به کارمند است.";
            if (employeeId.HasValue && !rateResourceId.HasValue) return "منبع وصل‌شده به کارمند نرخ ندارد؛ منبع نقش مربوط (مثلا «جوشکار») را برای نرخ انتخاب کنید.";
            return null;
        }

        // ------------------------------------------------------------------------------------------------ the draft of the planning agent

        /// <summary>An activity as the draft needs it.</summary>
        internal sealed class DraftActivity
        {
            public int Id { get; set; }
            public int DurationDays { get; set; }
            public string ResponsibleUnit { get; set; }
            public decimal DayHours { get; set; }

            /// <summary>The resource classes the cost baseline lines of the activity name (empty: labour).</summary>
            public HashSet<PcResourceClass> Classes { get; set; } = new HashSet<PcResourceClass>();
        }

        internal sealed class DraftResource
        {
            public int Id { get; set; }
            public string Code { get; set; }
            public PcResourceKind Kind { get; set; }
            public PcResourceUnit Unit { get; set; }
            public string ResponsibleUnit { get; set; }
        }

        internal sealed class DraftRow
        {
            public int ActivityId { get; set; }
            public int ResourceId { get; set; }
            public decimal Quantity { get; set; }
            public string ResponsibleRole { get; set; }
        }

        /// <summary>
        /// The deterministic draft: for each activity that has a duration, the first resource (by code) of every class the activity needs whose responsible unit is the unit of the activity.
        /// A class is needed when a cost baseline line of the activity names it; an activity without such a line needs labour. The quantity is the working days of the activity times the hours of a working day
        /// for a resource counted in hours, the working days for a resource counted in days and one for a resource counted in pieces. An activity that matches no resource gets no row. The candidates are active
        /// pool resources that are not linked to a person.
        /// </summary>
        public static List<DraftRow> Draft(IEnumerable<DraftActivity> activities, IEnumerable<DraftResource> candidates)
        {
            List<DraftResource> pool = (candidates ?? Enumerable.Empty<DraftResource>()).OrderBy(r => r.Code, StringComparer.Ordinal).ThenBy(r => r.Id).ToList();
            var rows = new List<DraftRow>();
            foreach (DraftActivity a in (activities ?? Enumerable.Empty<DraftActivity>()).OrderBy(x => x.Id))
            {
                string unit = Upper(a.ResponsibleUnit);
                if (a.DurationDays <= 0 || unit == null) continue;
                IEnumerable<PcResourceClass> needed = a.Classes != null && a.Classes.Count > 0 ? a.Classes.OrderBy(c => (int)c) : new[] { PcResourceClass.Labour };
                foreach (PcResourceClass cls in needed)
                {
                    DraftResource r = pool.FirstOrDefault(x => ClassOf(x.Kind) == cls && string.Equals(Upper(x.ResponsibleUnit), unit, StringComparison.Ordinal));
                    if (r == null) continue;
                    decimal quantity = r.Unit == PcResourceUnit.Hour ? a.DurationDays * (a.DayHours > 0m ? a.DayHours : DefaultDayHours) : r.Unit == PcResourceUnit.Day ? a.DurationDays : 1m;
                    rows.Add(new DraftRow { ActivityId = a.Id, ResourceId = r.Id, Quantity = Round4(quantity), ResponsibleRole = unit });
                }
            }
            return rows;
        }
    }

    /// <summary>Reads that the service, the baseline service and the agent steps share.</summary>
    internal static class PcResourceData
    {
        /// <summary>The plans that are still being made: draft, submitted or returned.</summary>
        public static IQueryable<PcResourcePlan> OpenPlans(IUnitOfWork uow) =>
            uow.Set<PcResourcePlan>().Where(p => !p.Deleted && (p.Status == PcResourcePlanStatus.Draft || p.Status == PcResourcePlanStatus.Submitted || p.Status == PcResourcePlanStatus.Returned));

        public static async Task<bool> HasOpenPlanAsync(IUnitOfWork uow, int contractId, CancellationToken ct) =>
            await uow.Set<PcResourcePlan>().AsNoTracking().AnyAsync(p => p.ContractId == contractId && !p.Deleted
                && (p.Status == PcResourcePlanStatus.Draft || p.Status == PcResourcePlanStatus.Submitted || p.Status == PcResourcePlanStatus.Returned), ct);

        /// <summary>The newest approved plan of the contract (the one the baseline takes), or null.</summary>
        public static Task<PcResourcePlan> LatestApprovedAsync(IUnitOfWork uow, int contractId, bool track, CancellationToken ct)
        {
            IQueryable<PcResourcePlan> q = uow.Set<PcResourcePlan>().Where(p => p.ContractId == contractId && !p.Deleted && p.Status == PcResourcePlanStatus.Approved);
            if (!track) q = q.AsNoTracking();
            return q.OrderByDescending(p => p.Version).FirstOrDefaultAsync(ct);
        }

        /// <summary>
        /// The approved rate that applies on the date for each resource (null: none). A resource that is linked to an employee takes the rate of its role resource. The rate is the latest approved one
        /// that starts on or before the date.
        /// </summary>
        public static async Task<Dictionary<int, decimal?>> EffectiveRatesAsync(IUnitOfWork uow, IEnumerable<int> resourceIds, DateTime asOf, CancellationToken ct)
        {
            int[] ids = resourceIds.Distinct().ToArray();
            var result = new Dictionary<int, decimal?>();
            if (ids.Length == 0) return result;
            var owners = await uow.Set<PcResource>().AsNoTracking().Where(r => ids.Contains(r.Id)).Select(r => new { r.Id, Owner = r.RateResourceId ?? r.Id }).ToListAsync(ct);
            int[] ownerIds = owners.Select(o => o.Owner).Distinct().ToArray();
            DateTime day = asOf.Date;
            var rates = await uow.Set<PcResourceRate>().AsNoTracking().Where(x => ownerIds.Contains(x.ResourceId) && !x.Deleted && x.Status == PcRateStatus.Approved && x.EffectiveFrom <= day)
                .OrderBy(x => x.ResourceId).ThenByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id).Select(x => new { x.ResourceId, x.Rate }).ToListAsync(ct);
            Dictionary<int, decimal> byOwner = rates.GroupBy(x => x.ResourceId).ToDictionary(g => g.Key, g => g.First().Rate);
            foreach (var o in owners) result[o.Id] = byOwner.TryGetValue(o.Owner, out decimal rate) ? rate : (decimal?)null;
            foreach (int id in ids) if (!result.ContainsKey(id)) result[id] = null;
            return result;
        }

        /// <summary>
        /// The approved change request that reopens the resource plan of a contract: decided "approve", still being carried out (not closed), BUILT a baseline of its own that is still in force, and not yet used by a plan.
        /// A request without a baseline of its own (a price or time change that does not touch the plan) opens nothing. Null when none.
        /// </summary>
        public static async Task<int?> OpenableChangeRequestAsync(IUnitOfWork uow, int contractId, int? requested, CancellationToken ct)
        {
            IQueryable<PcChangeRequest> q = uow.Set<PcChangeRequest>().AsNoTracking().Where(c => !c.Deleted && c.ContractId == contractId && c.Decision == PcChangeDecision.Approve && c.Status == PcChangeStatus.Implementing
                && uow.Set<PcBaseline>().Any(b => !b.Deleted && b.ChangeRequestId == c.Id && b.Status == PcBaselineStatus.Approved)
                && !uow.Set<PcResourcePlan>().Any(p => !p.Deleted && p.ChangeRequestId == c.Id));
            if (requested.HasValue) q = q.Where(c => c.Id == requested.Value);
            return await q.OrderBy(c => c.Id).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
        }
    }

    /// <summary>The change request of a plan: the Persian reason it can no longer be worked on, or null.</summary>
    internal static class PcResourceChange
    {
        public const string NotImplementing = "درخواست تغییرِ این نسخه از طرح منابع دیگر «تاییدشده و در حال اجرا» نیست؛ نسخه را نمی‌توان ویرایش، ارسال یا تایید کرد.";
        public const string BaselineNotCurrent = "خط مبنایی که این درخواست تغییر ساخته دیگر خط مبنای جاری نیست؛ این نسخه از طرح منابع تایید نمی‌شود.";

        public static async Task<string> RequestProblemAsync(IUnitOfWork uow, int changeRequestId, CancellationToken ct) =>
            await uow.Set<PcChangeRequest>().AsNoTracking().AnyAsync(c => c.Id == changeRequestId && !c.Deleted && c.Decision == PcChangeDecision.Approve && c.Status == PcChangeStatus.Implementing, ct) ? null : NotImplementing;

        /// <summary>The baseline that the change request built (the only one the version writes its resource rows into) and the Persian reason when it is not the current baseline of the contract.</summary>
        public static async Task<(PcBaseline Baseline, string Error)> BaselineAsync(IUnitOfWork uow, int contractId, int changeRequestId, CancellationToken ct)
        {
            PcBaseline baseline = await uow.Set<PcBaseline>().AsNoTracking().Where(b => !b.Deleted && b.ContractId == contractId && b.ChangeRequestId == changeRequestId).OrderByDescending(b => b.Id).FirstOrDefaultAsync(ct);
            int? current = await uow.Set<PcContractTerms>().AsNoTracking().Where(t => t.ContractId == contractId && !t.Deleted).Select(t => t.CurrentBaselineId).FirstOrDefaultAsync(ct);
            return baseline == null || current != baseline.Id ? (null, BaselineNotCurrent) : (baseline, null);
        }
    }

    /// <summary>The part of the resource plan that the baseline touches: the refusal while a plan is open, the snapshot of the approved assignments and the check that a draft baseline still holds the approved plan.</summary>
    internal static class PcResourceBaseline
    {
        /// <summary>The Persian reason a baseline cannot be made now (a plan of the contract is still open), or null. A contract without any resource plan is baselined as before.</summary>
        public static async Task<string> CreationProblemAsync(IUnitOfWork uow, int contractId, CancellationToken ct) =>
            await PcResourceData.HasOpenPlanAsync(uow, contractId, ct) ? PcResourceText.BaselineNeedsPlan : null;

        private sealed class Row
        {
            public int ActivityId;
            public int ResourceId;
            public decimal Quantity;
            public PcResourceDistribution Distribution;
        }

        private static async Task<List<Row>> ApprovedRowsAsync(IUnitOfWork uow, int contractId, ISet<int> activityIds, CancellationToken ct)
        {
            PcResourcePlan plan = await PcResourceData.LatestApprovedAsync(uow, contractId, false, ct);
            if (plan == null) return new List<Row>();
            List<Row> rows = await uow.Set<PcActivityResource>().AsNoTracking().Where(x => x.PlanId == plan.Id && !x.Activity.Deleted)
                .OrderBy(x => x.ActivityId).ThenBy(x => x.ResourceId).Select(x => new Row { ActivityId = x.ActivityId, ResourceId = x.ResourceId, Quantity = x.Quantity, Distribution = x.Distribution }).ToListAsync(ct);
            return activityIds == null ? rows : rows.Where(r => activityIds.Contains(r.ActivityId)).ToList();
        }

        /// <summary>
        /// Adds (does not save) the rows of the approved plan of the contract to the baseline, with the standard rate of the date. <paramref name="activityIds"/> limits them to the activities that are still in the plan.
        /// Returns the number of rows.
        /// </summary>
        public static async Task<int> SnapshotAsync(IUnitOfWork uow, PcBaseline baseline, int contractId, ISet<int> activityIds, DateTime asOf, CancellationToken ct)
        {
            List<Row> rows = await ApprovedRowsAsync(uow, contractId, activityIds, ct);
            if (rows.Count == 0) return 0;
            Dictionary<int, decimal?> rates = await PcResourceData.EffectiveRatesAsync(uow, rows.Select(r => r.ResourceId), asOf, ct);
            foreach (Row r in rows)
                uow.Set<PcBaselineResource>().Add(new PcBaselineResource { Baseline = baseline, ActivityId = r.ActivityId, ResourceId = r.ResourceId, Quantity = r.Quantity, Distribution = r.Distribution, RateAtBaseline = rates.TryGetValue(r.ResourceId, out decimal? rate) ? rate : null });
            return rows.Count;
        }

        /// <summary>Adds (does not save) a copy of the resource rows of one baseline to a new one, for the activities that remain.</summary>
        public static async Task CopyAsync(IUnitOfWork uow, int fromBaselineId, PcBaseline target, ISet<int> activityIds, CancellationToken ct)
        {
            List<PcBaselineResource> rows = await uow.Set<PcBaselineResource>().AsNoTracking().Where(x => x.BaselineId == fromBaselineId).OrderBy(x => x.ActivityId).ThenBy(x => x.ResourceId).ToListAsync(ct);
            foreach (PcBaselineResource r in rows.Where(r => activityIds.Contains(r.ActivityId)))
                uow.Set<PcBaselineResource>().Add(new PcBaselineResource { Baseline = target, ActivityId = r.ActivityId, ResourceId = r.ResourceId, Quantity = r.Quantity, Distribution = r.Distribution, RateAtBaseline = r.RateAtBaseline });
        }

        /// <summary>True when the rows frozen with a draft baseline are not the assignments of the approved plan of today.</summary>
        public static async Task<bool> SnapshotDiffersAsync(IUnitOfWork uow, int baselineId, int contractId, CancellationToken ct)
        {
            List<Row> expected = await ApprovedRowsAsync(uow, contractId, null, ct);
            List<Row> stored = await uow.Set<PcBaselineResource>().AsNoTracking().Where(x => x.BaselineId == baselineId).OrderBy(x => x.ActivityId).ThenBy(x => x.ResourceId)
                .Select(x => new Row { ActivityId = x.ActivityId, ResourceId = x.ResourceId, Quantity = x.Quantity, Distribution = x.Distribution }).ToListAsync(ct);
            if (expected.Count != stored.Count) return true;
            for (int i = 0; i < expected.Count; i++)
                if (expected[i].ActivityId != stored[i].ActivityId || expected[i].ResourceId != stored[i].ResourceId || expected[i].Quantity != stored[i].Quantity || expected[i].Distribution != stored[i].Distribution) return true;
            return false;
        }
    }
}
