using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Services.NasbNiro.Inventory;

namespace Services.NasbNiro.ProductBank
{
    /// <summary>Shared pieces of the product bank services: stamping, the rule that an AI seat never signs or approves, the stage dictionary and the item lookups.</summary>
    public abstract class PbServiceBase
    {
        protected readonly IUnitOfWork Uow;
        protected readonly IInventoryUserContext User;

        protected PbServiceBase(IUnitOfWork uow, IInventoryUserContext user)
        {
            Uow = uow ?? throw new ArgumentNullException(nameof(uow));
            User = user ?? throw new ArgumentNullException(nameof(user));
        }

        protected void Stamp(BaseEntity entity, bool isNew)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            if (isNew)
            {
                entity.CreateDate = now;
                entity.CreatorUserId = User.UserId;
            }
            entity.ModifyDate = now;
            entity.ModifierUserId = User.UserId;
        }

        /// <summary>An AI seat prepares drafts; it never signs, approves, releases or decides.</summary>
        protected static bool IsAi => Ai.AiActing.IsActing;

        protected bool InRole(string roles) => User.IsInRole(roles);

        /// <summary>Stages are seeded from the defaults the first time the table is read (the same way as the coding tables of the warehouse).</summary>
        protected async Task<List<PbStage>> LoadStagesAsync(bool tracked, CancellationToken ct)
        {
            IQueryable<PbStage> q = Uow.Set<PbStage>().Where(s => !s.Deleted);
            List<PbStage> list = await (tracked ? q : q.AsNoTracking()).OrderBy(s => s.SortOrder).ToListAsync(ct);
            if (list.Count > 0 || await Uow.Set<PbStage>().AnyAsync(ct)) return list;
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (PbStage s in PbStageDefaults.All())
            {
                s.CreateDate = now; s.ModifyDate = now; s.CreatorUserId = User.UserId; s.ModifierUserId = User.UserId;
                Uow.Set<PbStage>().Add(s);
            }
            try { await Uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { /* another request seeded them at the same time */ }
            q = Uow.Set<PbStage>().Where(s => !s.Deleted);
            return await (tracked ? q : q.AsNoTracking()).OrderBy(s => s.SortOrder).ToListAsync(ct);
        }

        protected async Task<string> SeparatorAsync(CancellationToken ct) =>
            (await PbSettingKeys.LoadAsync(Uow, ct))[PbSettingKeys.StageSeparator] ?? "-";

        /// <summary>A new item of a part (stageCode null) or of a part at a stage. The caller sets the code of a stage item (part number + separator + stage) and adds the item to the context.</summary>
        protected InventoryItem NewPartItem(string pn, string stageCode, string title, string unit, string material, string standard, int? groupId, ItemTrackingMode? tracking, InventoryItem partItem)
        {
            string group = partItem?.GroupCode ?? PbCodeRules.GroupOf(pn);
            var item = new InventoryItem
            {
                Code = pn,
                Title = title,
                PartNumber = pn,
                StageCode2 = stageCode,
                PartItemId = partItem?.Id,
                Unit = PbText.Clean(unit) ?? partItem?.Unit ?? "عدد",
                Material = PbText.Clean(material) ?? partItem?.Material,
                Standard = PbText.Clean(standard) ?? partItem?.Standard,
                ItemGroupId = groupId ?? partItem?.ItemGroupId,
                TrackingMode = tracking ?? partItem?.TrackingMode ?? ItemTrackingMode.None,
                GroupCode = group,
                BaseCode = pn,
                MescCode = partItem == null && group == ItemCodeRules.StandardPartGroup && ItemCodeRules.IsMesc(pn) ? pn : null,
                IsActive = true,
            };
            Stamp(item, true);
            return item;
        }

        /// <summary>
        /// The item of a part (code = part number). An existing item that has no part number yet but whose code or MESC code IS the part number (a standard part coded by WI-WH-002, or a legacy item whose MESC code it is)
        /// is adopted: its PartNumber is set (audited) instead of a second item being made. With adopt = false nothing is changed (previews).
        /// </summary>
        protected async Task<InventoryItem> FindPartItemAsync(string pn, bool adopt, CancellationToken ct)
        {
            IQueryable<InventoryItem> q = Uow.Set<InventoryItem>().Where(i => !i.Deleted && i.StageCode2 == null && (i.PartNumber == pn || (i.PartNumber == null && (i.Code == pn || i.MescCode == pn))));
            if (!adopt) q = q.AsNoTracking();
            InventoryItem item = await q.OrderByDescending(i => i.PartNumber == pn).ThenByDescending(i => i.Code == pn).ThenBy(i => i.Id).FirstOrDefaultAsync(ct);
            if (item != null && adopt && item.PartNumber == null)
            {
                item.PartNumber = pn;
                if (string.IsNullOrEmpty(item.BaseCode)) item.BaseCode = item.Code;
                Stamp(item, false);
            }
            return item;
        }

        /// <summary>The item of a part (code = part number); made when the part has none. A code that belongs to an item of another part is refused.</summary>
        protected async Task<(InventoryItem Item, string Error)> FindOrCreatePartItemAsync(string pn, string title, string unit, string material, string standard, CancellationToken ct, int? groupId = null, ItemTrackingMode? tracking = null)
        {
            string shape = PbCodeRules.ShapeError(pn);
            if (shape != null) return (null, shape);
            InventoryItem item = await FindPartItemAsync(pn, true, ct);
            if (item != null) return (item, null);
            InventoryItem clash = await Uow.Set<InventoryItem>().FirstOrDefaultAsync(i => !i.Deleted && i.Code == pn, ct);
            if (clash != null) return (null, "کد " + pn + " برای کالای دیگری به کار رفته است: " + clash.Title);
            item = NewPartItem(pn, null, PbText.Clean(title) ?? pn, unit, material, standard, groupId, tracking, null);
            Uow.Set<InventoryItem>().Add(item);
            return (item, null);
        }

        /// <summary>Saves; false with the Persian message when another request changed the same document at the same time.</summary>
        protected async Task<string> TrySaveAsync(CancellationToken ct)
        {
            try { await Uow.SaveChangesAsync(ct); return null; }
            catch (DbUpdateConcurrencyException) { return PbMessages.Concurrent; }
        }

        /// <summary>The setting "the four BOM reviewers must be different people" (default false).</summary>
        protected async Task<bool> DistinctReviewersAsync(CancellationToken ct) =>
            (await PbSettingKeys.LoadAsync(Uow, ct))[PbSettingKeys.DistinctReviewers] == "true";

        /// <summary>
        /// Runs the work in one database transaction (SQL Server); a work that returns a failed result rolls the transaction back. The database of the tests has no transactions, so the work simply runs.
        /// With the retrying execution strategy the whole delegate is repeated, so it must load what it changes itself.
        /// </summary>
        protected async Task<T> InTransactionAsync<T>(Func<Task<T>> work, Func<T, bool> succeeded, CancellationToken ct)
        {
            if (!(Uow is DbContext db) || !db.Database.IsRelational()) return await work();
            IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();
            int attempt = 0;
            return await strategy.ExecuteAsync<T>(async token =>
            {
                if (attempt++ > 0) db.ChangeTracker.Clear();
                await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(token);
                T result = await work();
                if (succeeded(result)) await transaction.CommitAsync(token);
                else await transaction.RollbackAsync(token);
                return result;
            }, ct);
        }
    }
}
