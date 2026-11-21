using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Office;

namespace Services.NasbNiro.Engineering
{
    /// <summary>One controlled case whose documents are at risk: the early warning (an emergency should never happen).</summary>
    public sealed class EnRiskRow
    {
        public int CaseId { get; set; }
        public string Number { get; set; }
        public int? ContractId { get; set; }

        /// <summary>The case is Awarded or later (in engineering): the CEO is told while the risk exists.</summary>
        public bool Awarded { get; set; }

        public int Missing { get; set; }
        public int? DaysLeft { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();

        public string Summary()
        {
            string days = DaysLeft.HasValue ? (DaysLeft.Value >= 0 ? EnText.Num(DaysLeft.Value) + " روز تا موعد تحویل" : "موعد تحویل " + EnText.Num(-DaysLeft.Value) + " روز گذشته") : "موعد تحویل مشخص نیست";
            return "پرونده " + Number + ": " + string.Join("؛ ", Reasons) + " (" + (Missing > 0 ? EnText.Num(Missing) + " مدرک الزامی کم است، " : string.Empty) + days + ").";   // no "0 required documents are missing": a reason of its own says what is left
        }
    }

    /// <summary>The state of a platform dossier as the risk, the case warning and the CEO alert word it: the ONE place of these texts, so the three can never drift or say something untrue ("0 required documents missing" while the dossier is not even assessed).</summary>
    internal enum EnDossierWord { Complete, NoDossier, NotAssessed, MissingDocuments, IncompleteNoMissing, AwaitingConfirmation, StaleConfirmation }

    internal sealed class EnDossierWording
    {
        /// <summary>The CEO alert hint: the risk is any of the reasons of <see cref="EnRisk"/>, not only missing documents.</summary>
        public const string CeoAlertHint = "(مدارک، تایید پرونده یا فهرست MDL ناقص است)";

        public EnDossierWord Word { get; private set; }
        public int Missing { get; private set; }
        public bool Complete => Word == EnDossierWord.Complete;

        public static async Task<EnDossierWording> LoadAsync(IUnitOfWork uow, int productId, string code, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(code)) return new EnDossierWording { Word = EnDossierWord.NoDossier };
            EnPlatformDossier d = await uow.Set<EnPlatformDossier>().AsNoTracking().Include(x => x.Documents).FirstOrDefaultAsync(x => x.ProductId == productId && x.ReferenceCode == code && !x.Deleted, ct);
            if (d == null) return new EnDossierWording { Word = EnDossierWord.NoDossier };
            if (await EnPlatformRules.IsCompleteAsync(uow, productId, code, ct)) return new EnDossierWording { Word = EnDossierWord.Complete };
            return Of(d.Status, d.Documents.Count(x => x.Required && x.State == EnLineState.Missing));
        }

        /// <summary>The pure choice (unit-testable): the dossier is known NOT to be complete and confirmed.</summary>
        public static EnDossierWording Of(EnDossierStatus status, int missing)
        {
            EnDossierWord w = status == EnDossierStatus.NotAssessed ? EnDossierWord.NotAssessed
                : missing > 0 ? EnDossierWord.MissingDocuments
                : status == EnDossierStatus.Incomplete ? EnDossierWord.IncompleteNoMissing
                : status == EnDossierStatus.Completing ? EnDossierWord.AwaitingConfirmation
                : EnDossierWord.StaleConfirmation;
            return new EnDossierWording { Word = w, Missing = missing };
        }

