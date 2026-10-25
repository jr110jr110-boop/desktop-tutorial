using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.AiDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Ai
{
    public interface IAiSeatRunner
    {
        /// <summary>Runs one seat now, as its own service user, and answers when it is finished (used by the scheduler and the tests). A run that cannot start answers success=false.</summary>
        Task<ResponseDto<AiSeatRunDto>> RunAsync(string code, DateTime asOf, CancellationToken ct);

        /// <summary>Opens the run (status Running) and hands the job to the background queue: answers at once with the run row, which is polled by its id. A run of the same seat that is still going refuses.</summary>
        Task<ResponseDto<AiSeatRunDto>> StartAsync(string code, DateTime asOf, CancellationToken ct);

        /// <summary>Runs the seats that are due at <paramref name="now"/> (local time), one after the other. Returns the number of runs started.</summary>
        Task<int> RunDueAsync(DateTime now, CancellationToken ct);
    }

    /// <summary>The background queue of the module: a run by hand and an evaluation are done here, one after the other, so the request that asked for them is answered at once.</summary>
    public interface IAiWorkQueue
    {
        void Enqueue(Func<CancellationToken, Task> work);
    }

    public class AiWorkQueue : BackgroundService, IAiWorkQueue
    {
        private readonly Channel<Func<CancellationToken, Task>> _channel = Channel.CreateUnbounded<Func<CancellationToken, Task>>(new UnboundedChannelOptions { SingleReader = true });
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<AiWorkQueue> _logger;

        public AiWorkQueue(IServiceScopeFactory scopes, ILogger<AiWorkQueue> logger = null)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _logger = logger;
        }

        public void Enqueue(Func<CancellationToken, Task> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            _channel.Writer.TryWrite(work);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await CloseLeftoversAsync();
            try
            {
                await foreach (Func<CancellationToken, Task> work in _channel.Reader.ReadAllAsync(stoppingToken))
                {
                    try { await work(stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception ex) { _logger?.LogError(ex, "AI background work failed"); }
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>After a restart nothing of the old process is still running: its open runs are closed so they do not block the next one.</summary>
        private async Task CloseLeftoversAsync()
        {
            try
            {
                using IServiceScope scope = _scopes.CreateScope();
                IUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                DateTimeOffset now = DateTimeOffset.Now;
                foreach (AiSeatRun r in await uow.Set<AiSeatRun>().Where(x => x.Status == AiSeatRunStatus.Running).ToListAsync())
                {
                    r.Status = AiSeatRunStatus.Skipped; r.FinishedAt = now; r.Error = "اجرا با راه‌اندازی دوباره‌ی سرویس متوقف شد.";
                }
                foreach (AiEvalRun r in await uow.Set<AiEvalRun>().Where(x => x.FinishedAt == null).ToListAsync()) r.FinishedAt = now;
                await uow.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "AI: the runs left open by the last process could not be closed"); }
        }
    }

    /// <summary>
    /// Executes the seats. A run is written to AiSeatRuns (Running, then Succeeded, Failed or Skipped) and to the audit; a failure opens an alert in the inbox of the PMO
    /// and the next successful run closes it. The steps use scopes of their own, so a database error of the job cannot stop the run from being closed.
    /// Two runs of one seat cannot overlap: the database allows one Running row per seat (a filtered unique index).
    /// </summary>
    public class AiSeatRunner : IAiSeatRunner
    {
        /// <summary>A run that has been "Running" for longer than this is taken as dead and written off as failed.</summary>
        public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);
        public const int DefaultRetryMinutes = 5;
        public const int DefaultSkipBackoffMinutes = 30;
        public const string AlreadyRunning = "اجرای قبلی همین صندلی هنوز در حال انجام است.";

        private readonly IServiceScopeFactory _scopes;
        private readonly IConfiguration _configuration;
        private readonly IAiWorkQueue _queue;
        private readonly ILogger<AiSeatRunner> _logger;

        public AiSeatRunner(IServiceScopeFactory scopes, IConfiguration configuration, IAiWorkQueue queue, ILogger<AiSeatRunner> logger = null)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _configuration = configuration;
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _logger = logger;
        }

        private int Minutes(string key, int fallback) => int.TryParse(_configuration?[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) && m > 0 ? m : fallback;

        private static AiSeatRunDto ToDto(AiSeatRun r, string seatCode) => new AiSeatRunDto
        {
            Id = r.Id, SeatId = r.SeatId, SeatCode = seatCode, StartedAt = r.StartedAt, StartedAtText = AiText.Stamp(r.StartedAt), FinishedAt = r.FinishedAt, Status = r.Status,
            Outputs = r.Outputs, Error = r.Error, AsOf = r.AsOf,
        };

        private async Task<Dictionary<string, AiActingUser>> UsersAsync(CancellationToken ct)
        {
            using IServiceScope scope = _scopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(ct);
        }

        public async Task<ResponseDto<AiSeatRunDto>> RunAsync(string code, DateTime asOf, CancellationToken ct) => await RunCoreAsync(code, asOf, false, null, ct);

        private async Task<ResponseDto<AiSeatRunDto>> RunCoreAsync(string code, DateTime asOf, bool scheduled, Dictionary<string, AiActingUser> users, CancellationToken ct)
        {
            AiSeatDefinition def = AiSeatCatalog.Find(code);
            if (def == null) return AiText.NotFound<AiSeatRunDto>("صندلی پیدا نشد.");
            try
            {
                users ??= await UsersAsync(ct);
                if (!users.TryGetValue(def.Code, out AiActingUser user)) return AiText.Bad<AiSeatRunDto>("کاربر سرویس صندلی ساخته نشد.");
                (ResponseDto<AiSeatRunDto> refused, AiSeat seat, AiSeatRun run) = await OpenAsync(def, asOf.Date, scheduled, ct);
                if (refused != null) return refused;
                return await FinishAsync(def, seat, run, asOf.Date, user, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Seat {Seat} could not be run", def.Code);
                return AiText.Bad<AiSeatRunDto>("اجرای صندلی انجام نشد: " + AiText.Cut(ex.Message, 300));
            }
        }

        public async Task<ResponseDto<AiSeatRunDto>> StartAsync(string code, DateTime asOf, CancellationToken ct)
        {
            AiSeatDefinition def = AiSeatCatalog.Find(code);
            if (def == null) return AiText.NotFound<AiSeatRunDto>("صندلی پیدا نشد.");
            try
            {
                Dictionary<string, AiActingUser> users = await UsersAsync(ct);
                if (!users.TryGetValue(def.Code, out AiActingUser user)) return AiText.Bad<AiSeatRunDto>("کاربر سرویس صندلی ساخته نشد.");
                (ResponseDto<AiSeatRunDto> refused, AiSeat seat, AiSeatRun run) = await OpenAsync(def, asOf.Date, false, ct);
                if (refused != null) return refused;
                int runId = run.Id;
                _queue.Enqueue(async stopping =>
                {
                    try { await FinishAsync(def, seat, run, asOf.Date, user, stopping); }
                    catch (Exception ex) { await CloseBrokenAsync(def, runId, ex); }
                });
                return ResponseDtoHelper.Ok(ToDto(run, def.Code));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Seat {Seat} could not be started", def.Code);
                return AiText.Bad<AiSeatRunDto>("اجرای صندلی انجام نشد: " + AiText.Cut(ex.Message, 300));
            }
        }

        /// <summary>Opens the run row. Old Running rows are closed as failed first; a second Running row for the seat is refused by the database.</summary>
        private async Task<(ResponseDto<AiSeatRunDto> Refused, AiSeat Seat, AiSeatRun Run)> OpenAsync(AiSeatDefinition def, DateTime asOf, bool scheduled, CancellationToken ct)
        {
            using IServiceScope s = _scopes.CreateScope();
            IUnitOfWork uow = s.ServiceProvider.GetRequiredService<IUnitOfWork>();
            IAiAudit audit = s.ServiceProvider.GetRequiredService<IAiAudit>();
            AiSeat seat = await uow.Set<AiSeat>().AsNoTracking().FirstOrDefaultAsync(x => x.Code == def.Code, ct);
            if (seat == null) return (AiText.Bad<AiSeatRunDto>("صندلی ثبت نشده است."), null, null);
            DateTimeOffset now = DateTimeOffset.Now;
            List<AiSeatRun> stale = (await uow.Set<AiSeatRun>().Where(r => r.SeatId == seat.Id && r.Status == AiSeatRunStatus.Running).ToListAsync(ct)).Where(r => now - r.StartedAt > StaleAfter).ToList();
            foreach (AiSeatRun old in stale)
            {
                old.Status = AiSeatRunStatus.Failed; old.FinishedAt = now; old.Error = "اجرا پایان نیافت (زمان‌بر شد) و ناتمام ثبت شد.";
                audit.Add(AiAuditKinds.Seat, $"run {old.Id} written off as failed (timeout: no result after {StaleAfter.TotalMinutes.ToString(CultureInfo.InvariantCulture)} minutes)", seatCode: def.Code);
            }
            if (stale.Count > 0) await uow.SaveChangesAsync(ct);

            var run = new AiSeatRun { SeatId = seat.Id, StartedAt = now, Status = AiSeatRunStatus.Running, AsOf = asOf, IsScheduled = scheduled };
            uow.Set<AiSeatRun>().Add(run);
            audit.Add(AiAuditKinds.Seat, $"run started as of {asOf:yyyy-MM-dd}{(scheduled ? " (scheduled)" : string.Empty)}", seatCode: def.Code);
            try
            {
                await uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // the filtered unique index: another run of this seat is Running
                return (AiText.Bad<AiSeatRunDto>(AlreadyRunning), null, null);
            }
            return (null, seat, run);
        }

        private async Task<ResponseDto<AiSeatRunDto>> FinishAsync(AiSeatDefinition def, AiSeat seat, AiSeatRun run, DateTime asOf, AiActingUser user, CancellationToken ct)
        {
            return await AiActing.RunAsync(_scopes, user, def.Code, acting => ExecuteAsync(acting, def, seat, run, asOf, ct));
        }

        private async Task CloseBrokenAsync(AiSeatDefinition def, int runId, Exception ex)
        {
            try
            {
                using IServiceScope s = _scopes.CreateScope();
                IUnitOfWork uow = s.ServiceProvider.GetRequiredService<IUnitOfWork>();
                AiSeatRun r = await uow.Set<AiSeatRun>().FirstOrDefaultAsync(x => x.Id == runId && x.Status == AiSeatRunStatus.Running, CancellationToken.None);
                if (r == null) return;
                r.Status = AiSeatRunStatus.Failed; r.FinishedAt = DateTimeOffset.Now; r.Error = AiText.Cut(ex.GetType().Name + ": " + ex.Message, 500);
                await uow.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception inner) { _logger?.LogError(inner, "Seat {Seat}: run {Run} could not be closed", def.Code, runId); }
        }

        private async Task<ResponseDto<AiSeatRunDto>> ExecuteAsync(IServiceProvider acting, AiSeatDefinition def, AiSeat seat, AiSeatRun run, DateTime asOf, CancellationToken ct)
        {
            IServiceScopeFactory scopes = acting.GetRequiredService<IServiceScopeFactory>();

            // the job, in the acting scope
            AiSeatJobResult result;
            try
            {
                IAiSeatJob job = acting.GetServices<IAiSeatJob>().FirstOrDefault(j => j.Code == def.Code);
                result = job == null
                    ? AiSeatJobResult.Fail("کار این صندلی تعریف نشده است.")
                    : await job.RunAsync(new AiSeatRunContext { Seat = seat, Definition = def, RunId = run.Id, AsOf = asOf }, ct) ?? AiSeatJobResult.Fail("کار صندلی نتیجه‌ای نداد.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // the service is stopping: the run is closed as skipped (no alert); the scheduler repeats it after the restart
                result = AiSeatJobResult.Skip(AiSeatTexts.Cancelled);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Seat {Seat} failed", def.Code);
                result = AiSeatJobResult.Fail(ex is OperationCanceledException ? "اجرا لغو شد." : AiText.Cut(ex.GetType().Name + ": " + ex.Message, 500));
            }

            // close the run
            AiSeatRun closed;
            using (IServiceScope s = scopes.CreateScope())
            {
                IUnitOfWork uow = s.ServiceProvider.GetRequiredService<IUnitOfWork>();
                IAiAudit audit = s.ServiceProvider.GetRequiredService<IAiAudit>();
                closed = await uow.Set<AiSeatRun>().FirstAsync(r => r.Id == run.Id, CancellationToken.None);
                closed.Status = result.Status == AiSeatRunStatus.Running ? AiSeatRunStatus.Succeeded : result.Status;
                closed.FinishedAt = DateTimeOffset.Now;
                closed.Outputs = AiText.Cut(result.Summary, 4000);
                closed.Error = AiText.Cut(result.Error, 4000);
                audit.Add(AiAuditKinds.Seat, $"run {closed.Id} {closed.Status.ToString().ToLowerInvariant()}; {AiText.Cut(result.Summary, 500)}{(string.IsNullOrEmpty(result.Error) ? string.Empty : "; " + AiText.Cut(result.Error, 800))}", seatCode: def.Code);
                await uow.SaveChangesAsync(CancellationToken.None);

                // the alert: a failure is shown to the PMO, a success closes it. (Skipped is a known state: the model is down or the service is stopping; the run is repeated.)
                try
                {
                    INotificationService notify = s.ServiceProvider.GetService<INotificationService>();
                    if (notify != null)
                    {
                        string key = "ai-seat:" + def.Code;
                        if (closed.Status == AiSeatRunStatus.Failed)
                            await notify.NotifyAsync(new NotifyRequest
                            {
                                SourceKey = key, Module = "ai", SourceType = "AiSeat", SourceId = seat.Id, Kind = OfInboxKind.Alert, Priority = OfUrgency.Urgent, Roles = "PMO", Link = "/ai/seats",
                                Title = AiText.Cut($"اجرای صندلی «{def.Title}» با خطا پایان یافت: {closed.Error}", 250),
                            }, CancellationToken.None);
                        else if (closed.Status == AiSeatRunStatus.Succeeded)
                            await notify.CloseAsync(key, "اجرای بعدی صندلی موفق بود.", CancellationToken.None);
                    }
                }
                catch (Exception ex) { _logger?.LogWarning(ex, "Seat {Seat}: the inbox alert could not be updated", def.Code); }
            }
            return ResponseDtoHelper.Ok(ToDto(closed, def.Code));
        }

        public async Task<int> RunDueAsync(DateTime now, CancellationToken ct)
        {
            // the service users are looked up once per tick
            Dictionary<string, AiActingUser> users;
            try { users = await UsersAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "AI seats: the service users could not be prepared; the tick is skipped");
                return 0;
            }

            // only scheduled runs decide whether a slot is still due (a run by hand, maybe for another day, does not)
            List<(string Code, DateTime? LastAttempt, DateTime? LastSuccess, string Schedule, AiSeatRunStatus? LastStatus, string LastError)> seats;
            using (IServiceScope scope = _scopes.CreateScope())
            {
                IUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var rows = await uow.Set<AiSeat>().AsNoTracking().Where(s => s.Enabled).OrderBy(s => s.Id).ToListAsync(ct);
                seats = new List<(string, DateTime?, DateTime?, string, AiSeatRunStatus?, string)>();
                foreach (AiSeat s in rows)
                {
                    var last = await uow.Set<AiSeatRun>().AsNoTracking().Where(r => r.SeatId == s.Id && r.IsScheduled).OrderByDescending(r => r.Id).Select(r => new { r.StartedAt, r.Status, r.Error }).FirstOrDefaultAsync(ct);
                    DateTimeOffset? attempt = await uow.Set<AiSeatRun>().AsNoTracking().Where(r => r.SeatId == s.Id && r.IsScheduled).MaxAsync(r => (DateTimeOffset?)r.StartedAt, ct);
                    DateTimeOffset? success = await uow.Set<AiSeatRun>().AsNoTracking().Where(r => r.SeatId == s.Id && r.IsScheduled && r.Status == AiSeatRunStatus.Succeeded).MaxAsync(r => (DateTimeOffset?)r.StartedAt, ct);
                    seats.Add((s.Code, attempt?.LocalDateTime, success?.LocalDateTime, s.Schedule, last?.Status, last?.Error));
                }
            }

            int started = 0;
            foreach (var s in seats)
            {
                if (ct.IsCancellationRequested) break;
                AiSchedule schedule = AiSchedule.Parse(s.Schedule);
                if (schedule == null || !schedule.IsDue(now, s.LastAttempt, s.LastSuccess, Minutes(AiConfigKeys.RetryMinutes, DefaultRetryMinutes))) continue;
                if (IsBackedOff(s.LastStatus, s.LastError, s.LastAttempt, now, Minutes(AiConfigKeys.SkipBackoffMinutes, DefaultSkipBackoffMinutes))) continue;
                var result = await RunCoreAsync(s.Code, now.Date, true, users, ct);
                if (result.Success) started++;
            }
            return started;
        }

        /// <summary>The model was down at the last scheduled run: the seat waits for the back-off before it asks again (instead of trying every few minutes).</summary>
        public static bool IsBackedOff(AiSeatRunStatus? lastStatus, string lastError, DateTime? lastAttempt, DateTime now, int backoffMinutes) =>
            lastStatus == AiSeatRunStatus.Skipped && lastAttempt.HasValue && !string.IsNullOrEmpty(lastError) && lastError.StartsWith(AiSeatTexts.LlmDown, StringComparison.Ordinal)
            && (now - lastAttempt.Value).TotalMinutes < backoffMinutes;
    }

    /// <summary>
    /// Ticks every minute and runs the seats that are due. Switch: Ai:Jobs=false. The seats run one after the other, so the model is never asked by two seats at once.
    /// </summary>
    public class AiSeatScheduler : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AiSeatScheduler> _logger;

        public AiSeatScheduler(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<AiSeatScheduler> logger = null)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _configuration = configuration;
            _logger = logger;
        }

        public bool Enabled => !string.Equals(_configuration?[AiConfigKeys.Jobs], "false", StringComparison.OrdinalIgnoreCase);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) { return; }
            while (!stoppingToken.IsCancellationRequested)
            {
                if (Enabled)
                {
                    try
                    {
                        using IServiceScope scope = _scopes.CreateScope();
                        int n = await scope.ServiceProvider.GetRequiredService<IAiSeatRunner>().RunDueAsync(DateTime.Now, stoppingToken);
                        if (n > 0) _logger?.LogInformation("AI seats: {Count} runs started", n);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { _logger?.LogWarning(ex, "AI seat scheduler tick failed; it will try again next minute."); }
                }
                try { await Task.Delay(TimeSpan.FromSeconds(60 - DateTime.Now.Second), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    public interface IAiSeatAdminService
    {
        Task<ResponseDto<List<AiSeatDto>>> GetSeatsAsync(CancellationToken ct);
        Task<ResponseDto<AiSeatDto>> EnableAsync(AiEnableSeatDto dto, CancellationToken ct);
        Task<ResponseDto<AiSeatRunDto>> RunAsync(AiRunSeatDto dto, CancellationToken ct);
        Task<ResponseDto<PagedList<AiSeatRunDto>>> GetRunsAsync(AiSeatRunQuery query, CancellationToken ct);
        Task<ResponseDto<AiSeatRunDto>> GetRunAsync(int id, CancellationToken ct);
    }

    /// <summary>The seats page: PMO changes and runs, PMO and CEO read.</summary>
    public class AiSeatAdminService : IAiSeatAdminService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IAiAudit _audit;
        private readonly IAiSeatProvisioner _provisioner;
        private readonly IAiSeatRunner _runner;
        private readonly IConfiguration _configuration;
        private readonly INotificationService _notify;

        public AiSeatAdminService(IUnitOfWork uow, IInventoryUserContext user, IAiAudit audit, IAiSeatProvisioner provisioner, IAiSeatRunner runner, IConfiguration configuration, INotificationService notify = null)
        {
            _notify = notify;
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _configuration = configuration;
        }

        private static AiSeatDto ToDto(AiSeat s, AiSeatRunDto last) => new AiSeatDto
        {
            Id = s.Id, Code = s.Code, Title = s.Title, ServiceUserName = s.ServiceUserName, Roles = s.Roles, Schedule = s.Schedule, ScheduleText = AiSchedule.Parse(s.Schedule)?.Describe() ?? s.Schedule,
            PromptVersionCode = s.PromptVersionCode, Enabled = s.Enabled, OutputKind = s.OutputKind, LastRun = last,
        };

        private async Task<AiSeatRunDto> LastRunAsync(AiSeat s, CancellationToken ct)
        {
            AiSeatRun r = await _uow.Set<AiSeatRun>().AsNoTracking().Where(x => x.SeatId == s.Id).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            return r == null ? null : new AiSeatRunDto
            {
                Id = r.Id, SeatId = r.SeatId, SeatCode = s.Code, StartedAt = r.StartedAt, StartedAtText = AiText.Stamp(r.StartedAt), FinishedAt = r.FinishedAt, Status = r.Status,
                Outputs = r.Outputs, Error = r.Error, AsOf = r.AsOf,
            };
        }

        public async Task<ResponseDto<List<AiSeatDto>>> GetSeatsAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(AiRoles.Readers)) return AiText.Bad<List<AiSeatDto>>(AiText.NotAllowed);
            await _provisioner.EnsureAsync(ct);
            List<AiSeat> seats = await _uow.Set<AiSeat>().AsNoTracking().OrderBy(s => s.Id).ToListAsync(ct);
            var result = new List<AiSeatDto>();
            foreach (AiSeat s in seats) result.Add(ToDto(s, await LastRunAsync(s, ct)));
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<AiSeatDto>> EnableAsync(AiEnableSeatDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(AiRoles.Admins)) return AiText.Bad<AiSeatDto>(AiText.NotAllowed);
            string code = AiText.Clean(dto?.Code);
            if (code == null) return AiText.Bad<AiSeatDto>(AiText.InvalidData);
            await _provisioner.EnsureAsync(ct);
            AiSeat seat = await _uow.Set<AiSeat>().FirstOrDefaultAsync(s => s.Code == code, ct);
            if (seat == null) return AiText.NotFound<AiSeatDto>("صندلی پیدا نشد.");
            if (seat.Enabled != dto.Enabled)
            {
                seat.Enabled = dto.Enabled;
                _audit.Add(AiAuditKinds.Admin, $"seat {seat.Code} {(dto.Enabled ? "enabled" : "disabled")}", seatCode: seat.Code);
                await _uow.SaveChangesAsync(ct);
                // a seat that is switched off raises no alert: the one of its last failure is closed
                if (!dto.Enabled && _notify != null)
                {
                    try { await _notify.CloseAsync("ai-seat:" + seat.Code, "صندلی غیرفعال شد.", ct); }
                    catch (Exception) { /* the switch is saved; a stuck alert is closed by the next success */ }
                }
            }
            return ResponseDtoHelper.Ok(ToDto(seat, await LastRunAsync(seat, ct)));
        }

        public async Task<ResponseDto<AiSeatRunDto>> RunAsync(AiRunSeatDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(AiRoles.Admins)) return AiText.Bad<AiSeatRunDto>(AiText.NotAllowed);
            string code = AiText.Clean(dto?.Code);
            if (code == null) return AiText.Bad<AiSeatRunDto>(AiText.InvalidData);
            AiSeatDefinition def = AiSeatCatalog.Find(code);
            if (def == null) return AiText.NotFound<AiSeatRunDto>("صندلی پیدا نشد.");
            DateTime today = DateTime.Today;
            DateTime asOf = (dto.AsOf ?? today).Date;
            bool allowFuture = string.Equals(_configuration?[AiConfigKeys.AllowFutureAsOf], "true", StringComparison.OrdinalIgnoreCase);
            if (asOf > today && !allowFuture) return AiText.Bad<AiSeatRunDto>("اجرای صندلی برای تاریخ آینده مجاز نیست.");
            await _provisioner.EnsureAsync(ct);
            AiSeat seat = await _uow.Set<AiSeat>().AsNoTracking().FirstOrDefaultAsync(s => s.Code == def.Code, ct);
            if (seat != null && !seat.Enabled) return AiText.Bad<AiSeatRunDto>("این صندلی غیرفعال است؛ ابتدا آن را فعال کنید.");
            _audit.Add(AiAuditKinds.Admin, $"seat {def.Code} run by hand as of {asOf:yyyy-MM-dd}", seatCode: def.Code);
            await _uow.SaveChangesAsync(ct);
            return await _runner.StartAsync(def.Code, asOf, ct);
        }

        public async Task<ResponseDto<AiSeatRunDto>> GetRunAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(AiRoles.Readers)) return AiText.Bad<AiSeatRunDto>(AiText.NotAllowed);
            AiSeatRunDto r = await _uow.Set<AiSeatRun>().AsNoTracking().Where(x => x.Id == id).Select(x => new AiSeatRunDto
            {
                Id = x.Id, SeatId = x.SeatId, SeatCode = x.Seat.Code, StartedAt = x.StartedAt, FinishedAt = x.FinishedAt, Status = x.Status, Outputs = x.Outputs, Error = x.Error, AsOf = x.AsOf,
            }).FirstOrDefaultAsync(ct);
            if (r == null) return AiText.NotFound<AiSeatRunDto>("اجرای صندلی پیدا نشد.");
            r.StartedAtText = AiText.Stamp(r.StartedAt);
            return ResponseDtoHelper.Ok(r);
        }

        public async Task<ResponseDto<PagedList<AiSeatRunDto>>> GetRunsAsync(AiSeatRunQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(AiRoles.Readers)) return AiText.Bad<PagedList<AiSeatRunDto>>(AiText.NotAllowed);
            query ??= new AiSeatRunQuery();
            IQueryable<AiSeatRun> q = _uow.Set<AiSeatRun>().AsNoTracking();
            string seat = AiText.Clean(query.Seat);
            if (seat != null) q = q.Where(r => r.Seat.Code == seat);
            if (query.Status.HasValue) q = q.Where(r => r.Status == query.Status.Value);
            IQueryable<AiSeatRunDto> proj = q.OrderByDescending(r => r.Id).Select(r => new AiSeatRunDto
            {
                Id = r.Id, SeatId = r.SeatId, SeatCode = r.Seat.Code, StartedAt = r.StartedAt, FinishedAt = r.FinishedAt, Status = r.Status, Outputs = r.Outputs, Error = r.Error, AsOf = r.AsOf,
            });
            PagedList<AiSeatRunDto> page = await AiText.PageAsync(proj, query.PageNumber, query.PageSize, ct);
            foreach (AiSeatRunDto r in page) r.StartedAtText = AiText.Stamp(r.StartedAt);
            return ResponseDtoHelper.Ok(page);
        }
    }
}
