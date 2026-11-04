using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectBalance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProductBank;
using ViewModels.NasbNiro.ProjectBalanceDtos;

namespace Services.NasbNiro.ProjectBalance
{
    public interface IKitService
    {
        /// <summary>Drafts the point-of-use kits (a draft material request per activity) for the activities that start within the days ahead of the setting; PLANNING may press it, the nightly job calls it too. Never submits or approves.</summary>
        Task<ResponseDto<KitRunResultDto>> DraftKitsAsync(bool fromJob, CancellationToken ct);
    }

    /// <summary>
    /// The 7-day point-of-use kit: stock that is reserved for an activity that starts soon is asked from the warehouse as one draft material request per activity (the existing approvals then apply).
    /// The draft is made once per activity: an activity that already has a kit that is not rejected is skipped.
    /// </summary>
    public sealed class KitService : PbServiceBase, IKitService
    {
        public KitService(IUnitOfWork uow, IInventoryUserContext user) : base(uow, user)
        {
        }

        public async Task<ResponseDto<KitRunResultDto>> DraftKitsAsync(bool fromJob, CancellationToken ct)
        {
            if (!fromJob && (!InRole(BalanceRoles.Planning) || IsAi)) return ResponseDtoHelper.BadRequest<KitRunResultDto>(BalanceMessages.NotAllowed);
            BalanceSettings s = await BalanceSettingKeys.LoadAsync(Uow, ct);
            var result = new KitRunResultDto();
            if (fromJob && !s.KitJobEnabled) { result.Notes.Add("کار خودکار کیت در تنظیمات خاموش است."); return ResponseDtoHelper.Ok(result); }
            DateTime today = DateTime.Today;
            DateTime last = today.AddDays(s.KitDaysAhead);
            int me = User.UserId;

            List<int> baselineIds = await Uow.Set<PbProjectBaseline>().AsNoTracking().Where(x => !x.Deleted && x.IsCurrent).Select(x => x.Id).ToListAsync(ct);
            foreach (int baselineId in baselineIds)
            {
                ProjectMaterialBalance run = await Uow.Set<ProjectMaterialBalance>().AsNoTracking().Where(x => !x.Deleted && x.BaselineId == baselineId).OrderByDescending(x => x.RunNumber).FirstOrDefaultAsync(ct);
                if (run == null) continue;
                List<ProjectMaterialBalanceLine> lines = await Uow.Set<ProjectMaterialBalanceLine>().AsNoTracking()
                    .Where(l => l.BalanceId == run.Id && l.ItemId != null && l.ActivityId != null && l.UseByDate != null && l.UseByDate >= today && l.UseByDate <= last && l.ReservedQty > 0).ToListAsync(ct);
                foreach (var group in lines.GroupBy(l => l.ActivityId.Value))
                {
                    result.Activities++;
                    int activityId = group.Key;
                    if (await Uow.Set<MaterialRequest>().AnyAsync(m => !m.Deleted && m.ActivityId == activityId && m.Status != MaterialRequestStatus.Rejected, ct)) { result.Skipped++; continue; }
                    string activity = await Uow.Set<Entities.Model.NasbNiro.ProjectControl.PcActivity>().AsNoTracking().Where(a => a.Id == activityId).Select(a => a.Title).FirstOrDefaultAsync(ct);
                    string wo = await Uow.Set<WorkOrder>().AsNoTracking().Where(w => w.Id == run.WorkOrderId).Select(w => w.Code).FirstOrDefaultAsync(ct);
                    DateTimeOffset now = DateTimeOffset.Now;
                    var kit = new MaterialRequest
                    {
                        Number = await InventoryNumbers.NextNumberAsync(Uow, InventoryNumbers.MaterialRequest, "MR", today, ct), RequestDate = today, Status = MaterialRequestStatus.Draft, WorkOrderId = run.WorkOrderId, ActivityId = activityId,
                        Purpose = "کیت نقطهٔ مصرف (۷ روز) برای فعالیت «" + activity + "» دستور کار " + wo, CreateDate = now, ModifyDate = now, CreatorUserId = me, ModifierUserId = me, Lines = new List<MaterialRequestLine>(),
                    };
                    int row = 1;
                    foreach (var byItem in group.GroupBy(l => l.ItemId.Value).OrderBy(g => g.Key))
                    {
                        decimal qty = BalanceRules.Round(byItem.Sum(l => l.ReservedQty));
                        if (qty <= 0) continue;
                        kit.Lines.Add(new MaterialRequestLine { RowNumber = row++, ItemId = byItem.Key, Quantity = qty, Description = "رزرو پروژه، نیاز تا " + byItem.Min(l => l.UseByDate)?.ToString("yyyy-MM-dd") });
                    }
                    if (kit.Lines.Count == 0) { result.Skipped++; continue; }
                    Uow.Set<MaterialRequest>().Add(kit);
                    try { await Uow.SaveChangesAsync(ct); }
                    catch (DbUpdateException) { if (Uow is DbContext db) db.ChangeTracker.Clear(); result.Skipped++; continue; }
                    result.Drafted++;
                    result.Requests.Add(kit.Number);
                }
            }
            return ResponseDtoHelper.Ok(result);
        }
    }

    /// <summary>The nightly material balance and the kit job. Switched off with <c>Balance:Jobs=false</c> (environment variable <c>Balance__Jobs</c>); the CI sets it so that its figures do not depend on the clock.</summary>
    public class ProjectBalanceHostedService : Microsoft.Extensions.Hosting.BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly Microsoft.Extensions.Logging.ILogger<ProjectBalanceHostedService> _logger;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
        private DateTime _doneOn = DateTime.MinValue;

        public ProjectBalanceHostedService(IServiceScopeFactory scopes, Microsoft.Extensions.Logging.ILogger<ProjectBalanceHostedService> logger, Microsoft.Extensions.Configuration.IConfiguration configuration = null)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _logger = logger;
            _configuration = configuration;
        }

        internal static bool Enabled(Microsoft.Extensions.Configuration.IConfiguration configuration) => !string.Equals(configuration?["Balance:Jobs"], "false", StringComparison.OrdinalIgnoreCase);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); }
            catch (OperationCanceledException) { return; }
            while (!stoppingToken.IsCancellationRequested)
            {
                DateTime now = DateTime.Now;
                if (Enabled(_configuration) && now.Hour >= 3 && _doneOn < now.Date)
                {
                    try
                    {
                        using (IServiceScope scope = _scopes.CreateScope())
                        {
                            IProjectBalanceService balance = scope.ServiceProvider.GetRequiredService<IProjectBalanceService>();
                            int runs = await balance.RunNightlyAsync(stoppingToken);
                            _logger?.LogInformation("Material balance: {Count} run(s) stored.", runs);
                        }
                        using (IServiceScope scope = _scopes.CreateScope())
                        {
                            IKitService kits = scope.ServiceProvider.GetRequiredService<IKitService>();
                            await kits.DraftKitsAsync(true, stoppingToken);
                        }
                        _doneOn = now.Date;
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { _logger?.LogWarning(ex, "Material balance nightly job failed; it will retry."); }
                }
                try { await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
