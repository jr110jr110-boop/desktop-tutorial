using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Ai;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.Agents
{
    /// <summary>The registered steps. A step is one <see cref="IAgentStep"/> in the container; a wrong registration (a duplicate code, an unknown owner, a wrong station) stops the first use with a clear message.</summary>
    public interface IAgentStepCatalog
    {
        IReadOnlyList<IAgentStep> All { get; }
        IAgentStep Find(string code);
        IReadOnlyList<IAgentStep> ForAgent(string agentCode);
    }

    public class AgentStepCatalog : IAgentStepCatalog
    {
        private readonly IReadOnlyList<IAgentStep> _steps;

        public AgentStepCatalog(IEnumerable<IAgentStep> steps)
        {
            var list = (steps ?? Enumerable.Empty<IAgentStep>()).ToList();
            foreach (IAgentStep s in list)
            {
                if (string.IsNullOrWhiteSpace(s.Code) || s.Code.Length > 60 || s.Code != s.Code.ToUpperInvariant()) throw new InvalidOperationException("کد گام ایجنت نامعتبر است: " + s.Code);
                if (!AgentStations.IsValid(s.Station)) throw new InvalidOperationException("ایستگاه گام نامعتبر است: " + s.Code);
                if (global::Services.NasbNiro.Ai.AiSeatCatalog.Find(s.AgentCode) == null) throw new InvalidOperationException("ایجنت گام در فهرست صندلی‌ها نیست: " + s.Code + " / " + s.AgentCode);
                if (!Enum.IsDefined(typeof(AgentStepKind), s.Kind)) throw new InvalidOperationException("نوع گام نامعتبر است: " + s.Code);
            }
            string dup = list.GroupBy(s => s.Code, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).FirstOrDefault();
            if (dup != null) throw new InvalidOperationException("کد گام ایجنت تکراری است: " + dup);
            _steps = list.OrderBy(s => s.Station).ThenBy(s => s.Code, StringComparer.Ordinal).ToList();
        }

        public IReadOnlyList<IAgentStep> All => _steps;

        public IAgentStep Find(string code) => string.IsNullOrWhiteSpace(code) ? null : _steps.FirstOrDefault(s => string.Equals(s.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<IAgentStep> ForAgent(string agentCode) => _steps.Where(s => string.Equals(s.AgentCode, agentCode, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>The switches of the steps and the seats, read the way the reconciler and the executor need them.</summary>
    public static class AgentSettings
    {
        /// <summary>
        /// The row of every step of the catalogue (a missing row is made now, with the defaults: on, 3 attempts, 48 hours), keyed by step code. The rows are tracked.
        /// A second process that makes the same row at the same moment wins through the unique index; the rows are read again.
        /// </summary>
        public static async Task<Dictionary<string, AgentStepSetting>> EnsureAsync(IUnitOfWork uow, IEnumerable<IAgentStep> steps, CancellationToken ct)
        {
            List<AgentStepSetting> rows = await uow.Set<AgentStepSetting>().Where(x => !x.Deleted).ToListAsync(ct);
            var map = rows.GroupBy(r => r.StepCode, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            bool added = false;
            foreach (IAgentStep step in steps)
            {
                if (map.ContainsKey(step.Code)) continue;
                var row = new AgentStepSetting { StepCode = step.Code, CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now };
                uow.Set<AgentStepSetting>().Add(row);
                map[step.Code] = row;
                added = true;
            }
            if (added)
            {
                try { await uow.SaveChangesAsync(ct); }
                catch (DbUpdateException)
                {
                    AgentDb.Clear(uow);
                    rows = await uow.Set<AgentStepSetting>().Where(x => !x.Deleted).ToListAsync(ct);
                    map = rows.GroupBy(r => r.StepCode, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                }
            }
            return map;
        }

        /// <summary>The seats whose row says they are switched off. A seat without a row (not provisioned yet) is not switched off.</summary>
        public static async Task<HashSet<string>> DisabledSeatsAsync(IUnitOfWork uow, CancellationToken ct) =>
            (await uow.Set<AiSeat>().AsNoTracking().Where(s => !s.Enabled).Select(s => s.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A job of an older seat that an agent step took over (the two must not both make the same documents).</summary>
    public static class AgentSupersession
    {
        /// <summary>True when the step is registered, switched on (no row yet means on, the default) and its agent seat is switched on.</summary>
        public static async Task<bool> IsSupersededAsync(IUnitOfWork uow, IAgentStepCatalog catalog, string stepCode, string agentCode, CancellationToken ct)
        {
            if (catalog.Find(stepCode) == null) return false;
            AgentStepSetting setting = await uow.Set<AgentStepSetting>().AsNoTracking().FirstOrDefaultAsync(x => x.StepCode == stepCode && !x.Deleted, ct);
            if (setting != null && !setting.Enabled) return false;
            return !(await DisabledSeatsFor(uow, agentCode, ct));
        }

        private static async Task<bool> DisabledSeatsFor(IUnitOfWork uow, string agentCode, CancellationToken ct) =>
            await uow.Set<AiSeat>().AsNoTracking().AnyAsync(s => s.Code == agentCode && !s.Enabled, ct);
    }

    public static class AgentDb
    {
        /// <summary>Drops every tracked change of the unit of work (after a failed save: the tracker would carry the failed changes into the next save).</summary>
        public static void Clear(IUnitOfWork uow)
        {
            if (uow is DbContext context) context.ChangeTracker.Clear();
        }

        /// <summary>Is anything waiting to be saved?</summary>
        public static bool HasChanges(IUnitOfWork uow) => !(uow is DbContext context) || context.ChangeTracker.HasChanges();
    }
}