        /// <summary>The reason sentence (no full stop) for the standard <paramref name="code"/>.</summary>
        public string Reason(string code)
        {
            string std = string.IsNullOrWhiteSpace(code) ? "پلتفرم" : "استاندارد " + code;
            switch (Word)
            {
                case EnDossierWord.NoDossier: return "برای " + std + " پروندهٔ مدارک پلتفرم ساخته نشده است";
                case EnDossierWord.NotAssessed: return "پروندهٔ مدارک پلتفرم برای " + std + " هنوز ارزیابی نشده است";
                case EnDossierWord.IncompleteNoMissing: return "مدارک الزامی پلتفرم برای " + std + " موجود است ولی پرونده هنوز کامل اعلام نشده است (کامل‌شدن باید اعلام شود)";
                case EnDossierWord.AwaitingConfirmation: return "مدارک الزامی پلتفرم برای " + std + " موجود است و فقط تایید کامل‌بودن پرونده توسط مدیر مهندسی مانده است";
                case EnDossierWord.StaleConfirmation: return "تایید پروندهٔ مدارک پلتفرم برای " + std + " دیگر معتبر نیست (فهرست MDL یا مدارک تغییر کرده)؛ ارزیابی دوباره و تایید مدیر مهندسی لازم است";
                default: return "مدارک پلتفرم برای " + std + " کامل و تایید نشده است";
            }
        }

        /// <summary>The warning of the case page: the reason, the count only when documents are really missing, then the fixed advice.</summary>
        public string Warning(string code) =>
            Reason(code) + (Word == EnDossierWord.MissingDocuments ? " (" + EnText.Num(Missing) + " مدرک الزامی کم است)" : string.Empty)
            + "؛ این فقط هشدار است و بسته فنی مناقصه را متوقف نمی‌کند، اما بستهٔ تحویل مدارک بدون آن ناقص به PMO می‌رسد و ابلاغ آن به اجرا فقط با مجوز اضطرار مدیرعامل (پس از پذیرش PMO) ممکن است.";
    }

    /// <summary>The document risk of the controlled cases, computed from the records (nothing is stored). A case is at risk while a platform dossier of its standard is not confirmed Complete, its draft handover misses required documents, or its standard has no Approved reference list. Legacy records have no case and so no risk.</summary>
    /// <summary>A human gate (or input, physical or failed agent task) of engineering that has waited more than 72 OFFICE hours (employer rule 14).</summary>
    internal sealed class EnStalledTask
    {
        public int TaskId { get; set; }
        public string StepCode { get; set; }
        public string Title { get; set; }
        public string Roles { get; set; }
        public string RolesText { get; set; }
        public string Link { get; set; }
        public bool Failed { get; set; }
        public DateTimeOffset? DueAt { get; set; }
    }

    internal static class EnRisk
    {
        public const string RiskKeyPrefix = "EN-RISK-";
        public const string DebtKeyPrefix = "EN-DEBT-";

        /// <summary>A task that has waited (or failed) more than 72 office hours: a permanent alarm of the CEO until the task leaves that state. Nothing is decided by the alarm.</summary>
        public const string StallKeyPrefix = "EN-STALL-";

        private static IReadOnlyList<string> _ruleCodes;

        /// <summary>The codes of the registered engineering steps that are under the office-hours rule (they implement <see cref="IOfficeDeadlineStep"/> with hours above 0). Steps are stateless, so one instance of each type is enough.</summary>
        public static IReadOnlyList<string> RuleStepCodes()
        {
            if (_ruleCodes != null) return _ruleCodes;
            List<string> codes = new List<string>();
            foreach (Type t in typeof(EnRisk).Assembly.GetTypes())
            {
                if (t.IsAbstract || !typeof(IAgentStep).IsAssignableFrom(t) || !typeof(IOfficeDeadlineStep).IsAssignableFrom(t)) continue;
                if (t.GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null) == null) continue;
                if (!(Activator.CreateInstance(t, true) is IAgentStep step) || !(step is IOfficeDeadlineStep rule) || rule.OfficeDeadlineHours <= 0) continue;
                if (!step.Code.StartsWith("ENG.", StringComparison.Ordinal)) continue;
                codes.Add(step.Code);
            }
            _ruleCodes = codes.Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
            return _ruleCodes;
        }

