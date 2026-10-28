using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.Production
{
    /// <summary>The routing of an item: the steps (OperationTemplate rows), their standards and the revision that two people approved for exactly this content.</summary>
    internal class MfResolvedRouting
    {
        public int? KeyItemId { get; set; }
        public int? KeyGroupId { get; set; }
        public List<OperationTemplate> Steps { get; set; } = new List<OperationTemplate>();
        public Dictionary<int, MfOperationStandard> Standards { get; set; } = new Dictionary<int, MfOperationStandard>();
        public string Hash { get; set; }

        /// <summary>The newest approved revision whose approved content equals the current steps; null when there is none (Message says why).</summary>
        public MfRoutingRevision Revision { get; set; }

        public string Message { get; set; }
    }

    /// <summary>
    /// OperationTemplate stays the single routing master. The key of a routing is the item (when the item has steps of its own) or the nearest item group that has some,
    /// the same rule the traceability screens use.
    /// </summary>
    internal static class MfRouting
    {
        public static async Task<List<OperationTemplate>> StepsOfKeyAsync(IUnitOfWork uow, int? itemId, int? groupId, bool tracked, CancellationToken ct)
        {
            IQueryable<OperationTemplate> q = uow.Set<OperationTemplate>().Where(t => t.IsActive && !t.Deleted);
            q = itemId.HasValue ? q.Where(t => t.ItemId == itemId.Value) : q.Where(t => t.ItemGroupId == groupId.Value && t.ItemId == null);
            if (!tracked) q = q.AsNoTracking();
            return await q.OrderBy(t => t.Sequence).ThenBy(t => t.Id).ToListAsync(ct);
        }

        public static async Task<Dictionary<int, MfOperationStandard>> StandardsAsync(IUnitOfWork uow, IEnumerable<int> templateIds, CancellationToken ct)
        {
            List<int> ids = templateIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, MfOperationStandard>();
            List<MfOperationStandard> rows = await uow.Set<MfOperationStandard>().AsNoTracking().Where(s => ids.Contains(s.OperationTemplateId) && !s.Deleted).ToListAsync(ct);
            return rows.GroupBy(s => s.OperationTemplateId).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Id).First());
        }

        private static string N(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        public static async Task<string> HashAsync(IUnitOfWork uow, List<OperationTemplate> steps, Dictionary<int, MfOperationStandard> standards, CancellationToken ct)
        {
            List<int> centerIds = standards.Values.Where(s => s.WorkCenterId.HasValue).Select(s => s.WorkCenterId.Value).Distinct().ToList();
            Dictionary<int, string> centers = centerIds.Count == 0
                ? new Dictionary<int, string>()
                : await uow.Set<MfWorkCenter>().AsNoTracking().Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            var sb = new StringBuilder();
            foreach (OperationTemplate t in steps.OrderBy(s => s.Sequence).ThenBy(s => s.Id))
            {
                sb.Append(t.Id).Append('|').Append(t.Sequence).Append('|').Append(t.Title).Append('|').Append(t.RequiresQc ? 1 : 0).Append('|').Append(t.MachineId);
                if (standards.TryGetValue(t.Id, out MfOperationStandard s))
                {
                    string wc = s.WorkCenterId.HasValue && centers.TryGetValue(s.WorkCenterId.Value, out string code) ? code : string.Empty;
                    sb.Append("|S|").Append(wc).Append('|').Append(N(s.SetupHours)).Append('|').Append(N(s.RunHoursPerUnit)).Append('|').Append(N(s.MoveDays)).Append('|').Append(N(s.QueueDays))
                      .Append('|').Append(s.IsOutside ? 1 : 0).Append('|').Append(s.DefaultSupplierId).Append('|').Append(s.OutsideLeadDays).Append('|').Append(s.WorkInstructionDocumentId)
                      .Append('|').Append(s.DrawingReferenceRule).Append('|').Append(s.RequiredQualificationKind).Append('|').Append(s.RequiredQualificationProcess)
                      .Append('|').Append((int)s.InspectionRequirement).Append('|').Append(s.ItpTestType).Append('|').Append(s.FirstArticleRequired ? 1 : 0);
                    // R1p D12: the welding procedure is part of the approved content, but only a welding step adds it, so the hash of every other step (and every revision approved before) stays as it was
                    if (s.IsWelding) sb.Append("|W|").Append(s.WpsId);
                }
                sb.Append('\n');
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).Substring(0, 32).ToLowerInvariant();
        }

        /// <summary>The key (item or nearest group with steps) and its steps for an item.</summary>
        public static async Task<MfResolvedRouting> KeyForItemAsync(IUnitOfWork uow, InventoryItem item, CancellationToken ct)
        {
            var result = new MfResolvedRouting();
            List<OperationTemplate> own = await StepsOfKeyAsync(uow, item.Id, null, false, ct);
            if (own.Count > 0)
            {
                result.KeyItemId = item.Id;
                result.Steps = own;
                return result;
            }
            var groups = await uow.Set<ItemGroup>().AsNoTracking().Where(g => !g.Deleted).Select(g => new { g.Id, g.ParentId }).ToListAsync(ct);
            int? groupId = item.ItemGroupId;
            var visited = new HashSet<int>();
            while (groupId.HasValue && visited.Add(groupId.Value))
            {
                List<OperationTemplate> list = await StepsOfKeyAsync(uow, null, groupId, false, ct);
                if (list.Count > 0)
                {
                    result.KeyGroupId = groupId;
                    result.Steps = list;
                    return result;
                }
                int current = groupId.Value;
                groupId = groups.FirstOrDefault(g => g.Id == current)?.ParentId;
            }
            return result;
        }

        public static async Task<MfResolvedRouting> ResolveAsync(IUnitOfWork uow, InventoryItem item, CancellationToken ct)
        {
            MfResolvedRouting r = await KeyForItemAsync(uow, item, ct);
            if (r.Steps.Count == 0)
            {
                r.Message = $"برای کالای {item.Code} یا گروه آن مسیر ساخت (مراحل) تعریف نشده است.";
                return r;
            }
            r.Standards = await StandardsAsync(uow, r.Steps.Select(s => s.Id), ct);
            r.Hash = await HashAsync(uow, r.Steps, r.Standards, ct);
            IQueryable<MfRoutingRevision> revisions = uow.Set<MfRoutingRevision>().AsNoTracking().Where(v => !v.Deleted && v.Status == MfRoutingStatus.Approved);
            revisions = r.KeyItemId.HasValue ? revisions.Where(v => v.ItemId == r.KeyItemId.Value) : revisions.Where(v => v.ItemGroupId == r.KeyGroupId.Value);
            string hash = r.Hash;
            r.Revision = await revisions.Where(v => v.ContentHash == hash).OrderByDescending(v => v.Revision).FirstOrDefaultAsync(ct);
            if (r.Revision == null) r.Message = $"مسیر ساخت کالای {item.Code} تأیید دوطرفه (مدیر تولید و مهندسی) برای محتوای فعلی ندارد.";
            return r;
        }

        /// <summary>The people who wrote or last changed the steps and standards: they cannot approve the revision.</summary>
        public static async Task<HashSet<int>> EditorsAsync(IUnitOfWork uow, List<OperationTemplate> steps, CancellationToken ct)
        {
            List<int> ids = steps.Select(s => s.Id).ToList();
            var users = new HashSet<int>();
            foreach (OperationTemplate t in steps) { users.Add(t.CreatorUserId); users.Add(t.ModifierUserId); }
            var standards = await uow.Set<MfOperationStandard>().AsNoTracking().Where(s => ids.Contains(s.OperationTemplateId) && !s.Deleted)
                .Select(s => new { s.CreatorUserId, s.ModifierUserId }).ToListAsync(ct);
            foreach (var s in standards) { users.Add(s.CreatorUserId); users.Add(s.ModifierUserId); }
            users.Remove(0);
            return users;
        }
    }
}
