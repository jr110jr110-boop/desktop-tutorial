using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Production.Dnc
{
    public interface IMfMonitorService
    {
        Task<ResponseDto<PagedList<MachineMonitorDto>>> GetMonitorsAsync(MachineMonitorQuery query, CancellationToken ct);
        Task<ResponseDto<MachineMonitorDto>> SaveMonitorAsync(SaveMachineMonitorDto dto, CancellationToken ct);
        Task<ResponseDto<MonitorInputsTestDto>> TestInputsAsync(MachineIdDto dto, CancellationToken ct);
        Task<ResponseDto<MonitorPollResultDto>> RunPollAsync(CancellationToken ct);

        /// <summary>The background job (no user): polls the due monitors, marks silent ones offline, raises and clears the alerts.</summary>
        Task<MonitorPollResultDto> PollAsync(CancellationToken ct);

        Task<ResponseDto<LiveBoardDto>> GetLiveAsync(int? workCenterId, CancellationToken ct);
        Task<ResponseDto<PagedList<StateEventDto>>> GetEventsAsync(StateEventQuery query, CancellationToken ct);
        Task<ResponseDto<List<MachineUtilisationDto>>> GetUtilisationAsync(UtilisationQuery query, CancellationToken ct);
        Task<ResponseDto<LiveMachineDto>> GatewayStatusAsync(string machine, string key, string remote, GatewayStatusDto dto, CancellationToken ct);
    }

    /// <summary>
    /// Live monitoring of the machines (production P2 part B): the state comes from a Modbus TCP input module wired to the stack light (polled) or from an edge box at the machine (pushed);
    /// every change is one period in MfMachineStateEvents. An alarm raises an inbox alert (and, when set, a maintenance request); a machine that disagrees with the shop floor (idle while a run booking is open,
    /// running without a booking) raises an alert after the set minutes. The live board, the periods and the measured utilisation against the booked hours are read here.
    /// </summary>
    public class MfMonitorService : IMfMonitorService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IMfModbusReader _modbus;
        private readonly INotificationService _notify;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

        /// <summary>Server local time (the bookings use it too); replaceable in tests.</summary>
        internal Func<DateTime> Clock { get; set; } = () => DateTime.Now;

        public MfMonitorService(IUnitOfWork uow, IInventoryUserContext user, IMfModbusReader modbus = null, INotificationService notify = null, Microsoft.Extensions.Configuration.IConfiguration configuration = null)
        {
            _configuration = configuration;
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _modbus = modbus ?? new ModbusTcpReader();
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => MfText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => MfText.Bad<T>(m);
        private static bool IsAi => global::Services.NasbNiro.Ai.AiActing.IsActing;

        public static string StateText(MfMachineState s) => s switch
        {
            MfMachineState.Off => "خاموش", MfMachineState.Idle => "آماده / بیکار", MfMachineState.Running => "در حال کار", MfMachineState.Alarm => "آلارم", MfMachineState.Offline => "بدون ارتباط", _ => s.ToString(),
        };

        private static PagedList<T> Page<T>(List<T> rows, int total, int number, int size)
        {
            PagedList<T> page = PagedList<T>.CreateAsync(rows, 1, int.MaxValue, CancellationToken.None).GetAwaiter().GetResult();
            page.TotalCount = total; page.CurrentPage = number; page.PageSize = size;
            page.TotalPages = (int)Math.Ceiling(total / (double)Math.Max(size, 1));
            return page;
        }

        // ================================================================================== settings

        public async Task<ResponseDto<PagedList<MachineMonitorDto>>> GetMonitorsAsync(MachineMonitorQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<PagedList<MachineMonitorDto>>(MfText.NotAllowed);
            query ??= new MachineMonitorQuery();
            IQueryable<Machine> q = _uow.Set<Machine>().AsNoTracking();
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(m => m.Title.Contains(text));
            if (query.WorkCenterId.HasValue) { int wc = query.WorkCenterId.Value; q = q.Where(m => _uow.Set<MfMachineInfo>().Any(i => !i.Deleted && i.MachineId == m.Id && i.WorkCenterId == wc)); }
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize), number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<Machine> machines = await q.OrderBy(m => m.Title).ThenBy(m => m.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            List<int> ids = machines.Select(m => m.Id).ToList();
            Dictionary<int, MfMachineMonitor> monitors = (await _uow.Set<MfMachineMonitor>().AsNoTracking().Where(x => !x.Deleted && ids.Contains(x.MachineId)).ToListAsync(ct)).ToDictionary(x => x.MachineId);
            HashSet<int> gateways = (await _uow.Set<MfMachineConnection>().AsNoTracking().Where(c => !c.Deleted && ids.Contains(c.MachineId) && c.LinkKind == MfNcLinkKind.EdgeGateway).Select(c => c.MachineId).ToListAsync(ct)).ToHashSet();
            Dictionary<int, string> centres = await CentreCodesAsync(ids, ct);
            var rows = machines.Select(m =>
            {
                monitors.TryGetValue(m.Id, out MfMachineMonitor x);
                return new MachineMonitorDto
                {
                    Id = x?.Id, MachineId = m.Id, MachineTitle = m.Title, WorkCenterCode = centres.TryGetValue(m.Id, out string wc) ? wc : null, Source = x?.Source ?? MfMonitorSource.None,
                    Host = x?.Host, Port = x?.Port, UnitId = x?.UnitId ?? 1, Function = x?.Function ?? MfModbusFunction.DiscreteInputs, StartAddress = x?.StartAddress ?? 0, RunInput = x?.RunInput,
                    AlarmInput = x?.AlarmInput, PowerInput = x?.PowerInput, InvertMask = x?.InvertMask ?? 0, PollSeconds = x?.PollSeconds, AlarmCreatesRequest = x?.AlarmCreatesRequest ?? false,
                    MismatchMinutes = x?.MismatchMinutes ?? 15, IsActive = x?.IsActive ?? true, Note = x?.Note, LastPollAtText = MfText.Stamp(x?.LastPollAt), LastSeenAtText = MfText.Stamp(x?.LastSeenAt),
                    FailedPolls = x?.FailedPolls ?? 0, LastError = x?.LastError, HasGatewayLink = gateways.Contains(m.Id), RowVersion = MfConcurrency.Text(x?.RowVersion),
                };
            }).ToList();
            return ResponseDtoHelper.Ok(Page(rows, total, number, size));
        }

        private async Task<Dictionary<int, string>> CentreCodesAsync(List<int> machineIds, CancellationToken ct) =>
            (await _uow.Set<MfMachineInfo>().AsNoTracking().Where(i => !i.Deleted && machineIds.Contains(i.MachineId) && i.WorkCenterId != null)
                .Join(_uow.Set<MfWorkCenter>().AsNoTracking(), i => i.WorkCenterId, w => w.Id, (i, w) => new { i.MachineId, w.Code }).ToListAsync(ct))
            .GroupBy(x => x.MachineId).ToDictionary(g => g.Key, g => g.First().Code);

        public async Task<ResponseDto<MachineMonitorDto>> SaveMonitorAsync(SaveMachineMonitorDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.LinkEditors) || IsAi) return Bad<MachineMonitorDto>(MfText.NotAllowed);
            if (dto == null || !Enum.IsDefined(typeof(MfMonitorSource), dto.Source) || !Enum.IsDefined(typeof(MfModbusFunction), dto.Function)) return Bad<MachineMonitorDto>(MfText.InvalidData);
            if (!await _uow.Set<Machine>().AnyAsync(m => m.Id == dto.MachineId, ct)) return Bad<MachineMonitorDto>("ماشین پیدا نشد.");
            string host = Clean(dto.Host);
            if (dto.Source == MfMonitorSource.ModbusInputs)
            {
                if (host == null || host.Length > 200 || host.Any(char.IsWhiteSpace)) return Bad<MachineMonitorDto>("نشانی ماژول ورودی را وارد کنید.");
                if (!NcLan.IsLanHost(host, string.Equals(_configuration?["Dnc:AllowLoopback"], "true", StringComparison.OrdinalIgnoreCase)))
                    return Bad<MachineMonitorDto>("نشانی ماژول ورودی باید نشانی IP شبکه داخلی کارگاه باشد (مثلاً 192.168.10.51).");
                if (dto.Port is < 1 or > 65535) return Bad<MachineMonitorDto>("درگاه باید بین ۱ و ۶۵۵۳۵ باشد.");
                if (dto.UnitId < 0 || dto.UnitId > 247 || dto.StartAddress < 0 || dto.StartAddress > 65535) return Bad<MachineMonitorDto>("شناسه واحد یا نشانی شروع نامعتبر است.");
                if (!dto.RunInput.HasValue) return Bad<MachineMonitorDto>("ورودی «در حال کار» (چراغ سبز یا اسپیندل) را مشخص کنید.");
                foreach (int? i in new[] { dto.RunInput, dto.AlarmInput, dto.PowerInput })
                    if (i is < 0 or > 30) return Bad<MachineMonitorDto>("شماره ورودی‌ها بین ۰ و ۳۰ است.");
                int?[] mapped = new[] { dto.RunInput, dto.AlarmInput, dto.PowerInput }.Where(i => i.HasValue).ToArray();
                if (mapped.Distinct().Count() != mapped.Length) return Bad<MachineMonitorDto>("هر ورودی فقط به یک معنا نگاشت می‌شود.");
            }
            if (dto.Source == MfMonitorSource.GatewayPush && !await _uow.Set<MfMachineConnection>().AnyAsync(c => !c.Deleted && c.MachineId == dto.MachineId && c.LinkKind == MfNcLinkKind.EdgeGateway, ct))
                return Bad<MachineMonitorDto>("برای دریافت وضعیت از گیت‌وی، ابتدا اتصال DNC ماشین را «گیت‌وی لبه» تعریف و کلید آن را صادر کنید.");
            if (dto.PollSeconds is < 2 or > 3600) return Bad<MachineMonitorDto>("فاصله پایش بین ۲ ثانیه و یک ساعت است.");
            if (dto.MismatchMinutes < 1 || dto.MismatchMinutes > 1440) return Bad<MachineMonitorDto>("آستانه ناهمخوانی بین ۱ و ۱۴۴۰ دقیقه است.");
            MfMachineMonitor row = await _uow.Set<MfMachineMonitor>().FirstOrDefaultAsync(x => x.MachineId == dto.MachineId && !x.Deleted, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            if (row == null)
            {
                row = new MfMachineMonitor { MachineId = dto.MachineId, CreateDate = now, CreatorUserId = Me };
                _uow.Set<MfMachineMonitor>().Add(row);
            }
            // no row-version check against the page: the poll writes the live fields of this row every few seconds, so the version the editor saw is always old (review finding 7); the save takes
            // the row as it is now and replaces only the settings (two editors at once: the last one wins, both saves are in the audit trail)
            row.Source = dto.Source; row.Host = host; row.Port = dto.Port; row.UnitId = (byte)dto.UnitId; row.Function = dto.Function; row.StartAddress = dto.StartAddress;
            row.RunInput = dto.RunInput; row.AlarmInput = dto.AlarmInput; row.PowerInput = dto.PowerInput; row.InvertMask = dto.InvertMask & 0x7FFFFFFF; row.PollSeconds = dto.PollSeconds;
            row.AlarmCreatesRequest = dto.AlarmCreatesRequest; row.MismatchMinutes = dto.MismatchMinutes; row.IsActive = dto.IsActive; row.Note = Clean(dto.Note);
            row.FailedPolls = 0; row.LastError = null; row.ModifyDate = now; row.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<MachineMonitorDto>(MfText.Concurrency); }
            catch (DbUpdateException) { return Bad<MachineMonitorDto>("برای این ماشین هم‌زمان پایش دیگری ثبت شد؛ صفحه را دوباره باز کنید."); }
            var page = await GetMonitorsAsync(new MachineMonitorQuery { PageSize = 500 }, ct);
            return ResponseDtoHelper.Ok(page.Data.First(x => x.MachineId == dto.MachineId));
        }

        public async Task<ResponseDto<MonitorInputsTestDto>> TestInputsAsync(MachineIdDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.LinkTesters)) return Bad<MonitorInputsTestDto>(MfText.NotAllowed);
            MfMachineMonitor m = dto == null ? null : await _uow.Set<MfMachineMonitor>().AsNoTracking().FirstOrDefaultAsync(x => x.MachineId == dto.MachineId && !x.Deleted, ct);
            if (m == null || m.Source != MfMonitorSource.ModbusInputs) return Bad<MonitorInputsTestDto>("پایش Modbus برای این ماشین تعریف نشده است.");
            var (inputs, error) = await _modbus.ReadAsync(m.Host, m.Port ?? 502, m.UnitId, m.Function, m.StartAddress, Math.Max(8, MfMonitorRules.InputCount(m)), ct);
            if (error != null) return ResponseDtoHelper.Ok(new MonitorInputsTestDto { Ok = false, Message = error });
            MfMachineState state = MfMonitorRules.FromInputs(inputs, m);
            return ResponseDtoHelper.Ok(new MonitorInputsTestDto { Ok = true, Message = "وضعیت خوانده‌شده: " + StateText(state), Inputs = inputs.ToList(), State = state });
        }

        // ================================================================================== polling and state changes

        public async Task<ResponseDto<MonitorPollResultDto>> RunPollAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Supervisors) || IsAi) return Bad<MonitorPollResultDto>(MfText.NotAllowed);
            return ResponseDtoHelper.Ok(await PollAsync(ct));
        }

        /// <summary>Opens the new period when the state (or the alarm) changed; true when it did. The caller saves.</summary>
        private async Task<(bool Changed, MfMachineStateEvent Opened)> ApplyAsync(MfMachineMonitor m, MfMachineState state, MfMonitorSource source, string alarmCode, string alarmText, DateTime now, MonitorPollResultDto result, CancellationToken ct)
        {
            MfMachineStateEvent open = await _uow.Set<MfMachineStateEvent>().Where(e => e.MachineId == m.MachineId && e.EndAt == null).OrderByDescending(e => e.Id).FirstOrDefaultAsync(ct);
            alarmCode = Cut(Clean(alarmCode), 50); alarmText = Cut(Clean(alarmText), 1000);
            if (open != null && open.State == state && (state != MfMachineState.Alarm || open.AlarmCode == alarmCode)) return (false, open);
            if (open != null) open.EndAt = now < open.StartAt ? open.StartAt : now;
            var opened = new MfMachineStateEvent { MachineId = m.MachineId, State = state, StartAt = now, Source = source, AlarmCode = state == MfMachineState.Alarm ? alarmCode : null, AlarmText = state == MfMachineState.Alarm ? alarmText : null };
            _uow.Set<MfMachineStateEvent>().Add(opened);
            if (state == MfMachineState.Alarm)
            {
                result.Alarms++;
                if (m.AlarmCreatesRequest && !await _uow.Set<MfMaintenanceRequest>().AnyAsync(r => !r.Deleted && r.MachineId == m.MachineId && (r.Status == MfMaintenanceRequestStatus.Open || r.Status == MfMaintenanceRequestStatus.InProgress), ct))
                {
                    MfMachineInfo info = await _uow.Set<MfMachineInfo>().AsNoTracking().FirstOrDefaultAsync(i => i.MachineId == m.MachineId && !i.Deleted, ct);
                    var request = new MfMaintenanceRequest
                    {
                        Number = await InventoryNumbers.NextNumberAsync(_uow, MfNumbers.MaintenanceRequest, "MNT", now, ct), MachineId = m.MachineId, WorkCenterId = info?.WorkCenterId,
                        ReportedByUserId = 0, ReportedAt = now, ReportedAtText = MfText.Stamp(now), Status = MfMaintenanceRequestStatus.Open,
                        Description = "آلارم ماشین (پایش خودکار)" + (alarmCode != null ? " - کد " + alarmCode : "") + (alarmText != null ? ": " + alarmText : ""),
                        CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now,
                    };
                    _uow.Set<MfMaintenanceRequest>().Add(request);
                    result.RequestsOpened++;
                    opened.MaintenanceRequestId = null;
                    _pendingRequests.Add((opened, request));
                }
            }
            return (true, opened);
        }

        /// <summary>The alarm period whose inbox item is open, per machine (a new alarm code reopens the item with its own text).</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, long> _lastAlarmEvent = new System.Collections.Concurrent.ConcurrentDictionary<int, long>();

        private readonly List<(MfMachineStateEvent Event, MfMaintenanceRequest Request)> _pendingRequests = new List<(MfMachineStateEvent, MfMaintenanceRequest)>();

        private static string Cut(string s, int n) => s == null ? null : s.Length > n ? s.Substring(0, n) : s;

        /// <summary>Saves, links the alarm periods to the requests they opened (their ids exist after the first save) and raises the inbox items of the new requests.</summary>
        private async Task SaveAsync(Dictionary<int, string> titles, CancellationToken ct)
        {
            await _uow.SaveChangesAsync(ct);
            if (_pendingRequests.Count == 0) return;
            foreach (var (e, r) in _pendingRequests) e.MaintenanceRequestId = r.Id;
            await _uow.SaveChangesAsync(ct);
            foreach (var (_, r) in _pendingRequests) await MfGates.MaintenanceAsync(_notify, r, titles.TryGetValue(r.MachineId, out string t) ? t : "#" + r.MachineId, ct);
            _pendingRequests.Clear();
        }

        public async Task<MonitorPollResultDto> PollAsync(CancellationToken ct)
        {
            var result = new MonitorPollResultDto();
            DateTime now = Clock();
            List<MfMachineMonitor> monitors = await _uow.Set<MfMachineMonitor>().Where(m => !m.Deleted && m.IsActive && m.Source != MfMonitorSource.None).ToListAsync(ct);
            Dictionary<int, string> titles = await _uow.Set<Machine>().AsNoTracking().Where(x => monitors.Select(m => m.MachineId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Title, ct);
            foreach (MfMachineMonitor m in monitors)
            {
                ct.ThrowIfCancellationRequested();
                int interval = MfMonitorRules.Interval(m);
                if (m.Source == MfMonitorSource.ModbusInputs)
                {
                    if (m.LastPollAt.HasValue && (now - m.LastPollAt.Value.LocalDateTime).TotalSeconds < interval - 0.5) continue;
                    result.Polled++;
                    m.LastPollAt = new DateTimeOffset(now);
                    var (inputs, error) = await _modbus.ReadAsync(m.Host, m.Port ?? 502, m.UnitId, m.Function, m.StartAddress, MfMonitorRules.InputCount(m), ct);
                    if (error == null)
                    {
                        m.FailedPolls = 0; m.LastError = null; m.LastSeenAt = new DateTimeOffset(now);
                        if ((await ApplyAsync(m, MfMonitorRules.FromInputs(inputs, m), m.Source, null, null, now, result, ct)).Changed) result.Changed++;
                    }
                    else
                    {
                        m.FailedPolls++; m.LastError = Cut(error, 1000);
                        if (m.FailedPolls >= MfMonitorRules.OfflineAfterFailures && (await ApplyAsync(m, MfMachineState.Offline, m.Source, null, null, now, result, ct)).Changed) { result.Changed++; result.Offline++; }
                    }
                }
                else if (m.Source == MfMonitorSource.GatewayPush)
                {
                    // a box that stopped pushing for three intervals: offline (a box that never pushed stays unobserved)
                    if (m.LastSeenAt.HasValue && (now - m.LastSeenAt.Value.LocalDateTime).TotalSeconds > interval * 3
                        && (await ApplyAsync(m, MfMachineState.Offline, m.Source, null, null, now, result, ct)).Changed) { result.Changed++; result.Offline++; }
                }
            }
            await SaveAsync(titles, ct);
            result.Mismatches = await AlertsAsync(monitors, titles, now, ct);
            return result;
        }

        /// <summary>Opens and closes the inbox alerts of every monitored machine: alarm, and disagreement with the shop floor. Returns the number of machines that disagree.</summary>
        private async Task<int> AlertsAsync(List<MfMachineMonitor> monitors, Dictionary<int, string> titles, DateTime now, CancellationToken ct)
        {
            if (monitors.Count == 0) return 0;
            List<int> ids = monitors.Select(m => m.MachineId).ToList();
            var open = await _uow.Set<MfMachineStateEvent>().AsNoTracking().Where(e => ids.Contains(e.MachineId) && e.EndAt == null).ToListAsync(ct);
            var booked = (await _uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && b.EndAt == null && b.MachineId != null && ids.Contains(b.MachineId.Value)
                && (b.Kind == MfBookingKind.Run || b.Kind == MfBookingKind.Setup)).Select(b => b.MachineId.Value).ToListAsync(ct)).ToHashSet();
            int mismatches = 0;
            foreach (MfMachineMonitor m in monitors)
            {
                MfMachineStateEvent e = open.Where(x => x.MachineId == m.MachineId).OrderByDescending(x => x.Id).FirstOrDefault();
                string title = titles.TryGetValue(m.MachineId, out string t) ? t : "#" + m.MachineId;
                bool alarm = e?.State == MfMachineState.Alarm;
                if (alarm != m.AlarmAlertOpen || (alarm && e.Id != _lastAlarmEvent.GetValueOrDefault(m.MachineId)))
                {
                    await OfficeGates.SyncAsync(_notify, "mf:mon-alarm:" + m.MachineId, alarm, "production", "machine-alarm", m.MachineId,
                        $"ماشین {title} در آلارم است" + (e?.AlarmText != null ? ": " + e.AlarmText : ""), "/production/monitor", ProductionRoles.Supervisors + ",MAINTENANCE", ct,
                        kind: Entities.Model.NasbNiro.Office.OfInboxKind.Alert, priority: Entities.Model.NasbNiro.Office.OfUrgency.Urgent);
                    m.AlarmAlertOpen = alarm;
                    if (alarm) _lastAlarmEvent[m.MachineId] = e.Id;
                }
                string mismatch = e == null ? null : MfMonitorRules.Mismatch(e.State, (now - e.StartAt).TotalMinutes, booked.Contains(m.MachineId), m.MismatchMinutes);
                if (mismatch != null) mismatches++;
                if (mismatch != m.MismatchAlertOpen)
                {
                    if (m.MismatchAlertOpen != null) await OfficeGates.SyncAsync(_notify, "mf:mon-mismatch:" + m.MachineId, false, "production", "machine-mismatch", m.MachineId, "", "/production/monitor", ProductionRoles.Supervisors, ct);
                    if (mismatch != null)
                        await OfficeGates.SyncAsync(_notify, "mf:mon-mismatch:" + m.MachineId, true, "production", "machine-mismatch", m.MachineId,
                            mismatch == "booked-idle" ? $"ماشین {title} بیکار است ولی ثبت زمان کار روی آن باز است" : $"ماشین {title} کار می‌کند ولی ثبت زمانی روی آن باز نیست",
                            "/production/monitor", ProductionRoles.Supervisors, ct, kind: Entities.Model.NasbNiro.Office.OfInboxKind.Alert);
                    m.MismatchAlertOpen = mismatch;
                }
            }
            try { await _uow.SaveChangesAsync(ct); } catch (DbUpdateException) { /* the flags are set again at the next poll */ }
            return mismatches;
        }

        // ================================================================================== edge gateway push

        public async Task<ResponseDto<LiveMachineDto>> GatewayStatusAsync(string machine, string key, string remote, GatewayStatusDto dto, CancellationToken ct)
        {
            if (dto == null || !Enum.IsDefined(typeof(MfMachineState), dto.State) || dto.State == MfMachineState.Offline) return Bad<LiveMachineDto>(MfText.InvalidData);
            if (!int.TryParse(machine, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int machineId) || string.IsNullOrEmpty(key)) return Unauthorized();
            MfMachineConnection c = await _uow.Set<MfMachineConnection>().AsNoTracking().FirstOrDefaultAsync(x => x.MachineId == machineId && !x.Deleted, ct);
            if (c == null || !c.IsActive || c.LinkKind != MfNcLinkKind.EdgeGateway || string.IsNullOrEmpty(c.GatewayKeyHash)) return Unauthorized();
            byte[] given = Encoding.UTF8.GetBytes(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))), stored = Encoding.UTF8.GetBytes(c.GatewayKeyHash);
            if (given.Length != stored.Length || !CryptographicOperations.FixedTimeEquals(given, stored) || !DeviceAddress.Allows(c.AllowedAddresses, remote)) return Unauthorized();
            MfMachineMonitor m = await _uow.Set<MfMachineMonitor>().FirstOrDefaultAsync(x => x.MachineId == machineId && !x.Deleted, ct);
            if (m == null || !m.IsActive || m.Source != MfMonitorSource.GatewayPush) return Bad<LiveMachineDto>("پایش این ماشین روی «دریافت از گیت‌وی» تنظیم نشده است.");
            DateTime now = Clock();
            m.LastSeenAt = new DateTimeOffset(now); m.LastProgramName = Cut(Clean(dto.ProgramName), 200); m.LastSpindleRpm = dto.SpindleRpm; m.LastFeedOverride = dto.FeedOverride; m.LastPartCount = dto.PartCount;
            var result = new MonitorPollResultDto();
            await ApplyAsync(m, dto.State, MfMonitorSource.GatewayPush, dto.AlarmCode, dto.AlarmText, now, result, ct);
            string title = await _uow.Set<Machine>().AsNoTracking().Where(x => x.Id == machineId).Select(x => x.Title).FirstOrDefaultAsync(ct);
            await SaveAsync(new Dictionary<int, string> { [machineId] = title }, ct);
            await AlertsAsync(new List<MfMachineMonitor> { m }, new Dictionary<int, string> { [machineId] = title }, now, ct);
            return ResponseDtoHelper.Ok(new LiveMachineDto { MachineId = machineId, MachineTitle = title, Source = m.Source, State = dto.State, LastSeenAtText = MfText.Stamp(now) });
        }

        private static ResponseDto<LiveMachineDto> Unauthorized() => ResponseDtoHelper.Unauthorized<LiveMachineDto>("کلید یا نشانی گیت‌وی پذیرفته نشد.");

        // ================================================================================== reading

        private static StateMinutesDto Dto(Dictionary<MfMachineState, double> m) => new StateMinutesDto
        {
            Running = Math.Round(m[MfMachineState.Running], 1), Idle = Math.Round(m[MfMachineState.Idle], 1), Off = Math.Round(m[MfMachineState.Off], 1),
            Alarm = Math.Round(m[MfMachineState.Alarm], 1), Offline = Math.Round(m[MfMachineState.Offline], 1),
        };

        public async Task<ResponseDto<LiveBoardDto>> GetLiveAsync(int? workCenterId, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<LiveBoardDto>(MfText.NotAllowed);
            DateTime now = Clock(), today = now.Date;
            IQueryable<MfMachineMonitor> q = _uow.Set<MfMachineMonitor>().AsNoTracking().Where(m => !m.Deleted && m.IsActive && m.Source != MfMonitorSource.None);
            if (workCenterId.HasValue) q = q.Where(m => _uow.Set<MfMachineInfo>().Any(i => !i.Deleted && i.MachineId == m.MachineId && i.WorkCenterId == workCenterId.Value));
            List<MfMachineMonitor> monitors = await q.ToListAsync(ct);
            List<int> ids = monitors.Select(m => m.MachineId).ToList();
            Dictionary<int, string> titles = await _uow.Set<Machine>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Title, ct);
            Dictionary<int, string> centres = await CentreCodesAsync(ids, ct);
            var events = await _uow.Set<MfMachineStateEvent>().AsNoTracking().Where(e => ids.Contains(e.MachineId) && (e.EndAt == null || e.EndAt > today)).ToListAsync(ct);
            var lastRuns = (await _uow.Set<MfNcRun>().AsNoTracking().Where(r => ids.Contains(r.MachineId)).GroupBy(r => r.MachineId).Select(g => g.Max(r => r.Id)).ToListAsync(ct));
            var runs = await _uow.Set<MfNcRun>().AsNoTracking().Where(r => lastRuns.Contains(r.Id))
                .Join(_uow.Set<MfNcProgram>().AsNoTracking(), r => r.ProgramId, p => p.Id, (r, p) => new { r.MachineId, p.Code, r.RevisionId })
                .Join(_uow.Set<MfNcRevision>().AsNoTracking(), x => x.RevisionId, v => v.Id, (x, v) => new { x.MachineId, x.Code, v.Revision }).ToListAsync(ct);
            var bookings = await _uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && b.EndAt == null && b.MachineId != null && ids.Contains(b.MachineId.Value))
                .Join(_uow.Set<MfOperation>().AsNoTracking(), b => b.OperationId, o => o.Id, (b, o) => new { MachineId = b.MachineId.Value, b.Kind, b.EmployeeId, o.Id, o.OrderId, o.Sequence, o.Title })
                .Join(_uow.Set<MfProductionOrder>().AsNoTracking(), x => x.OrderId, d => d.Id, (x, d) => new { x.MachineId, x.Kind, x.EmployeeId, x.Id, d.Number, x.Sequence, x.Title }).ToListAsync(ct);
            bool names = MfPrivacy.SeesNames(_user);
            Dictionary<int, string> employees = new Dictionary<int, string>();
            if (names)
            {
                List<int> empIds = bookings.Select(b => b.EmployeeId).Distinct().ToList();
                employees = MfNames.ToDictionary(await _uow.Set<FnEmployee>().AsNoTracking().Where(e => empIds.Contains(e.Id)).ToListAsync(ct));
            }
            var board = new LiveBoardDto { AsOfText = MfText.Stamp(now) };
            foreach (MfMachineMonitor m in monitors.OrderBy(x => titles.TryGetValue(x.MachineId, out string t) ? t : ""))
            {
                var mine = events.Where(e => e.MachineId == m.MachineId).ToList();
                MfMachineStateEvent open = mine.Where(e => e.EndAt == null).OrderByDescending(e => e.Id).FirstOrDefault();
                var minutes = MfMonitorRules.Minutes(mine.Select(e => new MfMonitorRules.Span { State = e.State, StartAt = e.StartAt, EndAt = e.EndAt }), today, now, now);
                var booking = bookings.Where(b => b.MachineId == m.MachineId).OrderBy(b => b.Kind == MfBookingKind.Run ? 0 : 1).FirstOrDefault();
                bool runBooking = bookings.Any(b => b.MachineId == m.MachineId && (b.Kind == MfBookingKind.Run || b.Kind == MfBookingKind.Setup));
                var run = runs.FirstOrDefault(r => r.MachineId == m.MachineId);
                var dto = new LiveMachineDto
                {
                    MachineId = m.MachineId, MachineTitle = titles.TryGetValue(m.MachineId, out string mt) ? mt : null, WorkCenterCode = centres.TryGetValue(m.MachineId, out string wc) ? wc : null,
                    Source = m.Source, State = open?.State, SinceText = MfText.Stamp(open?.StartAt), MinutesInState = open == null ? 0 : (int)Math.Max(0, (now - open.StartAt).TotalMinutes),
                    LastSeenAtText = MfText.Stamp(m.LastSeenAt), AlarmCode = open?.AlarmCode, AlarmText = open?.AlarmText, ProgramName = m.LastProgramName, SpindleRpm = m.LastSpindleRpm,
                    FeedOverride = m.LastFeedOverride, PartCount = m.LastPartCount, LastRunProgram = run?.Code, LastRunRevision = run?.Revision, BookingOpen = booking != null,
                    OperationId = booking?.Id, OperationName = booking == null ? null : MfNames.Operation(booking.Number, booking.Sequence) + " " + booking.Title,
                    OperatorName = names && booking != null && employees.TryGetValue(booking.EmployeeId, out string en) ? en : null,
                    Today = Dto(minutes), TodayRunPercent = MfMonitorRules.RunPercent(minutes),
                };
                dto.Mismatch = open == null ? null : MfMonitorRules.Mismatch(open.State, (now - open.StartAt).TotalMinutes, runBooking, m.MismatchMinutes);
                dto.MismatchText = dto.Mismatch == "booked-idle" ? "ثبت زمان کار باز است ولی ماشین کار نمی‌کند" : dto.Mismatch == "running-unbooked" ? "ماشین کار می‌کند ولی ثبت زمانی باز نیست" : null;
                board.Machines.Add(dto);
                switch (open?.State)
                {
                    case MfMachineState.Running: board.Running++; break;
                    case MfMachineState.Idle: board.Idle++; break;
                    case MfMachineState.Off: board.Off++; break;
                    case MfMachineState.Alarm: board.Alarm++; break;
                    case MfMachineState.Offline: board.Offline++; break;
                }
                if (dto.Mismatch != null) board.Mismatches++;
            }
            return ResponseDtoHelper.Ok(board);
        }

        public async Task<ResponseDto<PagedList<StateEventDto>>> GetEventsAsync(StateEventQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<PagedList<StateEventDto>>(MfText.NotAllowed);
            query ??= new StateEventQuery();
            IQueryable<MfMachineStateEvent> q = _uow.Set<MfMachineStateEvent>().AsNoTracking();
            if (query.MachineId.HasValue) q = q.Where(e => e.MachineId == query.MachineId.Value);
            if (query.State.HasValue) q = q.Where(e => e.State == query.State.Value);
            if (query.From.HasValue) q = q.Where(e => e.EndAt == null || e.EndAt > query.From.Value);
            if (query.To.HasValue) q = q.Where(e => e.StartAt < query.To.Value);
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize), number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            var rows = await q.OrderByDescending(e => e.StartAt).ThenByDescending(e => e.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            List<int> ids = rows.Select(r => r.MachineId).Distinct().ToList();
            Dictionary<int, string> titles = await _uow.Set<Machine>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Title, ct);
            DateTime now = Clock();
            List<StateEventDto> dtos = rows.Select(e => new StateEventDto
            {
                Id = e.Id, MachineId = e.MachineId, MachineTitle = titles.TryGetValue(e.MachineId, out string t) ? t : null, State = e.State, StartAt = e.StartAt, EndAt = e.EndAt,
                StartAtText = MfText.Stamp(e.StartAt), EndAtText = MfText.Stamp(e.EndAt), Minutes = Math.Round(((e.EndAt ?? now) - e.StartAt).TotalMinutes, 1), Source = e.Source,
                AlarmCode = e.AlarmCode, AlarmText = e.AlarmText, MaintenanceRequestId = e.MaintenanceRequestId,
            }).ToList();
            return ResponseDtoHelper.Ok(Page(dtos, total, number, size));
        }

        public async Task<ResponseDto<List<MachineUtilisationDto>>> GetUtilisationAsync(UtilisationQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<List<MachineUtilisationDto>>(MfText.NotAllowed);
            if (query == null || query.To <= query.From) return Bad<List<MachineUtilisationDto>>("بازه زمانی نامعتبر است.");
            if ((query.To - query.From).TotalDays > 400) return Bad<List<MachineUtilisationDto>>("بازه زمانی حداکثر ۴۰۰ روز است.");
            DateTime from = query.From, to = query.To, now = Clock();
            IQueryable<MfMachineMonitor> q = _uow.Set<MfMachineMonitor>().AsNoTracking().Where(m => !m.Deleted && m.Source != MfMonitorSource.None);
            if (query.WorkCenterId.HasValue) q = q.Where(m => _uow.Set<MfMachineInfo>().Any(i => !i.Deleted && i.MachineId == m.MachineId && i.WorkCenterId == query.WorkCenterId.Value));
            List<int> ids = await q.Select(m => m.MachineId).ToListAsync(ct);
            Dictionary<int, string> titles = await _uow.Set<Machine>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Title, ct);
            Dictionary<int, string> centres = await CentreCodesAsync(ids, ct);
            var events = await _uow.Set<MfMachineStateEvent>().AsNoTracking().Where(e => ids.Contains(e.MachineId) && e.StartAt < to && (e.EndAt == null || e.EndAt > from)).ToListAsync(ct);
            var bookings = await _uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && b.MachineId != null && ids.Contains(b.MachineId.Value) && (b.Kind == MfBookingKind.Run || b.Kind == MfBookingKind.Setup)
                && b.StartAt < to && (b.EndAt == null || b.EndAt > from)).Select(b => new { MachineId = b.MachineId.Value, b.StartAt, b.EndAt }).ToListAsync(ct);
            var result = new List<MachineUtilisationDto>();
            foreach (int id in ids.OrderBy(i => titles.TryGetValue(i, out string t) ? t : ""))
            {
                var mine = events.Where(e => e.MachineId == id).ToList();
                var minutes = MfMonitorRules.Minutes(mine.Select(e => new MfMonitorRules.Span { State = e.State, StartAt = e.StartAt, EndAt = e.EndAt }), from, to, now);
                double booked = bookings.Where(b => b.MachineId == id).Sum(b =>
                {
                    DateTime s = b.StartAt < from ? from : b.StartAt, e = b.EndAt ?? now;
                    if (e > to) e = to;
                    return e > s ? (e - s).TotalMinutes : 0;
                });
                decimal measuredHours = Math.Round((decimal)minutes[MfMachineState.Running] / 60m, 2), bookedHours = Math.Round((decimal)booked / 60m, 2);
                result.Add(new MachineUtilisationDto
                {
                    MachineId = id, MachineTitle = titles.TryGetValue(id, out string mt) ? mt : null, WorkCenterCode = centres.TryGetValue(id, out string wc) ? wc : null, Minutes = Dto(minutes),
                    RunPercent = MfMonitorRules.RunPercent(minutes), Alarms = mine.Count(e => e.State == MfMachineState.Alarm && e.StartAt >= from), MeasuredRunHours = measuredHours, BookedHours = bookedHours,
                    BookingGapPercent = measuredHours > 0 ? Math.Round((bookedHours - measuredHours) / measuredHours * 100m, 1, MidpointRounding.AwayFromZero) : null,
                });
            }
            return ResponseDtoHelper.Ok(result);
        }
    }
}