        private static string RoleText(string roles)
        {
            if (string.IsNullOrWhiteSpace(roles)) return "مسئول کار";
            return string.Join("، ", roles.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim().ToUpperInvariant() switch
            {
                "ENGINEERING" => "مدیر مهندسی", "ENGINEER" => "مهندس", "CEO" => "مدیرعامل", "PMO" => "PMO", "PROJECTMANAGER" => "مدیر پروژه", "SALES" => "فروش", "COMMERCIAL" => "بازرگانی",
                "QA" => "تضمین کیفیت", "QC" => "کنترل کیفیت", "PLANNING" => "برنامه‌ریزی", "DCC" => "کنترل مدارک", "PRODUCTIONMANAGER" => "مدیر تولید", _ => r.Trim(),
            }).Distinct());
        }

        private sealed class StallCandidate
        {
            public int Id { get; set; }
            public string StepCode { get; set; }
            public string Title { get; set; }
            public string GateRoles { get; set; }
            public string Link { get; set; }
            public AgentTaskState State { get; set; }
            public DateTimeOffset? EscalatedAt { get; set; }
            public DateTimeOffset ModifyDate { get; set; }
            public DateTimeOffset CreateDate { get; set; }
        }

        /// <summary>
        /// Open tasks of the engineering steps under the rule that waited for a person more than 72 OFFICE hours, or that failed and were not touched for 72 office hours.
        /// SQL only selects the candidates (a stored DueAt in the past, or Failed); the decision is made in memory from the office clock, because tasks opened before the rule carry a 48 calendar hour DueAt.
        /// The wait starts at the latest event that moved the task INTO its current state (fallback: its creation). No cap: every stalled task is returned; <paramref name="onlyTaskId"/> checks one task.
        /// </summary>
        public static async Task<List<EnStalledTask>> StalledTasksAsync(IUnitOfWork uow, CancellationToken ct, int? onlyTaskId = null, IReadOnlyCollection<string> ruleCodes = null)
        {
            string[] codes = (ruleCodes ?? RuleStepCodes()).ToArray();
            List<EnStalledTask> result = new List<EnStalledTask>();
            if (codes.Length == 0) return result;
            DateTimeOffset now = DateTimeOffset.Now;
            DateTimeOffset failedBefore = now.AddHours(-OfficeClock.StopLimitHours);   // 72 office hours are never fewer than 72 calendar hours: a safe pre-filter for the query
            int lastId = 0;
            while (true)
            {
                int after = lastId;
                List<StallCandidate> rows = await uow.Set<AgentTask>().AsNoTracking().Where(t => !t.Deleted && t.Id > after && (onlyTaskId == null || t.Id == onlyTaskId) && codes.Contains(t.StepCode)
                    && (((t.State == AgentTaskState.WaitingHumanGate || t.State == AgentTaskState.WaitingInput || t.State == AgentTaskState.WaitingPhysical) && t.DueAt != null && t.DueAt < now)
                        || (t.State == AgentTaskState.Failed && (t.EscalatedAt ?? t.ModifyDate) < failedBefore)))
                    .OrderBy(t => t.Id).Take(500)
                    .Select(t => new StallCandidate { Id = t.Id, StepCode = t.StepCode, Title = t.Title, GateRoles = t.GateRoles, Link = t.Link, State = t.State, EscalatedAt = t.EscalatedAt, ModifyDate = t.ModifyDate, CreateDate = t.CreateDate }).ToListAsync(ct);
                if (rows.Count == 0) break;
                lastId = rows[rows.Count - 1].Id;
                int[] ids = rows.Where(r => r.State != AgentTaskState.Failed).Select(r => r.Id).ToArray();
                // ONE query for the events of all waiting candidates of this page
                List<(int TaskId, DateTimeOffset At, AgentTaskState? From, AgentTaskState To)> events = ids.Length == 0 ? new List<(int TaskId, DateTimeOffset At, AgentTaskState? From, AgentTaskState To)>()
                    : (await uow.Set<AgentTaskEvent>().AsNoTracking().Where(e => ids.Contains(e.TaskId)).Select(e => new { e.TaskId, e.At, e.FromState, e.ToState }).ToListAsync(ct)).Select(e => (e.TaskId, e.At, e.FromState, e.ToState)).ToList();
                foreach (StallCandidate t in rows)
                {
                    bool failed = t.State == AgentTaskState.Failed;
                    DateTimeOffset due;
                    if (failed)
                    {
                        due = OfficeClock.Add(t.EscalatedAt ?? t.ModifyDate, OfficeClock.StopLimitHours);
                        if (due >= now) continue;
                    }
                    else
                    {
                        DateTimeOffset waitStart = events.Where(e => e.TaskId == t.Id && e.To == t.State && e.From != t.State).Select(e => (DateTimeOffset?)e.At).DefaultIfEmpty(null).Max() ?? t.CreateDate;
                        if (OfficeClock.Between(waitStart, now) < OfficeClock.StopLimitHours) continue;
                        due = OfficeClock.Add(waitStart, OfficeClock.StopLimitHours);
                    }
                    result.Add(new EnStalledTask { TaskId = t.Id, StepCode = t.StepCode, Title = t.Title, Roles = t.GateRoles, RolesText = RoleText(t.GateRoles), Link = t.Link, Failed = failed, DueAt = due });
                }
                if (onlyTaskId != null || rows.Count < 500) break;
            }
            return result;
        }

        public static async Task<List<EnRiskRow>> ComputeAsync(IUnitOfWork uow, CancellationToken ct, IReadOnlyCollection<int> onlyCaseIds = null)
        {
            // onlyCaseIds narrows the same query to the cases of one page (the case list shows the risk of exactly these rows); the rule itself is the one of the early warning
            List<int> only = onlyCaseIds == null ? null : onlyCaseIds.ToList();
            List<EnCase> cases = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && (only == null || only.Contains(c.Id)) && (c.CeoApprovedAt != null || (c.Kind != EnCaseKind.Turbine && c.EngConfirmedAt != null))
                && (c.Status == EnCaseStatus.TenderPreparation || c.Status == EnCaseStatus.TenderIssued || c.Status == EnCaseStatus.Awarded || c.Status == EnCaseStatus.InEngineering
                    || c.Status == EnCaseStatus.HandedOver || c.Status == EnCaseStatus.Accepted)).OrderBy(c => c.Id).Take(500).ToListAsync(ct);   // HandedOver / Accepted: an incomplete package with the PMO stays a risk until it is released (a released case leaves)
            List<string> approved = await EnReferenceRules.ApprovedCodesAsync(uow, ct);
            List<int> contractIds = cases.Where(c => c.ContractId.HasValue).Select(c => c.ContractId.Value).ToList();
            Dictionary<int, DateTime?> delivery = (await uow.Set<PcContractTerms>().AsNoTracking().Where(t => contractIds.Contains(t.ContractId) && !t.Deleted).Select(t => new { t.ContractId, t.ContractDeliveryDate }).ToListAsync(ct))
                .GroupBy(t => t.ContractId).ToDictionary(g => g.Key, g => g.First().ContractDeliveryDate);
            List<EnRiskRow> rows = new List<EnRiskRow>();
            foreach (EnCase c in cases)
            {
                bool atPmo = c.Status == EnCaseStatus.HandedOver || c.Status == EnCaseStatus.Accepted;
                EnRiskRow row = new EnRiskRow { CaseId = c.Id, Number = c.Number, ContractId = c.ContractId, Awarded = c.Status == EnCaseStatus.Awarded || c.Status == EnCaseStatus.InEngineering || atPmo };
                // a package at the PMO that an APPROVED permit already covers is no longer a risk of the dossier (its debts are tracked instead)
                bool permitted = atPmo && await uow.Set<EnWaiver>().AsNoTracking().AnyAsync(w => !w.Deleted && w.CaseId == c.Id && w.Status == EnWaiverStatus.Approved && uow.Set<EnHandover>().Any(h => h.Id == w.HandoverId && !h.Deleted && (h.Status == EnHandoverStatus.Submitted || h.Status == EnHandoverStatus.Accepted)), ct);
                int missing = 0;
                if (!permitted && c.Classification == EnClassification.Platform && c.ProductId.HasValue && !string.IsNullOrWhiteSpace(c.ReferenceCode))
                {
                    EnDossierWording state = await EnDossierWording.LoadAsync(uow, c.ProductId.Value, c.ReferenceCode, ct);
                    if (!state.Complete)
                    {
                        row.Reasons.Add(state.Reason(c.ReferenceCode));
                        missing = Math.Max(missing, state.Missing);
                    }
                }
                if (row.Awarded)
                {
                    int draft = await uow.Set<EnHandoverLine>().AsNoTracking().CountAsync(l => l.Handover.CaseId == c.Id && !l.Handover.Deleted && l.Handover.Status == EnHandoverStatus.Draft && l.Required && l.State == EnLineState.Missing, ct);
                    if (draft > 0) { row.Reasons.Add("تحویل مدارک پیش‌نویس مدرک الزامی کم دارد"); missing = Math.Max(missing, draft); }
                    // an incomplete package already with the PMO (rounds or accepted) stays a risk until released, unless an approved permit covers it
                    int sent = await uow.Set<EnHandoverLine>().AsNoTracking().CountAsync(l => l.Handover.CaseId == c.Id && !l.Handover.Deleted && (l.Handover.Status == EnHandoverStatus.Submitted || l.Handover.Status == EnHandoverStatus.Accepted) && l.Required && l.State == EnLineState.Missing
                        && !uow.Set<EnWaiver>().Any(w => w.HandoverId == l.HandoverId && !w.Deleted && w.Status == EnWaiverStatus.Approved), ct);
                    if (sent > 0 && !permitted) { row.Reasons.Add("بستهٔ ناقص تحویل مدارک نزد PMO است؛ ابلاغ به اجرا قفل است تا مدارک کامل شود یا مدیرعامل مجوز اضطرار بدهد"); missing = Math.Max(missing, sent); }
                }
                if (!string.IsNullOrWhiteSpace(c.ReferenceCode) && !approved.Contains(c.ReferenceCode)) row.Reasons.Add("برای استاندارد " + c.ReferenceCode + " فهرست MDL تاییدشده‌ای نیست");
                if (row.Reasons.Count == 0) continue;
                row.Missing = missing;
                if (c.ContractId.HasValue && delivery.TryGetValue(c.ContractId.Value, out DateTime? due) && due.HasValue) row.DaysLeft = (int)Math.Floor((due.Value.Date - DateTime.Today).TotalDays);
                rows.Add(row);
            }
            return rows;
        }

        public static async Task<List<(int Id, string Title)>> OverdueDebtsAsync(IUnitOfWork uow, CancellationToken ct) =>
            (await uow.Set<EnDocumentDebt>().AsNoTracking().Where(d => !d.Deleted && d.Status == EnDebtStatus.Open && d.DueAt < DateTimeOffset.Now).OrderBy(d => d.Id).Take(500).Select(d => new { d.Id, d.Title }).ToListAsync(ct)).Select(d => (d.Id, d.Title)).ToList();

        /// <summary>The source keys of the inbox items of the CEO that should be open right now: a case at risk from Awarded on, an overdue document debt.</summary>
        public static async Task<HashSet<string>> DesiredKeysAsync(IUnitOfWork uow, CancellationToken ct)
        {
            HashSet<string> keys = new HashSet<string>();
            foreach (EnRiskRow r in (await ComputeAsync(uow, ct)).Where(r => r.Awarded)) keys.Add(RiskKeyPrefix + r.CaseId.ToString());
            foreach ((int id, string _) in await OverdueDebtsAsync(uow, ct)) keys.Add(DebtKeyPrefix + id.ToString());
            foreach (EnStalledTask t in await StalledTasksAsync(uow, ct)) keys.Add(StallKeyPrefix + t.TaskId.ToString(CultureInfo.InvariantCulture));
            return keys;
        }

        public static async Task<List<string>> OpenKeysAsync(IUnitOfWork uow, CancellationToken ct) =>
            await uow.Set<OfInboxItem>().AsNoTracking().Where(i => (i.SourceKey.StartsWith(RiskKeyPrefix) || i.SourceKey.StartsWith(DebtKeyPrefix) || i.SourceKey.StartsWith(StallKeyPrefix)) && i.Status < OfInboxStatus.Done).Select(i => i.SourceKey).Distinct().ToListAsync(ct);
    }
}
