using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Production.Dnc;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Production.Dispatch
{
    public interface IMfDispatchService
    {
        /// <summary>The ranked queue of every work centre, its load and the proposals of the seat (read-only).</summary>
        Task<ResponseDto<DispatchPlanDto>> GetPlanAsync(AiDispatchQuery query, CancellationToken ct);

        /// <summary>The shop-floor overview: the numbers, the machines, what waits for a person and the top proposals.</summary>
        Task<ResponseDto<FloorOverviewDto>> GetFloorAsync(bool remote, CancellationToken ct);

        /// <summary>The plan without the role check (the seat runs as its own user and is checked by the job).</summary>
        Task<DispatchPlanDto> BuildAsync(DateTime asOf, int horizonDays, int? workCenterId, CancellationToken ct);
    }

    /// <summary>
    /// Production release P2 part D: the planning brain of the shop floor. It takes the finite-capacity plan of the scheduler (the same data as the load report and the dispatch list),
    /// adds the critical ratio of every open step, the live state of the machines, the measured run share and the DNC readiness, ranks each work-centre queue and writes concrete
    /// proposals in Persian. It changes nothing: a person decides (maker-checker); the seat MF-DISPATCH only leaves notes.
    /// </summary>
    public class MfDispatchService : IMfDispatchService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IMfMonitorService _monitor;

        internal Func<DateTime> Clock { get; set; } = () => DateTime.Now;

        /// <summary>At most this many steps per work-centre queue in the answer.</summary>
        public const int MaxQueue = 25;

        /// <summary>The top ready steps of each centre whose CNC program readiness is checked.</summary>
        public const int ProgramChecks = 3;

        public const string Method =
            "روش: برنامه ظرفیت محدود همان زمان‌بند شبانه (تقویم، شیفت، بهره‌وری و توقف‌های نگهداری) پایه است. برای هر مرحله باز، «نسبت بحرانی» برابر است با روزهای کاری تا تاریخ نیاز " +
            "تقسیم بر روزهای کاری که بقیه سفارش لازم دارد. کمتر از ۱ یعنی بدون جلو افتادن دیر می‌شود. صف هر مرکز به این ترتیب چیده می‌شود: کار در جریان، کار آماده با مواد کامل به ترتیب نسبت بحرانی، اولویت پروژه، تاریخ نیاز، " +
            "و سپس کارهای منتظر مرحله قبل. بار هر مرکز، تقاضای افق در برابر ظرفیت است و بیش از ۱۰۰٪ گلوگاه شمرده می‌شود. وضعیت زنده ماشین‌ها و آماده‌بودن برنامه CNC هم در پیشنهادها دیده می‌شود. " +
            "این برنامه فقط پیشنهاد است و چیزی را تغییر نمی‌دهد.";

        public MfDispatchService(IUnitOfWork uow, IInventoryUserContext user, IMfMonitorService monitor = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _monitor = monitor;
        }

        public async Task<ResponseDto<DispatchPlanDto>> GetPlanAsync(AiDispatchQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Readers)) return MfText.Bad<DispatchPlanDto>(MfText.NotAllowed);
            int horizon = query?.HorizonDays ?? MfDispatchRules.DefaultHorizonDays;
            if (horizon < 1 || horizon > 60) return MfText.Bad<DispatchPlanDto>("افق برنامه بین ۱ و ۶۰ روز است.");
            return ResponseDtoHelper.Ok(await BuildAsync(Clock().Date, horizon, query?.WorkCenterId, ct));
        }

        public async Task<DispatchPlanDto> BuildAsync(DateTime asOf, int horizonDays, int? workCenterId, CancellationToken ct)
        {
            asOf = asOf.Date;
            MfScheduleData data = await MfScheduler.ComputeAsync(_uow, asOf, ct, allCenters: true);
            var plan = new DispatchPlanDto
            {
                AsOf = asOf, AsOfText = MfText.Date(asOf), GeneratedAtText = MfText.Stamp(Clock()), HorizonDays = horizonDays, OpenOrders = data.Orders.Count,
                LateOrders = data.Orders.Count(o => o.IsLate), Method = Method,
            };

            // item titles and the machines of every centre
            List<int> itemIds = data.Orders.Where(o => o.Order.ItemId.HasValue).Select(o => o.Order.ItemId.Value).Distinct().ToList();
            Dictionary<int, string> items = itemIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Title, ct);
            var machineRows = await (from i in _uow.Set<MfMachineInfo>().AsNoTracking()
                                     join m in _uow.Set<Machine>().AsNoTracking() on i.MachineId equals m.Id
                                     where !i.Deleted && i.WorkCenterId != null && m.IsActive
                                     select new { i.MachineId, WorkCenterId = i.WorkCenterId.Value, m.Title }).ToListAsync(ct);
            Dictionary<int, List<(int Id, string Title)>> machines = machineRows.GroupBy(r => r.WorkCenterId).ToDictionary(g => g.Key, g => g.Select(r => (r.MachineId, r.Title)).OrderBy(r => r.Title, StringComparer.Ordinal).ToList());

            // live machine states and the run share of the last 7 days (only where the machines are monitored)
            Dictionary<int, LiveMachineDto> live = new Dictionary<int, LiveMachineDto>();
            Dictionary<int, decimal?> runShare = new Dictionary<int, decimal?>();
            if (_monitor != null)
            {
                var board = await _monitor.GetLiveAsync(null, ct);
                if (board.Success && board.Data != null) live = board.Data.Machines.Where(m => m.Source != MfMonitorSource.None).ToDictionary(m => m.MachineId);
                DateTime now = Clock();
                var util = await _monitor.GetUtilisationAsync(new UtilisationQuery { From = now.AddDays(-7), To = now }, ct);
                if (util.Success && util.Data != null) runShare = util.Data.ToDictionary(u => u.MachineId, u => u.RunPercent);
            }

            // every open step of every order, with the work the rest of its order still needs
            var steps = new List<(MfDispatchRules.Step Step, MfOrderPlan Order, MfOpPlan Op)>();
            foreach (MfOrderPlan order in data.Orders)
            {
                List<MfOpPlan> open = order.Ops.Where(o => !o.Completed).ToList();
                for (int k = 0; k < open.Count; k++)
                {
                    MfOpPlan p = open[k];
                    if (p.Op.IsOutside || !p.Op.WorkCenterId.HasValue) continue;
                    decimal days = 0;
                    for (int j = k; j < open.Count; j++) days += DaysOf(data, open[j]) + (j > k ? open[j].MoveDays : 0);
                    bool earlierOpen = k > 0;
                    string stage = p.Op.Status == MfOperationStatus.InProgress || p.Op.Status == MfOperationStatus.Paused ? MfDispatchRules.StageRunning
                        : p.Op.Status == MfOperationStatus.QcHold ? null
                        : !earlierOpen && (p.Op.Status == MfOperationStatus.Ready || p.Op.Status == MfOperationStatus.Waiting) ? MfDispatchRules.StageReady
                        : MfDispatchRules.StageUpcoming;
                    if (stage == null) continue;
                    data.Centers.TryGetValue(p.Op.WorkCenterId.Value, out MfCenter center);
                    steps.Add((new MfDispatchRules.Step
                    {
                        OperationId = p.Op.Id, OrderId = order.Order.Id, OrderNumber = order.Order.Number, Sequence = p.Op.Sequence, WorkCenterId = p.Op.WorkCenterId, Stage = stage, HoursLeft = Math.Round(p.Hours, 2),
                        NeedDate = order.Need, ScheduledStart = p.Start, Priority = order.Priority, DaysToNeed = order.Need.HasValue ? WorkingDays(center, asOf, order.Need.Value) : null,
                        DaysOfWork = Math.Round(days, 2), KitShortage = order.KitShortage,
                    }, order, p));
                }
            }

            foreach (MfCenter c in data.Centers.Values.Where(c => c.Wc.Kind != MfWorkCenterKind.Outside && (workCenterId == null || c.Wc.Id == workCenterId)).OrderBy(c => c.Wc.Code, StringComparer.Ordinal))
            {
                decimal capacity = 0, demand = 0;
                for (int d = 0; d < horizonDays; d++)
                {
                    DateTime day = asOf.AddDays(d);
                    capacity += c.Capacity(day);
                    if (data.Demand.TryGetValue((c.Wc.Id, day), out decimal h)) demand += h;
                }
                var mine = steps.Where(s => s.Step.WorkCenterId == c.Wc.Id).ToList();
                List<MfDispatchRules.Step> ordered = MfDispatchRules.Order(mine.Select(s => s.Step));
                List<(int Id, string Title)> wcMachines = machines.TryGetValue(c.Wc.Id, out var ms) ? ms : new List<(int, string)>();
                List<decimal> shares = wcMachines.Where(m => runShare.ContainsKey(m.Id) && runShare[m.Id].HasValue).Select(m => runShare[m.Id].Value).ToList();
                var dto = new DispatchCenterDto
                {
                    WorkCenterId = c.Wc.Id, Code = c.Wc.Code, Title = c.Wc.Title, DemandHours = Math.Round(demand, 1), CapacityHours = Math.Round(capacity, 1), LoadPercent = MfDispatchRules.LoadPercent(demand, capacity),
                    ReadyHours = Math.Round(ordered.Where(s => s.Stage == MfDispatchRules.StageReady).Sum(s => s.HoursLeft), 1), ReadyCount = ordered.Count(s => s.Stage == MfDispatchRules.StageReady),
                    RunningCount = ordered.Count(s => s.Stage == MfDispatchRules.StageRunning), MeasuredRunPercent = shares.Count > 0 ? Math.Round(shares.Average(), 1) : null,
                    Machines = wcMachines.Select(m => m.Title).ToList(),
                };
                dto.Level = MfDispatchRules.Level(demand, capacity, dto.ReadyCount, dto.RunningCount);
                int rank = 0, checkedPrograms = 0;
                foreach (MfDispatchRules.Step s in ordered)
                {
                    rank++;
                    var src = mine.First(x => x.Step.OperationId == s.OperationId);
                    decimal? cr = MfDispatchRules.CriticalRatio(s.DaysToNeed, s.DaysOfWork);
                    var item = new DispatchItemDto
                    {
                        Rank = rank, OperationId = s.OperationId, OrderId = s.OrderId, OrderNumber = s.OrderNumber, Sequence = s.Sequence, Name = MfNames.Operation(s.OrderNumber, s.Sequence), Title = src.Op.Op.Title,
                        ItemTitle = src.Order.Order.ItemId.HasValue && items.TryGetValue(src.Order.Order.ItemId.Value, out string it) ? it : null, Stage = s.Stage, HoursLeft = s.HoursLeft,
                        NeedDate = s.NeedDate, NeedDateText = MfText.Date(s.NeedDate), ScheduledStart = s.ScheduledStart, ScheduledStartText = MfText.Date(s.ScheduledStart), CriticalRatio = cr,
                        SlackDays = MfDispatchRules.Slack(s.DaysToNeed, s.DaysOfWork), KitShortage = s.KitShortage, Risk = MfDispatchRules.Risk(cr), Reason = MfDispatchRules.Reason(s, rank),
                    };
                    if (s.Stage == MfDispatchRules.StageReady && checkedPrograms < ProgramChecks && wcMachines.Count > 0)
                    {
                        checkedPrograms++;
                        (item.ProgramReady, item.ProgramNote) = await ProgramReadinessAsync(src.Op.Op, wcMachines, ct);
                    }
                    if (item.Risk == MfDispatchRules.RiskLate && s.Stage != MfDispatchRules.StageUpcoming) dto.LateRiskCount++;
                    if (dto.Queue.Count < MaxQueue) dto.Queue.Add(item);
                }
                plan.Centers.Add(dto);
            }
            plan.LateRiskOperations = plan.Centers.Sum(c => c.LateRiskCount);
            DispatchCenterDto top = plan.Centers.Where(c => c.LoadPercent.HasValue).OrderByDescending(c => c.LoadPercent).FirstOrDefault();
            if (top != null && top.LoadPercent > MfDispatchRules.BusyPercent)
                plan.BottleneckText = $"{(top.LoadPercent > MfDispatchRules.BottleneckPercent ? "گلوگاه" : "پربارترین مرکز")}: {top.Title} ({top.Code}) با {MfDispatchRules.Fa(top.LoadPercent.Value)}٪ ظرفیت در {MfDispatchRules.Fa(horizonDays)} روز آینده.";
            plan.Suggestions = Suggest(plan, live, machines);
            return plan;
        }

        /// <summary>Working days of the centre to finish the step at one day of full capacity per day (at least a tenth of a day for an open step).</summary>
        private static decimal DaysOf(MfScheduleData data, MfOpPlan p)
        {
            if (p.Op.IsOutside) return Math.Max(p.Op.OutsideLeadDays, 0);
            if (!p.Op.WorkCenterId.HasValue || !data.Centers.TryGetValue(p.Op.WorkCenterId.Value, out MfCenter c)) return Math.Max(p.Hours / 8m, 0.1m);
            decimal perDay = c.Gross * c.Factor;
            return perDay > 0 ? Math.Max(p.Hours / perDay, 0.1m) : Math.Max(p.Hours / 8m, 0.1m);
        }

        /// <summary>Working days of the centre's calendar from today to the need date (today counts; 0 when the date has passed).</summary>
        private static decimal WorkingDays(MfCenter center, DateTime from, DateTime to)
        {
            if (to.Date < from.Date) return -(decimal)(from.Date - to.Date).Days;
            int n = 0;
            for (DateTime d = from.Date; d <= to.Date && n < 2000; d = d.AddDays(1))
                if (center?.Calendar == null || center.Calendar.IsWorking(d)) n++;
            return n;
        }

        /// <summary>Is the CNC program of the step ready (approved, sent and confirmed) on a machine of the centre? Null when the step has no CNC program.</summary>
        private async Task<(bool?, string)> ProgramReadinessAsync(MfOperation op, List<(int Id, string Title)> machines, CancellationToken ct)
        {
            bool any = false;
            foreach (var m in machines)
            {
                List<MfNcProgram> programs = await MfDncGate.ProgramsForAsync(_uow, op, m.Id, ct);
                if (programs.Count == 0) continue;
                any = true;
                var gate = await MfDncGate.CheckStartAsync(_uow, op, m.Id, MfBookingKind.Run, ct);
                bool confirmedSomewhere = gate.Error == null && gate.Runs.Count > 0 && gate.Runs.All(r => r.TransferId.HasValue);
                if (confirmedSomewhere) return (true, "برنامه CNC روی " + m.Title + " تأیید شده است.");
            }
            return any ? (false, "برنامه CNC این کار هنوز روی ماشین تأیید نشده است؛ پیش از نوبت ارسال و تأیید شود.") : (null, null);
        }

        /// <summary>The proposals, most urgent first (at most 12).</summary>
        private static List<DispatchSuggestionDto> Suggest(DispatchPlanDto plan, Dictionary<int, LiveMachineDto> live, Dictionary<int, List<(int Id, string Title)>> machines)
        {
            var list = new List<DispatchSuggestionDto>();
            foreach (DispatchCenterDto c in plan.Centers)
            {
                List<(int Id, string Title)> ms = machines.TryGetValue(c.WorkCenterId, out var x) ? x : new List<(int, string)>();
                if (c.Level == MfDispatchRules.LevelBottleneck && c.DemandHours > 0)
                {
                    decimal extra = Math.Max(0, c.DemandHours - c.CapacityHours);
                    list.Add(new DispatchSuggestionDto
                    {
                        Kind = "bottleneck", Severity = 1, WorkCenterId = c.WorkCenterId, Link = "/production/dispatch?wc=" + c.WorkCenterId,
                        Title = $"گلوگاه: {c.Title}",
                        Text = c.CapacityHours > 0
                            ? $"{c.Title} در {MfDispatchRules.Fa(plan.HorizonDays)} روز آینده {MfDispatchRules.Fa(c.DemandHours)} ساعت کار دارد ولی {MfDispatchRules.Fa(c.CapacityHours)} ساعت ظرفیت ({MfDispatchRules.Fa(c.LoadPercent ?? 0)}٪). برای {MfDispatchRules.Fa(extra)} ساعت اضافه: اضافه‌کار یا شیفت دوم، انتقال بخشی از کار به مرکز هم‌توان، یا برون‌سپاری را بررسی کنید."
                            : $"{c.Title} در {MfDispatchRules.Fa(plan.HorizonDays)} روز آینده ظرفیتی ندارد (تعطیل یا توقف) ولی {MfDispatchRules.Fa(c.DemandHours)} ساعت کار دارد.",
                    });
                }
                foreach (DispatchItemDto i in c.Queue.Where(i => i.Risk == MfDispatchRules.RiskLate && i.Stage == MfDispatchRules.StageReady).Take(3))
                {
                    list.Add(new DispatchSuggestionDto
                    {
                        Kind = "late-risk", Severity = 1, WorkCenterId = c.WorkCenterId, OperationId = i.OperationId, Link = "/production/orders/" + i.OrderId,
                        Title = $"خطر تأخیر: {i.Name}",
                        Text = $"{i.Name} ({i.Title}) در {c.Title}: نسبت بحرانی {MfDispatchRules.Fa(i.CriticalRatio ?? 0)}. پیشنهاد: در نوبت {MfDispatchRules.Fa(i.Rank)} صف این مرکز اجرا شود" + (i.KitShortage ? "؛ اما کیت مواد کامل نیست و ابتدا باید تکمیل شود." : "."),
                    });
                }
                foreach (var m in ms)
                {
                    if (!live.TryGetValue(m.Id, out LiveMachineDto lm) || lm.State == null) continue;
                    if ((lm.State == MfMachineState.Alarm || lm.State == MfMachineState.Offline) && c.ReadyCount + c.RunningCount > 0)
                        list.Add(new DispatchSuggestionDto
                        {
                            Kind = "machine-down", Severity = 1, WorkCenterId = c.WorkCenterId, MachineId = m.Id, Link = "/production/monitor",
                            Title = $"{m.Title} {(lm.State == MfMachineState.Alarm ? "در آلارم" : "بدون ارتباط")}",
                            Text = $"{m.Title} {(lm.State == MfMachineState.Alarm ? "در آلارم است" + (string.IsNullOrWhiteSpace(lm.AlarmText) ? "" : " (" + lm.AlarmText + ")") : "پاسخ نمی‌دهد")} و {MfDispatchRules.Fa(c.ReadyCount)} کار آماده در صف {c.Title} است. پیشنهاد: نگهداری فوری، یا اجرای کارهای صف روی ماشین دیگر این مرکز.",
                        });
                    else if (lm.State == MfMachineState.Idle && c.ReadyCount > 0 && lm.MinutesInState >= 15)
                    {
                        DispatchItemDto next = c.Queue.FirstOrDefault(i => i.Stage == MfDispatchRules.StageReady);
                        list.Add(new DispatchSuggestionDto
                        {
                            Kind = "machine-idle", Severity = 2, WorkCenterId = c.WorkCenterId, MachineId = m.Id, OperationId = next?.OperationId, Link = "/production/terminal",
                            Title = $"{m.Title} بیکار است",
                            Text = $"{m.Title} {MfDispatchRules.Fa(lm.MinutesInState)} دقیقه است بیکار است در حالی که {MfDispatchRules.Fa(c.ReadyCount)} کار آماده دارد" + (next != null ? $"؛ کار بعدی پیشنهادی: {next.Name} ({next.Title})." : "."),
                        });
                    }
                }
                foreach (DispatchItemDto i in c.Queue.Where(i => i.Stage == MfDispatchRules.StageReady && i.ProgramReady == false).Take(2))
                    list.Add(new DispatchSuggestionDto
                    {
                        Kind = "program", Severity = 2, WorkCenterId = c.WorkCenterId, OperationId = i.OperationId, Link = "/production/dnc",
                        Title = $"برنامه CNC آماده نیست: {i.Name}",
                        Text = $"{i.Name} در نوبت {MfDispatchRules.Fa(i.Rank)} صف {c.Title} است ولی برنامه CNC آن هنوز روی ماشین تأیید نشده است؛ پیش از رسیدن نوبت، نسخه جاری را ارسال کنید تا اپراتور تأیید کند.",
                    });
                foreach (DispatchItemDto i in c.Queue.Where(i => i.Stage == MfDispatchRules.StageReady && i.KitShortage && i.Risk != MfDispatchRules.RiskLate).Take(1))
                    list.Add(new DispatchSuggestionDto
                    {
                        Kind = "kit", Severity = 3, WorkCenterId = c.WorkCenterId, OperationId = i.OperationId, Link = "/production/orders/" + i.OrderId,
                        Title = $"کیت مواد ناقص: {i.Name}", Text = $"مواد {i.Name} کامل نیست؛ این کار پشت کارهای آماده دیگر قرار گرفت. پیگیری انبار و خرید پیشنهاد می‌شود.",
                    });
            }
            return list.OrderBy(s => s.Severity).ThenBy(s => KindRank(s.Kind)).Take(12).ToList();
        }

        private static int KindRank(string kind) => kind switch { "machine-down" => 0, "bottleneck" => 1, "late-risk" => 2, "machine-idle" => 3, "program" => 4, _ => 5 };

        // ================================================================================== shop-floor overview

        public async Task<ResponseDto<FloorOverviewDto>> GetFloorAsync(bool remote, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Readers)) return MfText.Bad<FloorOverviewDto>(MfText.NotAllowed);
            DateTime now = Clock();
            DispatchPlanDto plan = await BuildAsync(now.Date, MfDispatchRules.DefaultHorizonDays, null, ct);
            var dto = new FloorOverviewDto
            {
                AsOfText = MfText.Stamp(now), Remote = remote, Suggestions = plan.Suggestions.Take(6).ToList(), BottleneckText = plan.BottleneckText,
                Centers = plan.Centers.Where(c => c.DemandHours > 0 || c.ReadyCount > 0 || c.RunningCount > 0).OrderByDescending(c => c.LoadPercent ?? 0).Take(8)
                    .Select(c => new DispatchCenterDto
                    {
                        WorkCenterId = c.WorkCenterId, Code = c.Code, Title = c.Title, DemandHours = c.DemandHours, CapacityHours = c.CapacityHours, LoadPercent = c.LoadPercent, ReadyHours = c.ReadyHours,
                        ReadyCount = c.ReadyCount, RunningCount = c.RunningCount, LateRiskCount = c.LateRiskCount, Level = c.Level, MeasuredRunPercent = c.MeasuredRunPercent, Machines = c.Machines,
                        Queue = c.Queue.Where(q => q.Stage != MfDispatchRules.StageUpcoming).Take(3).ToList(),
                    }).ToList(),
                GreetingText = now.Hour < 12 ? "صبح بخیر" : now.Hour < 17 ? "روز بخیر" : "عصر بخیر",
            };
            if (_monitor != null)
            {
                var board = await _monitor.GetLiveAsync(null, ct);
                if (board.Success) dto.Live = board.Data;
            }
            LiveBoardDto lb = dto.Live;
            int monitored = lb?.Machines.Count(m => m.Source != MfMonitorSource.None) ?? 0;
            List<decimal> today = lb?.Machines.Where(m => m.TodayRunPercent.HasValue).Select(m => m.TodayRunPercent.Value).ToList() ?? new List<decimal>();
            decimal avg = today.Count > 0 ? Math.Round(today.Average(), 0) : 0;
            dto.Tiles.Add(new FloorTileDto
            {
                Key = "running", Title = "ماشین در حال کار", Value = lb?.Running ?? 0, ValueText = monitored > 0 ? $"{MfDispatchRules.Fa(lb.Running)} از {MfDispatchRules.Fa(monitored)}" : "—",
                Hint = monitored > 0 ? "از چراغ برج ماشین‌ها" : "پایش ماشین‌ها هنوز راه‌اندازی نشده است", Tone = monitored == 0 ? "neutral" : lb.Running > 0 ? "good" : "warn", Link = "/production/monitor",
            });
            dto.Tiles.Add(new FloorTileDto
            {
                Key = "alarms", Title = "آلارم یا قطع ارتباط", Value = (lb?.Alarm ?? 0) + (lb?.Offline ?? 0), ValueText = MfDispatchRules.Fa((lb?.Alarm ?? 0) + (lb?.Offline ?? 0)),
                Hint = (lb?.Alarm ?? 0) > 0 ? "نیاز به نگهداری فوری" : "همه ماشین‌ها بدون آلارم", Tone = (lb?.Alarm ?? 0) + (lb?.Offline ?? 0) > 0 ? "bad" : "good", Link = "/production/monitor",
            });
            dto.Tiles.Add(new FloorTileDto
            {
                Key = "utilisation", Title = "کارکرد امروز", Value = avg, ValueText = today.Count > 0 ? MfDispatchRules.Fa(avg) + "٪" : "—", Hint = "میانگین سهم کار واقعی ماشین‌ها از امروز",
                Tone = today.Count == 0 ? "neutral" : avg >= 60 ? "good" : avg >= 35 ? "warn" : "bad", Link = "/production/monitor",
            });
            dto.Tiles.Add(new FloorTileDto
            {
                Key = "late", Title = "سفارش دیرکرد", Value = plan.LateOrders, ValueText = $"{MfDispatchRules.Fa(plan.LateOrders)} از {MfDispatchRules.Fa(plan.OpenOrders)}",
                Hint = plan.LateRiskOperations > 0 ? MfDispatchRules.Fa(plan.LateRiskOperations) + " مرحله آماده در خطر تأخیر" : "مرحله آماده‌ای در خطر تأخیر نیست", Tone = plan.LateOrders > 0 ? "bad" : plan.LateRiskOperations > 0 ? "warn" : "good",
                Link = "/production/dispatch",
            });

            // what waits for a person
            int revisions = await _uow.Set<MfNcRevision>().CountAsync(r => r.Status == MfNcRevisionStatus.Submitted, ct);
            int failed = await _uow.Set<MfNcTransfer>().CountAsync(t => !t.Deleted && t.Status == MfNcTransferStatus.Failed, ct);
            int checklists = await _uow.Set<MfChecklistResult>().CountAsync(r => !r.Deleted && r.Status == MfChecklistStatus.Submitted, ct);
            int staff = _user.IsInRole("HR,PRODUCTIONMANAGER") ? await _uow.Set<HrPerformanceEvent>().CountAsync(e => !e.Deleted && e.Status == HrEventStatus.Submitted, ct) : 0;
            int maintenance = await _uow.Set<MfMaintenanceRequest>().CountAsync(r => !r.Deleted && (r.Status == MfMaintenanceRequestStatus.Open || r.Status == MfMaintenanceRequestStatus.InProgress), ct);
            int qcHold = await _uow.Set<MfOperation>().CountAsync(o => !o.Deleted && o.Status == MfOperationStatus.QcHold, ct);
            void Wait(string key, string title, int count, string link, string tone) { if (count > 0) dto.Waiting.Add(new FloorWaitingDto { Key = key, Title = title, Count = count, Link = link, Tone = tone }); }
            Wait("nc-revisions", "نسخه برنامه CNC منتظر تأیید", revisions, "/production/dnc", "warn");
            Wait("nc-failed", "انتقال ناموفق برنامه به ماشین", failed, "/production/dnc/transfers", "bad");
            Wait("checklists", "چک‌لیست منتظر بازبینی", checklists, "/production/orders", "warn");
            Wait("qc-hold", "مرحله منتظر بازرسی کیفی", qcHold, "/production/orders", "warn");
            Wait("maintenance", "درخواست تعمیر باز", maintenance, "/production/maintenance", "bad");
            Wait("staff", "رویداد کارکنان منتظر تصمیم", staff, "/production/staff-events", "neutral");
            return ResponseDtoHelper.Ok(dto);
        }
    }
}
