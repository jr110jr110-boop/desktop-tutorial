using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Integrity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.IntegrityDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Integrity
{
    public static class IntegrityRoles
    {
        /// <summary>Who reads the page «سلامت سوابق» and may start a verification by hand. (The admin of the system holds the PMO role.)</summary>
        public const string Readers = "PMO,CEO,QA,AUDITOR,FINANCEMANAGER";

        /// <summary>Who is told when the chain is broken: the QA manager, the PMO and the CEO.</summary>
        public const string AlertRoles = "QA,PMO,CEO";

        /// <summary>Fix round (M8): who starts a full pass by hand (it can run for minutes and loads the database): the PMO, the CEO and the QA manager.</summary>
        public const string FullPass = "PMO,CEO,QA";

        public const string NotAllowed = "شما مجوز دیدن یا اجرای بررسی سلامت سوابق را ندارید.";
    }

    /// <summary>A page whose items were cut in the database.</summary>
    internal sealed class IntegrityPage<T> : PagedList<T>
    {
        public IntegrityPage(IEnumerable<T> items, int count, int pageNumber, int pageSize) : base(items, count, pageNumber, pageSize) { }
    }

    public interface IAuditIntegrityService
    {
        /// <summary>A verification started by a person (PMO, CEO, QA, AUDITOR, FINANCEMANAGER): "incremental" from the checkpoint, or "full" from the first row.</summary>
        Task<ResponseDto<AuditChainRunDto>> RunOnDemandAsync(AuditChainRunRequestDto dto, CancellationToken ct);

        /// <summary>The scheduled run (nightly incremental, a full pass weekly and when none was made for a week). No user; alerts on a break.</summary>
        Task<AuditChainRunDto> RunScheduledAsync(DateTime now, CancellationToken ct);

        Task<ResponseDto<PagedList<AuditChainRunDto>>> GetRunsAsync(AuditChainRunQuery query, CancellationToken ct);

        /// <summary>One run (the page polls a queued full pass until it is no longer Running).</summary>
        Task<ResponseDto<AuditChainRunDto>> GetRunAsync(int id, CancellationToken ct);

        /// <summary>The body of a queued full pass (called by the background task the on-demand call starts, and by the tests).</summary>
        Task ExecuteQueuedAsync(int runId, CancellationToken ct);

        /// <summary>The two lights: the chain and the restore test.</summary>
        Task<ResponseDto<IntegritySummaryDto>> GetSummaryAsync(CancellationToken ct);
    }

    /// <summary>
    /// Package D: the scheduled verification of the HMAC chain of the audit trail (H9, ISO 27001 A.8.15). Every night at 02:30 the part of the chain written since the last good run is verified from its checkpoint
    /// (the head the last good run recorded; the row at that place must still carry the recorded seal), and once a week (★ Friday night) or when no full pass was made for eight days the whole chain is walked.
    /// A break writes the run with the first broken entry (kind, place in the chain, row id, record type, time) and puts an alert in the inbox of the QA manager, the PMO and the CEO.
    /// Everything is also available on demand. One run at a time per server process.
    /// </summary>
    public class AuditIntegrityService : IAuditIntegrityService
    {
        /// <summary>★ The weekday of the weekly full pass (Friday is the weekend of the company), and the longest time without one.</summary>
        public const DayOfWeek FullPassDay = DayOfWeek.Friday;
        public const int FullPassMaxDays = 8;

        /// <summary>★ The restore test is red on the dashboard when the last good one is older than this many days.</summary>
        public const int RestoreWarnDays = 35;

        private static readonly SemaphoreSlim OneAtATime = new SemaphoreSlim(1, 1);

        private readonly IUnitOfWork _uow;
        private readonly IAuditChainService _chain;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;
        private readonly IRestoreTestService _restore;
        private readonly ILogger<AuditIntegrityService> _logger;
        private readonly IServiceScopeFactory _scopes;

        /// <summary>★ A queued run that did not finish within this time is taken as dead (the server stopped) and is marked as an error.</summary>
        public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);

        public AuditIntegrityService(IUnitOfWork uow, IAuditChainService chain, IInventoryUserContext user, INotificationService notify, IRestoreTestService restore, ILogger<AuditIntegrityService> logger = null, IServiceScopeFactory scopes = null)
        {
            _scopes = scopes;
            _uow = uow;
            _chain = chain;
            _user = user;
            _notify = notify;
            _restore = restore;
            _logger = logger;
        }

        // ------------------------------------------------------------------------------------------ runs

        public async Task<ResponseDto<AuditChainRunDto>> RunOnDemandAsync(AuditChainRunRequestDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(IntegrityRoles.Readers)) return ResponseDtoHelper.Forbidden<AuditChainRunDto>(IntegrityRoles.NotAllowed);
            string mode = (dto?.Mode ?? "incremental").Trim().ToLowerInvariant();
            if (mode != "incremental" && mode != "full") return ResponseDtoHelper.BadRequest<AuditChainRunDto>("نوع بررسی باید incremental (از آخرین نقطه سالم) یا full (کل زنجیره) باشد.");
            if (mode == "full")
            {
                // M8: a full pass loads the database for minutes: only the PMO, the CEO and the QA manager start it, and it runs in the background; the answer is the queued run (status Running)
                if (!_user.IsInRole(IntegrityRoles.FullPass)) return ResponseDtoHelper.Forbidden<AuditChainRunDto>("بررسی کامل زنجیره را فقط PMO، مدیرعامل یا مدیر کیفیت شروع می‌کند.");
                return await QueueFullAsync(ct);
            }
            AuditChainRun run = await RunAsync(IntegrityRunKind.Incremental, true, _user.UserId, ct);
            if (run == null) return ResponseDtoHelper.BadRequest<AuditChainRunDto>("بررسی دیگری هم‌اکنون در حال اجراست؛ چند دقیقه بعد دوباره امتحان کنید.");
            return ResponseDtoHelper.Ok(await ToDtoAsync(run, ct));
        }

        private async Task<ResponseDto<AuditChainRunDto>> QueueFullAsync(CancellationToken ct)
        {
            await MarkStaleAsync(ct);
            if (await _uow.Set<AuditChainRun>().AnyAsync(r => r.Status == IntegrityRunStatus.Running, ct))
                return ResponseDtoHelper.BadRequest<AuditChainRunDto>("بررسی دیگری هم‌اکنون در حال اجراست؛ چند دقیقه بعد دوباره امتحان کنید.");
            var run = new AuditChainRun { Kind = IntegrityRunKind.Full, Manual = true, RequestedByUserId = _user.UserId, StartedAt = DateTimeOffset.Now, Status = IntegrityRunStatus.Running, Message = "بررسی کامل در پس‌زمینه در حال اجراست." };
            _uow.Set<AuditChainRun>().Add(run);
            await _uow.SaveChangesAsync(ct);
            int id = run.Id;
            if (_scopes == null) await ExecuteQueuedAsync(id, ct);
            else
            {
                IServiceScopeFactory scopes = _scopes;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using IServiceScope scope = scopes.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<IAuditIntegrityService>().ExecuteQueuedAsync(id, CancellationToken.None);
                    }
                    catch (Exception ex) { _logger?.LogWarning(ex, "The queued full verification of the audit chain failed."); }
                });
            }
            return ResponseDtoHelper.Ok(await ToDtoAsync(run, ct));
        }

        /// <summary>A queued run that never finished (the server stopped) is closed as an error so that the next one can start.</summary>
        private async Task MarkStaleAsync(CancellationToken ct)
        {
            DateTimeOffset limit = DateTimeOffset.Now - StaleAfter;
            List<AuditChainRun> stale = await _uow.Set<AuditChainRun>().Where(r => r.Status == IntegrityRunStatus.Running && r.StartedAt < limit).ToListAsync(ct);
            if (stale.Count == 0) return;
            foreach (AuditChainRun r in stale) { r.Status = IntegrityRunStatus.Error; r.FinishedAt = DateTimeOffset.Now; r.Message = "بررسی در پس‌زمینه تمام نشد (سرور متوقف شد یا بیش از حد طول کشید)."; }
            await _uow.SaveChangesAsync(ct);
        }

        public async Task ExecuteQueuedAsync(int runId, CancellationToken ct)
        {
            await OneAtATime.WaitAsync(ct);
            try
            {
                AuditChainRun run = await _uow.Set<AuditChainRun>().FirstOrDefaultAsync(r => r.Id == runId, ct);
                if (run == null || run.Status != IntegrityRunStatus.Running) return;
                await VerifyIntoAsync(run, ct);
                run.FinishedAt = DateTimeOffset.Now;
                await _uow.SaveChangesAsync(ct);
                if (run.Status != IntegrityRunStatus.Ok) await AlertAsync(run, ct);
            }
            finally { OneAtATime.Release(); }
        }

        public async Task<ResponseDto<AuditChainRunDto>> GetRunAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(IntegrityRoles.Readers)) return ResponseDtoHelper.Forbidden<AuditChainRunDto>(IntegrityRoles.NotAllowed);
            AuditChainRun run = await _uow.Set<AuditChainRun>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (run == null) return ResponseDtoHelper.NotFound<AuditChainRunDto>("بررسی پیدا نشد.");
            return ResponseDtoHelper.Ok(await ToDtoAsync(run, ct));
        }

        public async Task<AuditChainRunDto> RunScheduledAsync(DateTime now, CancellationToken ct)
        {
            IntegrityRunKind kind = IntegrityRunKind.Incremental;
            DateTimeOffset? lastFull = await _uow.Set<AuditChainRun>().AsNoTracking().Where(r => r.Kind == IntegrityRunKind.Full && r.Status != IntegrityRunStatus.Error)
                .OrderByDescending(r => r.StartedAt).Select(r => (DateTimeOffset?)r.StartedAt).FirstOrDefaultAsync(ct);
            if (lastFull == null || now - lastFull.Value.LocalDateTime > TimeSpan.FromDays(FullPassMaxDays) || (now.DayOfWeek == FullPassDay && lastFull.Value.LocalDateTime.Date < now.Date))
                kind = IntegrityRunKind.Full;
            AuditChainRun run = await RunAsync(kind, false, null, ct);
            return run == null ? null : await ToDtoAsync(run, ct);
        }

        private async Task<AuditChainRun> RunAsync(IntegrityRunKind kind, bool manual, int? userId, CancellationToken ct)
        {
            if (!await OneAtATime.WaitAsync(0, ct)) return null;
            try
            {
                var run = new AuditChainRun { Kind = kind, Manual = manual, RequestedByUserId = userId, StartedAt = DateTimeOffset.Now };
                await VerifyIntoAsync(run, ct);
                run.FinishedAt = DateTimeOffset.Now;
                _uow.Set<AuditChainRun>().Add(run);
                await _uow.SaveChangesAsync(ct);
                if (run.Status != IntegrityRunStatus.Ok) await AlertAsync(run, ct);
                return run;
            }
            finally { OneAtATime.Release(); }
        }

        /// <summary>
        /// The checks of one run, written into <paramref name="run"/>. Rules (H2, fix round): the status of the chain stays Broken until a later FULL pass is Ok, so after a break every run is a full pass; a run never continues from a good run that is
        /// older than a later broken run; a break that three persons accepted (<see cref="ChainBreakAcceptance"/>) is the new start of the walk. After the walk of the chain the records are compared with the trail (H1, <see cref="RecordDrift"/>).
        /// </summary>
        private async Task VerifyIntoAsync(AuditChainRun run, CancellationToken ct)
        {
            try
            {
                DateTimeOffset? lastBroken = await _uow.Set<AuditChainRun>().AsNoTracking().Where(r => r.Status == IntegrityRunStatus.Broken && r.Id != run.Id).OrderByDescending(r => r.StartedAt).Select(r => (DateTimeOffset?)r.StartedAt).FirstOrDefaultAsync(ct);
                bool fullAfterBreak = lastBroken.HasValue && await _uow.Set<AuditChainRun>().AsNoTracking().AnyAsync(r => r.Kind == IntegrityRunKind.Full && r.Status == IntegrityRunStatus.Ok && r.StartedAt > lastBroken.Value, ct);
                if (lastBroken.HasValue && !fullAfterBreak) run.Kind = IntegrityRunKind.Full;   // the status clears only by a full pass

                // the start accepted by three persons (the highest one)
                var anchor = await _uow.Set<ChainBreakAcceptance>().AsNoTracking().Where(a => a.Status == SafetyChangeStatus.Approved && a.AnchorSeq != null)
                    .OrderByDescending(a => a.AnchorSeq).Select(a => new { Seq = a.AnchorSeq.Value, Hash = a.AnchorHash }).FirstOrDefaultAsync(ct);

                // the checkpoint of an incremental run: the head of a good run that is NEWER than the last broken run (never one that is older)
                var checkpoint = run.Kind == IntegrityRunKind.Incremental
                    ? await _uow.Set<AuditChainRun>().AsNoTracking().Where(r => r.Status == IntegrityRunStatus.Ok && r.HeadSeq != null && (lastBroken == null || r.StartedAt > lastBroken.Value))
                        .OrderByDescending(r => r.HeadSeq).ThenByDescending(r => r.Id).Select(r => new { Seq = r.HeadSeq.Value, Hash = r.HeadHash }).FirstOrDefaultAsync(ct)
                    : null;
                if (checkpoint != null && anchor != null && anchor.Seq > checkpoint.Seq) checkpoint = anchor;
                if (checkpoint == null && anchor != null) checkpoint = anchor;
                if (run.Kind == IntegrityRunKind.Incremental && checkpoint == null) run.Kind = IntegrityRunKind.Full;   // nothing to continue from: the first run is the whole chain
                if (run.Kind == IntegrityRunKind.Full && anchor != null) checkpoint = anchor;

                AuditChainVerifyDto result = checkpoint == null
                    ? await _chain.VerifyUncheckedAsync(1, null, null, null, ct)
                    : await _chain.VerifyUncheckedAsync(checkpoint.Seq + 1, null, checkpoint.Seq, checkpoint.Hash, ct);

                run.FromSeq = checkpoint == null ? 1 : checkpoint.Seq + 1;
                run.ToSeq = result.HeadSeq;
                run.EntriesChecked = result.Checked;
                run.HeadSeq = result.HeadSeq;
                run.HeadHash = result.HeadHash;
                run.KeyConfigured = result.KeyConfigured;
                run.LegacyRows = result.LegacyRows;
                run.Message = result.Message;
                if (!result.Ok)
                {
                    run.Status = IntegrityRunStatus.Broken;
                    AuditChainBreakDto b = result.FirstBreak;
                    if (b != null)
                    {
                        run.BreakKind = b.Kind; run.BreakSeq = b.Seq; run.BreakRowId = b.Id > 0 ? b.Id : (int?)null; run.BreakEntityType = b.EntityType; run.BreakAt = b.At;
                    }
                    return;
                }

                // H1: the chain is sound; now the records themselves
                RecordDriftFinding drift = await RecordDrift.FindFirstAsync(_uow, ct);
                if (drift != null)
                {
                    run.Status = IntegrityRunStatus.Broken;
                    run.BreakKind = RecordDriftKind; run.BreakSeq = null; run.BreakRowId = drift.RowId; run.BreakEntityType = drift.EntityType; run.BreakColumn = drift.Column; run.BreakAt = drift.TrailAt;
                    run.Message = drift.Message;
                    return;
                }
                run.Status = IntegrityRunStatus.Ok;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                run.Status = IntegrityRunStatus.Error;
                run.Message = "بررسی زنجیره سوابق انجام نشد: " + ex.GetType().Name;
                _logger?.LogWarning(ex, "The verification of the audit chain failed.");
            }
        }

        /// <summary>The kind of break of a record that differs from its trail (the other kinds are the ones of the chain walk).</summary>
        public const string RecordDriftKind = "record-drift";

        /// <summary>The alert of a break (or of a check that could not be made) for the QA manager, the PMO and the CEO. The same break is told once; a different break is a new item.</summary>
        private async Task AlertAsync(AuditChainRun run, CancellationToken ct)
        {
            string title;
            string key;
            if (run.Status == IntegrityRunStatus.Broken)
            {
                string table = AuditEntityLabels.Of(run.BreakEntityType) ?? run.BreakEntityType ?? "-";
                if (run.BreakKind == RecordDriftKind)
                    title = "هشدار سلامت سوابق: یک رکورد پس از ثبت در سوابق دست‌کاری شده است. جدول «" + table + "»، شناسه رکورد " + (run.BreakRowId?.ToString(CultureInfo.InvariantCulture) ?? "-")
                            + "، ستون «" + (run.BreakColumn ?? "-") + "»، آخرین ثبت در سوابق " + (run.BreakAt.HasValue ? StampText(run.BreakAt.Value) : "-");
                else
                title = "هشدار سلامت سوابق: زنجیره سوابق حسابرسی دست‌کاری یا ناقص است. نخستین ردیف مشکوک: ردیف شماره " + (run.BreakSeq?.ToString(CultureInfo.InvariantCulture) ?? "بدون مهر")
                        + "، جدول «" + table + "»، شناسه ردیف " + (run.BreakRowId?.ToString(CultureInfo.InvariantCulture) ?? "-") + "، زمان ثبت " + (run.BreakAt.HasValue ? StampText(run.BreakAt.Value) : "-")
                        + " (نوع مشکل: " + run.BreakKind + ")";
                key = "integrity:break:" + run.BreakKind + ":" + (run.BreakSeq?.ToString(CultureInfo.InvariantCulture) ?? "-") + ":" + (run.BreakRowId?.ToString(CultureInfo.InvariantCulture) ?? "-") + (run.BreakColumn != null ? ":" + run.BreakColumn : "");
            }
            else
            {
                title = "هشدار سلامت سوابق: بررسی زنجیره سوابق انجام نشد (" + run.Message + "). وضعیت زنجیره نامعلوم است.";
                key = "integrity:error:" + InventoryText.PersianDate(run.StartedAt.LocalDateTime);
            }
            await _notify.NotifyAsync(new NotifyRequest
            {
                SourceKey = key, Module = "integrity", SourceType = "AuditChainRun", SourceId = run.Id, Title = title, Link = "/integrity",
                Kind = Entities.Model.NasbNiro.Office.OfInboxKind.Alert, Priority = Entities.Model.NasbNiro.Office.OfUrgency.Immediate, Roles = IntegrityRoles.AlertRoles,
            }, ct);
        }

        // ------------------------------------------------------------------------------------------ reading

        public async Task<ResponseDto<PagedList<AuditChainRunDto>>> GetRunsAsync(AuditChainRunQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(IntegrityRoles.Readers)) return ResponseDtoHelper.Forbidden<PagedList<AuditChainRunDto>>(IntegrityRoles.NotAllowed);
            query ??= new AuditChainRunQuery();
            IQueryable<AuditChainRun> q = _uow.Set<AuditChainRun>().AsNoTracking();
            if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse(query.Status.Trim(), true, out IntegrityRunStatus status)) q = q.Where(r => r.Status == status);
            int size = query.PageSize <= 0 ? 25 : Math.Min(query.PageSize, 200);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            int total = await q.CountAsync(ct);
            List<AuditChainRun> rows = await q.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(r => r.RequestedByUserId), ct);
            List<AuditChainRunDto> dtos = rows.Select(r => ToDto(r, r.RequestedByUserId.HasValue ? names.Name(r.RequestedByUserId.Value) : null)).ToList();
            return ResponseDtoHelper.Ok<PagedList<AuditChainRunDto>>(new IntegrityPage<AuditChainRunDto>(dtos, total, number, size));
        }

        public async Task<ResponseDto<IntegritySummaryDto>> GetSummaryAsync(CancellationToken ct)
        {
            // the CEO dashboard and the page read it; the CEO and the managers see it as a light, so every reader of the dashboards may see the light (no detail beyond the dates)
            if (!_user.IsInRole(IntegrityRoles.Readers + ",PRODUCTIONMANAGER,PLANNING,ENGINEERING")) return ResponseDtoHelper.Forbidden<IntegritySummaryDto>(IntegrityRoles.NotAllowed);
            var dto = new IntegritySummaryDto { AsOf = DateTimeOffset.Now, RestoreWarnDays = RestoreWarnDays };

            IQueryable<AuditChainRun> runs = _uow.Set<AuditChainRun>().AsNoTracking();
            AuditChainRun last = await runs.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).FirstOrDefaultAsync(ct);
            AuditChainRun good = await runs.Where(r => r.Status == IntegrityRunStatus.Ok).OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).FirstOrDefaultAsync(ct);
            AuditChainRun full = await runs.Where(r => r.Kind == IntegrityRunKind.Full).OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).FirstOrDefaultAsync(ct);
            dto.LastChainRun = last == null ? null : ToDto(last, null);
            dto.LastGoodChainRun = good == null ? null : ToDto(good, null);
            dto.LastFullRun = full == null ? null : ToDto(full, null);
            // H2: a break stays until a later FULL pass is Ok (a good incremental run after a break does not clear it)
            AuditChainRun lastBroken = await runs.Where(r => r.Status == IntegrityRunStatus.Broken).OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).FirstOrDefaultAsync(ct);
            bool brokenOpen = lastBroken != null && !await runs.AnyAsync(r => r.Kind == IntegrityRunKind.Full && r.Status == IntegrityRunStatus.Ok && r.StartedAt > lastBroken.StartedAt, ct);
            dto.OpenBreak = brokenOpen ? ToDto(lastBroken, null) : null;
            dto.ChainStatus = last == null ? "NeverRun" : brokenOpen ? "Broken" : last.Status == IntegrityRunStatus.Running ? "Running" : last.Status.ToString();
            dto.ChainStatusText = last == null ? "هنوز بررسی انجام نشده است"
                : brokenOpen ? "زنجیره سوابق شکسته است و هنوز بررسی کامل سالمی پس از آن نبوده است"
                : last.Status == IntegrityRunStatus.Ok ? "زنجیره سوابق سالم است"
                : last.Status == IntegrityRunStatus.Running ? "بررسی کامل در حال اجراست"
                : "بررسی زنجیره انجام نشد";
            dto.PendingAcceptanceId = await _uow.Set<ChainBreakAcceptance>().AsNoTracking().Where(a => a.Status == SafetyChangeStatus.Pending).OrderByDescending(a => a.Id).Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);

            RestoreTestResult lastTest = await _uow.Set<RestoreTestResult>().AsNoTracking().OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(ct);
            RestoreTestResult lastGood = await _uow.Set<RestoreTestResult>().AsNoTracking().Where(r => r.Success).OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(ct);
            dto.LastRestoreTest = lastTest == null ? null : _restore.ToDto(lastTest);
            dto.LastRestoreSuccessAt = lastGood?.FinishedAt ?? lastGood?.StartedAt;
            dto.LastRestoreSuccessAtText = dto.LastRestoreSuccessAt.HasValue ? InventoryText.PersianDate(dto.LastRestoreSuccessAt.Value.LocalDateTime) : null;
            if (dto.LastRestoreSuccessAt.HasValue) dto.DaysSinceRestoreSuccess = (int)Math.Floor((DateTimeOffset.Now - dto.LastRestoreSuccessAt.Value).TotalDays);
            bool stale = lastGood == null || dto.DaysSinceRestoreSuccess > RestoreWarnDays;
            dto.RestoreStatus = lastGood == null ? (lastTest == null ? "Never" : "Failed") : stale ? "Stale" : (lastTest != null && !lastTest.Success && lastTest.StartedAt > lastGood.StartedAt ? "Failed" : "Ok");
            dto.RestoreStatusText = dto.RestoreStatus == "Ok" ? "آزمون بازیابی پشتیبان موفق بوده است"
                : dto.RestoreStatus == "Never" ? "هنوز آزمون بازیابی پشتیبان انجام نشده است"
                : dto.RestoreStatus == "Stale" ? "آخرین آزمون بازیابی موفق بیش از " + RestoreWarnDays.ToString(CultureInfo.InvariantCulture) + " روز پیش بوده است"
                : "آخرین آزمون بازیابی پشتیبان شکست خورده است";
            dto.Red = brokenOpen || (last != null && last.Status != IntegrityRunStatus.Ok && last.Status != IntegrityRunStatus.Running) || dto.RestoreStatus != "Ok";
            return ResponseDtoHelper.Ok(dto);
        }

        // ------------------------------------------------------------------------------------------ mapping

        internal static string StampText(DateTimeOffset at) => InventoryText.PersianDate(at.LocalDateTime) + " " + at.LocalDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        private async Task<AuditChainRunDto> ToDtoAsync(AuditChainRun run, CancellationToken ct)
        {
            string name = null;
            if (run.RequestedByUserId.HasValue) name = (await InventoryUsers.GetNamesAsync(_uow, new int?[] { run.RequestedByUserId }, ct)).Name(run.RequestedByUserId.Value);
            return ToDto(run, name);
        }

        internal static AuditChainRunDto ToDto(AuditChainRun r, string requestedBy) => new AuditChainRunDto
        {
            Id = r.Id, Kind = r.Kind.ToString(), KindText = r.Kind == IntegrityRunKind.Full ? "کامل (کل زنجیره)" : "افزایشی (از آخرین نقطه سالم)", Manual = r.Manual, RequestedByUserId = r.RequestedByUserId,
            RequestedByName = r.Manual ? requestedBy : "سامانه (کار شبانه)", StartedAt = r.StartedAt, StartedAtText = StampText(r.StartedAt), FinishedAt = r.FinishedAt,
            Status = r.Status.ToString(), StatusText = r.Status == IntegrityRunStatus.Ok ? "سالم" : r.Status == IntegrityRunStatus.Broken ? "شکسته" : r.Status == IntegrityRunStatus.Running ? "در حال اجرا" : "خطا در بررسی",
            FromSeq = r.FromSeq, ToSeq = r.ToSeq, EntriesChecked = r.EntriesChecked, HeadSeq = r.HeadSeq, HeadHash = r.HeadHash, KeyConfigured = r.KeyConfigured, LegacyRows = r.LegacyRows,
            BreakKind = r.BreakKind, BreakSeq = r.BreakSeq, BreakRowId = r.BreakRowId, BreakColumn = r.BreakColumn, BreakEntityType = r.BreakEntityType, BreakEntityTypeText = AuditEntityLabels.Of(r.BreakEntityType),
            BreakAt = r.BreakAt, BreakAtText = r.BreakAt.HasValue ? StampText(r.BreakAt.Value) : null, Message = r.Message,
        };
    }
}
