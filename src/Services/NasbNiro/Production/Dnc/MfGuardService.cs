using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Production.Dnc
{
    /// <summary>Roles of the guarded machining process (P2 part E). ★ provisional decisions, to be confirmed by the company.</summary>
    public static class GuardRoles
    {
        public const string Readers = DncRoles.Readers;
        public const string Editors = DncRoles.LinkEditors;
        public const string Testers = DncRoles.LinkTesters;

        /// <summary>Only the production manager opens a gate once, with a written reason (agreed with the company).</summary>
        public const string Overriders = "PRODUCTIONMANAGER";

        /// <summary>Who is told about every override.</summary>
        public const string OverrideReaders = "CEO,PRODUCTIONMANAGER,AUDITOR";
    }

    public interface IMfGuardService
    {
        Task<ResponseDto<List<MachineGuardDto>>> GetMachinesAsync(int? workCenterId, CancellationToken ct);
        Task<ResponseDto<MachineGuardDto>> SaveAsync(SaveMachineGuardDto dto, CancellationToken ct);
        Task<ResponseDto<GuardCapabilityDto>> TestAsync(MachineIdDto dto, CancellationToken ct);
        Task<ResponseDto<GuardPollResultDto>> RunPollAsync(CancellationToken ct);

        /// <summary>The background job (no user): reads every guarded control, records what changed, reads the measuring results and drives the start interlocks.</summary>
        Task<GuardPollResultDto> PollAsync(CancellationToken ct);

        Task<ResponseDto<PagedList<MachineActivityDto>>> GetActivityAsync(MachineActivityQuery query, CancellationToken ct);
        Task<ResponseDto<TerminalGuardDto>> TerminalAsync(int operationId, int machineId, CancellationToken ct);
        Task<ResponseDto<GateOverrideDto>> CreateOverrideAsync(CreateGateOverrideDto dto, CancellationToken ct);
        Task<ResponseDto<PagedList<GateOverrideDto>>> GetOverridesAsync(GateOverrideQuery query, CancellationToken ct);

        /// <summary>
        /// The guarded start (called by the terminal start after the DNC gate, before the booking is added): the link to the control, the program read back from the control against the approved
        /// revision, the tool table against the CAM tool list, and whether this run has to be proven. Fills the run records; on a refusal it saves the refusal on the time line of the machine.
        /// </summary>
        Task<string> CheckStartAsync(MfOperation op, int? machineId, MfBookingKind kind, int employeeId, List<MfNcRun> runs, CancellationToken ct);

        /// <summary>The finish of an operation: a run that had to be proven needs the data link to have seen single block or reduced feed (or the production manager's override).</summary>
        Task<string> CheckFinishAsync(MfOperation op, CancellationToken ct);

        /// <summary>After a booking on the machine was opened or closed: the start interlock follows at once (the poll would do it within seconds).</summary>
        Task SyncInterlockAsync(int machineId, CancellationToken ct);

        /// <summary>An edge box pushed the control data (FANUC FOCAS agent and the like): the same records as the LSV2 poll. The caller saves.</summary>
        Task ApplyPushAsync(MfMachineMonitor monitor, ControlSnapshot snapshot, string programSha256, DateTime now, CancellationToken ct);
    }

    /// <summary>
    /// The guarded machining process (production P2 part E). On a machine with a data link to its control nothing rests on the operator's word: the program the control will run is read back and compared with
    /// the approved revision, the tool table of the control with the CAM tool list, the first run of a revision has to be proven (single block or reduced feed, seen by the link), every change of mode, program
    /// state, override, program and tool is on the time line of the machine, and a relay on the NC-start enable lets the start button work only while a guarded booking is open. Only the production manager
    /// opens one gate once, with a written reason; the CEO, the production manager and the auditor are told.
    /// </summary>
    public class MfGuardService : IMfGuardService
    {
        public const string RequireCamEvidenceKey = "Dnc:RequireCamEvidence";
        public const int OverrideMinutes = 30;
        public const int MinReasonLength = 10;

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IMfControlLinkFactory _links;
        private readonly IMfCoilWriter _coils;
        private readonly INotificationService _notify;
        private readonly IConfiguration _configuration;

        internal Func<DateTime> Clock { get; set; } = () => DateTime.Now;

        public MfGuardService(IUnitOfWork uow, IInventoryUserContext user, IMfControlLinkFactory links = null, IMfCoilWriter coils = null, INotificationService notify = null, IConfiguration configuration = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _links = links ?? new MfControlLinkFactory();
            _coils = coils ?? new ModbusCoilWriter();
            _notify = notify;
            _configuration = configuration;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => MfText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => MfText.Bad<T>(m);
        private static bool IsAi => global::Services.NasbNiro.Ai.AiActing.IsActing;
        private static string Cut(string s, int n) => s == null ? null : s.Length > n ? s.Substring(0, n) : s;

        private decimal Setting(string key, decimal fallback) =>
            decimal.TryParse(_configuration?[key], NumberStyles.Float, CultureInfo.InvariantCulture, out decimal d) && d >= 0 ? d : fallback;

        private string ToolTablePath => Clean(_configuration?["Dnc:Guard:ToolTable"]) ?? "TNC:\\TOOL.T";

        private string PlcPassword(MfMachineMonitor m) =>
            string.IsNullOrWhiteSpace(m?.DataCredentialKey) ? null : _configuration?[$"Dnc:Credentials:{m.DataCredentialKey}:Password"];

        public static bool Guarded(MfMachineMonitor m) => m != null && !m.Deleted && m.IsActive && m.DataLink != MfDataLinkKind.None;

        /// <summary>The link answered within three poll intervals (at least 30 seconds) and is not failing.</summary>
        public static bool Healthy(MfMachineMonitor m, DateTime now)
        {
            if (m == null || m.DataLink == MfDataLinkKind.None || !m.DataSeenAt.HasValue) return false;
            double window = Math.Max(30, MfMonitorRules.Interval(m) * 3);
            return m.DataFailedPolls < MfMonitorRules.OfflineAfterFailures && (now - m.DataSeenAt.Value.LocalDateTime).TotalSeconds <= window;
        }

        public static string GateText(string gate) => gate switch
        {
            "program" => "هویت برنامه روی کنترلر",
            "tools" => "جدول ابزار ماشین",
            "prove-out" => "اثبات اجرای اول",
            "interlock" => "ارتباط داده با کنترلر",
            "measurement" => "اندازه‌گیری با دستگاه",
            _ => gate,
        };

        public static string KindText(MfActivityKind k) => k switch
        {
            MfActivityKind.ExecMode => "حالت کار",
            MfActivityKind.ProgramState => "وضعیت برنامه",
            MfActivityKind.Override => "Override",
            MfActivityKind.SelectedProgram => "برنامه انتخاب‌شده",
            MfActivityKind.Tool => "تعویض ابزار",
            MfActivityKind.ProgramVerified => "برنامه تطبیق داده شد",
            MfActivityKind.ProgramMismatch => "برنامه مغایر",
            MfActivityKind.ToolsVerified => "ابزارها تطبیق داده شد",
            MfActivityKind.ToolsMismatch => "ابزار مغایر",
            MfActivityKind.Measurement => "اندازه‌گیری",
            MfActivityKind.ProveOut => "اثبات اجرای اول",
            MfActivityKind.Interlock => "قفل استارت",
            MfActivityKind.GateRefused => "شروع رد شد",
            MfActivityKind.GateOverride => "مجوز یک‌باره",
            MfActivityKind.LinkLost => "قطع ارتباط",
            MfActivityKind.LinkBack => "وصل ارتباط",
            MfActivityKind.UnapprovedProgram => "برنامه تأییدنشده",
            _ => k.ToString(),
        };

        private static string Tone(MfActivityKind k, string value) => k switch
        {
            MfActivityKind.ProgramVerified or MfActivityKind.ToolsVerified or MfActivityKind.LinkBack => "ok",
            MfActivityKind.ProgramMismatch or MfActivityKind.ToolsMismatch or MfActivityKind.GateRefused or MfActivityKind.UnapprovedProgram or MfActivityKind.LinkLost => "bad",
            MfActivityKind.GateOverride => "warn",
            MfActivityKind.ProveOut => value == "observed" ? "ok" : "warn",
            MfActivityKind.Interlock => value == "on" ? "ok" : "warn",
            MfActivityKind.ProgramState => value == nameof(MfProgramState.Error) ? "bad" : "info",
            _ => "info",
        };

        private MfMachineActivity Add(int machineId, int? operationId, int? employeeId, DateTime at, MfActivityKind kind, string value, string text)
        {
            var a = new MfMachineActivity { MachineId = machineId, OperationId = operationId, EmployeeId = employeeId, At = at, Kind = kind, Value = Cut(value, 200), Text = Cut(text, 1000) };
            _uow.Set<MfMachineActivity>().Add(a);
            return a;
        }

        /// <summary>The open terminal booking of a machining step on the machine (the one a guarded start opened).</summary>
        private Task<MfTimeBooking> OpenBookingAsync(int machineId, CancellationToken ct) =>
            _uow.Set<MfTimeBooking>().AsNoTracking()
                .Where(b => !b.Deleted && b.EndAt == null && b.MachineId == machineId && b.Source == MfBookingSource.Terminal
                    && (b.Kind == MfBookingKind.Run || b.Kind == MfBookingKind.Setup || b.Kind == MfBookingKind.Rework))
                .OrderByDescending(b => b.StartAt).ThenByDescending(b => b.Id).FirstOrDefaultAsync(ct);

        // ================================================================================== settings

        private static MachineGuardDto Dto(Machine machine, MfMachineMonitor m, string centre, DateTime now) => new MachineGuardDto
        {
            MachineId = machine.Id, MachineTitle = machine.Title, WorkCenterCode = centre, HasMonitor = m != null, Guarded = Guarded(m),
            DataLink = m?.DataLink ?? MfDataLinkKind.None, DataHost = m?.DataHost, DataPort = m?.DataPort, DataCredentialKey = m?.DataCredentialKey, PlcMap = m?.PlcMap,
            ProgramFolder = m?.ProgramFolder, ResultFile = m?.ResultFile, Capabilities = m?.Capabilities, DataSeenAtText = MfText.Stamp(m?.DataSeenAt), DataFailedPolls = m?.DataFailedPolls ?? 0,
            DataError = m?.DataError, LinkHealthy = Healthy(m, now), ExecMode = m?.ExecMode ?? MfExecMode.Unknown, ExecModeText = MfGuardRules.Text(m?.ExecMode ?? MfExecMode.Unknown),
            ProgramState = m?.ProgramState ?? MfProgramState.Unknown, ProgramStateText = MfGuardRules.Text(m?.ProgramState ?? MfProgramState.Unknown), FeedOverride = m?.LastFeedOverride,
            SpindleOverride = m?.LastSpindleOverride, SelectedProgram = m?.LastProgramName, ToolNumber = m?.LastToolNumber, BlockNumber = m?.LastBlockNumber,
            InterlockEnabled = m?.InterlockEnabled ?? false, InterlockHost = m?.InterlockHost, InterlockPort = m?.InterlockPort, InterlockUnitId = m?.InterlockUnitId ?? 1, InterlockCoil = m?.InterlockCoil ?? 0,
            InterlockInvert = m?.InterlockInvert ?? false, StartEnabled = m?.StartEnabled ?? false, StartEnabledAtText = MfText.Stamp(m?.StartEnabledAt), StartBlockReason = m?.StartBlockReason,
            InterlockError = m?.InterlockError,
        };

        public async Task<ResponseDto<List<MachineGuardDto>>> GetMachinesAsync(int? workCenterId, CancellationToken ct)
        {
            if (!_user.IsInRole(GuardRoles.Readers)) return Bad<List<MachineGuardDto>>(MfText.NotAllowed);
            var infos = await _uow.Set<MfMachineInfo>().AsNoTracking().Where(i => !i.Deleted && i.Status != MfMachineStatus.Retired && (workCenterId == null || i.WorkCenterId == workCenterId))
                .Select(i => new { i.MachineId, i.WorkCenterId }).ToListAsync(ct);
            List<int> ids = infos.Select(i => i.MachineId).Distinct().ToList();
            List<Machine> machines = await _uow.Set<Machine>().AsNoTracking().Where(m => ids.Contains(m.Id)).OrderBy(m => m.Title).ToListAsync(ct);
            Dictionary<int, MfMachineMonitor> monitors = (await _uow.Set<MfMachineMonitor>().AsNoTracking().Where(m => !m.Deleted && ids.Contains(m.MachineId)).ToListAsync(ct)).ToDictionary(m => m.MachineId);
            List<int> centreIds = infos.Where(i => i.WorkCenterId.HasValue).Select(i => i.WorkCenterId.Value).Distinct().ToList();
            Dictionary<int, string> codes = await _uow.Set<MfWorkCenter>().AsNoTracking().Where(w => centreIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Code, ct);
            DateTime now = Clock();
            return ResponseDtoHelper.Ok(machines.Select(machine =>
            {
                int? wc = infos.FirstOrDefault(i => i.MachineId == machine.Id)?.WorkCenterId;
                return Dto(machine, monitors.GetValueOrDefault(machine.Id), wc.HasValue && codes.TryGetValue(wc.Value, out string c) ? c : null, now);
            }).ToList());
        }

        private async Task<MachineGuardDto> OneAsync(int machineId, CancellationToken ct)
        {
            Machine machine = await _uow.Set<Machine>().AsNoTracking().FirstAsync(m => m.Id == machineId, ct);
            MfMachineMonitor m = await _uow.Set<MfMachineMonitor>().AsNoTracking().FirstOrDefaultAsync(x => x.MachineId == machineId && !x.Deleted, ct);
            int? wc = await _uow.Set<MfMachineInfo>().AsNoTracking().Where(i => i.MachineId == machineId && !i.Deleted).Select(i => i.WorkCenterId).FirstOrDefaultAsync(ct);
            string code = wc.HasValue ? await _uow.Set<MfWorkCenter>().AsNoTracking().Where(w => w.Id == wc.Value).Select(w => w.Code).FirstOrDefaultAsync(ct) : null;
            return Dto(machine, m, code, Clock());
        }

        private static readonly Regex CredentialKey = new Regex(@"^[A-Za-z0-9_\-]{1,50}$", RegexOptions.CultureInvariant);

        public async Task<ResponseDto<MachineGuardDto>> SaveAsync(SaveMachineGuardDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(GuardRoles.Editors) || IsAi) return Bad<MachineGuardDto>(MfText.NotAllowed);
            if (dto == null || !Enum.IsDefined(typeof(MfDataLinkKind), dto.DataLink)) return Bad<MachineGuardDto>(MfText.InvalidData);
            if (!await _uow.Set<Machine>().AnyAsync(m => m.Id == dto.MachineId, ct)) return Bad<MachineGuardDto>("ماشین پیدا نشد.");
            bool loopback = string.Equals(_configuration?["Dnc:AllowLoopback"], "true", StringComparison.OrdinalIgnoreCase);
            string host = Clean(dto.DataHost), key = Clean(dto.DataCredentialKey), map = Clean(dto.PlcMap), folder = Clean(dto.ProgramFolder), result = Clean(dto.ResultFile);
            if (dto.DataLink == MfDataLinkKind.Lsv2Tcp)
            {
                if (host == null || !NcLan.IsLanHost(host, loopback)) return Bad<MachineGuardDto>("نشانی کنترلر باید نشانی IP شبکه داخلی کارگاه باشد (مثلاً 192.168.10.21).");
                if (dto.DataPort is < 1 or > 65535) return Bad<MachineGuardDto>("درگاه باید بین ۱ و ۶۵۵۳۵ باشد (LSV2: 19000).");
            }
            if (dto.DataLink == MfDataLinkKind.GatewayPush && !await _uow.Set<MfMachineConnection>().AnyAsync(c => !c.Deleted && c.MachineId == dto.MachineId && c.LinkKind == MfNcLinkKind.EdgeGateway, ct))
                return Bad<MachineGuardDto>("برای دریافت داده از گیت‌وی، ابتدا اتصال DNC ماشین را «گیت‌وی لبه» تعریف و کلید آن را صادر کنید.");
            if (key != null && !CredentialKey.IsMatch(key)) return Bad<MachineGuardDto>("نام کلید رمز فقط حروف لاتین، رقم، خط تیره و زیرخط است (رمز در تنظیمات سرور نگه‌داری می‌شود، نه اینجا).");
            if (map != null && MfGuardRules.ParsePlcMap(map).Error is string mapError) return Bad<MachineGuardDto>(mapError);
            if (folder != null && !MfGuardRules.IsControlPath(folder)) return Bad<MachineGuardDto>("پوشه برنامه‌ها روی کنترلر نامعتبر است (نمونه: TNC:\\nc_prog\\).");
            if (result != null && !MfGuardRules.IsControlPath(result)) return Bad<MachineGuardDto>("مسیر فایل نتایج اندازه‌گیری روی کنترلر نامعتبر است (نمونه: TNC:\\RESULT.TXT).");
            string ilHost = Clean(dto.InterlockHost);
            if (dto.InterlockEnabled)
            {
                if (dto.DataLink == MfDataLinkKind.None) return Bad<MachineGuardDto>("قفل استارت فقط روی ماشینی که اتصال داده به کنترلر دارد فعال می‌شود.");
                if (ilHost == null || !NcLan.IsLanHost(ilHost, loopback)) return Bad<MachineGuardDto>("نشانی ماژول رله قفل استارت باید نشانی IP شبکه داخلی باشد.");
                if (dto.InterlockPort is < 1 or > 65535) return Bad<MachineGuardDto>("درگاه ماژول رله باید بین ۱ و ۶۵۵۳۵ باشد (Modbus: 502).");
                if (dto.InterlockUnitId < 0 || dto.InterlockUnitId > 247 || dto.InterlockCoil < 0 || dto.InterlockCoil > 65535) return Bad<MachineGuardDto>("شناسه واحد یا شماره خروجی رله نامعتبر است.");
            }
            MfMachineMonitor row = await _uow.Set<MfMachineMonitor>().FirstOrDefaultAsync(x => x.MachineId == dto.MachineId && !x.Deleted, ct);
            DateTimeOffset stamp = DateTimeOffset.Now;
            if (row == null)
            {
                row = new MfMachineMonitor { MachineId = dto.MachineId, Source = MfMonitorSource.None, MismatchMinutes = 15, IsActive = true, CreateDate = stamp, CreatorUserId = Me };
                _uow.Set<MfMachineMonitor>().Add(row);
            }
            bool linkChanged = row.DataLink != dto.DataLink || row.DataHost != host || row.DataPort != dto.DataPort;
            row.DataLink = dto.DataLink; row.DataHost = dto.DataLink == MfDataLinkKind.Lsv2Tcp ? host : null; row.DataPort = dto.DataLink == MfDataLinkKind.Lsv2Tcp ? dto.DataPort : null;
            row.DataCredentialKey = key; row.PlcMap = map; row.ProgramFolder = folder; row.ResultFile = result;
            // a monitor without its own source takes the state from the control
            if (row.Source == MfMonitorSource.None && dto.DataLink == MfDataLinkKind.Lsv2Tcp) row.Source = MfMonitorSource.ControlLink;
            if (row.Source == MfMonitorSource.ControlLink && dto.DataLink != MfDataLinkKind.Lsv2Tcp) row.Source = MfMonitorSource.None;
            if (linkChanged) { row.DataFailedPolls = 0; row.DataError = null; row.DataSeenAt = null; row.Capabilities = null; }
            bool wasEnabled = row.InterlockEnabled;
            row.InterlockEnabled = dto.InterlockEnabled; row.InterlockHost = ilHost; row.InterlockPort = dto.InterlockPort; row.InterlockUnitId = (byte)Math.Clamp(dto.InterlockUnitId, 0, 247);
            row.InterlockCoil = dto.InterlockCoil; row.InterlockInvert = dto.InterlockInvert; row.InterlockError = null;
            if (!dto.InterlockEnabled) { row.StartEnabled = false; row.StartBlockReason = null; }
            if (dto.InterlockEnabled && !wasEnabled) { row.StartEnabled = false; row.StartEnabledAt = null; }
            row.ModifyDate = stamp; row.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<MachineGuardDto>(MfText.Concurrency); }
            catch (DbUpdateException) { return Bad<MachineGuardDto>("برای این ماشین هم‌زمان پایش دیگری ثبت شد؛ صفحه را دوباره باز کنید."); }
            if (row.InterlockEnabled) await SyncInterlockAsync(row.MachineId, ct);
            return ResponseDtoHelper.Ok(await OneAsync(dto.MachineId, ct));
        }

        public async Task<ResponseDto<GuardCapabilityDto>> TestAsync(MachineIdDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(GuardRoles.Testers) || IsAi) return Bad<GuardCapabilityDto>(MfText.NotAllowed);
            MfMachineMonitor m = dto == null ? null : await _uow.Set<MfMachineMonitor>().FirstOrDefaultAsync(x => x.MachineId == dto.MachineId && !x.Deleted, ct);
            if (m == null || m.DataLink == MfDataLinkKind.None) return Bad<GuardCapabilityDto>("برای این ماشین اتصال داده به کنترلر تعریف نشده است.");
            var result = new GuardCapabilityDto();
            if (m.DataLink == MfDataLinkKind.GatewayPush)
            {
                result.Ok = Healthy(m, Clock());
                result.Message = result.Ok ? "گیت‌وی ماشین داده می‌فرستد." : "از گیت‌وی ماشین داده تازه‌ای نرسیده است.";
                result.Lines.Add("آخرین داده: " + (MfText.Stamp(m.DataSeenAt) ?? "هرگز"));
                result.Lines.Add((m.LastProgramShaAt.HasValue ? "✔ " : "✘ ") + "اثر انگشت برنامه انتخاب‌شده (برای گیت هویت برنامه)");
                result.Lines.Add("✘ جدول ابزار از راه گیت‌وی خوانده نمی‌شود؛ گیت ابزار روی این ماشین بررسی نمی‌شود.");
            }
            else
            {
                try
                {
                    using IMfControlSession s = await _links.OpenAsync(m, PlcPassword(m), ct);
                    result.Lines = await s.CapabilitiesAsync(m.ProgramFolder, ct);
                    result.Ok = true;
                    result.Message = "ارتباط با کنترلر برقرار شد.";
                }
                catch (Exception ex) when (ex is Lsv2Exception || ex is System.IO.IOException || ex is System.Net.Sockets.SocketException || ex is TimeoutException)
                {
                    result.Ok = false;
                    result.Message = ex is Lsv2Exception ? ex.Message : NcLinkText.Network(ex);
                }
            }
            m.Capabilities = Cut(string.Join("\n", new[] { (result.Ok ? "✔ " : "✘ ") + result.Message }.Concat(result.Lines)), 4000);
            m.ModifyDate = DateTimeOffset.Now;
            try { await _uow.SaveChangesAsync(ct); } catch (DbUpdateException) { /* the report is returned anyway */ }
            return ResponseDtoHelper.Ok(result);
        }

        // ================================================================================== the poll

        public async Task<ResponseDto<GuardPollResultDto>> RunPollAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Supervisors) || IsAi) return Bad<GuardPollResultDto>(MfText.NotAllowed);
            return ResponseDtoHelper.Ok(await PollAsync(ct));
        }

        /// <summary>The last poll of each data link (the poll interval of the monitor).</summary>
        private static readonly ConcurrentDictionary<int, DateTime> _lastPoll = new ConcurrentDictionary<int, DateTime>();

        /// <summary>
        /// The program per machine that ran without approval, and the booking it ran under. The start stays locked until that booking is closed: the next start passes the gates again (program read back
        /// from the control). The inbox alert stays for the supervisor. Kept in memory: after a restart the next poll finds a program that still runs.
        /// </summary>
        private static readonly ConcurrentDictionary<int, (string Program, long? BookingId)> _unapproved = new ConcurrentDictionary<int, (string, long?)>();

        /// <summary>Runs whose prove-out warning was already sent.</summary>
        private static readonly ConcurrentDictionary<int, byte> _proveOutWarned = new ConcurrentDictionary<int, byte>();

        public async Task<GuardPollResultDto> PollAsync(CancellationToken ct)
        {
            var result = new GuardPollResultDto();
            DateTime now = Clock();
            List<MfMachineMonitor> monitors = await _uow.Set<MfMachineMonitor>().Where(m => !m.Deleted && m.IsActive && m.DataLink == MfDataLinkKind.Lsv2Tcp).ToListAsync(ct);
            foreach (MfMachineMonitor m in monitors)
            {
                ct.ThrowIfCancellationRequested();
                if (_lastPoll.TryGetValue(m.MachineId, out DateTime last) && (now - last).TotalSeconds < MfMonitorRules.Interval(m) - 0.5 && (now - last).TotalSeconds >= 0) continue;
                _lastPoll[m.MachineId] = now;
                result.Polled++;
                try
                {
                    using IMfControlSession s = await _links.OpenAsync(m, PlcPassword(m), ct);
                    ControlSnapshot snap = await s.ReadAsync(ct);
                    if (m.DataFailedPolls >= MfMonitorRules.OfflineAfterFailures) { Add(m.MachineId, null, null, now, MfActivityKind.LinkBack, null, "ارتباط داده با کنترلر دوباره برقرار شد."); result.Activities++; }
                    m.DataFailedPolls = 0; m.DataError = null;
                    var stats = await ApplyAsync(m, snap, now, s, ct);
                    result.Activities += stats.Activities; result.Unapproved += stats.Unapproved; result.Measurements += stats.Measurements;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    result.Failed++;
                    m.DataFailedPolls++;
                    m.DataError = Cut(ex is Lsv2Exception ? ex.Message : NcLinkText.Network(ex), 1000);
                    if (m.DataFailedPolls == MfMonitorRules.OfflineAfterFailures)
                    {
                        MfTimeBooking open = await OpenBookingAsync(m.MachineId, ct);
                        Add(m.MachineId, open?.OperationId, open?.EmployeeId, now, MfActivityKind.LinkLost, null, "ارتباط داده با کنترلر قطع شد: " + m.DataError);
                        result.Activities++;
                        if (m.Source == MfMonitorSource.ControlLink) await StateAsync(m, MfMachineState.Offline, null, now, ct);
                    }
                }
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { if (_uow is DbContext db) db.ChangeTracker.Clear(); }
            result.InterlockWrites = await SyncInterlocksAsync(null, ct);
            return result;
        }

        private sealed class ApplyStats
        {
            public int Activities { get; set; }
            public int Unapproved { get; set; }
            public int Measurements { get; set; }
        }

        public async Task ApplyPushAsync(MfMachineMonitor monitor, ControlSnapshot snapshot, string programSha256, DateTime now, CancellationToken ct)
        {
            if (monitor == null || snapshot == null || monitor.DataLink != MfDataLinkKind.GatewayPush) return;
            if (monitor.DataFailedPolls >= MfMonitorRules.OfflineAfterFailures) Add(monitor.MachineId, null, null, now, MfActivityKind.LinkBack, null, "گیت‌وی ماشین دوباره داده فرستاد.");
            monitor.DataFailedPolls = 0; monitor.DataError = null;
            string sha = Clean(programSha256)?.ToUpperInvariant();
            if (sha != null && Regex.IsMatch(sha, "^[0-9A-F]{64}$", RegexOptions.CultureInvariant)) { monitor.LastProgramSha256 = sha; monitor.LastProgramShaAt = new DateTimeOffset(now); }
            await ApplyAsync(monitor, snapshot, now, null, ct);
        }

        /// <summary>Records what changed on the control, opens the state period of a machine whose state comes from the control, flags an unapproved program and reads the measuring results.</summary>
        private async Task<ApplyStats> ApplyAsync(MfMachineMonitor m, ControlSnapshot snap, DateTime now, IMfControlSession session, CancellationToken ct)
        {
            var stats = new ApplyStats();
            MfTimeBooking open = await OpenBookingAsync(m.MachineId, ct);
            int? opId = open?.OperationId, empId = open?.EmployeeId;
            MfProgramState before = m.ProgramState;
            m.DataSeenAt = new DateTimeOffset(now);
            if (snap.ExecMode.HasValue && snap.ExecMode.Value != m.ExecMode)
            {
                Add(m.MachineId, opId, empId, now, MfActivityKind.ExecMode, snap.ExecMode.Value.ToString(), "حالت کار: " + MfGuardRules.Text(snap.ExecMode.Value)); stats.Activities++;
                m.ExecMode = snap.ExecMode.Value;
            }
            if (snap.ProgramState.HasValue && snap.ProgramState.Value != m.ProgramState)
            {
                Add(m.MachineId, opId, empId, now, MfActivityKind.ProgramState, snap.ProgramState.Value.ToString(), "برنامه: " + MfGuardRules.Text(snap.ProgramState.Value)); stats.Activities++;
                m.ProgramState = snap.ProgramState.Value;
            }
            if ((snap.FeedOverride.HasValue && snap.FeedOverride != m.LastFeedOverride) || (snap.SpindleOverride.HasValue && snap.SpindleOverride != m.LastSpindleOverride))
            {
                int? feed = snap.FeedOverride ?? m.LastFeedOverride, spindle = snap.SpindleOverride ?? m.LastSpindleOverride;
                Add(m.MachineId, opId, empId, now, MfActivityKind.Override, $"{feed?.ToString(CultureInfo.InvariantCulture) ?? "-"}/{spindle?.ToString(CultureInfo.InvariantCulture) ?? "-"}",
                    $"Override پیشروی {feed?.ToString(CultureInfo.InvariantCulture) ?? "-"}٪، اسپیندل {spindle?.ToString(CultureInfo.InvariantCulture) ?? "-"}٪"); stats.Activities++;
                m.LastFeedOverride = feed; m.LastSpindleOverride = spindle;
            }
            string program = Cut(Clean(snap.SelectedProgram), 200);
            if (program != null && !string.Equals(program, m.LastProgramName, StringComparison.OrdinalIgnoreCase))
            {
                Add(m.MachineId, opId, empId, now, MfActivityKind.SelectedProgram, program, "برنامه انتخاب‌شده روی کنترلر: " + program); stats.Activities++;
                m.LastProgramName = program;
            }
            if (snap.ToolNumber.HasValue && snap.ToolNumber != m.LastToolNumber)
            {
                Add(m.MachineId, opId, empId, now, MfActivityKind.Tool, snap.ToolNumber.Value.ToString(CultureInfo.InvariantCulture), "ابزار جاری: T" + snap.ToolNumber.Value.ToString(CultureInfo.InvariantCulture)); stats.Activities++;
                m.LastToolNumber = snap.ToolNumber;
            }
            if (snap.BlockNumber.HasValue) m.LastBlockNumber = snap.BlockNumber;

            if (m.Source == MfMonitorSource.ControlLink && snap.ProgramState.HasValue)
                await StateAsync(m, m.ProgramState == MfProgramState.Running ? MfMachineState.Running : m.ProgramState == MfProgramState.Error ? MfMachineState.Alarm : MfMachineState.Idle,
                    m.ProgramState == MfProgramState.Error ? "خطای برنامه روی کنترلر" : null, now, ct);

            // a program runs that is not the approved revision of the booked step (or runs with no guarded booking at all)
            bool running = m.ProgramState == MfProgramState.Running && (m.ExecMode == MfExecMode.Automatic || m.ExecMode == MfExecMode.SingleBlock);
            string flagged = null;
            if (running && m.LastProgramName != null)
            {
                List<string> allowed = await AllowedProgramsAsync(m.MachineId, open, ct);
                if (allowed != null && !allowed.Any(a => SameProgram(m.LastProgramName, a))) flagged = m.LastProgramName;
            }
            long? bookingId = open?.Id;
            string was = _unapproved.TryGetValue(m.MachineId, out var w) ? w.Program : null;
            if (was != null && flagged == null && w.BookingId != bookingId)
            {
                // the booking it ran under is closed (or another one is open after passing the gates): the lock goes; the alert stays for the supervisor
                _unapproved.TryRemove(m.MachineId, out _);
                was = null;
            }
            if (flagged != null && (!string.Equals(flagged, was, StringComparison.OrdinalIgnoreCase) || w.BookingId != bookingId))
            {
                string why = open == null ? "بدون ثبت شروع کار روی ترمینال" : "که برنامه تأییدشده مرحله ثبت‌شده نیست";
                Add(m.MachineId, opId, empId, now, MfActivityKind.UnapprovedProgram, flagged, $"برنامه «{flagged}» روی ماشین اجرا شد {why}."); stats.Activities++; stats.Unapproved++;
                string title = await _uow.Set<Machine>().AsNoTracking().Where(x => x.Id == m.MachineId).Select(x => x.Title).FirstOrDefaultAsync(ct);
                await OfficeGates.SyncAsync(_notify, "mf:guard-unapproved:" + m.MachineId, true, "production", "guard-unapproved", m.MachineId,
                    $"برنامه تأییدنشده «{flagged}» روی ماشین {title} اجرا شد", "/production/guard?machineId=" + m.MachineId, ProductionRoles.Supervisors + ",QC", ct,
                    kind: OfInboxKind.Alert, priority: OfUrgency.Urgent);
                _unapproved[m.MachineId] = (flagged, bookingId);
            }

            // the first run of a revision at full feed in automatic: told at once (the finish asks for the proof)
            if (running && m.ExecMode == MfExecMode.Automatic && before != MfProgramState.Running && open != null && (m.LastFeedOverride ?? 100) > MfGuardRules.ProveOutFeedMax)
            {
                MfNcRun prove = await _uow.Set<MfNcRun>().AsNoTracking().Where(r => r.OperationId == open.OperationId && r.MachineId == m.MachineId && r.ProveOut && r.ProveOutObserved == null && r.StartedAt >= open.StartAt)
                    .FirstOrDefaultAsync(ct);
                if (prove != null && !await ProveOutSeenAsync(m.MachineId, prove.StartedAt, now, ct) && _proveOutWarned.TryAdd(prove.Id, 0))
                {
                    Add(m.MachineId, opId, empId, now, MfActivityKind.ProveOut, "missing", "اجرای اول این نسخه بدون تک‌بلوک و با پیشروی بالای ۵۰٪ شروع شد."); stats.Activities++;
                    await OfficeGates.SyncAsync(_notify, "mf:guard-proveout:" + prove.Id, true, "production", "guard-proveout", m.MachineId,
                        "اجرای اول برنامه بدون اثبات (تک‌بلوک یا پیشروی کم) روی ماشین شروع شد", "/production/guard?machineId=" + m.MachineId, ProductionRoles.Supervisors, ct,
                        kind: OfInboxKind.Alert, priority: OfUrgency.Urgent);
                }
            }

            // the measuring cycles wrote their results: read once per change of the file, after the program ended
            if (session != null && m.ResultFile != null && before == MfProgramState.Running && m.ProgramState != MfProgramState.Running)
            {
                DateTimeOffset? time = await session.FileTimeAsync(m.ResultFile, ct);
                if (time.HasValue && (!m.ResultReadAt.HasValue || time.Value > m.ResultReadAt.Value))
                {
                    byte[] bytes = await session.ReadFileAsync(m.ResultFile, 256 * 1024, ct);
                    var values = MfGuardRules.ParseResults(bytes == null ? null : Encoding.Latin1.GetString(bytes));
                    foreach (var (code, value) in values.Take(500))
                        _uow.Set<MfMeasurement>().Add(new MfMeasurement { OperationId = opId, MachineId = m.MachineId, Characteristic = code, Value = value, Source = MfMeasurementSource.Probe, Device = "probe:" + m.ResultFile, At = now });
                    if (values.Count > 0)
                    {
                        Add(m.MachineId, opId, empId, now, MfActivityKind.Measurement, values.Count.ToString(CultureInfo.InvariantCulture), $"{values.Count} نتیجه اندازه‌گیری پراب از کنترلر خوانده شد."); stats.Activities++;
                        stats.Measurements += values.Count;
                    }
                    m.ResultReadAt = time;
                }
            }
            return stats;
        }

        /// <summary>Same program: the control may report the full path ("TNC:\nc_prog\1234.H") where the transfer named only the file.</summary>
        private static bool SameProgram(string onControl, string approved)
        {
            string Name(string p) => (p ?? string.Empty).Replace('/', '\\').Split('\\').Last().Trim();
            return string.Equals(Name(onControl), Name(approved), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The files the open booking may run (the transfers of its runs). No booking, or a step without a CNC program: nothing is allowed.</summary>
        private async Task<List<string>> AllowedProgramsAsync(int machineId, MfTimeBooking open, CancellationToken ct)
        {
            if (open == null) return new List<string>();
            List<int?> transfers = await _uow.Set<MfNcRun>().AsNoTracking().Where(r => r.OperationId == open.OperationId && r.MachineId == machineId && r.StartedAt >= open.StartAt)
                .Select(r => r.TransferId).ToListAsync(ct);
            // a step with no CNC program in the library runs no program in automatic either (manual and MDI work are not "running a program")
            if (transfers.Count == 0) return new List<string>();
            List<int> ids = transfers.Where(t => t.HasValue).Select(t => t.Value).ToList();
            return await _uow.Set<MfNcTransfer>().AsNoTracking().Where(t => ids.Contains(t.Id)).Select(t => t.TargetName).ToListAsync(ct);
        }

        /// <summary>Opens the new state period of a machine whose state comes from the control (the monitor's alerts follow in its own poll).</summary>
        private async Task StateAsync(MfMachineMonitor m, MfMachineState state, string alarmText, DateTime now, CancellationToken ct)
        {
            MfMachineStateEvent open = await _uow.Set<MfMachineStateEvent>().Where(e => e.MachineId == m.MachineId && e.EndAt == null).OrderByDescending(e => e.Id).FirstOrDefaultAsync(ct);
            if (open != null && open.State == state) return;
            if (open != null) open.EndAt = now < open.StartAt ? open.StartAt : now;
            _uow.Set<MfMachineStateEvent>().Add(new MfMachineStateEvent { MachineId = m.MachineId, State = state, StartAt = now, Source = MfMonitorSource.ControlLink, AlarmText = state == MfMachineState.Alarm ? alarmText : null });
            m.LastPollAt = new DateTimeOffset(now); m.LastSeenAt = state == MfMachineState.Offline ? m.LastSeenAt : new DateTimeOffset(now);
        }

        // ================================================================================== interlock

        public async Task SyncInterlockAsync(int machineId, CancellationToken ct)
        {
            try { await SyncInterlocksAsync(machineId, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* the poll does it again within seconds */ }
        }

        /// <summary>
        /// The start may be pressed only while a guarded terminal booking of a machining step is open on the machine, the data link is healthy (or the production manager let this operation go on without it)
        /// and no unapproved program runs. Written when it changes and every minute (a relay module that lost its power gets the state back; its watchdog turns the output off when the server is silent).
        /// A cycle that runs is never interrupted: the relay is wired into the start enable only.
        /// </summary>
        private async Task<int> SyncInterlocksAsync(int? machineId, CancellationToken ct)
        {
            List<MfMachineMonitor> monitors = await _uow.Set<MfMachineMonitor>().Where(m => !m.Deleted && m.IsActive && m.InterlockEnabled && (machineId == null || m.MachineId == machineId)).ToListAsync(ct);
            if (monitors.Count == 0) return 0;
            DateTime now = Clock();
            int writes = 0;
            foreach (MfMachineMonitor m in monitors)
            {
                MfTimeBooking open = await OpenBookingAsync(m.MachineId, ct);
                string reason = null;
                if (open == null) reason = "شروع کار روی ترمینال ثبت نشده است.";
                else if (_unapproved.ContainsKey(m.MachineId)) reason = "برنامه تأییدنشده روی ماشین اجرا شده است؛ سرپرست باید بررسی کند.";
                else if (m.DataLink != MfDataLinkKind.None && !Healthy(m, now))
                {
                    DateTimeOffset since = new DateTimeOffset(open.StartAt);
                    bool allowed = await _uow.Set<MfGateOverride>().AsNoTracking().AnyAsync(o => !o.Deleted && o.MachineId == m.MachineId && o.OperationId == open.OperationId && o.Gate == "interlock"
                        && o.ConsumedAt != null && o.ConsumedAt >= since.AddMinutes(-1), ct);
                    if (!allowed) reason = "ارتباط داده با کنترلر برقرار نیست.";
                }
                bool desired = reason == null;
                bool refresh = !m.StartEnabledAt.HasValue || (now - m.StartEnabledAt.Value.LocalDateTime).TotalSeconds >= 60 || m.InterlockError != null;
                if (desired == m.StartEnabled && !refresh && m.StartBlockReason == reason) continue;
                string error = await _coils.WriteAsync(m.InterlockHost, m.InterlockPort ?? 502, m.InterlockUnitId, m.InterlockCoil, desired ^ m.InterlockInvert, ct);
                writes++;
                if (error != null)
                {
                    if (m.InterlockError != error) Add(m.MachineId, open?.OperationId, open?.EmployeeId, now, MfActivityKind.Interlock, "error", "فرمان قفل استارت نرسید: " + error);
                    m.InterlockError = Cut(error, 1000);
                    continue;
                }
                if (desired != m.StartEnabled)
                    Add(m.MachineId, open?.OperationId, open?.EmployeeId, now, MfActivityKind.Interlock, desired ? "on" : "off", desired ? "استارت ماشین آزاد شد." : "استارت ماشین قفل شد: " + reason);
                m.StartEnabled = desired; m.StartEnabledAt = new DateTimeOffset(now); m.StartBlockReason = reason; m.InterlockError = null;
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { if (_uow is DbContext db) db.ChangeTracker.Clear(); }
            return writes;
        }

        // ================================================================================== the guarded start

        private async Task<List<MfGateOverride>> ActiveOverridesAsync(int machineId, int operationId, DateTime now, CancellationToken ct)
        {
            DateTimeOffset at = new DateTimeOffset(now);
            return await _uow.Set<MfGateOverride>().Where(o => !o.Deleted && o.MachineId == machineId && o.OperationId == operationId && o.ConsumedAt == null && o.ExpiresAt > at).OrderBy(o => o.Id).ToListAsync(ct);
        }

        private sealed class GateOutcome
        {
            public List<GuardGateDto> Gates { get; } = new List<GuardGateDto>();
            public string Error { get; set; }
            public string ErrorGate { get; set; }
            public List<MfGateOverride> Used { get; } = new List<MfGateOverride>();
            public List<(MfActivityKind Kind, string Value, string Text)> Activities { get; } = new List<(MfActivityKind, string, string)>();

            public void Gate(string code, string state, string text, bool overridden = false) =>
                Gates.Add(new GuardGateDto { Code = code, Title = GateText(code), State = state, Text = text, Overridden = overridden });
        }

        /// <summary>
        /// The gates of the guarded start. <paramref name="runs"/>: the run records the DNC gate made (one per program of the step); they get the fingerprint read from the control, the tool result and the
        /// prove-out flag. Overrides found are marked used (the caller saves them with the booking). Stops at the first refusal.
        /// </summary>
        private async Task<GateOutcome> EvaluateAsync(MfMachineMonitor m, MfOperation op, MfBookingKind kind, List<MfNcRun> runs, DateTime now, CancellationToken ct)
        {
            var o = new GateOutcome();
            List<MfGateOverride> overrides = await ActiveOverridesAsync(m.MachineId, op.Id, now, ct);
            MfGateOverride Use(string gate)
            {
                MfGateOverride x = overrides.FirstOrDefault(v => v.Gate == gate);
                if (x != null && !o.Used.Contains(x)) o.Used.Add(x);
                return x;
            }

            // 1. the data link
            IMfControlSession session = null;
            bool linkUp;
            if (m.DataLink == MfDataLinkKind.Lsv2Tcp)
            {
                try { session = await _links.OpenAsync(m, PlcPassword(m), ct); linkUp = true; m.DataSeenAt = new DateTimeOffset(now); m.DataFailedPolls = 0; m.DataError = null; }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { linkUp = false; m.DataError = Cut(ex is Lsv2Exception ? ex.Message : NcLinkText.Network(ex), 1000); }
            }
            else linkUp = Healthy(m, now);
            try
            {
                bool linkOverridden = false;
                if (!linkUp)
                {
                    if (Use("interlock") is MfGateOverride ov)
                    {
                        linkOverridden = true;
                        o.Gate("interlock", "ok", "ارتباط برقرار نیست؛ با مجوز یک‌باره مدیر تولید: " + ov.Reason, true);
                        o.Activities.Add((MfActivityKind.GateOverride, "interlock", "شروع بدون ارتباط داده با مجوز یک‌باره مدیر تولید: " + ov.Reason));
                    }
                    else
                    {
                        o.Gate("interlock", "blocked", "ارتباط داده با کنترلر برقرار نیست" + (m.DataError != null ? ": " + m.DataError : "."));
                        o.Error = "ارتباط داده با کنترلر این ماشین برقرار نیست؛ بدون آن شروع کار ممکن نیست. کابل شبکه و روشن بودن کنترلر را بررسی کنید یا به سرپرست خبر دهید.";
                        o.ErrorGate = "interlock";
                        return o;
                    }
                }
                else o.Gate("interlock", "ok", m.DataLink == MfDataLinkKind.Lsv2Tcp ? "کنترلر پاسخ داد." : "گیت‌وی ماشین داده تازه فرستاده است.");

                List<MfNcRun> programRuns = runs.Where(r => r.RevisionId > 0).ToList();
                // 2. the program the control will run is the approved revision, byte for byte (after the control's own re-spacing)
                if (programRuns.Count == 0) o.Gate("program", "skipped", "این مرحله برنامه CNC ندارد.");
                else if (linkOverridden) o.Gate("program", "skipped", "بدون ارتباط بررسی نشد (مجوز یک‌باره).", true);
                else
                {
                    string problem = null;
                    foreach (MfNcRun run in programRuns)
                    {
                        problem = await VerifyProgramAsync(m, run, session, now, ct);
                        if (problem != null) break;
                    }
                    if (problem != null)
                    {
                        if (Use("program") is MfGateOverride ov)
                        {
                            o.Gate("program", "ok", problem + " — با مجوز یک‌باره مدیر تولید: " + ov.Reason, true);
                            o.Activities.Add((MfActivityKind.ProgramMismatch, "override", problem));
                            o.Activities.Add((MfActivityKind.GateOverride, "program", "شروع با برنامه تطبیق‌نیافته با مجوز یک‌باره مدیر تولید: " + ov.Reason));
                        }
                        else
                        {
                            o.Gate("program", "blocked", problem);
                            o.Activities.Add((MfActivityKind.ProgramMismatch, null, problem));
                            o.Error = problem; o.ErrorGate = "program";
                            return o;
                        }
                    }
                    else
                    {
                        o.Gate("program", "ok", "برنامه روی کنترلر با نسخه تأییدشده یکی است.");
                        o.Activities.Add((MfActivityKind.ProgramVerified, programRuns[0].VerifiedSha256?.Substring(0, 12), "برنامه روی کنترلر با نسخه تأییدشده یکی است."));
                    }
                }

                // 3. the tool table of the control against the CAM tool list
                List<int> revisionIds = programRuns.Select(r => r.RevisionId).Distinct().ToList();
                List<MfCamTool> cam = revisionIds.Count == 0 ? new List<MfCamTool>() : await _uow.Set<MfCamTool>().AsNoTracking().Where(t => revisionIds.Contains(t.RevisionId)).ToListAsync(ct);
                cam = cam.GroupBy(t => t.ToolNumber).Select(g => g.First()).ToList();
                if (cam.Count == 0) o.Gate("tools", "skipped", programRuns.Count == 0 ? "برنامه‌ای نیست." : "برای نسخه فهرست ابزار CAM ثبت نشده است.");
                else if (session == null) o.Gate("tools", "skipped", linkOverridden ? "بدون ارتباط بررسی نشد (مجوز یک‌باره)." : "جدول ابزار از راه گیت‌وی خوانده نمی‌شود.", linkOverridden);
                else
                {
                    byte[] table = null;
                    string readError = null;
                    try { table = await session.ReadFileAsync(ToolTablePath, 4 * 1024 * 1024, ct); }
                    catch (Lsv2Exception ex) { readError = ex.Message; }
                    List<string> problems = table == null
                        ? new List<string> { "جدول ابزار ماشین (" + ToolTablePath + ") خوانده نشد" + (readError != null ? ": " + readError : ".") }
                        : MfGuardRules.CheckTools(cam, MfGuardRules.ParseToolTable(Encoding.Latin1.GetString(table)),
                            Setting("Dnc:Guard:RadiusTolerance", MfGuardRules.DefaultRadiusTolerance), Setting("Dnc:Guard:LengthTolerance", MfGuardRules.DefaultLengthTolerance));
                    if (problems.Count > 0)
                    {
                        string text = string.Join("؛ ", problems.Take(5)) + (problems.Count > 5 ? $"؛ و {problems.Count - 5} مورد دیگر" : string.Empty);
                        if (Use("tools") is MfGateOverride ov)
                        {
                            foreach (MfNcRun r in programRuns) r.ToolsVerified = false;
                            o.Gate("tools", "ok", text + " — با مجوز یک‌باره مدیر تولید: " + ov.Reason, true);
                            o.Activities.Add((MfActivityKind.ToolsMismatch, "override", text));
                            o.Activities.Add((MfActivityKind.GateOverride, "tools", "شروع با ابزار مغایر با مجوز یک‌باره مدیر تولید: " + ov.Reason));
                        }
                        else
                        {
                            o.Gate("tools", "blocked", text);
                            o.Activities.Add((MfActivityKind.ToolsMismatch, null, text));
                            o.Error = "ابزارهای ماشین با فهرست ابزار برنامه نمی‌خواند: " + text; o.ErrorGate = "tools";
                            return o;
                        }
                    }
                    else
                    {
                        foreach (MfNcRun r in programRuns) r.ToolsVerified = true;
                        o.Gate("tools", "ok", $"{cam.Count} ابزار برنامه در جدول ابزار ماشین هست و اندازه‌ها می‌خواند.");
                        o.Activities.Add((MfActivityKind.ToolsVerified, cam.Count.ToString(CultureInfo.InvariantCulture), $"{cam.Count} ابزار برنامه با جدول ابزار ماشین تطبیق داده شد."));
                    }
                }

                // 4. the first run of a revision on this machine has to be proven
                bool anyProve = false;
                foreach (MfNcRun run in programRuns)
                {
                    bool proven = await _uow.Set<MfNcRun>().AsNoTracking().AnyAsync(r => r.RevisionId == run.RevisionId && r.MachineId == run.MachineId && r.ProveOutObserved == true, ct);
                    run.ProveOut = !proven && kind == MfBookingKind.Run;
                    anyProve |= run.ProveOut;
                }
                o.Gate("prove-out", anyProve ? "pending" : "skipped",
                    anyProve ? $"اجرای اول این نسخه روی این ماشین است: برنامه را با تک‌بلوک یا پیشروی حداکثر {MfGuardRules.ProveOutFeedMax}٪ اجرا کنید؛ پایان عملیات بدون آن پذیرفته نمی‌شود."
                        : "این نسخه پیش‌تر روی این ماشین اثبات شده است.");
                return o;
            }
            finally
            {
                session?.Dispose();
            }
        }

        /// <summary>Reads the program from the control and compares its fingerprint with the approved revision; null when they are the same (the run gets the fingerprint).</summary>
        private async Task<string> VerifyProgramAsync(MfMachineMonitor m, MfNcRun run, IMfControlSession session, DateTime now, CancellationToken ct)
        {
            string code = await _uow.Set<MfNcProgram>().AsNoTracking().Where(p => p.Id == run.ProgramId).Select(p => p.Code).FirstOrDefaultAsync(ct);
            MfNcTransfer transfer = run.TransferId.HasValue ? await _uow.Set<MfNcTransfer>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == run.TransferId.Value, ct) : null;
            if (transfer == null || string.IsNullOrWhiteSpace(transfer.TargetName))
                return $"برنامه {code} از راه DNC به این ماشین ارسال و تأیید نشده است؛ ماشین تحت پایش فقط برنامه ارسال‌شده از سامانه را اجرا می‌کند.";
            byte[] approved = await _uow.Set<MfNcContent>().AsNoTracking().Where(c => c.RevisionId == run.RevisionId).Select(c => c.Bytes).FirstOrDefaultAsync(ct);
            string expected = MfGuardRules.CanonicalSha(approved);
            if (session != null)
            {
                string path = MfGuardRules.ProgramPath(m.ProgramFolder, transfer.TargetName);
                byte[] onControl;
                try { onControl = await session.ReadFileAsync(path, Math.Max(256 * 1024, (approved?.Length ?? 0) * 4L), ct); }
                catch (Lsv2Exception ex) { return $"برنامه {code} از کنترلر خوانده نشد ({path}): {ex.Message}"; }
                if (onControl == null) return $"برنامه {code} با نام {transfer.TargetName} روی کنترلر نیست ({path}).";
                string actual = MfGuardRules.CanonicalSha(onControl);
                if (actual != expected)
                {
                    int line = MfGuardRules.FirstDifference(approved, onControl);
                    return $"برنامه {code} روی کنترلر با نسخه تأییدشده یکی نیست (اولین تفاوت در سطر {line})؛ روی کنترلر تغییر کرده است. نسخه را دوباره ارسال کنید یا تغییر را از راه DNC برای تأیید بفرستید.";
                }
                run.VerifiedSha256 = actual; run.VerifiedAt = now;
                return null;
            }
            // edge box: the fingerprint it pushed for the selected program, fresh
            if (m.LastProgramSha256 == null || !m.LastProgramShaAt.HasValue || (now - m.LastProgramShaAt.Value.LocalDateTime).TotalMinutes > 15)
                return $"گیت‌وی ماشین اثر انگشت تازه‌ای از برنامه انتخاب‌شده نفرستاده است؛ برنامه {code} را روی کنترلر انتخاب کنید و چند ثانیه بعد دوباره شروع کنید.";
            if (m.LastProgramName == null || !SameProgram(m.LastProgramName, transfer.TargetName))
                return $"برنامه انتخاب‌شده روی کنترلر ({m.LastProgramName ?? "نامعلوم"}) برنامه {code} ({transfer.TargetName}) نیست.";
            if (!string.Equals(m.LastProgramSha256, expected, StringComparison.OrdinalIgnoreCase))
                return $"برنامه {code} روی کنترلر با نسخه تأییدشده یکی نیست؛ روی کنترلر تغییر کرده است.";
            run.VerifiedSha256 = expected; run.VerifiedAt = now;
            return null;
        }

        public async Task<string> CheckStartAsync(MfOperation op, int? machineId, MfBookingKind kind, int employeeId, List<MfNcRun> runs, CancellationToken ct)
        {
            if (op == null || !machineId.HasValue || !MfNcRules.Machining(kind)) return null;
            MfMachineMonitor m = await _uow.Set<MfMachineMonitor>().FirstOrDefaultAsync(x => x.MachineId == machineId.Value && !x.Deleted, ct);
            if (!Guarded(m)) return null;
            if (IsAi) return MfText.NotAllowed;
            DateTime now = Clock();
            GateOutcome o = await EvaluateAsync(m, op, kind, runs ?? new List<MfNcRun>(), now, ct);
            if (o.Error != null)
            {
                foreach (var a in o.Activities) Add(m.MachineId, op.Id, employeeId, now, a.Kind, a.Value, a.Text);
                Add(m.MachineId, op.Id, employeeId, now, MfActivityKind.GateRefused, o.ErrorGate, "شروع رد شد: " + o.Error);
                // the refusal stays on the time line even though the start is not saved
                await SaveRefusalAsync(ct);
                return o.Error;
            }
            foreach (var a in o.Activities) Add(m.MachineId, op.Id, employeeId, now, a.Kind, a.Value, a.Text);
            foreach (MfGateOverride used in o.Used)
            {
                used.ConsumedAt = new DateTimeOffset(now);
                foreach (MfNcRun r in runs ?? new List<MfNcRun>()) r.OverrideId ??= used.Id;
            }
            return null;
        }

        /// <summary>
        /// Saves the refusal on the time line. The start saves nothing else, but the unit of work may hold the status the shop floor recomputed before the gates (a kit issued since the last look):
        /// that is the truth of the order and is saved with it. A failed save drops only the time line rows.
        /// </summary>
        private async Task SaveRefusalAsync(CancellationToken ct)
        {
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                if (_uow is DbContext db) foreach (var e in db.ChangeTracker.Entries<MfMachineActivity>().Where(e => e.State == EntityState.Added).ToList()) e.State = EntityState.Detached;
            }
        }

        private async Task<List<MfGuardRules.Sample>> SamplesAsync(int machineId, DateTime from, DateTime to, CancellationToken ct)
        {
            var kinds = new[] { MfActivityKind.ExecMode, MfActivityKind.ProgramState, MfActivityKind.Override };
            var rows = await _uow.Set<MfMachineActivity>().AsNoTracking().Where(a => a.MachineId == machineId && kinds.Contains(a.Kind) && a.At >= from && a.At <= to).OrderBy(a => a.At).ThenBy(a => a.Id).ToListAsync(ct);
            var before = new List<MfMachineActivity>();
            foreach (MfActivityKind k in kinds)
            {
                MfMachineActivity b = await _uow.Set<MfMachineActivity>().AsNoTracking().Where(a => a.MachineId == machineId && a.Kind == k && a.At < from).OrderByDescending(a => a.At).ThenByDescending(a => a.Id).FirstOrDefaultAsync(ct);
                if (b != null) before.Add(b);
            }
            return before.Select(b => { var s = Sample(b); return new MfGuardRules.Sample { At = from, Exec = s.Exec, State = s.State, Feed = s.Feed }; })
                .Concat(rows.Select(Sample)).ToList();
        }

        private static MfGuardRules.Sample Sample(MfMachineActivity a) => new MfGuardRules.Sample
        {
            At = a.At,
            Exec = a.Kind == MfActivityKind.ExecMode && Enum.TryParse(a.Value, out MfExecMode e) ? e : null,
            State = a.Kind == MfActivityKind.ProgramState && Enum.TryParse(a.Value, out MfProgramState p) ? p : null,
            Feed = a.Kind == MfActivityKind.Override && int.TryParse((a.Value ?? string.Empty).Split('/')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int f) ? f : null,
        };

        private async Task<bool> ProveOutSeenAsync(int machineId, DateTime from, DateTime to, CancellationToken ct) => MfGuardRules.ProveOutObserved(await SamplesAsync(machineId, from, to, ct));

        public async Task<string> CheckFinishAsync(MfOperation op, CancellationToken ct)
        {
            if (op == null) return null;
            List<MfNcRun> runs = await _uow.Set<MfNcRun>().Where(r => r.OperationId == op.Id && r.ProveOut && r.ProveOutObserved == null).ToListAsync(ct);
            if (runs.Count == 0) return null;
            DateTime now = Clock();
            foreach (MfNcRun run in runs)
            {
                MfMachineMonitor m = await _uow.Set<MfMachineMonitor>().AsNoTracking().FirstOrDefaultAsync(x => x.MachineId == run.MachineId && !x.Deleted, ct);
                if (!Guarded(m)) { run.ProveOutObserved = false; continue; }
                if (await ProveOutSeenAsync(run.MachineId, run.StartedAt, now, ct))
                {
                    run.ProveOutObserved = true;
                    Add(run.MachineId, op.Id, run.EmployeeId, now, MfActivityKind.ProveOut, "observed", "اجرای اول نسخه با تک‌بلوک یا پیشروی کم دیده شد (اثبات‌شده).");
                    continue;
                }
                MfGateOverride ov = (await ActiveOverridesAsync(run.MachineId, op.Id, now, ct)).FirstOrDefault(x => x.Gate == "prove-out");
                if (ov == null)
                    return $"اجرای اول این نسخه برنامه روی ماشین باید با تک‌بلوک یا پیشروی حداکثر {MfGuardRules.ProveOutFeedMax}٪ انجام می‌شد و داده ماشین آن را نشان نمی‌دهد؛ پایان عملیات فقط با مجوز یک‌باره مدیر تولید ثبت می‌شود.";
                ov.ConsumedAt = new DateTimeOffset(now);
                run.ProveOutObserved = false; run.OverrideId ??= ov.Id;
                Add(run.MachineId, op.Id, run.EmployeeId, now, MfActivityKind.GateOverride, "prove-out", "پایان بدون اثبات اجرای اول با مجوز یک‌باره مدیر تولید: " + ov.Reason);
            }
            return null;
        }

        // ================================================================================== terminal panel, time line, overrides

        public async Task<ResponseDto<TerminalGuardDto>> TerminalAsync(int operationId, int machineId, CancellationToken ct)
        {
            if (!_user.IsInRole(GuardRoles.Readers)) return Bad<TerminalGuardDto>(MfText.NotAllowed);
            MfOperation op = await _uow.Set<MfOperation>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == operationId && !o.Deleted, ct);
            if (op == null) return MfText.NotFound<TerminalGuardDto>("عملیات پیدا نشد.");
            if (!await _uow.Set<Machine>().AnyAsync(x => x.Id == machineId, ct)) return MfText.NotFound<TerminalGuardDto>("ماشین پیدا نشد.");
            MfMachineMonitor m = await _uow.Set<MfMachineMonitor>().AsNoTracking().FirstOrDefaultAsync(x => x.MachineId == machineId && !x.Deleted, ct);
            var dto = new TerminalGuardDto { OperationId = operationId, MachineId = machineId, Guarded = Guarded(m), Machine = await OneAsync(machineId, ct) };
            DateTime now = Clock();
            dto.Overrides = (await OverrideDtosAsync(await ActiveOverridesAsync(machineId, operationId, now, ct), ct));
            if (!dto.Guarded) { dto.CanStart = true; return ResponseDtoHelper.Ok(dto); }
            var dnc = await MfDncGate.CheckStartAsync(_uow, op, machineId, MfBookingKind.Run, ct);
            if (dnc.Error != null)
            {
                dto.Gates.Add(new GuardGateDto { Code = "dnc", Title = "ارسال و تأیید برنامه (DNC)", State = "blocked", Text = dnc.Error });
                return ResponseDtoHelper.Ok(dto);
            }
            dto.Gates.Add(new GuardGateDto { Code = "dnc", Title = "ارسال و تأیید برنامه (DNC)", State = dnc.Runs.Count == 0 ? "skipped" : "ok", Text = dnc.Runs.Count == 0 ? "این مرحله برنامه CNC ندارد." : "نسخه جاری ارسال و روی کنترلر تأیید شده است." });
            // a look only: nothing is consumed or saved
            MfMachineMonitor copy = await _uow.Set<MfMachineMonitor>().AsNoTracking().FirstAsync(x => x.Id == m.Id, ct);
            GateOutcome o = await EvaluateAsync(copy, op, MfBookingKind.Run, dnc.Runs, now, ct);
            dto.Gates.AddRange(o.Gates);
            dto.CanStart = o.Error == null;
            if (_uow is DbContext db) foreach (var e in db.ChangeTracker.Entries<MfGateOverride>().ToList()) e.State = EntityState.Detached;
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<PagedList<MachineActivityDto>>> GetActivityAsync(MachineActivityQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(GuardRoles.Readers)) return Bad<PagedList<MachineActivityDto>>(MfText.NotAllowed);
            query ??= new MachineActivityQuery();
            if (!query.MachineId.HasValue && !query.OperationId.HasValue) return Bad<PagedList<MachineActivityDto>>("ماشین یا عملیات را انتخاب کنید.");
            IQueryable<MfMachineActivity> q = _uow.Set<MfMachineActivity>().AsNoTracking();
            if (query.MachineId.HasValue) q = q.Where(a => a.MachineId == query.MachineId.Value);
            if (query.OperationId.HasValue) q = q.Where(a => a.OperationId == query.OperationId.Value);
            if (query.From.HasValue) q = q.Where(a => a.At >= query.From.Value);
            if (query.To.HasValue) q = q.Where(a => a.At <= query.To.Value);
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize), number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MfMachineActivity> rows = await q.OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            List<int> machineIds = rows.Select(r => r.MachineId).Distinct().ToList();
            Dictionary<int, string> titles = await _uow.Set<Machine>().AsNoTracking().Where(x => machineIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Title, ct);
            var list = rows.Select(a => new MachineActivityDto
            {
                Id = a.Id, MachineId = a.MachineId, MachineTitle = titles.GetValueOrDefault(a.MachineId), OperationId = a.OperationId, At = a.At, AtText = MfText.Stamp(a.At), Kind = a.Kind, KindText = KindText(a.Kind),
                Value = a.Value, Text = a.Text, Tone = Tone(a.Kind, a.Value),
            }).ToList();
            return ResponseDtoHelper.Ok(ExtrasShared.Page(list, total, number, size));
        }

        private async Task<List<GateOverrideDto>> OverrideDtosAsync(List<MfGateOverride> rows, CancellationToken ct)
        {
            var names = await ExtrasShared.UserNamesAsync(_uow, rows.Select(r => (int?)r.ByUserId), ct);
            List<int> machineIds = rows.Select(r => r.MachineId).Distinct().ToList();
            Dictionary<int, string> titles = await _uow.Set<Machine>().AsNoTracking().Where(x => machineIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Title, ct);
            DateTimeOffset now = new DateTimeOffset(Clock());
            return rows.Select(r => new GateOverrideDto
            {
                Id = r.Id, MachineId = r.MachineId, MachineTitle = titles.GetValueOrDefault(r.MachineId), OperationId = r.OperationId, Gate = r.Gate, GateText = GateText(r.Gate), Reason = r.Reason,
                ByName = ExtrasShared.NameOf(names, r.ByUserId), At = r.At, AtText = MfText.Stamp(r.At), ExpiresAt = r.ExpiresAt, ExpiresAtText = MfText.Stamp(r.ExpiresAt),
                ConsumedAtText = MfText.Stamp(r.ConsumedAt), Active = r.ConsumedAt == null && r.ExpiresAt > now,
            }).ToList();
        }

        public async Task<ResponseDto<GateOverrideDto>> CreateOverrideAsync(CreateGateOverrideDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(GuardRoles.Overriders) || IsAi) return Bad<GateOverrideDto>(MfText.NotAllowed);
            if (dto == null || !MfGuardRules.Gates.Contains(dto.Gate) && dto.Gate != "measurement") return Bad<GateOverrideDto>("گیت انتخاب‌شده نامعتبر است.");
            string reason = Clean(dto.Reason);
            if (reason == null || reason.Length < MinReasonLength) return Bad<GateOverrideDto>($"دلیل مجوز را کامل بنویسید (دست‌کم {MinReasonLength} نویسه)؛ این دلیل برای مدیرعامل و حسابرس فرستاده می‌شود.");
            if (reason.Length > 1000) return Bad<GateOverrideDto>("دلیل حداکثر ۱۰۰۰ نویسه است.");
            MfOperation op = await _uow.Set<MfOperation>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == dto.OperationId && !o.Deleted, ct);
            if (op == null) return Bad<GateOverrideDto>("عملیات پیدا نشد.");
            if (op.Status == MfOperationStatus.Done || op.Status == MfOperationStatus.Skipped) return Bad<GateOverrideDto>("این عملیات تمام شده است.");
            string title = await _uow.Set<Machine>().AsNoTracking().Where(m => m.Id == dto.MachineId).Select(m => m.Title).FirstOrDefaultAsync(ct);
            if (title == null) return Bad<GateOverrideDto>("ماشین پیدا نشد.");
            DateTime now = Clock();
            if ((await ActiveOverridesAsync(dto.MachineId, dto.OperationId, now, ct)).Any(o => o.Gate == dto.Gate)) return Bad<GateOverrideDto>("برای این گیت مجوز فعال وجود دارد.");
            var row = new MfGateOverride
            {
                MachineId = dto.MachineId, OperationId = dto.OperationId, Gate = dto.Gate, Reason = reason, ByUserId = Me, At = new DateTimeOffset(now), ExpiresAt = new DateTimeOffset(now.AddMinutes(OverrideMinutes)),
                CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now, CreatorUserId = Me, ModifierUserId = Me,
            };
            _uow.Set<MfGateOverride>().Add(row);
            Add(dto.MachineId, dto.OperationId, null, now, MfActivityKind.GateOverride, dto.Gate, $"مجوز یک‌باره «{GateText(dto.Gate)}» صادر شد (تا {OverrideMinutes} دقیقه): {reason}");
            await _uow.SaveChangesAsync(ct);
            if (_notify != null)
                await _notify.NotifyAsync(new NotifyRequest
                {
                    SourceKey = "mf:guard-override:" + row.Id, Module = "production", SourceType = "guard-override", SourceId = row.Id, Kind = OfInboxKind.Alert, Priority = OfUrgency.Urgent,
                    Title = $"مجوز یک‌باره «{GateText(dto.Gate)}» روی ماشین {title}: {Cut(reason, 150)}", Link = "/production/guard?machineId=" + dto.MachineId, Roles = GuardRoles.OverrideReaders,
                }, ct);
            return ResponseDtoHelper.Ok((await OverrideDtosAsync(new List<MfGateOverride> { row }, ct))[0]);
        }

        public async Task<ResponseDto<PagedList<GateOverrideDto>>> GetOverridesAsync(GateOverrideQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(GuardRoles.Readers)) return Bad<PagedList<GateOverrideDto>>(MfText.NotAllowed);
            query ??= new GateOverrideQuery();
            IQueryable<MfGateOverride> q = _uow.Set<MfGateOverride>().AsNoTracking().Where(o => !o.Deleted);
            if (query.MachineId.HasValue) q = q.Where(o => o.MachineId == query.MachineId.Value);
            if (query.ActiveOnly) { DateTimeOffset now = new DateTimeOffset(Clock()); q = q.Where(o => o.ConsumedAt == null && o.ExpiresAt > now); }
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize), number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MfGateOverride> rows = await q.OrderByDescending(o => o.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(ExtrasShared.Page(await OverrideDtosAsync(rows, ct), total, number, size));
        }
    }
}
