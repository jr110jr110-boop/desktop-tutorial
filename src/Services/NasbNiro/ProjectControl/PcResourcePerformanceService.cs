using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Production;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    public interface IPcResourcePerformanceService
    {
        /// <summary>Priority 4d: the budget and performance of the resources of a project: baseline, planned value to the data date, current plan, actuals and variance, per resource and per class.</summary>
        Task<ResponseDto<PcResourcePerformanceDto>> GetPerformanceAsync(int contractId, DateTime? asOf, CancellationToken cancellationToken);

        /// <summary>The planning agent only (step RES.COST.PROPOSE): prepares the proposed cost-baseline lines by resource class from the newest approved resource plan, for a project whose cost baseline can still be edited.</summary>
        Task<ResponseDto<int>> ProposeCostAsync(int contractId, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PcResourceCostProposalDto>>> GetProposalsAsync(int? contractId, PcCostProposalStatus? status, int pageNumber, int pageSize, CancellationToken cancellationToken);

        /// <summary>The owner of the cost baseline accepts (the line is written through the existing cost-baseline path) or rejects a proposal.</summary>
        Task<ResponseDto<PcResourceCostProposalDto>> DecideProposalAsync(PcCostProposalDecideDto dto, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Priority 4d: actual cost and performance of the resources of a project, and the proposal of the cost-baseline lines. Read only on every other module: it never changes an earned-value number or a cost-baseline line by itself.
    /// An amount that could show one person's cost is hidden (the budget view's rule), actuals are aggregates, and what the data model does not tie is reported as not tied, never estimated.
    /// </summary>
    public class PcResourcePerformanceService : IPcResourcePerformanceService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IPcBaselineService _baselines;

        public PcResourcePerformanceService(IUnitOfWork uow, IInventoryUserContext user, IPcBaselineService baselines)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _baselines = baselines ?? throw new ArgumentNullException(nameof(baselines));
        }

        private int Me => _user.UserId;
        private static ResponseDto<T> Bad<T>(string m) => PcText.Bad<T>(m);
        private static ResponseDto<T> NotFound<T>(string m) => PcText.NotFound<T>(m);
        private static decimal R2(decimal v) => PcResourceRules.Round2(v);

        // ============================================================================================== performance

        private sealed class Line
        {
            public int ResourceId;
            public decimal BaselineQty, PvQty, PlanQty;
            public decimal BaselineAmount, PvAmount, PlanAmount;
            public bool BaselineHasRate, PlanHasRate, BaselineAny, PlanAny;
        }

        public async Task<ResponseDto<PcResourcePerformanceDto>> GetPerformanceAsync(int contractId, DateTime? asOf, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PcResourceRules.Readers)) return Bad<PcResourcePerformanceDto>(PcResourceText.NotAllowed);
            if (!await _uow.Set<ContractEntity>().AsNoTracking().AnyAsync(c => c.Id == contractId && !c.Deleted, cancellationToken)) return NotFound<PcResourcePerformanceDto>(PcText.ContractNotFound);
            DateTime day = (asOf ?? DateTime.Today).Date;
            bool sees = MfPrivacy.SeesPayroll(_user);
            PcContractTerms terms = await PcContext.TermsAsync(_uow, contractId, false, cancellationToken);
            var dto = new PcResourcePerformanceDto { ContractId = contractId, AsOf = day };

            // the baseline rows, spread over the frozen dates of their activities (time-phased planned value)
            var baseline = terms?.CurrentBaselineId == null ? null : await _uow.Set<PcBaseline>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == terms.CurrentBaselineId.Value && !b.Deleted, cancellationToken);
            List<PcBaselineResource> bRows = baseline == null ? new List<PcBaselineResource>() : await _uow.Set<PcBaselineResource>().AsNoTracking().Where(x => x.BaselineId == baseline.Id).OrderBy(x => x.ActivityId).ThenBy(x => x.ResourceId).ToListAsync(cancellationToken);
            dto.HasBaseline = baseline != null && bRows.Count > 0; dto.BaselineId = baseline?.Id; dto.BaselineNumber = baseline?.Number;
            PcResourcePlan plan = await PcResourceData.LatestApprovedAsync(_uow, contractId, false, cancellationToken);
            List<PcActivityResource> pRows = plan == null ? new List<PcActivityResource>() : await _uow.Set<PcActivityResource>().AsNoTracking().Where(x => x.PlanId == plan.Id && !x.Activity.Deleted).OrderBy(x => x.ActivityId).ThenBy(x => x.ResourceId).ToListAsync(cancellationToken);
            dto.PlanId = plan?.Id; dto.PlanVersion = plan?.Version;

            int[] ids = bRows.Select(r => r.ResourceId).Concat(pRows.Select(r => r.ResourceId)).Distinct().OrderBy(i => i).ToArray();
            Dictionary<int, PcResource> resources = ids.Length == 0 ? new Dictionary<int, PcResource>() : await _uow.Set<PcResource>().AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
            Dictionary<int, decimal?> rates = await PcResourceData.EffectiveRatesAsync(_uow, ids, day, cancellationToken);
            bool Hidden(PcResource r) => !sees && PcResourceRules.IsSensitive(r);

            var lines = ids.ToDictionary(i => i, i => new Line { ResourceId = i });
            var periods = new Dictionary<(string Key, PcResourceClass Class), (decimal Qty, decimal Amount, DateTime From, DateTime To)>();
            if (baseline != null && bRows.Count > 0)
            {
                Dictionary<int, PcBaselineActivity> acts = (await _uow.Set<PcBaselineActivity>().AsNoTracking().Where(a => a.BaselineId == baseline.Id).ToListAsync(cancellationToken)).GroupBy(a => a.ActivityId).ToDictionary(g => g.Key, g => g.First());
                var calendars = new Dictionary<int, PcWorkCalendar>();
                PcWorkCalendar contractCalendar = await PcContext.CalendarForContractAsync(_uow, terms, cancellationToken);
                foreach (PcBaselineResource r in bRows)
                {
                    if (!resources.TryGetValue(r.ResourceId, out PcResource res) || !acts.TryGetValue(r.ActivityId, out PcBaselineActivity act)) continue;
                    PcWorkCalendar cal = contractCalendar;
                    if (act.CalendarId.HasValue)
                    {
                        if (!calendars.TryGetValue(act.CalendarId.Value, out cal)) { cal = await PcContext.CalendarAsync(_uow, act.CalendarId.Value, cancellationToken); calendars[act.CalendarId.Value] = cal; }
                    }
                    bool priced = r.RateAtBaseline.HasValue;
                    bool visible = priced && !Hidden(res);
                    decimal amount = priced ? R2(r.Quantity * r.RateAtBaseline.Value) : 0m;
                    Line line = lines[r.ResourceId];
                    line.BaselineAny = true; line.BaselineQty += r.Quantity;
                    if (priced) line.BaselineHasRate = true;
                    if (visible) line.BaselineAmount += amount;
                    PcResourceClass cls = PcResourceRules.ClassOf(res.Kind);
                    foreach (var d in PcPvRules.Phase(cal, act.Start, act.Finish, r.Quantity, amount, r.Distribution))
                    {
                        string key = PcPvRules.PeriodKey(d.Day);
                        var range = PcPvRules.PeriodRange(d.Day);
                        periods.TryGetValue((key, cls), out var cur);
                        periods[(key, cls)] = (cur.Qty + d.Quantity, cur.Amount + (visible ? d.Amount : 0m), range.From, range.To);
                        if (d.Day <= day) { line.PvQty += d.Quantity; if (visible) line.PvAmount += d.Amount; }
                    }
                }
            }
            foreach (PcActivityResource r in pRows)
            {
                if (!resources.TryGetValue(r.ResourceId, out PcResource res)) continue;
                Line line = lines[r.ResourceId];
                line.PlanAny = true; line.PlanQty += r.Quantity;
                decimal? rate = rates.TryGetValue(r.ResourceId, out decimal? v) ? v : null;
                if (rate.HasValue) { line.PlanHasRate = true; if (!Hidden(res)) line.PlanAmount += R2(r.Quantity * rate.Value); }
            }

            PcResourceActuals.Result actual = await PcResourceActuals.LoadAsync(_uow, contractId, day, cancellationToken);
            var rows = new List<PcPerfRowDto>();
            var centreTaken = new HashSet<int>();
            var providerTaken = new HashSet<int>();
            foreach (int id in ids)
            {
                PcResource res = resources[id];
                Line line = lines[id];
                bool hidden = Hidden(res);
                bool noRate = (line.BaselineAny && !line.BaselineHasRate) || (line.PlanAny && !line.PlanHasRate);
                var row = new PcPerfRowDto
                {
                    ResourceId = id, ResourceCode = res.Code, ResourceTitle = res.Title, ResourceClass = PcResourceRules.ClassOf(res.Kind), Unit = res.Unit,
                    BaselineQuantity = line.BaselineQty, PlannedToDateQuantity = R4(line.PvQty), PlanQuantity = line.PlanQty, NoApprovedRate = noRate, RateHidden = hidden,
                    BaselineAmount = line.BaselineHasRate && !hidden ? line.BaselineAmount : (decimal?)null,
                    PlannedToDateAmount = line.BaselineHasRate && !hidden ? line.PvAmount : (decimal?)null,
                    PlanAmount = line.PlanHasRate && !hidden ? line.PlanAmount : (decimal?)null,
                    Note = noRate ? "بدون نرخ مصوب" : null,
                };
                if (row.PlanAmount.HasValue && row.BaselineAmount.HasValue) row.PlanVsBaselineAmount = row.PlanAmount.Value - row.BaselineAmount.Value;
                decimal? rate = rates.TryGetValue(id, out decimal? rv) ? rv : null;
                if (res.Kind == PcResourceKind.Machine && res.WorkCenterId.HasValue && res.Unit == PcResourceUnit.Hour)
                {
                    if (centreTaken.Add(res.WorkCenterId.Value))
                    {
                        decimal hours = actual.MachineHoursByCentre.TryGetValue(res.WorkCenterId.Value, out decimal h) ? h : 0m;
                        row.ActualQuantity = hours; row.ActualBasis = "ساعت ثبت‌شدهٔ ترمینال تولید روی مرکز کار وصل‌شده";
                        row.ActualAmount = rate.HasValue ? R2(hours * rate.Value) : (decimal?)null;
                    }
                    else row.ActualNote = "ساعت این مرکز کار در منبع دیگری که به آن وصل است شمرده شده است.";
                }
                else if (res.Kind == PcResourceKind.Subcontract && res.ProviderId.HasValue)
                {
                    if (providerTaken.Add(res.ProviderId.Value))
                    {
                        row.ActualAmount = actual.OrderedByProvider.TryGetValue(res.ProviderId.Value, out decimal a) ? a : 0m;
                        row.ActualBasis = "مبلغ سفارش‌های خرید صادرشدهٔ همین پروژه و همین تأمین‌کننده (تعهد، نه مقدار × نرخ)";
                    }
                    else row.ActualNote = "سفارش‌های این تأمین‌کننده در منبع دیگری که به آن وصل است شمرده شده است.";
                }
                else if (res.Kind == PcResourceKind.Labour) row.ActualNote = "ساعت کار ثبت‌شده به منبع نقش وصل نیست؛ فقط در سطح رده آمده است.";
                else if (res.Kind == PcResourceKind.Material) row.ActualNote = "خروج انبار به منبع مصرفی وصل نیست؛ فقط در سطح رده آمده است.";
                else row.ActualNote = "منبع به مرکز کار یا تأمین‌کننده وصل نیست؛ مقدار واقعی ندارد.";
                if (row.ActualQuantity.HasValue) row.VarianceQuantity = R4(row.ActualQuantity.Value - row.PlannedToDateQuantity);
                if (row.ActualAmount.HasValue && row.PlannedToDateAmount.HasValue) row.VarianceAmount = row.ActualAmount.Value - row.PlannedToDateAmount.Value;
                rows.Add(row);
            }
            dto.Rows = rows;

            // per class: the amounts of the lines the viewer may see; the quantities only where every line of the class is counted in one unit
            foreach (PcResourceClass cls in new[] { PcResourceClass.Labour, PcResourceClass.Equipment, PcResourceClass.Material, PcResourceClass.Subcontract })
            {
                List<PcPerfRowDto> inClass = rows.Where(r => r.ResourceClass == cls).ToList();
                bool anyActual = cls == PcResourceClass.Labour || cls == PcResourceClass.Material || inClass.Any(r => r.ActualAmount.HasValue || r.ActualQuantity.HasValue);
                if (inClass.Count == 0 && !(cls == PcResourceClass.Material && actual.IssuedCost != 0m) && !(cls == PcResourceClass.Labour && actual.BookedHours != 0m)) continue;
                PcResourceUnit? unit = inClass.Count > 0 && inClass.Select(r => r.Unit).Distinct().Count() == 1 ? inClass[0].Unit : null;
                var c = new PcPerfRowDto
                {
                    ResourceClass = cls, Unit = unit, BaselineQuantity = unit.HasValue ? inClass.Sum(r => r.BaselineQuantity) : 0m, PlannedToDateQuantity = unit.HasValue ? inClass.Sum(r => r.PlannedToDateQuantity) : 0m,
                    PlanQuantity = unit.HasValue ? inClass.Sum(r => r.PlanQuantity) : 0m, NoApprovedRate = inClass.Any(r => r.NoApprovedRate), RateHidden = inClass.Any(r => r.RateHidden),
                    BaselineAmount = inClass.Any(r => r.BaselineAmount.HasValue) ? inClass.Sum(r => r.BaselineAmount ?? 0m) : (decimal?)null,
                    PlannedToDateAmount = inClass.Any(r => r.PlannedToDateAmount.HasValue) ? inClass.Sum(r => r.PlannedToDateAmount ?? 0m) : (decimal?)null,
                    PlanAmount = inClass.Any(r => r.PlanAmount.HasValue) ? inClass.Sum(r => r.PlanAmount ?? 0m) : (decimal?)null,
                };
                if (cls == PcResourceClass.Labour)
                {
                    // the hours of the whole project are aggregated; a group smaller than the protected size is not shown to a reader who may not see payroll
                    bool small = actual.BookedEmployees < MfLegacyLabour.MinEmployees && actual.BookedHours != 0m;
                    c.ActualBasis = "ساعت ثبت‌شدهٔ ترمینال تولید برای پروژه (جمع، نه به تفکیک فرد)";
                    if (small && !sees) c.ActualNote = "ساعت کار گروهی کمتر از حد محرمانگی (" + MfLegacyLabour.MinEmployees.ToString(CultureInfo.InvariantCulture) + " نفر) است و برای شما نمایش داده نمی‌شود.";
                    else { c.ActualQuantity = actual.BookedHours; c.ActualNote = "مبلغ ساعت کار محاسبه نمی‌شود: ساعت ثبت‌شده به منبع نقش و نرخ مصوب آن وصل نیست."; }
                }
                else if (cls == PcResourceClass.Material)
                {
                    c.ActualAmount = actual.IssuedCost; c.ActualBasis = "ارزش بهای خروج‌های تاییدشدهٔ انبار برای همین پروژه (بهای انبار، نه مقدار × نرخ مصوب)";
                    c.ActualNote = "خروج انبار به منبع مصرفی استخر وصل نیست.";
                }
                else if (anyActual)
                {
                    c.ActualQuantity = inClass.Any(r => r.ActualQuantity.HasValue) ? inClass.Sum(r => r.ActualQuantity ?? 0m) : (decimal?)null;
                    c.ActualAmount = inClass.Any(r => r.ActualAmount.HasValue) ? inClass.Sum(r => r.ActualAmount ?? 0m) : (decimal?)null;
                }
                if (c.ActualQuantity.HasValue && unit.HasValue && cls != PcResourceClass.Labour) c.VarianceQuantity = R4(c.ActualQuantity.Value - c.PlannedToDateQuantity);
                if (c.ActualAmount.HasValue && c.PlannedToDateAmount.HasValue) c.VarianceAmount = c.ActualAmount.Value - c.PlannedToDateAmount.Value;
                if (c.PlanAmount.HasValue && c.BaselineAmount.HasValue) c.PlanVsBaselineAmount = c.PlanAmount.Value - c.BaselineAmount.Value;
                dto.ByClass.Add(c);
            }

            dto.Periods = periods.OrderBy(p => p.Key.Key, StringComparer.Ordinal).ThenBy(p => (int)p.Key.Class).Select(p => new PcPerfPeriodDto
            {
                Key = p.Key.Key, Text = PcPvRules.Digits(p.Key.Key), From = p.Value.From, To = p.Value.To, ResourceClass = p.Key.Class, Quantity = p.Value.Qty, Amount = p.Value.Amount,
            }).ToList();
            dto.BaselineTotal = rows.Sum(r => r.BaselineAmount ?? 0m);
            dto.PlannedToDateTotal = rows.Sum(r => r.PlannedToDateAmount ?? 0m);
            dto.PlanTotal = rows.Sum(r => r.PlanAmount ?? 0m);
            dto.ActualTotal = dto.ByClass.Sum(r => r.ActualAmount ?? 0m);
            dto.NoRateCount = rows.Count(r => r.NoApprovedRate);
            dto.HiddenLineCount = rows.Count(r => r.RateHidden);
            dto.RateHidden = dto.HiddenLineCount > 0;
            // a group smaller than the protected size is never shown as a number to a reader who may not see payroll: not in the rows and not in the notes
            bool bookedSmall = actual.BookedEmployees < MfLegacyLabour.MinEmployees && actual.BookedHours != 0m;
            bool attendanceSmall = actual.AttendanceEmployees < MfLegacyLabour.MinEmployees && actual.AttendanceHours != 0m;
            string bookedText = bookedSmall && !sees ? "عدد برای شما نمایش داده نمی‌شود (گروه کوچک)" : actual.BookedHours.ToString("0.##", CultureInfo.InvariantCulture) + " ساعت";
            string attendanceText = attendanceSmall && !sees ? "عدد برای شما نمایش داده نمی‌شود (گروه کوچک)" : actual.AttendanceHours.ToString("0.##", CultureInfo.InvariantCulture) + " ساعت";
            dto.Ties = new List<PcPerfTieDto>
            {
                new PcPerfTieDto { Source = "ساعت ثبت‌شدهٔ ترمینال تولید", Tied = true, Note = "به پروژه از راه عملیات و دستور تولید وصل است (" + bookedText + "). به منبع نقش، فعالیت برنامه و منبع استخر وصل نیست؛ فقط ساعت ماشین مرکز کارِ وصل‌شده به منبع ماشین تا سطح منبع می‌رسد." },
                new PcPerfTieDto { Source = "برگهٔ حضور ماهانه", Tied = true, Note = "ساعت حضور روی دستورکارهای پروژه (" + attendanceText + ") فقط جمع پروژه است؛ در محاسبهٔ مبلغ و انحراف نمی‌آید." },
                new PcPerfTieDto { Source = "ساعت ماشین", Tied = true, Note = "از ساعت ثبت‌شده روی مرکز کار وصل‌شده به منبع ماشین، در نرخ مصوب منبع." },
                new PcPerfTieDto { Source = "سفارش خرید پیمانکاری و خدمات", Tied = true, Note = "از قرارداد و تأمین‌کنندهٔ سفارش به منبع پیمانکار وصل است؛ مبلغ سفارش (تعهد) است، مقدار × نرخ نیست." },
                new PcPerfTieDto { Source = "خروج انبار", Tied = true, Note = "به پروژه وصل است؛ به منبع مصرفی استخر وصل نیست، فقط ارزش بهای انبار در سطح رده می‌آید." },
                new PcPerfTieDto { Source = "هزینهٔ ساعت کار نیروی انسانی", Tied = false, Note = "نرخ مصوب نقش به ساعت ثبت‌شده وصل نیست، پس مبلغ واقعی نیروی انسانی محاسبه نمی‌شود (و هرگز از حقوق فرد محاسبه نمی‌شود)." },
            };
            return ResponseDtoHelper.Ok(dto);
        }

        private static decimal R4(decimal v) => PcResourceRules.Round4(v);

        // ============================================================================================== the cost-baseline proposal (agent) and its decision (human)

        private static PcResourceCostProposalDto ToDto(PcResourceCostProposal p, string contractCode) => new PcResourceCostProposalDto
        {
            Id = p.Id, ContractId = p.ContractId, ContractCode = contractCode, PlanId = p.PlanId, PlanVersion = p.PlanVersion, ResourceClass = p.ResourceClass, Amount = p.Amount, ExistingAmount = p.ExistingAmount, RowVersion = p.RowVersion == null ? null : Convert.ToBase64String(p.RowVersion), RateHidden = false,
            ExcludedNoRate = p.ExcludedNoRate, ExcludedSensitive = p.ExcludedSensitive, LineCount = p.LineCount, Status = p.Status, PreparedByAgent = p.PreparedByAgent, CostLineId = p.CostLineId, RejectReason = p.RejectReason, CreateDate = p.CreateDate,
        };

        public async Task<ResponseDto<int>> ProposeCostAsync(int contractId, CancellationToken cancellationToken)
        {
            if (!AgentMaker.Allows("RES.COST.PROPOSE") || !_user.IsInRole(PcResourceRules.Writers)) return Bad<int>(PcResourceText.NotAllowed);
            if (!await _uow.Set<ContractEntity>().AsNoTracking().AnyAsync(c => c.Id == contractId && !c.Deleted, cancellationToken)) return NotFound<int>(PcText.ContractNotFound);
            // the cost baseline can be edited only until the baseline is approved: after that it changes through a change request, never by this proposal
            if (await PcPlanLock.IsLockedAsync(_uow, contractId, cancellationToken)) return Bad<int>(PcPlanLock.Refusal);
            PcResourcePlan plan = await PcResourceData.LatestApprovedAsync(_uow, contractId, false, cancellationToken);
            if (plan == null) return Bad<int>(PcResourceText.NoApprovedPlan);
            List<PcResourceCostProposal> old = await _uow.Set<PcResourceCostProposal>().Where(p => p.ContractId == contractId && !p.Deleted && p.Status == PcCostProposalStatus.Proposed).ToListAsync(cancellationToken);
            if (old.Any(p => p.PlanId == plan.Id)) return ResponseDtoHelper.Ok(0);

            var rows = await _uow.Set<PcActivityResource>().AsNoTracking().Where(x => x.PlanId == plan.Id && !x.Activity.Deleted).Select(x => new { x.ResourceId, x.Quantity }).ToListAsync(cancellationToken);
            int[] ids = rows.Select(r => r.ResourceId).Distinct().ToArray();
            Dictionary<int, PcResource> resources = ids.Length == 0 ? new Dictionary<int, PcResource>() : await _uow.Set<PcResource>().AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
            Dictionary<int, decimal?> rates = await PcResourceData.EffectiveRatesAsync(_uow, ids, DateTime.Today, cancellationToken);
            var byClass = new Dictionary<PcResourceClass, PcResourceCostProposal>();
            DateTimeOffset now = DateTimeOffset.Now;
            // what the cost baseline already holds per resource class: the proposal is only for the difference
            Dictionary<PcResourceClass, decimal> existing = (await _uow.Set<PcCostBaselineLine>().AsNoTracking().Where(l => l.ContractId == contractId && l.ResourceClass != null).GroupBy(l => l.ResourceClass.Value).Select(g => new { Class = g.Key, Sum = g.Sum(l => l.Amount) }).ToListAsync(cancellationToken)).ToDictionary(x => x.Class, x => x.Sum);
            foreach (var r in rows)
            {
                PcResource res = resources[r.ResourceId];
                PcResourceClass cls = PcResourceRules.ClassOf(res.Kind);
                if (!byClass.TryGetValue(cls, out PcResourceCostProposal p))
                {
                    p = new PcResourceCostProposal { ContractId = contractId, PlanId = plan.Id, PlanVersion = plan.Version, ResourceClass = cls, Status = PcCostProposalStatus.Proposed, PreparedByAgent = true, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me };
                    byClass[cls] = p;
                }
                p.LineCount++;
                decimal? rate = rates.TryGetValue(r.ResourceId, out decimal? v) ? v : null;
                // a line of a small labour group is never priced into a class amount: its cost could be one person's
                if (PcResourceRules.IsSensitive(res)) p.ExcludedSensitive++;
                else if (!rate.HasValue) p.ExcludedNoRate++;
                else p.Amount += R2(r.Quantity * rate.Value);
            }
            foreach (PcResourceCostProposal p in byClass.Values)
            {
                p.ExistingAmount = existing.TryGetValue(p.ResourceClass, out decimal ex) ? ex : 0m;
                p.Amount = R2(p.Amount - p.ExistingAmount); // the delta; no proposal when it is not above zero
            }
            if (byClass.Values.All(p => p.Amount <= 0m) && rows.Count > 0)
            {
                // nothing can be priced (no rate, or only small labour groups): a marker row, so that the agent does not look at the same plan again and again
                var any = byClass.Values.First();
                _uow.Set<PcResourceCostProposal>().Add(new PcResourceCostProposal
                {
                    ContractId = contractId, PlanId = plan.Id, PlanVersion = plan.Version, ResourceClass = any.ResourceClass, Amount = 0m, ExistingAmount = 0m, ExcludedNoRate = byClass.Values.Sum(p => p.ExcludedNoRate), ExcludedSensitive = byClass.Values.Sum(p => p.ExcludedSensitive),
                    LineCount = byClass.Values.Sum(p => p.LineCount), Status = PcCostProposalStatus.Superseded, PreparedByAgent = true, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
                });
            }
            foreach (PcResourceCostProposal o in old) { o.Status = PcCostProposalStatus.Superseded; o.ModifyDate = now; o.ModifierUserId = Me; }
            int made = 0;
            foreach (PcResourceCostProposal p in byClass.Values.OrderBy(p => (int)p.ResourceClass))
            {
                if (p.Amount <= 0m) continue;
                _uow.Set<PcResourceCostProposal>().Add(p); made++;
            }
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(made);
        }

        public async Task<ResponseDto<PagedList<PcResourceCostProposalDto>>> GetProposalsAsync(int? contractId, PcCostProposalStatus? status, int pageNumber, int pageSize, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(PcResourceRules.Readers)) return Bad<PagedList<PcResourceCostProposalDto>>(PcResourceText.NotAllowed);
            IQueryable<PcResourceCostProposal> q = _uow.Set<PcResourceCostProposal>().AsNoTracking().Where(p => !p.Deleted);
            if (contractId.HasValue) q = q.Where(p => p.ContractId == contractId.Value);
            if (status.HasValue) q = q.Where(p => p.Status == status.Value);
            PagedList<PcResourceCostProposal> page = await PagedList<PcResourceCostProposal>.CreateAsync(q.OrderBy(p => p.ContractId).ThenByDescending(p => p.Id), pageNumber <= 0 ? 1 : pageNumber, pageSize <= 0 ? 0 : Math.Min(pageSize, 200), cancellationToken);
            int[] cids = page.Select(p => p.ContractId).Distinct().ToArray();
            Dictionary<int, string> codes = cids.Length == 0 ? new Dictionary<int, string>() : await _uow.Set<ContractEntity>().AsNoTracking().Where(c => cids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, cancellationToken);
            List<PcResourceCostProposalDto> dtos = page.Select(p => ToDto(p, codes.TryGetValue(p.ContractId, out string code) ? code : null)).ToList();
            return ResponseDtoHelper.Ok((PagedList<PcResourceCostProposalDto>)new PcPage<PcResourceCostProposalDto>(dtos, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        public async Task<ResponseDto<PcResourceCostProposalDto>> DecideProposalAsync(PcCostProposalDecideDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcResourceCostProposalDto>(PcText.InvalidData);
            // the owner of the cost baseline is planning and the PMO (they write its lines); the AI never decides
            if (global::Services.NasbNiro.Ai.AiActing.IsActing || !_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcResourceCostProposalDto>(PcResourceText.NotAllowed);
            PcResourceCostProposal p = await _uow.Set<PcResourceCostProposal>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (p == null) return NotFound<PcResourceCostProposalDto>("پیشنهاد هزینه پیدا نشد.");
            if (p.Status != PcCostProposalStatus.Proposed) return Bad<PcResourceCostProposalDto>("فقط پیشنهادِ در وضعیت «پیشنهادشده» تصمیم‌گیری می‌شود.");
            // the decision is made on the version that the person saw: a proposal that changed since is refused (the same text as every other concurrent change)
            if (p.RowVersion != null && string.IsNullOrEmpty(dto.RowVersion)) return Bad<PcResourceCostProposalDto>(PcResourceText.RowVersionMissing);
            if (!string.IsNullOrEmpty(dto.RowVersion))
            {
                byte[] seen;
                try { seen = Convert.FromBase64String(dto.RowVersion); }
                catch (FormatException) { return Bad<PcResourceCostProposalDto>(PcText.InvalidData); }
                if (p.RowVersion != null && !p.RowVersion.SequenceEqual(seen)) return Bad<PcResourceCostProposalDto>(PcText.Concurrency); // changed since the person saw it: refused before anything is staged
                _uow.Entry(p).Property(x => x.RowVersion).OriginalValue = seen;
            }
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            DateTimeOffset now = DateTimeOffset.Now;
            PcCostBaselineLine staged = null;
            if (action == "reject")
            {
                string reason = PcText.Clean(dto.Reason);
                if (reason == null) return Bad<PcResourceCostProposalDto>("علت رد پیشنهاد را بنویسید.");
                if (reason.Length > 1000) return Bad<PcResourceCostProposalDto>("علت حداکثر ۱۰۰۰ نویسه است.");
                p.Status = PcCostProposalStatus.Rejected; p.RejectReason = reason;
            }
            else if (action == "accept")
            {
                PcResourcePlan latest = await PcResourceData.LatestApprovedAsync(_uow, p.ContractId, false, cancellationToken);
                if (latest == null || latest.Id != p.PlanId) return Bad<PcResourceCostProposalDto>("طرح منابعِ تاییدشده عوض شده است و این پیشنهاد کهنه شد؛ ایجنت پیشنهاد تازه می‌سازد.");
                // the cost baseline may have changed since the proposal: the line is the difference to what it holds now, and it must still be above zero
                decimal holds = await _uow.Set<PcCostBaselineLine>().AsNoTracking().Where(l => l.ContractId == p.ContractId && l.ResourceClass == p.ResourceClass).SumAsync(l => (decimal?)l.Amount, cancellationToken) ?? 0m;
                decimal delta = R2(p.Amount + p.ExistingAmount - holds);
                if (delta <= 0m) return Bad<PcResourceCostProposalDto>("خط مبنای هزینه این گروه از پیشنهاد کمتر یا برابر نیست؛ پیشنهاد کهنه شد.");
                // the line goes through the rules of the existing cost-baseline path (its role, lock and amount checks) and is saved WITH the decision, in one transaction: either both are written or neither
                ResponseDto<PcCostBaselineLine> line = await _baselines.StageCostLineAsync(new PcCostLineDto
                {
                    ContractId = p.ContractId, CostClass = PcCostClass.Direct, ResourceClass = p.ResourceClass, Amount = delta,
                    Description = "از طرح منابع نسخهٔ " + p.PlanVersion.ToString(CultureInfo.InvariantCulture) + " (پیشنهاد ایجنت، تاییدشده توسط انسان)",
                }, cancellationToken);
                if (!line.Success) return Bad<PcResourceCostProposalDto>(line.Message);
                staged = line.Data;
                p.Status = PcCostProposalStatus.Accepted; p.CostLine = staged; p.Amount = delta; p.ExistingAmount = holds;
            }
            else return Bad<PcResourceCostProposalDto>("عملیات نامعتبر است.");
            p.DecidedByUserId = Me; p.DecidedAt = now; p.ModifyDate = now; p.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex is DbUpdateException)
            {
                if (staged != null) _uow.Set<PcCostBaselineLine>().Remove(staged);
                return Bad<PcResourceCostProposalDto>(PcText.Concurrency);
            }
            string code = await _uow.Set<ContractEntity>().AsNoTracking().Where(c => c.Id == p.ContractId).Select(c => c.Code).FirstOrDefaultAsync(cancellationToken);
            return ResponseDtoHelper.Ok(ToDto(p, code));
        }
    }
}
