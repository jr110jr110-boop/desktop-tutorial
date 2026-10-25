using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Ceo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.CeoDtos;
using ViewModels.NasbNiro.FinanceDtos;

namespace Services.NasbNiro.Ceo
{
    public interface ICeoSnapshotService
    {
        /// <summary>"Snapshot now" (CEO, PMO): stores today's figures and says how many keys are stored for today.</summary>
        Task<ResponseDto<CeoSnapshotResultDto>> TakeNowAsync(CancellationToken ct);

        /// <summary>The job: stores the figures for a day without a user. Running it again for the same day replaces the values (one row per day and key). Returns the keys stored for the day.</summary>
        Task<int> RunAsync(DateTime day, CancellationToken ct);
    }

    /// <summary>
    /// The nightly snapshots of the figures that have no transactional history (cash balance, receivables, stock value, open counts, ...).
    /// The figures are computed exactly as the overview computes them. Computing the production figures loads the production orders into the context of the scope
    /// and sets their lateness in memory (the same call as the CEO dashboard makes), so the rows are written through another scope and context.
    /// </summary>
    public class CeoSnapshotService : ICeoSnapshotService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ICeoDashboardService _dashboard;
        private readonly IServiceScopeFactory _scopes;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CeoSnapshotService> _logger;

        public CeoSnapshotService(IUnitOfWork uow, IInventoryUserContext user, ICeoDashboardService dashboard, IServiceScopeFactory scopes, IMemoryCache cache, ILogger<CeoSnapshotService> logger)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger;
        }

        public async Task<ResponseDto<CeoSnapshotResultDto>> TakeNowAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(CeoRoles.Readers)) return ResponseDtoHelper.BadRequest<CeoSnapshotResultDto>(CeoText.NotAllowed);
            DateTime day = DateTime.Today;
            int keys = await RunAsync(day, ct);
            return ResponseDtoHelper.Ok(new CeoSnapshotResultDto { DateText = CeoText.Date(day), Keys = keys });
        }

        public async Task<int> RunAsync(DateTime day, CancellationToken ct)
        {
            day = day.Date;
            List<KeyValuePair<string, decimal>> values = await ComputeAsync(day, ct);
            int keys = await StoreAsync(day, values, ct);
            // the trends changed: the cached overviews are older than the snapshot
            CeoCache.Clear(_cache);
            _logger?.LogInformation("CEO snapshot: {Keys} figures stored for {Day:yyyy-MM-dd}.", keys, day);
            return keys;
        }

        /// <summary>The figures of the snapshot keys, in the order of <see cref="CeoKeys.Snapshot"/>.</summary>
        private async Task<List<KeyValuePair<string, decimal>>> ComputeAsync(DateTime day, CancellationToken ct)
        {
            ResponseDto<CeoDashboardDto> answer = await _dashboard.GetAsync(ct);
            if (answer == null || !answer.Success || answer.Data == null) throw new InvalidOperationException("The CEO dashboard is not available: " + answer?.Message);
            decimal payablesOverdue = await CeoFigures.PayablesOverdueAsync(_uow, day, ct);
            CeoBacklog backlog = await CeoFigures.SalesBacklogAsync(_uow, ct);
            return CeoFigures.SnapshotValues(answer.Data, payablesOverdue, backlog.Total);
        }

        /// <summary>
        /// Writes the values for the day: a key that exists is updated, a missing one is added. Two runs at the same moment can both find a key missing;
        /// the unique index refuses the second insert, and that run reads the rows again and updates them.
        /// </summary>
        private async Task<int> StoreAsync(DateTime day, IReadOnlyList<KeyValuePair<string, decimal>> values, CancellationToken ct)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using (IServiceScope scope = _scopes.CreateScope())
                    {
                        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                        DateTimeOffset now = DateTimeOffset.Now;
                        Dictionary<string, CeoKpiSnapshot> existing = (await uow.Set<CeoKpiSnapshot>().Where(s => s.Date == day).ToListAsync(ct)).ToDictionary(s => s.Key);
                        foreach (KeyValuePair<string, decimal> value in values)
                        {
                            if (existing.TryGetValue(value.Key, out CeoKpiSnapshot row))
                            {
                                row.Value = value.Value;
                                row.CreatedAt = now;
                            }
                            else
                            {
                                uow.Set<CeoKpiSnapshot>().Add(new CeoKpiSnapshot { Date = day, Key = value.Key, Value = value.Value, CreatedAt = now });
                            }
                        }
                        await uow.SaveChangesAsync(ct);
                        return await uow.Set<CeoKpiSnapshot>().CountAsync(s => s.Date == day, ct);
                    }
                }
                catch (DbUpdateException) when (attempt == 0 && !ct.IsCancellationRequested)
                {
                    // the other run stored the day first: start again, the rows exist now and are updated
                }
            }
        }
    }
}
