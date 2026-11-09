using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Office;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Production.Dispatch
{
    /// <summary>
    /// MF-DISPATCH (every hour of the day): the production planning seat. It builds the dispatch plan as its own read-only user (role PLANNING) and leaves, per day, one plan note,
    /// one note per bottleneck centre and one note per step at risk of being late, each with an inbox item for the production manager, the shop supervisors and planning.
    /// Rule-based (no model needed, so it works offline and every number is explained). It never changes an order, an operation or a schedule and never approves anything.
    /// </summary>
    public class MfDispatchSeatJob : IAiSeatJob
    {
        private readonly IMfDispatchService _dispatch;
        private readonly IAiNoteWriter _notes;
        private readonly INotificationService _notify;

        public const string TargetPlan = "MfDispatchPlan";
        public const string TargetCenter = "MfWorkCenter";
        public const string TargetOperation = "MfOperation";
        public const string RulePlan = "DAILY-PLAN";
        public const string RuleLateRisk = "LATE-RISK";
        public const string Recipients = "PRODUCTIONMANAGER,SHOPSUPERVISOR,PLANNING";

        public MfDispatchSeatJob(IMfDispatchService dispatch, IAiNoteWriter notes, INotificationService notify)
        {
            _dispatch = dispatch; _notes = notes; _notify = notify;
        }

        public string Code => AiSeatCodes.MfDispatch;

        /// <summary>The rule code of a bottleneck note: one per centre and day (the target is the centre).</summary>
        public static string BottleneckRule(DateTime day) => "BOTTLENECK-" + AiSeatTexts.DayKey(day);

        public async Task<AiSeatJobResult> RunAsync(AiSeatRunContext context, CancellationToken ct)
        {
            DateTime day = context.AsOf.Date;
            DispatchPlanDto plan = await _dispatch.BuildAsync(day, MfDispatchRules.DefaultHorizonDays, null, ct);
            if (plan.OpenOrders == 0) return AiSeatJobResult.Done("no open production orders");
            int notes = 0;

            // 1. the plan of the day (one note a day)
            if (await _notes.TryAddAsync(new AiNote
            {
                SeatCode = Code, TargetType = TargetPlan, TargetId = AiSeatTexts.DayKey(day), RuleCode = RulePlan, Title = "برنامه پیشنهادی تولید " + plan.AsOfText, Text = PlanText(plan), Verified = true, SeatRunId = context.RunId,
            }, ct))
            {
                notes++;
                await NotifyAsync("mfd:plan:" + AiSeatTexts.DayKey(day), "برنامه پیشنهادی تولید امروز آماده است", "/production/dispatch", OfUrgency.Normal, ct);
            }

            // 2. a bottleneck centre (one note per centre and day)
            foreach (DispatchCenterDto c in plan.Centers.Where(c => c.Level == MfDispatchRules.LevelBottleneck && c.DemandHours > 0))
            {
                DispatchSuggestionDto s = plan.Suggestions.FirstOrDefault(x => x.Kind == "bottleneck" && x.WorkCenterId == c.WorkCenterId);
                if (!await _notes.TryAddAsync(new AiNote
                {
                    SeatCode = Code, TargetType = TargetCenter, TargetId = c.WorkCenterId, RuleCode = BottleneckRule(day), Title = $"گلوگاه {c.Title} ({plan.AsOfText})",
                    Text = s?.Text ?? $"{c.Title}: بار {MfDispatchRules.Fa(c.LoadPercent ?? 0)}٪ ظرفیت.", Verified = true, SeatRunId = context.RunId,
                }, ct)) continue;
                notes++;
                await NotifyAsync("mfd:bn:" + c.WorkCenterId + ":" + AiSeatTexts.DayKey(day), $"گلوگاه تولید: {c.Title} ({MfDispatchRules.Fa(c.LoadPercent ?? 0)}٪ ظرفیت)", "/production/dispatch?wc=" + c.WorkCenterId, OfUrgency.Urgent, ct);
            }

            // 3. a ready step that will be late unless it moves up (one note per step: it is not repeated every hour)
            foreach (DispatchCenterDto c in plan.Centers)
                foreach (DispatchItemDto i in c.Queue.Where(i => i.Stage == MfDispatchRules.StageReady && i.Risk == MfDispatchRules.RiskLate).Take(AiSeatTexts.MaxItemsPerRun))
                {
                    if (!await _notes.TryAddAsync(new AiNote
                    {
                        SeatCode = Code, TargetType = TargetOperation, TargetId = i.OperationId, RuleCode = RuleLateRisk, Title = $"خطر تأخیر {i.Name}",
                        Text = $"{i.Name} ({i.Title}) در {c.Title}. {i.Reason} پیشنهاد: در نوبت {MfDispatchRules.Fa(i.Rank)} صف این مرکز اجرا شود.", Verified = true, SeatRunId = context.RunId,
                    }, ct)) continue;
                    notes++;
                    await NotifyAsync("mfd:late:" + i.OperationId, $"خطر تأخیر: {i.Name} در {c.Title}", "/production/orders/" + i.OrderId, OfUrgency.Normal, ct);
                }

            return AiSeatJobResult.Done($"orders={plan.OpenOrders}; centres={plan.Centers.Count}; lateRisk={plan.LateRiskOperations}; suggestions={plan.Suggestions.Count}; notes={notes}");
        }

        private Task NotifyAsync(string key, string title, string link, OfUrgency priority, CancellationToken ct) =>
            _notify.NotifyAsync(new NotifyRequest { SourceKey = key, Module = "production", SourceType = "mf-dispatch", Kind = OfInboxKind.Task, Priority = priority, Title = title, Link = link, Roles = Recipients }, ct);

        /// <summary>The note of the day: the bottleneck, the proposals and the first steps of every busy centre.</summary>
        public static string PlanText(DispatchPlanDto plan)
        {
            var sb = new StringBuilder();
            sb.Append($"سفارش باز: {MfDispatchRules.Fa(plan.OpenOrders)}، دیرکرد: {MfDispatchRules.Fa(plan.LateOrders)}، مرحله آماده در خطر تأخیر: {MfDispatchRules.Fa(plan.LateRiskOperations)}.");
            if (plan.BottleneckText != null) sb.Append(' ').Append(plan.BottleneckText);
            foreach (DispatchSuggestionDto s in plan.Suggestions.Take(6)) sb.Append("\n• ").Append(s.Text);
            foreach (DispatchCenterDto c in plan.Centers.Where(c => c.Queue.Count > 0).OrderByDescending(c => c.LoadPercent ?? 0).Take(5))
            {
                List<string> first = c.Queue.Where(q => q.Stage != MfDispatchRules.StageUpcoming).Take(3).Select(q => q.Name + " " + q.Title).ToList();
                if (first.Count > 0) sb.Append($"\n{c.Title}: ").Append(string.Join("، سپس ", first));
            }
            return sb.ToString();
        }
    }
}
