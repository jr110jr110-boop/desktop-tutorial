using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Production.Dnc
{
    public interface IMfDncService
    {
        Task<ResponseDto<PagedList<MachineConnectionDto>>> GetConnectionsAsync(MachineConnectionQuery query, CancellationToken ct);
        Task<ResponseDto<MachineConnectionDto>> GetConnectionAsync(int machineId, CancellationToken ct);
        Task<ResponseDto<MachineConnectionDto>> SaveConnectionAsync(SaveMachineConnectionDto dto, CancellationToken ct);
        Task<ResponseDto<GatewayKeyDto>> NewGatewayKeyAsync(MachineIdDto dto, CancellationToken ct);
        Task<ResponseDto<LinkTestResultDto>> TestConnectionAsync(MachineIdDto dto, CancellationToken ct);

        Task<ResponseDto<PagedList<NcProgramDto>>> GetProgramsAsync(NcProgramQuery query, CancellationToken ct);
        Task<ResponseDto<NcProgramDto>> GetProgramAsync(int id, CancellationToken ct);
        Task<ResponseDto<NcProgramDto>> SaveProgramAsync(SaveNcProgramDto dto, CancellationToken ct);

        Task<ResponseDto<NcRevisionDto>> UploadRevisionAsync(UploadNcRevisionDto dto, CancellationToken ct);
        Task<ResponseDto<NcRevisionDto>> RevisionActionAsync(NcRevisionActionDto dto, CancellationToken ct);
        Task<ResponseDto<NcFileDto>> GetRevisionContentAsync(int revisionId, CancellationToken ct);
        Task<ResponseDto<NcDiffDto>> GetRevisionDiffAsync(int revisionId, CancellationToken ct);
        Task<ResponseDto<List<NcValidationDto>>> ValidateAsync(ValidateNcDto dto, CancellationToken ct);

        Task<ResponseDto<PagedList<NcTransferDto>>> GetTransfersAsync(NcTransferQuery query, CancellationToken ct);
        Task<ResponseDto<NcTransferDto>> SendAsync(SendNcDto dto, CancellationToken ct);
        Task<ResponseDto<NcTransferDto>> ReceiveAsync(ReceiveNcDto dto, CancellationToken ct);
        Task<ResponseDto<NcTransferDto>> TransferActionAsync(NcTransferActionDto dto, CancellationToken ct);
        Task<ResponseDto<NcFileDto>> GetTransferFileAsync(int transferId, CancellationToken ct);
        Task<ResponseDto<NcRunPendingResultDto>> RunPendingAsync(CancellationToken ct);

        /// <summary>The background job (no user): the pending transfers of the server-driven links.</summary>
        Task<NcRunPendingResultDto> ProcessPendingAsync(CancellationToken ct);
        Task<NcRunPendingResultDto> ProcessPendingAsync(int? machineId, CancellationToken ct);
        Task<List<int>> PendingMachinesAsync(CancellationToken ct);

        Task<ResponseDto<PagedList<NcRunDto>>> GetRunsAsync(NcRunQuery query, CancellationToken ct);
        Task<ResponseDto<TerminalNcDto>> GetTerminalAsync(TerminalNcQuery query, CancellationToken ct);

        Task<ResponseDto<List<GatewayJobDto>>> GatewayJobsAsync(string machine, string key, string remote, CancellationToken ct);
        Task<ResponseDto<NcFileDto>> GatewayContentAsync(string machine, string key, string remote, int transferId, CancellationToken ct);
        Task<ResponseDto<NcTransferDto>> GatewayResultAsync(string machine, string key, string remote, GatewayResultDto dto, CancellationToken ct);
        Task<ResponseDto<NcTransferDto>> GatewayUploadAsync(string machine, string key, string remote, GatewayUploadDto dto, CancellationToken ct);
    }

    /// <summary>
    /// The DNC of the production module: the program library with revisions (two people: the author never approves), the machine links, the controlled transfer to a machine with the operator's confirmation on the control,
    /// the program fetched back from a machine as a new revision (difference shown, same approval) and the record of the revision every operation ran by. Offline: the server talks only to the machines on the LAN.
    /// </summary>
    public class MfDncService : IMfDncService
    {
        /// <summary>Attempts of a server-driven transfer before it waits for a person (retry).</summary>
        public const int MaxAttempts = 3;

        /// <summary>A transfer left "sending" this long (the server stopped in the middle) counts as failed.</summary>
        private static readonly TimeSpan StaleSending = TimeSpan.FromMinutes(10);

        private static readonly Regex CredentialName = new Regex(@"^[A-Za-z0-9_\-]{1,50}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex Extension = new Regex(@"^\.?[A-Za-z0-9]{1,8}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex ControlName = new Regex(@"^[A-Za-z0-9._\-]{1,32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly int[] BaudRates = { 300, 600, 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IMfNcLinkFactory _links;
        private readonly IConfiguration _configuration;
        private readonly INotificationService _notify;

        public MfDncService(IUnitOfWork uow, IInventoryUserContext user, IMfNcLinkFactory links = null, IConfiguration configuration = null, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _links = links ?? new MfNcLinkFactory();
            _configuration = configuration;
            _notify = notify;
        }

        private int Me => _user.UserId;

        /// <summary>The folders the administrator allowed for network-folder links (Dnc:FolderRoots, a list or one text with ';'): a machine folder must be one of them or inside one.</summary>
        private IReadOnlyList<string> FolderRoots
        {
            get
            {
                var roots = new List<string>();
                IConfigurationSection section = _configuration?.GetSection("Dnc:FolderRoots");
                if (section == null) return roots;
                if (!string.IsNullOrWhiteSpace(section.Value)) roots.AddRange(section.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                roots.AddRange(section.GetChildren().Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()));
                return roots;
            }
        }

        /// <summary>Only the tests reach a converter or FTP server on the server itself (Dnc:AllowLoopback=true); the machines are always on the LAN.</summary>
        private bool AllowLoopback => string.Equals(_configuration?["Dnc:AllowLoopback"], "true", StringComparison.OrdinalIgnoreCase);
        private static string Clean(string s) => MfText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => MfText.Bad<T>(m);
        private static bool IsAi => global::Services.NasbNiro.Ai.AiActing.IsActing;

        // ================================================================================== shared lookups

        private async Task<Dictionary<int, string>> UserNamesAsync(IEnumerable<int?> ids, CancellationToken ct)
        {
            List<int> list = ids.Where(i => i.HasValue && i.Value > 0).Select(i => i.Value).Distinct().ToList();
            if (list.Count == 0) return new Dictionary<int, string>();
            var rows = await _uow.Set<User>().AsNoTracking().Where(u => list.Contains(u.Id)).Select(u => new { u.Id, u.Name, u.LastName, u.UserName }).ToListAsync(ct);
            return rows.ToDictionary(u => u.Id, u => { string n = ((u.Name ?? "") + " " + (u.LastName ?? "")).Trim(); return n.Length > 0 ? n : u.UserName; });
        }

        private static string NameOf(Dictionary<int, string> names, int? id) => id.HasValue && names.TryGetValue(id.Value, out string n) ? n : null;

        private Task<Dictionary<int, string>> MachineTitlesAsync(IEnumerable<int> ids, CancellationToken ct)
        {
            List<int> list = ids.Distinct().ToList();
            return _uow.Set<Machine>().AsNoTracking().Where(m => list.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Title, ct);
        }

        private string CredentialUser(string key) => string.IsNullOrWhiteSpace(key) ? null : _configuration?["Dnc:Credentials:" + key + ":User"];

        private NcLinkTarget TargetOf(MfMachineConnection c) => new NcLinkTarget
        {
            Host = c.Host, Port = c.Port, RemotePath = c.RemotePath, BaudRate = c.BaudRate, Family = c.ControllerFamily, FolderRoots = FolderRoots,
            MaxBytes = c.MaxProgramBytes ?? MfNcRules.GlobalMaxBytes,
            User = CredentialUser(c.CredentialKey), Password = string.IsNullOrWhiteSpace(c.CredentialKey) ? null : _configuration?["Dnc:Credentials:" + c.CredentialKey + ":Password"],
        };

        private static List<NcFindingDto> FindingsOf(string stored)
        {
            var list = new List<NcFindingDto>();
            if (string.IsNullOrEmpty(stored)) return list;
            foreach (string line in stored.Split('\n'))
            {
                string[] parts = line.Split('|', 4);
                if (parts.Length < 4) continue;
                list.Add(new NcFindingDto
                {
                    Severity = parts[0] == "E" ? "error" : "warning",
                    Line = int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int l) ? l : 0,
                    Code = parts[2], Text = parts[3],
                });
            }
            return list;
        }

        private static List<NcFindingDto> FindingsOf(NcAnalysis a) =>
            a.Findings.Select(f => new NcFindingDto { Severity = f.Severity == NcSeverity.Error ? "error" : "warning", Line = f.Line, Code = f.Code, Text = f.Text }).ToList();

        // ================================================================================== machine connections

        private async Task<List<MachineConnectionDto>> ConnectionDtosAsync(List<Machine> machines, CancellationToken ct)
        {
            List<int> ids = machines.Select(m => m.Id).ToList();
            Dictionary<int, MfMachineConnection> connections = (await _uow.Set<MfMachineConnection>().AsNoTracking().Where(c => !c.Deleted && ids.Contains(c.MachineId)).ToListAsync(ct))
                .ToDictionary(c => c.MachineId);
            Dictionary<int, MfMachineInfo> infos = (await _uow.Set<MfMachineInfo>().AsNoTracking().Where(i => !i.Deleted && ids.Contains(i.MachineId)).ToListAsync(ct)).ToDictionary(i => i.MachineId);
            List<int> centreIds = infos.Values.Where(i => i.WorkCenterId.HasValue).Select(i => i.WorkCenterId.Value).Distinct().ToList();
            Dictionary<int, string> centres = centreIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<MfWorkCenter>().AsNoTracking().Where(w => centreIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Code, ct);
            var result = new List<MachineConnectionDto>();
            foreach (Machine m in machines)
            {
                connections.TryGetValue(m.Id, out MfMachineConnection c);
                infos.TryGetValue(m.Id, out MfMachineInfo info);
                var dto = new MachineConnectionDto
                {
                    Id = c?.Id, MachineId = m.Id, MachineTitle = m.Title, MachineIsActive = m.IsActive, WorkCenterId = info?.WorkCenterId, MachineStatus = info?.Status,
                    WorkCenterCode = info?.WorkCenterId != null && centres.TryGetValue(info.WorkCenterId.Value, out string wc) ? wc : null,
                    LinkKind = c?.LinkKind ?? MfNcLinkKind.Manual, LineEnding = c?.LineEnding ?? MfNcLineEnding.CrLf, RequireConfirmedProgram = c?.RequireConfirmedProgram ?? true, IsActive = c?.IsActive ?? true,
                };
                if (c != null)
                {
                    dto.ControllerFamily = c.ControllerFamily; dto.ControllerModel = c.ControllerModel; dto.Host = c.Host; dto.Port = c.Port; dto.RemotePath = c.RemotePath;
                    dto.CredentialKey = c.CredentialKey; dto.CredentialConfigured = !string.IsNullOrEmpty(CredentialUser(c.CredentialKey)); dto.ProgramNameFormat = c.ProgramNameFormat;
                    dto.FileExtension = c.FileExtension; dto.RequirePercent = c.RequirePercent; dto.MaxProgramBytes = c.MaxProgramBytes; dto.Axes = c.Axes; dto.MaxSpindleRpm = c.MaxSpindleRpm;
                    dto.MaxFeedMmPerMin = c.MaxFeedMmPerMin; dto.ToolCapacity = c.ToolCapacity; dto.IsLathe = c.IsLathe; dto.BaudRate = c.BaudRate; dto.HasGatewayKey = !string.IsNullOrEmpty(c.GatewayKeyHash);
                    dto.GatewayKeyRotatedAtText = MfText.Stamp(c.GatewayKeyRotatedAt); dto.AllowedAddresses = c.AllowedAddresses; dto.LastSeenAtText = MfText.Stamp(c.LastSeenAt); dto.Note = c.Note;
                    dto.RowVersion = MfConcurrency.Text(c.RowVersion);
                }
                result.Add(dto);
            }
            return result;
        }

        public async Task<ResponseDto<PagedList<MachineConnectionDto>>> GetConnectionsAsync(MachineConnectionQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<PagedList<MachineConnectionDto>>(MfText.NotAllowed);
            query ??= new MachineConnectionQuery();
            IQueryable<Machine> q = _uow.Set<Machine>().AsNoTracking();
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(m => m.Title.Contains(text));
            if (query.WorkCenterId.HasValue)
            {
                int wc = query.WorkCenterId.Value;
                q = q.Where(m => _uow.Set<MfMachineInfo>().Any(i => !i.Deleted && i.MachineId == m.Id && i.WorkCenterId == wc));
            }
            if (query.LinkKind.HasValue)
            {
                MfNcLinkKind kind = query.LinkKind.Value;
                q = kind == MfNcLinkKind.Manual
                    ? q.Where(m => !_uow.Set<MfMachineConnection>().Any(c => !c.Deleted && c.MachineId == m.Id && c.LinkKind != MfNcLinkKind.Manual))
                    : q.Where(m => _uow.Set<MfMachineConnection>().Any(c => !c.Deleted && c.MachineId == m.Id && c.LinkKind == kind));
            }
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize), number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<Machine> rows = await q.OrderBy(m => m.Title).ThenBy(m => m.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(Page(await ConnectionDtosAsync(rows, ct), total, number, size));
        }

        /// <summary>A page that was cut in the database: the rows of the page plus the total (as MfMasterService does).</summary>
        private static PagedList<T> Page<T>(List<T> rows, int total, int number, int size)
        {
            PagedList<T> page = PagedList<T>.CreateAsync(rows, 1, int.MaxValue, CancellationToken.None).GetAwaiter().GetResult();
            page.TotalCount = total;
            page.CurrentPage = number;
            page.PageSize = size;
            page.TotalPages = (int)Math.Ceiling(total / (double)Math.Max(size, 1));
            return page;
        }

        public async Task<ResponseDto<MachineConnectionDto>> GetConnectionAsync(int machineId, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<MachineConnectionDto>(MfText.NotAllowed);
            Machine m = await _uow.Set<Machine>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == machineId, ct);
            if (m == null) return MfText.NotFound<MachineConnectionDto>("ماشین پیدا نشد.");
            return ResponseDtoHelper.Ok((await ConnectionDtosAsync(new List<Machine> { m }, ct))[0]);
        }

        public async Task<ResponseDto<MachineConnectionDto>> SaveConnectionAsync(SaveMachineConnectionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.LinkEditors) || IsAi) return Bad<MachineConnectionDto>(MfText.NotAllowed);
            if (dto == null || !Enum.IsDefined(typeof(MfNcControllerFamily), dto.ControllerFamily) || !Enum.IsDefined(typeof(MfNcLinkKind), dto.LinkKind)
                || !Enum.IsDefined(typeof(MfNcLineEnding), dto.LineEnding)) return Bad<MachineConnectionDto>(MfText.InvalidData);
            Machine machine = await _uow.Set<Machine>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == dto.MachineId, ct);
            if (machine == null) return Bad<MachineConnectionDto>("ماشین پیدا نشد.");
            string host = Clean(dto.Host), path = Clean(dto.RemotePath), credential = Clean(dto.CredentialKey), format = Clean(dto.ProgramNameFormat), ext = Clean(dto.FileExtension);
            string axes = Clean(dto.Axes)?.ToUpperInvariant();
            if (host != null && (host.Length > 200 || host.Any(char.IsWhiteSpace) || NcLan.HasControl(host))) return Bad<MachineConnectionDto>("نشانی دستگاه نامعتبر است.");
            if (NcLan.HasControl(dto.RemotePath)) return Bad<MachineConnectionDto>("مسیر پوشه نویسه کنترلی دارد و پذیرفته نمی‌شود.");
            // review finding 9: the server talks only to the machines of the LAN, by address (no outside host gets the FTP password, no inner scan through the test button)
            if (host != null && (dto.LinkKind == MfNcLinkKind.Ftp || dto.LinkKind == MfNcLinkKind.SerialGateway) && !NcLan.IsLanHost(host, AllowLoopback))
                return Bad<MachineConnectionDto>("نشانی دستگاه باید نشانی IP شبکه داخلی کارگاه باشد (مثلاً 192.168.10.21).");
            // review finding 1: a network folder only under the folders allowed in the server settings, never the application folder
            if (dto.LinkKind == MfNcLinkKind.NetworkFolder && path != null)
            {
                if (NcLan.FullFolder(path) == null) return Bad<MachineConnectionDto>("مسیر پوشه ماشین باید مسیر کامل باشد (مثلاً \\\\cnc-pc\\nc یا D:\\DNC\\Mikron).");
                if (NcLan.TouchesApplication(path) || !NcLan.UnderRoot(path, FolderRoots))
                    return Bad<MachineConnectionDto>("این پوشه در فهرست پوشه‌های مجاز ماشین‌ها در تنظیمات سرور (Dnc:FolderRoots) نیست؛ مدیر سیستم باید آن را اضافه کند.");
            }
            if (dto.Port.HasValue && (dto.Port.Value < 1 || dto.Port.Value > 65535)) return Bad<MachineConnectionDto>("درگاه باید بین ۱ و ۶۵۵۳۵ باشد.");
            if (credential != null && !CredentialName.IsMatch(credential)) return Bad<MachineConnectionDto>("نام کلید اعتبارنامه فقط حروف لاتین، رقم، خط تیره و زیرخط است (رمز در تنظیمات سرور ثبت می‌شود، نه این‌جا).");
            if (ext != null && (!Extension.IsMatch(ext) || !MfNcRules.IsNcExtension(ext))) return Bad<MachineConnectionDto>("پسوند فایل باید پسوند برنامه CNC باشد (مثلاً .H، .NC یا .MPF).");
            if (axes != null && (axes.Length > 9 || axes.Any(ch => "XYZABCUVW".IndexOf(ch) < 0) || axes.Distinct().Count() != axes.Length))
                return Bad<MachineConnectionDto>("محورها را با حروف X Y Z A B C U V W و بدون تکرار وارد کنید (مثلاً XYZ).");
            if (dto.MaxProgramBytes.HasValue && (dto.MaxProgramBytes.Value < 1024 || dto.MaxProgramBytes.Value > MfNcRules.GlobalMaxBytes))
                return Bad<MachineConnectionDto>("ظرفیت حافظه کنترلر باید بین ۱ کیلوبایت و ۴ مگابایت باشد.");
            if (dto.MaxSpindleRpm is <= 0 or > 200000 || dto.MaxFeedMmPerMin is <= 0 or > 200000 || dto.ToolCapacity is <= 0 or > 1000) return Bad<MachineConnectionDto>("حداکثر دور، پیشروی یا ظرفیت ابزار نامعتبر است.");
            if (dto.BaudRate.HasValue && !BaudRates.Contains(dto.BaudRate.Value)) return Bad<MachineConnectionDto>("نرخ باود نامعتبر است (مثلاً ۹۶۰۰).");
            if ((dto.LinkKind == MfNcLinkKind.Ftp || dto.LinkKind == MfNcLinkKind.SerialGateway) && host == null) return Bad<MachineConnectionDto>("برای این نوع اتصال نشانی دستگاه لازم است.");
            if (dto.LinkKind == MfNcLinkKind.NetworkFolder && path == null) return Bad<MachineConnectionDto>("برای اتصال پوشه شبکه، مسیر پوشه لازم است.");
            if (format != null)
            {
                if (!format.Contains("{number}") && !format.Contains("{O}") && !format.Contains("{code}"))
                    return Bad<MachineConnectionDto>("قالب نام برنامه باید یکی از {number}، {O} یا {code} را داشته باشد (نام ثابت پذیرفته نمی‌شود).");
                var sample = MfNcRules.TargetName(format, ext, "O1234", "P-1", "sample.nc");
                if (sample.Error != null) return Bad<MachineConnectionDto>("قالب نام برنامه روی کنترلر نامعتبر است: " + sample.Error);
            }
            string addresses = null;
            if (Clean(dto.AllowedAddresses) != null && !DeviceAddress.TryNormalize(dto.AllowedAddresses, out addresses, out string addressError)) return Bad<MachineConnectionDto>(addressError);

            MfMachineConnection row = await _uow.Set<MfMachineConnection>().FirstOrDefaultAsync(c => c.MachineId == dto.MachineId && !c.Deleted, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            if (row == null)
            {
                row = new MfMachineConnection { MachineId = dto.MachineId, CreateDate = now, CreatorUserId = Me };
                _uow.Set<MfMachineConnection>().Add(row);
            }
            else if (!MfConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<MachineConnectionDto>(MfText.Concurrency);
            row.ControllerFamily = dto.ControllerFamily; row.ControllerModel = Clean(dto.ControllerModel); row.LinkKind = dto.LinkKind; row.Host = host; row.Port = dto.Port; row.RemotePath = path;
            row.CredentialKey = credential; row.ProgramNameFormat = format; row.FileExtension = ext; row.LineEnding = dto.LineEnding; row.RequirePercent = dto.RequirePercent;
            row.MaxProgramBytes = dto.MaxProgramBytes; row.Axes = axes; row.MaxSpindleRpm = dto.MaxSpindleRpm; row.MaxFeedMmPerMin = dto.MaxFeedMmPerMin; row.ToolCapacity = dto.ToolCapacity;
            row.IsLathe = dto.IsLathe; row.BaudRate = dto.BaudRate; row.RequireConfirmedProgram = dto.RequireConfirmedProgram; row.AllowedAddresses = addresses; row.IsActive = dto.IsActive;
            row.Note = Clean(dto.Note); row.ModifyDate = now; row.ModifierUserId = Me;
            if (row.LinkKind != MfNcLinkKind.EdgeGateway) row.GatewayKeyHash = null;   // a key outlives no link kind change: a new gateway gets a new key
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<MachineConnectionDto>(MfText.Concurrency); }
            catch (DbUpdateException) { return Bad<MachineConnectionDto>("برای این ماشین هم‌زمان اتصال دیگری ثبت شد؛ صفحه را دوباره باز کنید."); }
            return await GetConnectionAsync(dto.MachineId, ct);
        }

        private static string HashKey(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key ?? "")));

        public async Task<ResponseDto<GatewayKeyDto>> NewGatewayKeyAsync(MachineIdDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.LinkEditors) || IsAi) return Bad<GatewayKeyDto>(MfText.NotAllowed);
            if (dto == null) return Bad<GatewayKeyDto>(MfText.InvalidData);
            MfMachineConnection row = await _uow.Set<MfMachineConnection>().FirstOrDefaultAsync(c => c.MachineId == dto.MachineId && !c.Deleted, ct);
            if (row == null || row.LinkKind != MfNcLinkKind.EdgeGateway) return Bad<GatewayKeyDto>("ابتدا نوع اتصال ماشین را «گیت‌وی لبه» ثبت کنید.");
            string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            DateTimeOffset now = DateTimeOffset.Now;
            row.GatewayKeyHash = HashKey(key); row.GatewayKeyRotatedAt = now; row.GatewayKeyRotatedByUserId = Me; row.ModifyDate = now; row.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<GatewayKeyDto>(MfText.Concurrency); }
            return ResponseDtoHelper.Ok(new GatewayKeyDto { MachineId = dto.MachineId, Key = key, RotatedAtText = MfText.Stamp(now) });
        }

        public async Task<ResponseDto<LinkTestResultDto>> TestConnectionAsync(MachineIdDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.LinkTesters)) return Bad<LinkTestResultDto>(MfText.NotAllowed);
            if (dto == null) return Bad<LinkTestResultDto>(MfText.InvalidData);
            MfMachineConnection c = await _uow.Set<MfMachineConnection>().AsNoTracking().FirstOrDefaultAsync(x => x.MachineId == dto.MachineId && !x.Deleted, ct);
            if (c == null) return Bad<LinkTestResultDto>("اتصال این ماشین تعریف نشده است.");
            if (c.LinkKind == MfNcLinkKind.Manual) return ResponseDtoHelper.Ok(new LinkTestResultDto { Ok = true, Message = "انتقال دستی است؛ آزمونی لازم نیست." });
            if (c.LinkKind == MfNcLinkKind.EdgeGateway)
                return ResponseDtoHelper.Ok(new LinkTestResultDto
                {
                    Ok = c.LastSeenAt.HasValue && DateTimeOffset.Now - c.LastSeenAt.Value < TimeSpan.FromMinutes(5),
                    Message = c.LastSeenAt.HasValue ? "آخرین تماس گیت‌وی: " + MfText.Stamp(c.LastSeenAt) : "گیت‌وی هنوز تماسی نگرفته است.",
                });
            IMfNcLink link = _links.For(c.LinkKind);
            if (link == null) return Bad<LinkTestResultDto>(MfText.InvalidData);
            NcLinkResult r = await link.TestAsync(TargetOf(c), ct);
            return ResponseDtoHelper.Ok(new LinkTestResultDto { Ok = r.Ok, Message = r.Message });
        }

        // ================================================================================== programs

        private async Task<List<NcProgramDto>> ProgramDtosAsync(List<MfNcProgram> rows, CancellationToken ct)
        {
            List<int> itemIds = rows.Where(r => r.ItemId.HasValue).Select(r => r.ItemId.Value).Distinct().ToList();
            List<int> templateIds = rows.Where(r => r.OperationTemplateId.HasValue).Select(r => r.OperationTemplateId.Value).Distinct().ToList();
            List<int> centreIds = rows.Where(r => r.WorkCenterId.HasValue).Select(r => r.WorkCenterId.Value).Distinct().ToList();
            List<int> revisionIds = rows.Where(r => r.CurrentRevisionId.HasValue).Select(r => r.CurrentRevisionId.Value).ToList();
            List<int> programIds = rows.Select(r => r.Id).ToList();
            var items = itemIds.Count == 0 ? new Dictionary<int, (string Code, string Title)>()
                : (await _uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).Select(i => new { i.Id, i.Code, i.Title }).ToListAsync(ct)).ToDictionary(i => i.Id, i => (i.Code, i.Title));
            Dictionary<int, string> templates = templateIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<OperationTemplate>().AsNoTracking().Where(t => templateIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Title, ct);
            Dictionary<int, string> centres = centreIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<MfWorkCenter>().AsNoTracking().Where(w => centreIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Code, ct);
            Dictionary<int, string> machines = await MachineTitlesAsync(rows.Where(r => r.MachineId.HasValue).Select(r => r.MachineId.Value), ct);
            var current = revisionIds.Count == 0 ? new Dictionary<int, (int Revision, DateTimeOffset? ApprovedAt)>()
                : (await _uow.Set<MfNcRevision>().AsNoTracking().Where(r => revisionIds.Contains(r.Id)).Select(r => new { r.Id, r.Revision, r.ApprovedAt }).ToListAsync(ct))
                    .ToDictionary(r => r.Id, r => (r.Revision, r.ApprovedAt));
            var pending = await _uow.Set<MfNcRevision>().AsNoTracking().Where(r => !r.Deleted && programIds.Contains(r.ProgramId) && r.Status == MfNcRevisionStatus.Submitted)
                .GroupBy(r => r.ProgramId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
            return rows.Select(r => new NcProgramDto
            {
                Id = r.Id, Code = r.Code, Title = r.Title, ItemId = r.ItemId,
                ItemCode = r.ItemId.HasValue && items.TryGetValue(r.ItemId.Value, out var it) ? it.Code : null,
                ItemTitle = r.ItemId.HasValue && items.TryGetValue(r.ItemId.Value, out var it2) ? it2.Title : null,
                OperationTemplateId = r.OperationTemplateId, OperationTitle = r.OperationTemplateId.HasValue && templates.TryGetValue(r.OperationTemplateId.Value, out string tt) ? tt : null,
                WorkCenterId = r.WorkCenterId, WorkCenterCode = r.WorkCenterId.HasValue && centres.TryGetValue(r.WorkCenterId.Value, out string wc) ? wc : null,
                MachineId = r.MachineId, MachineTitle = r.MachineId.HasValue && machines.TryGetValue(r.MachineId.Value, out string mt) ? mt : null,
                ControllerFamily = r.ControllerFamily, CurrentRevisionId = r.CurrentRevisionId,
                CurrentRevision = r.CurrentRevisionId.HasValue && current.TryGetValue(r.CurrentRevisionId.Value, out var cr) ? cr.Revision : null,
                CurrentApprovedAtText = r.CurrentRevisionId.HasValue && current.TryGetValue(r.CurrentRevisionId.Value, out var cr2) ? MfText.Stamp(cr2.ApprovedAt) : null,
                PendingRevisions = pending.TryGetValue(r.Id, out int pc) ? pc : 0, Status = r.Status, Note = r.Note, RowVersion = MfConcurrency.Text(r.RowVersion),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<NcProgramDto>>> GetProgramsAsync(NcProgramQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<PagedList<NcProgramDto>>(MfText.NotAllowed);
            query ??= new NcProgramQuery();
            IQueryable<MfNcProgram> q = _uow.Set<MfNcProgram>().AsNoTracking().Where(p => !p.Deleted);
            if (query.ItemId.HasValue) q = q.Where(p => p.ItemId == query.ItemId.Value);
            if (query.OperationTemplateId.HasValue) q = q.Where(p => p.OperationTemplateId == query.OperationTemplateId.Value);
            if (query.WorkCenterId.HasValue) q = q.Where(p => p.WorkCenterId == query.WorkCenterId.Value);
            if (query.MachineId.HasValue) q = q.Where(p => p.MachineId == query.MachineId.Value);
            if (query.Status.HasValue) q = q.Where(p => p.Status == query.Status.Value);
            if (query.Pending == true) q = q.Where(p => _uow.Set<MfNcRevision>().Any(r => !r.Deleted && r.ProgramId == p.Id && r.Status == MfNcRevisionStatus.Submitted));
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(p => p.Code.Contains(text) || p.Title.Contains(text));
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize), number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MfNcProgram> rows = await q.OrderBy(p => p.Code).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(Page(await ProgramDtosAsync(rows, ct), total, number, size));
        }

        public async Task<ResponseDto<NcProgramDto>> GetProgramAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<NcProgramDto>(MfText.NotAllowed);
            MfNcProgram row = await _uow.Set<MfNcProgram>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && !p.Deleted, ct);
            if (row == null) return MfText.NotFound<NcProgramDto>("برنامه پیدا نشد.");
            NcProgramDto dto = (await ProgramDtosAsync(new List<MfNcProgram> { row }, ct))[0];
            List<MfNcRevision> revisions = await _uow.Set<MfNcRevision>().AsNoTracking().Where(r => r.ProgramId == id && !r.Deleted).OrderByDescending(r => r.Revision).ToListAsync(ct);
            dto.Revisions = await RevisionDtosAsync(revisions, new Dictionary<int, MfNcProgram> { [row.Id] = row }, ct);
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<NcProgramDto>> SaveProgramAsync(SaveNcProgramDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Writers) || IsAi) return Bad<NcProgramDto>(MfText.NotAllowed);
            if (dto == null || !Enum.IsDefined(typeof(MfNcControllerFamily), dto.ControllerFamily) || !Enum.IsDefined(typeof(MfNcProgramStatus), dto.Status)) return Bad<NcProgramDto>(MfText.InvalidData);
            string code = Clean(dto.Code), title = Clean(dto.Title);
            if (code == null || title == null) return Bad<NcProgramDto>("کد و عنوان برنامه را وارد کنید.");
            if (code.Length > 50 || title.Length > 300) return Bad<NcProgramDto>("کد برنامه حداکثر ۵۰ و عنوان حداکثر ۳۰۰ نویسه است.");
            if (await _uow.Set<MfNcProgram>().AnyAsync(p => p.Code == code && !p.Deleted && p.Id != (dto.Id ?? 0), ct)) return Bad<NcProgramDto>("این کد برنامه قبلاً ثبت شده است.");
            if (dto.ItemId.HasValue && !await _uow.Set<InventoryItem>().AnyAsync(i => i.Id == dto.ItemId.Value && !i.Deleted, ct)) return Bad<NcProgramDto>("قطعه پیدا نشد.");
            if (dto.OperationTemplateId.HasValue && !await _uow.Set<OperationTemplate>().AnyAsync(t => t.Id == dto.OperationTemplateId.Value && !t.Deleted, ct)) return Bad<NcProgramDto>("مرحله مسیر ساخت پیدا نشد.");
            if (dto.WorkCenterId.HasValue && !await _uow.Set<MfWorkCenter>().AnyAsync(w => w.Id == dto.WorkCenterId.Value && !w.Deleted, ct)) return Bad<NcProgramDto>("مرکز کار پیدا نشد.");
            if (dto.MachineId.HasValue)
            {
                if (!await _uow.Set<Machine>().AnyAsync(m => m.Id == dto.MachineId.Value, ct)) return Bad<NcProgramDto>("ماشین پیدا نشد.");
                MfMachineInfo info = await _uow.Set<MfMachineInfo>().AsNoTracking().FirstOrDefaultAsync(i => i.MachineId == dto.MachineId.Value && !i.Deleted, ct);
                if (dto.WorkCenterId.HasValue && info?.WorkCenterId != null && info.WorkCenterId != dto.WorkCenterId) return Bad<NcProgramDto>("این ماشین متعلق به مرکز کار انتخاب‌شده نیست.");
                MfMachineConnection c = await _uow.Set<MfMachineConnection>().AsNoTracking().FirstOrDefaultAsync(x => x.MachineId == dto.MachineId.Value && !x.Deleted, ct);
                if (c != null && !DncText.Fits(dto.ControllerFamily, c.ControllerFamily))
                    return Bad<NcProgramDto>($"خانواده کنترلر برنامه ({DncText.Family(dto.ControllerFamily)}) با کنترلر ماشین ({DncText.Family(c.ControllerFamily)}) یکی نیست.");
            }
            DateTimeOffset now = DateTimeOffset.Now;
            MfNcProgram row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<MfNcProgram>().FirstOrDefaultAsync(p => p.Id == dto.Id.Value && !p.Deleted, ct);
                if (row == null) return MfText.NotFound<NcProgramDto>("برنامه پیدا نشد.");
                if (!MfConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<NcProgramDto>(MfText.Concurrency);
                if (row.CurrentRevisionId.HasValue && row.ControllerFamily != dto.ControllerFamily)
                    return Bad<NcProgramDto>("خانواده کنترلر برنامه‌ای که نسخه تأییدشده دارد تغییر نمی‌کند؛ برنامه تازه‌ای باز کنید.");
                // review finding 4: what an approved program serves (part, step, work centre, machine) is part of what was approved; one person does not move it elsewhere
                if (row.CurrentRevisionId.HasValue && (row.ItemId != dto.ItemId || row.OperationTemplateId != dto.OperationTemplateId || row.WorkCenterId != dto.WorkCenterId || row.MachineId != dto.MachineId))
                    return Bad<NcProgramDto>("قطعه، مرحله، مرکز کار یا ماشین برنامه‌ای که نسخه تأییدشده دارد تغییر نمی‌کند؛ برای کاربرد دیگر برنامه تازه‌ای باز کنید.");
                // switching an approved program off (or on again) takes an approver and a written reason: it opens or closes the start gate of the step
                if (row.CurrentRevisionId.HasValue && row.Status != dto.Status)
                {
                    if (!_user.IsInRole(DncRoles.Approvers)) return Bad<NcProgramDto>("تغییر وضعیت برنامه‌ای که نسخه تأییدشده دارد با تأییدکنندگان برنامه است.");
                    if (Clean(dto.Note) == null || Clean(dto.Note) == row.Note) return Bad<NcProgramDto>("دلیل تغییر وضعیت برنامه را در توضیح بنویسید.");
                }
            }
            else
            {
                row = new MfNcProgram { CreateDate = now, CreatorUserId = Me };
                _uow.Set<MfNcProgram>().Add(row);
            }
            row.Code = code; row.Title = title; row.ItemId = dto.ItemId; row.OperationTemplateId = dto.OperationTemplateId; row.WorkCenterId = dto.WorkCenterId; row.MachineId = dto.MachineId;
            row.ControllerFamily = dto.ControllerFamily; row.Status = dto.Status; row.Note = Clean(dto.Note); row.ModifyDate = now; row.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<NcProgramDto>(MfText.Concurrency); }
            catch (DbUpdateException) { return Bad<NcProgramDto>("این کد برنامه هم‌زمان توسط کاربر دیگری ثبت شد."); }
            return await GetProgramAsync(row.Id, ct);
        }

        // ================================================================================== revisions

        /// <summary>The machines a program may go to (with their links): the bound machine; else the machines of its work centre; else every machine with a link of the same control family.</summary>
        private async Task<List<(int MachineId, string Title, MfMachineConnection Connection)>> TargetsAsync(MfNcProgram p, int? onlyMachine, CancellationToken ct)
        {
            IQueryable<MfMachineConnection> q = _uow.Set<MfMachineConnection>().AsNoTracking().Where(c => !c.Deleted && c.IsActive);
            List<MfMachineConnection> connections;
            if (onlyMachine.HasValue) connections = await q.Where(c => c.MachineId == onlyMachine.Value).ToListAsync(ct);
            else if (p.MachineId.HasValue) connections = await q.Where(c => c.MachineId == p.MachineId.Value).ToListAsync(ct);
            else if (p.WorkCenterId.HasValue)
            {
                int wc = p.WorkCenterId.Value;
                connections = await q.Where(c => _uow.Set<MfMachineInfo>().Any(i => !i.Deleted && i.MachineId == c.MachineId && i.WorkCenterId == wc && i.Status != MfMachineStatus.Retired)).ToListAsync(ct);
            }
            else if (p.ControllerFamily != MfNcControllerFamily.Unknown) connections = await q.Where(c => c.ControllerFamily == p.ControllerFamily).ToListAsync(ct);
            else connections = new List<MfMachineConnection>();
            connections = connections.Where(c => DncText.Fits(p.ControllerFamily, c.ControllerFamily)).ToList();
            Dictionary<int, string> titles = await MachineTitlesAsync(connections.Select(c => c.MachineId), ct);
            return connections.Select(c => (c.MachineId, titles.TryGetValue(c.MachineId, out string t) ? t : "#" + c.MachineId, c)).OrderBy(x => x.Item2).ToList();
        }

        /// <summary>The check of the content against every machine the program may go to (findings of several machines merged, the same finding once); with no machine, the check of the program family alone.</summary>
        private async Task<NcAnalysis> AnalyseAsync(MfNcProgram p, byte[] bytes, CancellationToken ct)
        {
            var targets = await TargetsAsync(p, null, ct);
            if (targets.Count == 0) return MfNcRules.Analyse(bytes, new NcProfile { Family = p.ControllerFamily, Name = DncText.Family(p.ControllerFamily) });
            NcAnalysis merged = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in targets)
            {
                NcAnalysis a = MfNcRules.Analyse(bytes, NcProfile.Of(t.Connection, t.Title));
                if (merged == null) { merged = new NcAnalysis { ProgramNumber = a.ProgramNumber, LineCount = a.LineCount, SizeBytes = a.SizeBytes, Sha256 = a.Sha256 }; }
                foreach (NcFinding f in a.Findings)
                    if (seen.Add(f.Stored)) merged.Findings.Add(f);
            }
            return merged;
        }

        private async Task<List<NcRevisionDto>> RevisionDtosAsync(List<MfNcRevision> rows, Dictionary<int, MfNcProgram> programs, CancellationToken ct)
        {
            Dictionary<int, string> names = await UserNamesAsync(rows.SelectMany(r => new int?[] { r.CreatorUserId, r.SubmittedByUserId, r.ApprovedByUserId, r.RejectedByUserId }), ct);
            List<int> baseIds = rows.Where(r => r.BaseRevisionId.HasValue).Select(r => r.BaseRevisionId.Value).Distinct().ToList();
            Dictionary<int, int> bases = baseIds.Count == 0 ? new Dictionary<int, int>()
                : await _uow.Set<MfNcRevision>().AsNoTracking().Where(r => baseIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Revision, ct);
            bool writer = _user.IsInRole(DncRoles.Writers), approver = _user.IsInRole(DncRoles.Approvers) && !IsAi;
            return rows.Select(r =>
            {
                programs.TryGetValue(r.ProgramId, out MfNcProgram p);
                var dto = new NcRevisionDto
                {
                    Id = r.Id, ProgramId = r.ProgramId, ProgramCode = p?.Code, Revision = r.Revision, Status = r.Status, FileName = r.FileName, SizeBytes = r.SizeBytes, LineCount = r.LineCount,
                    Sha256 = r.Sha256, ProgramNumber = r.ProgramNumber, Source = r.Source, FromTransferId = r.FromTransferId, BaseRevisionId = r.BaseRevisionId,
                    BaseRevision = r.BaseRevisionId.HasValue && bases.TryGetValue(r.BaseRevisionId.Value, out int br) ? br : null, DiffAdded = r.DiffAdded, DiffRemoved = r.DiffRemoved,
                    ChangeNote = r.ChangeNote, Findings = FindingsOf(r.Findings), ErrorCount = r.ErrorCount, WarningCount = r.WarningCount, AuthorUserId = r.CreatorUserId,
                    AuthorName = NameOf(names, r.CreatorUserId), CreatedAtText = MfText.Stamp(r.CreateDate), SubmittedByName = NameOf(names, r.SubmittedByUserId),
                    SubmittedAtText = MfText.Stamp(r.SubmittedAt), ApprovedByName = NameOf(names, r.ApprovedByUserId), ApprovedAtText = MfText.Stamp(r.ApprovedAt),
                    RejectedByName = NameOf(names, r.RejectedByUserId), RejectedAtText = MfText.Stamp(r.RejectedAt), RejectReason = r.RejectReason, RowVersion = MfConcurrency.Text(r.RowVersion),
                };
                bool author = r.CreatorUserId == Me;
                if (r.Status == MfNcRevisionStatus.Draft && (author || writer) && r.ErrorCount == 0) dto.AllowedActions.Add("submit");
                if ((r.Status == MfNcRevisionStatus.Draft || r.Status == MfNcRevisionStatus.Submitted) && (author || _user.IsInRole(ProductionRoles.MasterEditors))) dto.AllowedActions.Add("withdraw");
                if (r.Status == MfNcRevisionStatus.Submitted && approver && !author && r.SubmittedByUserId != Me) { dto.AllowedActions.Add("approve"); dto.AllowedActions.Add("reject"); }
                return dto;
            }).ToList();
        }

        private async Task<NcRevisionDto> RevisionDtoAsync(int id, CancellationToken ct)
        {
            MfNcRevision r = await _uow.Set<MfNcRevision>().AsNoTracking().FirstAsync(x => x.Id == id, ct);
            MfNcProgram p = await _uow.Set<MfNcProgram>().AsNoTracking().FirstAsync(x => x.Id == r.ProgramId, ct);
            return (await RevisionDtosAsync(new List<MfNcRevision> { r }, new Dictionary<int, MfNcProgram> { [p.Id] = p }, ct))[0];
        }

        private static string SafeFileName(string name)
        {
            string n = Clean(name);
            if (n == null) return null;
            n = Path.GetFileName(n.Replace('\\', '/'));
            if (n.Length == 0 || n.Length > 200 || n.Any(char.IsControl)) return null;
            return n;
        }

        /// <summary>Makes a revision from content (an upload or what came back from a machine). The caller saves.</summary>
        private async Task<(MfNcRevision Revision, string Error)> NewRevisionAsync(MfNcProgram p, string fileName, byte[] bytes, string note, MfNcRevisionSource source, int? fromTransferId, int authorUserId, CancellationToken ct)
        {
            if (bytes == null || bytes.Length == 0) return (null, "فایل برنامه خالی است.");
            if (bytes.Length > MfNcRules.GlobalMaxBytes) return (null, "حجم فایل بیش از ۴ مگابایت است.");
            if (await _uow.Set<MfNcRevision>().AnyAsync(r => !r.Deleted && r.ProgramId == p.Id && (r.Status == MfNcRevisionStatus.Draft || r.Status == MfNcRevisionStatus.Submitted), ct))
                return (null, "این برنامه نسخه باز دیگری (پیش‌نویس یا منتظر تأیید) دارد؛ ابتدا آن را تأیید، رد یا پس‌گرفته کنید.");
            string sha = MfNcRules.Sha256Hex(bytes);
            MfNcRevision current = p.CurrentRevisionId.HasValue ? await _uow.Set<MfNcRevision>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == p.CurrentRevisionId.Value, ct) : null;
            if (current != null && current.Sha256 == sha) return (null, "محتوای این فایل با نسخه جاری برنامه یکی است؛ نسخه تازه‌ای لازم نیست.");
            NcAnalysis a = await AnalyseAsync(p, bytes, ct);
            NcDiff diff = null;
            if (current != null)
            {
                byte[] old = await _uow.Set<MfNcContent>().AsNoTracking().Where(c => c.RevisionId == current.Id).Select(c => c.Bytes).FirstOrDefaultAsync(ct);
                diff = MfNcRules.Diff(MfNcRules.Lines(old), MfNcRules.Lines(bytes));
            }
            int next = (await _uow.Set<MfNcRevision>().Where(r => r.ProgramId == p.Id).MaxAsync(r => (int?)r.Revision, ct) ?? 0) + 1;
            DateTimeOffset now = DateTimeOffset.Now;
            var revision = new MfNcRevision
            {
                ProgramId = p.Id, Revision = next, Status = MfNcRevisionStatus.Draft, FileName = fileName, SizeBytes = bytes.Length, LineCount = a.LineCount, Sha256 = sha,
                ProgramNumber = a.ProgramNumber, Source = source, FromTransferId = fromTransferId, BaseRevisionId = current?.Id, DiffAdded = diff?.Added ?? 0, DiffRemoved = diff?.Removed ?? 0,
                ChangeNote = Clean(note), Findings = a.Stored(), ErrorCount = a.ErrorCount, WarningCount = a.WarningCount,
                CreateDate = now, ModifyDate = now, CreatorUserId = authorUserId, ModifierUserId = authorUserId,
            };
            _uow.Set<MfNcRevision>().Add(revision);
            return (revision, null);
        }

        public async Task<ResponseDto<NcRevisionDto>> UploadRevisionAsync(UploadNcRevisionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Writers) || IsAi) return Bad<NcRevisionDto>(MfText.NotAllowed);
            if (dto == null) return Bad<NcRevisionDto>(MfText.InvalidData);
            string fileName = SafeFileName(dto.FileName);
            if (fileName == null) return Bad<NcRevisionDto>("نام فایل نامعتبر است.");
            if (string.IsNullOrWhiteSpace(dto.ContentBase64)) return Bad<NcRevisionDto>("فایل برنامه خالی است.");
            if (dto.ContentBase64.Length > (MfNcRules.GlobalMaxBytes / 3 + 1) * 4 + 8) return Bad<NcRevisionDto>("حجم فایل بیش از ۴ مگابایت است.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(dto.ContentBase64.Trim()); }
            catch (FormatException) { return Bad<NcRevisionDto>("محتوای فایل به‌درستی ارسال نشده است."); }
            MfNcProgram p = await _uow.Set<MfNcProgram>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.ProgramId && !x.Deleted, ct);
            if (p == null) return MfText.NotFound<NcRevisionDto>("برنامه پیدا نشد.");
            if (p.Status != MfNcProgramStatus.Active) return Bad<NcRevisionDto>("برنامه منسوخ است؛ نسخه تازه نمی‌گیرد.");
            var made = await NewRevisionAsync(p, fileName, bytes, dto.ChangeNote, MfNcRevisionSource.Upload, null, Me, ct);
            if (made.Error != null) return Bad<NcRevisionDto>(made.Error);
            _uow.Set<MfNcContent>().Add(new MfNcContent { Revision = made.Revision, Bytes = bytes });
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<NcRevisionDto>("نسخه هم‌زمان توسط کاربر دیگری ثبت شد؛ صفحه را دوباره باز کنید."); }
            return ResponseDtoHelper.Ok(await RevisionDtoAsync(made.Revision.Id, ct));
        }

        public async Task<ResponseDto<NcRevisionDto>> RevisionActionAsync(NcRevisionActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Action)) return Bad<NcRevisionDto>(MfText.InvalidData);
            string action = dto.Action.Trim().ToLowerInvariant();
            MfNcRevision r = await _uow.Set<MfNcRevision>().FirstOrDefaultAsync(x => x.Id == dto.RevisionId && !x.Deleted, ct);
            if (r == null) return MfText.NotFound<NcRevisionDto>("نسخه پیدا نشد.");
            MfNcProgram p = await _uow.Set<MfNcProgram>().FirstAsync(x => x.Id == r.ProgramId, ct);
            if (!MfConcurrency.Apply(_uow, r, dto.RowVersion)) return Bad<NcRevisionDto>(MfText.Concurrency);
            DateTimeOffset now = DateTimeOffset.Now;
            bool author = r.CreatorUserId == Me;
            switch (action)
            {
                case "submit":
                {
                    if (!(author || _user.IsInRole(DncRoles.Writers)) || IsAi) return Bad<NcRevisionDto>(MfText.NotAllowed);
                    if (r.Status != MfNcRevisionStatus.Draft) return Bad<NcRevisionDto>("فقط پیش‌نویس برای تأیید ارسال می‌شود.");
                    if (p.Status != MfNcProgramStatus.Active) return Bad<NcRevisionDto>("برنامه منسوخ است.");
                    // the machines may have changed since the upload: check again
                    byte[] bytes = await ContentAsync(r.Id, ct);
                    NcAnalysis a = await AnalyseAsync(p, bytes, ct);
                    r.Findings = a.Stored(); r.ErrorCount = a.ErrorCount; r.WarningCount = a.WarningCount;
                    if (a.ErrorCount > 0)
                    {
                        await SaveQuietAsync(ct);
                        return Bad<NcRevisionDto>("برنامه خطای بررسی دارد و برای تأیید ارسال نمی‌شود (" + a.ErrorCount + " خطا).");
                    }
                    r.Status = MfNcRevisionStatus.Submitted; r.SubmittedByUserId = Me; r.SubmittedAt = now;
                    break;
                }
                case "withdraw":
                    if (!(author || _user.IsInRole(ProductionRoles.MasterEditors)) || IsAi) return Bad<NcRevisionDto>(MfText.NotAllowed);
                    if (r.Status != MfNcRevisionStatus.Draft && r.Status != MfNcRevisionStatus.Submitted) return Bad<NcRevisionDto>("فقط نسخه پیش‌نویس یا منتظر تأیید پس گرفته می‌شود.");
                    r.Status = MfNcRevisionStatus.Withdrawn;
                    break;
                case "approve":
                case "reject":
                {
                    if (!_user.IsInRole(DncRoles.Approvers) || IsAi) return Bad<NcRevisionDto>(MfText.NotAllowed);
                    if (author || r.SubmittedByUserId == Me) return Bad<NcRevisionDto>(MfText.SameUser);
                    if (r.Status != MfNcRevisionStatus.Submitted) return Bad<NcRevisionDto>("این نسخه منتظر تأیید نیست.");
                    if (action == "reject")
                    {
                        string reason = Clean(dto.Reason);
                        if (reason == null) return Bad<NcRevisionDto>("دلیل رد را بنویسید.");
                        r.Status = MfNcRevisionStatus.Rejected; r.RejectedByUserId = Me; r.RejectedAt = now; r.RejectReason = reason.Length > 1000 ? reason.Substring(0, 1000) : reason;
                        break;
                    }
                    if (p.Status != MfNcProgramStatus.Active) return Bad<NcRevisionDto>("برنامه منسوخ است و نسخه تأییدی نمی‌گیرد.");
                    byte[] bytes = await ContentAsync(r.Id, ct);
                    if (MfNcRules.Sha256Hex(bytes) != r.Sha256) return Bad<NcRevisionDto>("محتوای ذخیره‌شده با اثر انگشت نسخه نمی‌خواند؛ نسخه تأیید نمی‌شود. موضوع را به مدیر سیستم گزارش دهید.");
                    NcAnalysis a = await AnalyseAsync(p, bytes, ct);
                    r.Findings = a.Stored(); r.ErrorCount = a.ErrorCount; r.WarningCount = a.WarningCount;
                    if (a.ErrorCount > 0)
                    {
                        await SaveQuietAsync(ct);
                        return Bad<NcRevisionDto>("برنامه با تنظیمات فعلی ماشین‌ها خطای بررسی دارد (" + a.ErrorCount + " خطا) و تأیید نمی‌شود.");
                    }
                    if (p.CurrentRevisionId.HasValue)
                    {
                        MfNcRevision old = await _uow.Set<MfNcRevision>().FirstOrDefaultAsync(x => x.Id == p.CurrentRevisionId.Value, ct);
                        if (old != null && old.Status == MfNcRevisionStatus.Approved) { old.Status = MfNcRevisionStatus.Superseded; old.ModifyDate = now; old.ModifierUserId = Me; }
                    }
                    r.Status = MfNcRevisionStatus.Approved; r.ApprovedByUserId = Me; r.ApprovedAt = now;
                    p.CurrentRevisionId = r.Id; p.ModifyDate = now; p.ModifierUserId = Me;
                    break;
                }
                default:
                    return Bad<NcRevisionDto>(MfText.InvalidData);
            }
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<NcRevisionDto>(MfText.Concurrency); }
            await DncGates.RevisionAsync(_notify, r, p.Code, ct);
            return ResponseDtoHelper.Ok(await RevisionDtoAsync(r.Id, ct));
        }

        private async Task SaveQuietAsync(CancellationToken ct)
        {
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { ClearTracker(); /* refreshed next time; nothing failed stays tracked for the next save (review finding 5) */ }
        }

        /// <summary>Forgets every tracked change after a failed save, so the next save of this unit of work does not retry it.</summary>
        private void ClearTracker()
        {
            if (_uow is DbContext db) db.ChangeTracker.Clear();
        }

        private Task<byte[]> ContentAsync(int revisionId, CancellationToken ct) =>
            _uow.Set<MfNcContent>().AsNoTracking().Where(c => c.RevisionId == revisionId).Select(c => c.Bytes).FirstOrDefaultAsync(ct);

        public async Task<ResponseDto<NcFileDto>> GetRevisionContentAsync(int revisionId, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<NcFileDto>(MfText.NotAllowed);
            MfNcRevision r = await _uow.Set<MfNcRevision>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == revisionId && !x.Deleted, ct);
            if (r == null) return MfText.NotFound<NcFileDto>("نسخه پیدا نشد.");
            byte[] bytes = await ContentAsync(r.Id, ct);
            return ResponseDtoHelper.Ok(new NcFileDto { FileName = r.FileName, Bytes = bytes, Sha256 = r.Sha256 });
        }

        public async Task<ResponseDto<NcDiffDto>> GetRevisionDiffAsync(int revisionId, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<NcDiffDto>(MfText.NotAllowed);
            MfNcRevision r = await _uow.Set<MfNcRevision>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == revisionId && !x.Deleted, ct);
            if (r == null) return MfText.NotFound<NcDiffDto>("نسخه پیدا نشد.");
            // the revision before it: the base it was made against, else the latest earlier approved one
            int? baseId = r.BaseRevisionId ?? await _uow.Set<MfNcRevision>().AsNoTracking()
                .Where(x => x.ProgramId == r.ProgramId && x.Revision < r.Revision && (x.Status == MfNcRevisionStatus.Approved || x.Status == MfNcRevisionStatus.Superseded))
                .OrderByDescending(x => x.Revision).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
            var dto = new NcDiffDto { RevisionId = r.Id, BaseRevisionId = baseId };
            byte[] now = await ContentAsync(r.Id, ct);
            if (!baseId.HasValue)
            {
                dto.Added = MfNcRules.Lines(now).Count;
                return ResponseDtoHelper.Ok(dto);
            }
            dto.BaseRevision = await _uow.Set<MfNcRevision>().AsNoTracking().Where(x => x.Id == baseId.Value).Select(x => (int?)x.Revision).FirstOrDefaultAsync(ct);
            NcDiff diff = MfNcRules.Diff(MfNcRules.Lines(await ContentAsync(baseId.Value, ct)), MfNcRules.Lines(now));
            dto.Added = diff.Added; dto.Removed = diff.Removed; dto.Lines = diff.Lines; dto.Truncated = diff.Truncated; dto.Approximate = diff.Approximate;
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<List<NcValidationDto>>> ValidateAsync(ValidateNcDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<List<NcValidationDto>>(MfText.NotAllowed);
            if (dto == null) return Bad<List<NcValidationDto>>(MfText.InvalidData);
            MfNcRevision r = await _uow.Set<MfNcRevision>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.RevisionId && !x.Deleted, ct);
            if (r == null) return MfText.NotFound<List<NcValidationDto>>("نسخه پیدا نشد.");
            MfNcProgram p = await _uow.Set<MfNcProgram>().AsNoTracking().FirstAsync(x => x.Id == r.ProgramId, ct);
            byte[] bytes = await ContentAsync(r.Id, ct);
            var result = new List<NcValidationDto>();
            foreach (var t in await TargetsAsync(p, dto.MachineId, ct))
            {
                NcAnalysis a = MfNcRules.Analyse(bytes, NcProfile.Of(t.Connection, t.Title));
                var name = MfNcRules.TargetName(t.Connection.ProgramNameFormat, t.Connection.FileExtension, a.ProgramNumber, p.Code, r.FileName);
                var v = new NcValidationDto { MachineId = t.MachineId, MachineTitle = t.Title, TargetName = name.Name, Findings = FindingsOf(a), ErrorCount = a.ErrorCount, WarningCount = a.WarningCount };
                if (name.Error != null) { v.Findings.Add(new NcFindingDto { Severity = "error", Code = "NAME", Text = name.Error }); v.ErrorCount++; }
                result.Add(v);
            }
            return ResponseDtoHelper.Ok(result);
        }

        // ================================================================================== transfers

        private async Task<List<NcTransferDto>> TransferDtosAsync(List<MfNcTransfer> rows, CancellationToken ct)
        {
            Dictionary<int, string> machines = await MachineTitlesAsync(rows.Select(r => r.MachineId), ct);
            List<int> programIds = rows.Where(r => r.ProgramId.HasValue).Select(r => r.ProgramId.Value).Distinct().ToList();
            Dictionary<int, string> programs = programIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<MfNcProgram>().AsNoTracking().Where(p => programIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
            List<int> revisionIds = rows.Where(r => r.RevisionId.HasValue).Select(r => r.RevisionId.Value).Distinct().ToList();
            Dictionary<int, int> revisions = revisionIds.Count == 0 ? new Dictionary<int, int>()
                : await _uow.Set<MfNcRevision>().AsNoTracking().Where(r => revisionIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Revision, ct);
            List<int> opIds = rows.Where(r => r.OperationId.HasValue).Select(r => r.OperationId.Value).Distinct().ToList();
            var ops = opIds.Count == 0 ? new Dictionary<int, string>()
                : (await _uow.Set<MfOperation>().AsNoTracking().Where(o => opIds.Contains(o.Id))
                    .Join(_uow.Set<MfProductionOrder>().AsNoTracking(), o => o.OrderId, d => d.Id, (o, d) => new { o.Id, d.Number, o.Sequence, o.Title }).ToListAsync(ct))
                    .ToDictionary(o => o.Id, o => MfNames.Operation(o.Number, o.Sequence) + " " + o.Title);
            Dictionary<int, string> names = await UserNamesAsync(rows.SelectMany(r => new int?[] { r.RequestedByUserId, r.ConfirmedByUserId }), ct);
            bool supervisor = _user.IsInRole(ProductionRoles.Supervisors), sender = _user.IsInRole(DncRoles.Senders);
            return rows.Select(r =>
            {
                var dto = new NcTransferDto
                {
                    Id = r.Id, Direction = r.Direction, MachineId = r.MachineId, MachineTitle = machines.TryGetValue(r.MachineId, out string mt) ? mt : null, ProgramId = r.ProgramId,
                    ProgramCode = r.ProgramId.HasValue && programs.TryGetValue(r.ProgramId.Value, out string pc) ? pc : null, RevisionId = r.RevisionId,
                    Revision = r.RevisionId.HasValue && revisions.TryGetValue(r.RevisionId.Value, out int rv) ? rv : null, OperationId = r.OperationId,
                    OperationName = r.OperationId.HasValue && ops.TryGetValue(r.OperationId.Value, out string on) ? on : null, TargetName = r.TargetName, LinkKind = r.LinkKind, Status = r.Status,
                    Attempts = r.Attempts, LastError = r.LastError, SentSha256 = r.SentSha256, SentBytes = r.SentBytes, RequestedByName = NameOf(names, r.RequestedByUserId),
                    RequestedAtText = MfText.Stamp(r.RequestedAt), DeliveredAtText = MfText.Stamp(r.DeliveredAt), ConfirmedByName = NameOf(names, r.ConfirmedByUserId),
                    ConfirmedAtText = MfText.Stamp(r.ConfirmedAt), ConfirmNote = r.ConfirmNote, CancelReason = r.CancelReason, RowVersion = MfConcurrency.Text(r.RowVersion),
                };
                bool mine = r.RequestedByUserId == Me;
                if (r.Direction == MfNcTransferDirection.ToMachine && r.Status == MfNcTransferStatus.Delivered && sender) dto.AllowedActions.Add("confirm");
                if ((r.Status == MfNcTransferStatus.Queued || r.Status == MfNcTransferStatus.Failed || (r.Status == MfNcTransferStatus.Delivered && r.Direction == MfNcTransferDirection.ToMachine)) && (mine || supervisor))
                    dto.AllowedActions.Add("cancel");
                if (r.Status == MfNcTransferStatus.Failed && (mine || supervisor)) dto.AllowedActions.Add("retry");
                if (r.Direction == MfNcTransferDirection.ToMachine && r.LinkKind == MfNcLinkKind.Manual && (r.Status == MfNcTransferStatus.Queued || r.Status == MfNcTransferStatus.Delivered) && (sender || supervisor))
                    dto.AllowedActions.Add("download");
                return dto;
            }).ToList();
        }

        private async Task<NcTransferDto> TransferDtoAsync(int id, CancellationToken ct) =>
            (await TransferDtosAsync(new List<MfNcTransfer> { await _uow.Set<MfNcTransfer>().AsNoTracking().FirstAsync(t => t.Id == id, ct) }, ct))[0];

        public async Task<ResponseDto<PagedList<NcTransferDto>>> GetTransfersAsync(NcTransferQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<PagedList<NcTransferDto>>(MfText.NotAllowed);
            query ??= new NcTransferQuery();
            IQueryable<MfNcTransfer> q = _uow.Set<MfNcTransfer>().AsNoTracking().Where(t => !t.Deleted);
            if (query.MachineId.HasValue) q = q.Where(t => t.MachineId == query.MachineId.Value);
            if (query.ProgramId.HasValue) q = q.Where(t => t.ProgramId == query.ProgramId.Value);
            if (query.OperationId.HasValue) q = q.Where(t => t.OperationId == query.OperationId.Value);
            if (query.Status.HasValue) q = q.Where(t => t.Status == query.Status.Value);
            if (query.Direction.HasValue) q = q.Where(t => t.Direction == query.Direction.Value);
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize), number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MfNcTransfer> rows = await q.OrderByDescending(t => t.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(Page(await TransferDtosAsync(rows, ct), total, number, size));
        }

        /// <summary>The machine may receive a program now: it exists, is active and available, and has an active link.</summary>
        private async Task<(Machine Machine, MfMachineConnection Connection, string Error)> MachineForTransferAsync(int machineId, CancellationToken ct)
        {
            Machine machine = await _uow.Set<Machine>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == machineId, ct);
            if (machine == null) return (null, null, "ماشین پیدا نشد.");
            if (!machine.IsActive) return (null, null, "ماشین غیرفعال است.");
            MfMachineInfo info = await _uow.Set<MfMachineInfo>().AsNoTracking().FirstOrDefaultAsync(i => i.MachineId == machineId && !i.Deleted, ct);
            if (info != null && info.Status != MfMachineStatus.Available)
                return (null, null, $"ماشین {machine.Title} در دسترس نیست ({(info.Status == MfMachineStatus.Down ? "خراب" : info.Status == MfMachineStatus.UnderMaintenance ? "در تعمیر" : "بازنشسته")}).");
            MfMachineConnection c = await _uow.Set<MfMachineConnection>().AsNoTracking().FirstOrDefaultAsync(x => x.MachineId == machineId && !x.Deleted, ct);
            if (c == null || !c.IsActive) return (null, null, $"اتصال DNC ماشین {machine.Title} تعریف یا فعال نشده است.");
            return (machine, c, null);
        }

        public async Task<ResponseDto<NcTransferDto>> SendAsync(SendNcDto dto, CancellationToken ct)
        {
            if (!(_user.IsInRole(DncRoles.Senders) || _user.IsInRole(ProductionRoles.Supervisors)) || IsAi) return Bad<NcTransferDto>(MfText.NotAllowed);
            if (dto == null) return Bad<NcTransferDto>(MfText.InvalidData);
            MfNcProgram p = await _uow.Set<MfNcProgram>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.ProgramId && !x.Deleted, ct);
            if (p == null) return MfText.NotFound<NcTransferDto>("برنامه پیدا نشد.");
            if (p.Status != MfNcProgramStatus.Active) return Bad<NcTransferDto>("برنامه منسوخ است و ارسال نمی‌شود.");
            if (!p.CurrentRevisionId.HasValue) return Bad<NcTransferDto>("برنامه هنوز نسخه تأییدشده ندارد.");
            var m = await MachineForTransferAsync(dto.MachineId, ct);
            if (m.Error != null) return Bad<NcTransferDto>(m.Error);
            if (!(await TargetsAsync(p, null, ct)).Any(t => t.MachineId == dto.MachineId)) return Bad<NcTransferDto>($"این برنامه برای ماشین {m.Machine.Title} تعریف نشده است (ماشین، مرکز کار یا خانواده کنترلر).");
            if (dto.OperationId.HasValue)
            {
                MfOperation op = await _uow.Set<MfOperation>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == dto.OperationId.Value && !o.Deleted, ct);
                if (op == null) return Bad<NcTransferDto>("عملیات پیدا نشد.");
                if (p.OperationTemplateId.HasValue && op.OperationTemplateId != p.OperationTemplateId) return Bad<NcTransferDto>("این برنامه متعلق به مرحله مسیر ساخت این عملیات نیست.");
                if (op.Status == MfOperationStatus.Done || op.Status == MfOperationStatus.Skipped) return Bad<NcTransferDto>("این عملیات تمام شده است.");
                MfMachineInfo info = await _uow.Set<MfMachineInfo>().AsNoTracking().FirstOrDefaultAsync(i => i.MachineId == dto.MachineId && !i.Deleted, ct);
                if (info?.WorkCenterId != null && op.WorkCenterId.HasValue && info.WorkCenterId != op.WorkCenterId) return Bad<NcTransferDto>("این ماشین متعلق به مرکز کار این عملیات نیست.");
            }
            MfNcRevision r = await _uow.Set<MfNcRevision>().AsNoTracking().FirstAsync(x => x.Id == p.CurrentRevisionId.Value, ct);
            byte[] bytes = await ContentAsync(r.Id, ct);
            if (MfNcRules.Sha256Hex(bytes) != r.Sha256) return Bad<NcTransferDto>("محتوای ذخیره‌شده با اثر انگشت نسخه نمی‌خواند؛ ارسال انجام نمی‌شود. موضوع را به مدیر سیستم گزارش دهید.");
            NcAnalysis a = MfNcRules.Analyse(bytes, NcProfile.Of(m.Connection, m.Machine.Title));
            if (a.ErrorCount > 0) return Bad<NcTransferDto>("برنامه برای این ماشین خطای بررسی دارد: " + a.Findings.First(f => f.Severity == NcSeverity.Error).Text);
            var name = MfNcRules.TargetName(m.Connection.ProgramNameFormat, m.Connection.FileExtension, r.ProgramNumber, p.Code, r.FileName);
            if (name.Error != null) return Bad<NcTransferDto>(name.Error);
            // the same revision already on its way to the same machine: that transfer answers (a double tap on the terminal does not send twice)
            MfNcTransfer open = await _uow.Set<MfNcTransfer>().AsNoTracking().Where(t => !t.Deleted && t.MachineId == dto.MachineId && t.RevisionId == r.Id && t.Direction == MfNcTransferDirection.ToMachine
                && (t.Status == MfNcTransferStatus.Queued || t.Status == MfNcTransferStatus.Sending || t.Status == MfNcTransferStatus.Delivered)).OrderByDescending(t => t.Id).FirstOrDefaultAsync(ct);
            if (open != null) return ResponseDtoHelper.Ok(await TransferDtoAsync(open.Id, ct));
            DateTimeOffset now = DateTimeOffset.Now;
            var transfer = new MfNcTransfer
            {
                Direction = MfNcTransferDirection.ToMachine, MachineId = dto.MachineId, ProgramId = p.Id, RevisionId = r.Id, OperationId = dto.OperationId, TargetName = name.Name,
                LinkKind = m.Connection.LinkKind, Status = MfNcTransferStatus.Queued, RequestedByUserId = Me, RequestedAt = now, CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
            };
            _uow.Set<MfNcTransfer>().Add(transfer);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await TransferDtoAsync(transfer.Id, ct));
        }

        public async Task<ResponseDto<NcTransferDto>> ReceiveAsync(ReceiveNcDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Receivers) || IsAi) return Bad<NcTransferDto>(MfText.NotAllowed);
            if (dto == null) return Bad<NcTransferDto>(MfText.InvalidData);
            MfNcProgram p = await _uow.Set<MfNcProgram>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.ProgramId && !x.Deleted, ct);
            if (p == null) return MfText.NotFound<NcTransferDto>("برنامه پیدا نشد.");
            if (p.Status != MfNcProgramStatus.Active) return Bad<NcTransferDto>("برنامه منسوخ است.");
            Machine machine = await _uow.Set<Machine>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.MachineId, ct);
            if (machine == null) return Bad<NcTransferDto>("ماشین پیدا نشد.");
            MfMachineConnection c = await _uow.Set<MfMachineConnection>().AsNoTracking().FirstOrDefaultAsync(x => x.MachineId == dto.MachineId && !x.Deleted, ct);
            if (c == null || !c.IsActive) return Bad<NcTransferDto>($"اتصال DNC ماشین {machine.Title} تعریف یا فعال نشده است.");
            if (c.LinkKind == MfNcLinkKind.Manual) return Bad<NcTransferDto>("اتصال این ماشین دستی است؛ فایل را از کنترلر بردارید و به‌صورت نسخه تازه بارگذاری کنید.");
            if (!(await TargetsAsync(p, null, ct)).Any(t => t.MachineId == dto.MachineId)) return Bad<NcTransferDto>($"این برنامه برای ماشین {machine.Title} تعریف نشده است.");
            if (await _uow.Set<MfNcRevision>().AnyAsync(r => !r.Deleted && r.ProgramId == p.Id && (r.Status == MfNcRevisionStatus.Draft || r.Status == MfNcRevisionStatus.Submitted), ct))
                return Bad<NcTransferDto>("این برنامه نسخه باز دیگری دارد؛ ابتدا آن را تأیید، رد یا پس‌گرفته کنید.");
            if (await _uow.Set<MfNcTransfer>().AnyAsync(t => !t.Deleted && t.ProgramId == p.Id && t.Direction == MfNcTransferDirection.FromMachine
                    && (t.Status == MfNcTransferStatus.Queued || t.Status == MfNcTransferStatus.Sending), ct))
                return Bad<NcTransferDto>("درخواست دریافت دیگری برای این برنامه در جریان است.");
            string name = Clean(dto.ProgramName);
            if (name == null && p.CurrentRevisionId.HasValue)
            {
                MfNcRevision r = await _uow.Set<MfNcRevision>().AsNoTracking().FirstAsync(x => x.Id == p.CurrentRevisionId.Value, ct);
                name = MfNcRules.TargetName(c.ProgramNameFormat, c.FileExtension, r.ProgramNumber, p.Code, r.FileName).Name;
            }
            if (name == null || !ControlName.IsMatch(name) || !MfNcRules.IsNcFileName(name))
                return Bad<NcTransferDto>("نام برنامه روی کنترلر را درست وارد کنید (حروف لاتین، رقم، نقطه، خط تیره؛ حداکثر ۳۲ نویسه؛ با پسوند برنامه CNC یا بدون پسوند).");
            DateTimeOffset now = DateTimeOffset.Now;
            var transfer = new MfNcTransfer
            {
                Direction = MfNcTransferDirection.FromMachine, MachineId = dto.MachineId, ProgramId = p.Id, TargetName = name, LinkKind = c.LinkKind, Status = MfNcTransferStatus.Queued,
                RequestNote = Clean(dto.ChangeNote), RequestedByUserId = Me, RequestedAt = now, CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
            };
            _uow.Set<MfNcTransfer>().Add(transfer);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await TransferDtoAsync(transfer.Id, ct));
        }

        public async Task<ResponseDto<NcTransferDto>> TransferActionAsync(NcTransferActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Action)) return Bad<NcTransferDto>(MfText.InvalidData);
            if (IsAi) return Bad<NcTransferDto>(MfText.NotAllowed);
            string action = dto.Action.Trim().ToLowerInvariant();
            MfNcTransfer t = await _uow.Set<MfNcTransfer>().FirstOrDefaultAsync(x => x.Id == dto.TransferId && !x.Deleted, ct);
            if (t == null) return MfText.NotFound<NcTransferDto>("انتقال پیدا نشد.");
            if (!MfConcurrency.Apply(_uow, t, dto.RowVersion)) return Bad<NcTransferDto>(MfText.Concurrency);
            bool mine = t.RequestedByUserId == Me, supervisor = _user.IsInRole(ProductionRoles.Supervisors);
            DateTimeOffset now = DateTimeOffset.Now;
            switch (action)
            {
                case "confirm":
                {
                    if (!_user.IsInRole(DncRoles.Senders)) return Bad<NcTransferDto>(MfText.NotAllowed);
                    if (t.Direction != MfNcTransferDirection.ToMachine || t.Status != MfNcTransferStatus.Delivered) return Bad<NcTransferDto>("فقط برنامه تحویل‌شده به کنترلر تأیید می‌شود.");
                    MfNcProgram p = await _uow.Set<MfNcProgram>().AsNoTracking().FirstAsync(x => x.Id == t.ProgramId, ct);
                    if (p.CurrentRevisionId != t.RevisionId) return Bad<NcTransferDto>("پس از این ارسال نسخه تازه‌تری از برنامه تأیید شده است؛ نسخه جاری را دوباره ارسال کنید.");
                    t.Status = MfNcTransferStatus.Confirmed; t.ConfirmedByUserId = Me; t.ConfirmedAt = now; t.ConfirmNote = Clean(dto.Note);
                    break;
                }
                case "cancel":
                {
                    if (!(mine || supervisor)) return Bad<NcTransferDto>(MfText.NotAllowed);
                    bool cancellable = t.Status == MfNcTransferStatus.Queued || t.Status == MfNcTransferStatus.Failed || (t.Status == MfNcTransferStatus.Delivered && t.Direction == MfNcTransferDirection.ToMachine);
                    if (!cancellable) return Bad<NcTransferDto>("این انتقال قابل لغو نیست (وضعیت: " + DncText.TransferStatus(t.Status) + ").");
                    string reason = Clean(dto.Note);
                    if (reason == null) return Bad<NcTransferDto>("دلیل لغو را بنویسید.");
                    t.Status = MfNcTransferStatus.Cancelled; t.CancelledByUserId = Me; t.CancelledAt = now; t.CancelReason = reason.Length > 1000 ? reason.Substring(0, 1000) : reason;
                    break;
                }
                case "retry":
                    if (!(mine || supervisor)) return Bad<NcTransferDto>(MfText.NotAllowed);
                    if (t.Status != MfNcTransferStatus.Failed) return Bad<NcTransferDto>("فقط انتقال ناموفق دوباره تلاش می‌شود.");
                    if (t.Direction == MfNcTransferDirection.ToMachine && await StaleReasonAsync(t, ct) is string stale) return Bad<NcTransferDto>(stale + " به جای تلاش دوباره، نسخه جاری را ارسال کنید.");
                    t.Status = MfNcTransferStatus.Queued; t.Attempts = 0; t.NextAttemptAt = null;
                    break;
                default:
                    return Bad<NcTransferDto>(MfText.InvalidData);
            }
            t.ModifyDate = now; t.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<NcTransferDto>(MfText.Concurrency); }
            await DncGates.TransferFailedAsync(_notify, t, null, ct);
            return ResponseDtoHelper.Ok(await TransferDtoAsync(t.Id, ct));
        }

        /// <summary>
        /// Null while the transfer still carries the current approved revision of an active program; else the Persian reason (review finding 2: a late retry of an older revision must never write over the
        /// program the operator confirmed on the control).
        /// </summary>
        private async Task<string> StaleReasonAsync(MfNcTransfer t, CancellationToken ct)
        {
            if (t.Direction != MfNcTransferDirection.ToMachine) return null;
            var p = await _uow.Set<MfNcProgram>().AsNoTracking().Where(x => x.Id == t.ProgramId).Select(x => new { x.Deleted, x.Status, x.CurrentRevisionId }).FirstOrDefaultAsync(ct);
            if (p == null || p.Deleted || p.Status != MfNcProgramStatus.Active) return "برنامه این انتقال دیگر فعال نیست.";
            if (p.CurrentRevisionId != t.RevisionId) return "پس از درخواست این انتقال نسخه دیگری از برنامه جاری شده است.";
            return null;
        }

        /// <summary>Cancels a transfer the system will not deliver any more (no person pressed cancel: the reason says why).</summary>
        private static void CancelBySystem(MfNcTransfer t, string reason, DateTimeOffset now)
        {
            t.Status = MfNcTransferStatus.Cancelled; t.CancelledAt = now; t.CancelReason = "لغو خودکار: " + reason; t.NextAttemptAt = null; t.ModifyDate = now;
        }

        /// <summary>The bytes that go out: the stored content with the line endings of the machine.</summary>
        private static byte[] Outgoing(byte[] content, MfMachineConnection c) => MfNcRules.Normalise(content, c?.LineEnding ?? MfNcLineEnding.CrLf);

        public async Task<ResponseDto<NcFileDto>> GetTransferFileAsync(int transferId, CancellationToken ct)
        {
            if (!(_user.IsInRole(DncRoles.Senders) || _user.IsInRole(ProductionRoles.Supervisors)) || IsAi) return Bad<NcFileDto>(MfText.NotAllowed);
            MfNcTransfer t = await _uow.Set<MfNcTransfer>().FirstOrDefaultAsync(x => x.Id == transferId && !x.Deleted, ct);
            if (t == null) return MfText.NotFound<NcFileDto>("انتقال پیدا نشد.");
            if (t.Direction != MfNcTransferDirection.ToMachine || t.LinkKind != MfNcLinkKind.Manual || (t.Status != MfNcTransferStatus.Queued && t.Status != MfNcTransferStatus.Delivered))
                return Bad<NcFileDto>("فایل فقط برای انتقال دستیِ در جریان داده می‌شود.");
            if (await StaleReasonAsync(t, ct) is string stale) return Bad<NcFileDto>(stale);
            MfMachineConnection c = await _uow.Set<MfMachineConnection>().AsNoTracking().FirstOrDefaultAsync(x => x.MachineId == t.MachineId && !x.Deleted, ct);
            MfNcRevision r = await _uow.Set<MfNcRevision>().AsNoTracking().FirstAsync(x => x.Id == t.RevisionId, ct);
            byte[] content = await ContentAsync(r.Id, ct);
            if (MfNcRules.Sha256Hex(content) != r.Sha256) return Bad<NcFileDto>("محتوای ذخیره‌شده با اثر انگشت نسخه نمی‌خواند.");
            byte[] bytes = Outgoing(content, c);
            if (t.Status == MfNcTransferStatus.Queued)
            {
                DateTimeOffset now = DateTimeOffset.Now;
                t.Status = MfNcTransferStatus.Delivered; t.DeliveredAt = now; t.SentSha256 = MfNcRules.Sha256Hex(bytes); t.SentBytes = bytes.Length; t.Attempts++; t.ModifyDate = now; t.ModifierUserId = Me;
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException) { return Bad<NcFileDto>(MfText.Concurrency); }
            }
            return ResponseDtoHelper.Ok(new NcFileDto { FileName = t.TargetName, Bytes = bytes, Sha256 = t.SentSha256 });
        }

        public async Task<ResponseDto<NcRunPendingResultDto>> RunPendingAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Supervisors) || IsAi) return Bad<NcRunPendingResultDto>(MfText.NotAllowed);
            return ResponseDtoHelper.Ok(await ProcessPendingAsync(ct));
        }

        private static bool ServerDriven(MfNcLinkKind kind) => kind == MfNcLinkKind.Ftp || kind == MfNcLinkKind.NetworkFolder || kind == MfNcLinkKind.SerialGateway;

        public Task<NcRunPendingResultDto> ProcessPendingAsync(CancellationToken ct) => ProcessPendingAsync(null, ct);

        /// <summary>The machines with server-driven transfers waiting (the background job works each of them on its own, one transfer at a time per machine: review finding 6).</summary>
        public async Task<List<int>> PendingMachinesAsync(CancellationToken ct)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            return await _uow.Set<MfNcTransfer>().AsNoTracking()
                .Where(t => !t.Deleted && (t.LinkKind == MfNcLinkKind.Ftp || t.LinkKind == MfNcLinkKind.NetworkFolder || t.LinkKind == MfNcLinkKind.SerialGateway)
                    && (t.Status == MfNcTransferStatus.Queued || t.Status == MfNcTransferStatus.Sending
                        || (t.Status == MfNcTransferStatus.Failed && t.Attempts < MaxAttempts && t.NextAttemptAt != null && t.NextAttemptAt <= now)))
                .Select(t => t.MachineId).Distinct().ToListAsync(ct);
        }

        /// <summary>
        /// How long a transfer may stay "sending" before it counts as left in the middle: ten minutes, plus for a serial line the time the bytes need at its baud rate (10 bits a byte), so a long program at
        /// 9600 baud is never declared stale and sent a second time while it is still on the wire (review finding 6).
        /// </summary>
        public static TimeSpan AllowedSending(MfNcLinkKind kind, long bytes, int? baud)
        {
            if (kind != MfNcLinkKind.SerialGateway) return StaleSending;
            int rate = baud is > 0 ? baud.Value : 9600;
            return StaleSending + TimeSpan.FromSeconds(Math.Max(0, bytes) * 10.0 / rate * 1.25);
        }

        public async Task<NcRunPendingResultDto> ProcessPendingAsync(int? machineId, CancellationToken ct)
        {
            var result = new NcRunPendingResultDto();
            DateTimeOffset now = DateTimeOffset.Now, stale = now - StaleSending;
            // a transfer the server left in the middle (it stopped) is failed, so it is retried or seen by a person
            List<MfNcTransfer> stuck = await _uow.Set<MfNcTransfer>().Where(t => !t.Deleted && t.Status == MfNcTransferStatus.Sending && t.ModifyDate < stale
                && (machineId == null || t.MachineId == machineId)
                && (t.LinkKind == MfNcLinkKind.Ftp || t.LinkKind == MfNcLinkKind.NetworkFolder || t.LinkKind == MfNcLinkKind.SerialGateway)).ToListAsync(ct);
            bool anyStuck = false;
            foreach (MfNcTransfer t in stuck)
            {
                MfMachineConnection sc = await _uow.Set<MfMachineConnection>().AsNoTracking().FirstOrDefaultAsync(x => x.MachineId == t.MachineId && !x.Deleted, ct);
                long size = t.Direction == MfNcTransferDirection.ToMachine && t.RevisionId.HasValue
                    ? await _uow.Set<MfNcRevision>().AsNoTracking().Where(r => r.Id == t.RevisionId.Value).Select(r => (long)r.SizeBytes).FirstOrDefaultAsync(ct)
                    : sc?.MaxProgramBytes ?? MfNcRules.GlobalMaxBytes;
                if (now - t.ModifyDate < AllowedSending(t.LinkKind, size, sc?.BaudRate)) continue;
                Fail(t, "انتقال نیمه‌کاره ماند (سرور در میانه کار متوقف شد).", now);
                anyStuck = true;
            }
            if (anyStuck) await SaveQuietAsync(ct);
            else if (stuck.Count > 0) ClearTracker();

            List<int> ids = await _uow.Set<MfNcTransfer>().AsNoTracking()
                .Where(t => !t.Deleted && (t.LinkKind == MfNcLinkKind.Ftp || t.LinkKind == MfNcLinkKind.NetworkFolder || t.LinkKind == MfNcLinkKind.SerialGateway)
                    && (machineId == null || t.MachineId == machineId)
                    && (t.Status == MfNcTransferStatus.Queued || (t.Status == MfNcTransferStatus.Failed && t.Attempts < MaxAttempts && t.NextAttemptAt != null && t.NextAttemptAt <= now)))
                .OrderBy(t => t.Id).Select(t => t.Id).Take(20).ToListAsync(ct);
            foreach (int id in ids)
            {
                ct.ThrowIfCancellationRequested();
                MfNcTransfer t = await _uow.Set<MfNcTransfer>().FirstOrDefaultAsync(x => x.Id == id, ct);
                if (t == null || !(t.Status == MfNcTransferStatus.Queued || t.Status == MfNcTransferStatus.Failed)) continue;
                // one transfer in flight per machine: while another is on its way to (or from) this machine, the next one waits
                if (await _uow.Set<MfNcTransfer>().AsNoTracking().AnyAsync(x => !x.Deleted && x.Id != id && x.MachineId == t.MachineId && x.Status == MfNcTransferStatus.Sending, ct)) continue;
                // claim the row: a second worker (or a person pressing "run") gets a concurrency error and leaves it
                t.Status = MfNcTransferStatus.Sending; t.Attempts++; t.ModifyDate = DateTimeOffset.Now;
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateException) { ClearTracker(); continue; }
                result.Processed++;
                MfMachineConnection c = await _uow.Set<MfMachineConnection>().AsNoTracking().FirstOrDefaultAsync(x => x.MachineId == t.MachineId && !x.Deleted, ct);
                string title = await _uow.Set<Machine>().AsNoTracking().Where(m => m.Id == t.MachineId).Select(m => m.Title).FirstOrDefaultAsync(ct);
                IMfNcLink link = c != null && c.IsActive && c.LinkKind == t.LinkKind ? _links.For(c.LinkKind) : null;
                if (link == null)
                {
                    Fail(t, "اتصال ماشین از زمان درخواست تغییر کرده یا غیرفعال شده است.", DateTimeOffset.Now, final: true);
                    result.Failed++;
                }
                else if (t.Direction == MfNcTransferDirection.ToMachine) await DeliverAsync(t, c, link, result, ct);
                else await FetchAsync(t, c, link, result, ct);
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateException ex)
                {
                    // review finding 5: nothing of the failed save stays tracked, and the row is closed for a person (never left "sending" to be retried and delivered a second time)
                    result.Messages.Add("انتقال " + id + ": " + ex.GetBaseException().Message);
                    ClearTracker();
                    MfNcTransfer again = await _uow.Set<MfNcTransfer>().FirstOrDefaultAsync(x => x.Id == id, ct);
                    if (again != null && again.Status == MfNcTransferStatus.Sending)
                    {
                        Fail(again, "نتیجه انتقال ذخیره نشد؛ پیش از تلاش دوباره وضعیت برنامه روی کنترلر را بررسی کنید.", DateTimeOffset.Now, final: true);
                        result.Failed++;
                        await SaveQuietAsync(ct);
                    }
                    continue;
                }
                if (t.Status == MfNcTransferStatus.Failed) await DncGates.TransferFailedAsync(_notify, t, title, ct);
                if (t.Direction == MfNcTransferDirection.FromMachine && t.Status == MfNcTransferStatus.Received && t.RevisionId.HasValue)
                {
                    MfNcRevision made = await _uow.Set<MfNcRevision>().AsNoTracking().FirstAsync(x => x.Id == t.RevisionId.Value, ct);
                    string code = await _uow.Set<MfNcProgram>().AsNoTracking().Where(x => x.Id == made.ProgramId).Select(x => x.Code).FirstAsync(ct);
                    result.Messages.Add($"برنامه {code} از ماشین {title} دریافت شد: نسخه پیش‌نویس {made.Revision}.");
                }
            }
            return result;
        }

        private static void Fail(MfNcTransfer t, string error, DateTimeOffset now, bool final = false)
        {
            t.Status = MfNcTransferStatus.Failed;
            t.LastError = error != null && error.Length > 1000 ? error.Substring(0, 1000) : error;
            if (final) t.Attempts = Math.Max(t.Attempts, MaxAttempts);
            t.NextAttemptAt = t.Attempts < MaxAttempts ? now.AddMinutes(1) : null;
            t.ModifyDate = now;
        }

        private async Task DeliverAsync(MfNcTransfer t, MfMachineConnection c, IMfNcLink link, NcRunPendingResultDto result, CancellationToken ct)
        {
            if (await StaleReasonAsync(t, ct) is string stale)
            {
                CancelBySystem(t, stale, DateTimeOffset.Now);
                result.Messages.Add("انتقال " + t.Id + ": " + stale);
                return;
            }
            MfNcRevision r = t.RevisionId.HasValue ? await _uow.Set<MfNcRevision>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == t.RevisionId.Value, ct) : null;
            byte[] content = r == null ? null : await ContentAsync(r.Id, ct);
            if (r == null || content == null || MfNcRules.Sha256Hex(content) != r.Sha256)
            {
                Fail(t, "محتوای نسخه با اثر انگشت آن نمی‌خواند؛ ارسال انجام نشد.", DateTimeOffset.Now, final: true);
                result.Failed++;
                return;
            }
            byte[] bytes = Outgoing(content, c);
            NcLinkResult sent = await link.SendAsync(TargetOf(c), t.TargetName, bytes, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            if (!sent.Ok)
            {
                Fail(t, sent.Message, now);
                result.Failed++;
                return;
            }
            t.Status = MfNcTransferStatus.Delivered; t.DeliveredAt = now; t.SentSha256 = MfNcRules.Sha256Hex(bytes); t.SentBytes = bytes.Length; t.LastError = null; t.NextAttemptAt = null; t.ModifyDate = now;
            result.Delivered++;
        }

        private async Task FetchAsync(MfNcTransfer t, MfMachineConnection c, IMfNcLink link, NcRunPendingResultDto result, CancellationToken ct)
        {
            NcLinkResult got = await link.ReceiveAsync(TargetOf(c), t.TargetName, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            if (!got.Ok)
            {
                Fail(t, got.Message, now);
                result.Failed++;
                return;
            }
            string error = await AcceptFromMachineAsync(t, got.Bytes, ct);
            if (error != null)
            {
                Fail(t, error, now, final: true);
                result.Failed++;
                return;
            }
            result.Received++;
        }

        /// <summary>Makes the draft revision from what came back from a machine (author: the person who asked for it, so he cannot approve it). Null or the Persian reason.</summary>
        private async Task<string> AcceptFromMachineAsync(MfNcTransfer t, byte[] bytes, CancellationToken ct)
        {
            MfNcProgram p = await _uow.Set<MfNcProgram>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == t.ProgramId && !x.Deleted, ct);
            if (p == null || p.Status != MfNcProgramStatus.Active) return "برنامه دیگر فعال نیست.";
            string fileName = SafeFileName(t.TargetName) ?? ("machine-" + t.MachineId.ToString(CultureInfo.InvariantCulture) + ".nc");
            var made = await NewRevisionAsync(p, fileName, bytes, t.RequestNote ?? "دریافت از ماشین", MfNcRevisionSource.FromMachine, t.Id, t.RequestedByUserId, ct);
            if (made.Error != null) return made.Error;
            _uow.Set<MfNcContent>().Add(new MfNcContent { Revision = made.Revision, Bytes = bytes });
            DateTimeOffset now = DateTimeOffset.Now;
            t.Status = MfNcTransferStatus.Received; t.Revision = made.Revision; t.SentSha256 = made.Revision.Sha256; t.SentBytes = bytes.Length; t.DeliveredAt = now; t.LastError = null; t.NextAttemptAt = null; t.ModifyDate = now;
            return null;
        }

        // ================================================================================== runs and terminal

        public async Task<ResponseDto<PagedList<NcRunDto>>> GetRunsAsync(NcRunQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<PagedList<NcRunDto>>(MfText.NotAllowed);
            query ??= new NcRunQuery();
            IQueryable<MfNcRun> q = _uow.Set<MfNcRun>().AsNoTracking();
            if (query.OperationId.HasValue) q = q.Where(r => r.OperationId == query.OperationId.Value);
            if (query.OrderId.HasValue) { int order = query.OrderId.Value; q = q.Where(r => _uow.Set<MfOperation>().Any(o => o.Id == r.OperationId && o.OrderId == order)); }
            if (query.MachineId.HasValue) q = q.Where(r => r.MachineId == query.MachineId.Value);
            if (query.ProgramId.HasValue) q = q.Where(r => r.ProgramId == query.ProgramId.Value);
            if (query.RevisionId.HasValue) q = q.Where(r => r.RevisionId == query.RevisionId.Value);
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize), number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MfNcRun> rows = await q.OrderByDescending(r => r.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            Dictionary<int, string> machines = await MachineTitlesAsync(rows.Select(r => r.MachineId), ct);
            List<int> programIds = rows.Select(r => r.ProgramId).Distinct().ToList(), revisionIds = rows.Select(r => r.RevisionId).Distinct().ToList(), opIds = rows.Select(r => r.OperationId).Distinct().ToList();
            Dictionary<int, string> programs = await _uow.Set<MfNcProgram>().AsNoTracking().Where(p => programIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
            var revisions = (await _uow.Set<MfNcRevision>().AsNoTracking().Where(r => revisionIds.Contains(r.Id)).Select(r => new { r.Id, r.Revision, r.Sha256 }).ToListAsync(ct)).ToDictionary(r => r.Id);
            var ops = (await _uow.Set<MfOperation>().AsNoTracking().Where(o => opIds.Contains(o.Id))
                .Join(_uow.Set<MfProductionOrder>().AsNoTracking(), o => o.OrderId, d => d.Id, (o, d) => new { o.Id, d.Number, o.Sequence, o.Title }).ToListAsync(ct))
                .ToDictionary(o => o.Id, o => MfNames.Operation(o.Number, o.Sequence) + " " + o.Title);
            bool names = MfPrivacy.SeesNames(_user);
            Dictionary<int, string> employees = new Dictionary<int, string>();
            if (names)
            {
                List<int> empIds = rows.Where(r => r.EmployeeId.HasValue).Select(r => r.EmployeeId.Value).Distinct().ToList();
                employees = MfNames.ToDictionary(await _uow.Set<FnEmployee>().AsNoTracking().Where(e => empIds.Contains(e.Id)).ToListAsync(ct));
            }
            List<NcRunDto> dtos = rows.Select(r => new NcRunDto
            {
                Id = r.Id, OperationId = r.OperationId, OperationName = ops.TryGetValue(r.OperationId, out string on) ? on : null, MachineId = r.MachineId,
                MachineTitle = machines.TryGetValue(r.MachineId, out string mt) ? mt : null, ProgramId = r.ProgramId, ProgramCode = programs.TryGetValue(r.ProgramId, out string pc) ? pc : null,
                RevisionId = r.RevisionId, Revision = revisions.TryGetValue(r.RevisionId, out var rv) ? rv.Revision : 0, Sha256 = revisions.TryGetValue(r.RevisionId, out var rv2) ? rv2.Sha256 : null,
                TransferId = r.TransferId, EmployeeName = names && r.EmployeeId.HasValue && employees.TryGetValue(r.EmployeeId.Value, out string en) ? en : null, Kind = r.Kind,
                StartedAtText = MfText.Stamp(r.StartedAt),
            }).ToList();
            return ResponseDtoHelper.Ok(Page(dtos, total, number, size));
        }

        public async Task<ResponseDto<TerminalNcDto>> GetTerminalAsync(TerminalNcQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(DncRoles.Readers)) return Bad<TerminalNcDto>(MfText.NotAllowed);
            if (query == null) return Bad<TerminalNcDto>(MfText.InvalidData);
            MfOperation op = await _uow.Set<MfOperation>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == query.OperationId && !o.Deleted, ct);
            if (op == null) return MfText.NotFound<TerminalNcDto>("عملیات پیدا نشد.");
            var dto = new TerminalNcDto { OperationId = op.Id, MachineId = query.MachineId, CanStart = true };
            MfNcProgram p = await MfDncGate.ProgramForAsync(_uow, op, query.MachineId, ct);
            if (p == null) return ResponseDtoHelper.Ok(dto);
            dto.HasProgram = true; dto.ProgramId = p.Id; dto.ProgramCode = p.Code; dto.ProgramTitle = p.Title; dto.CurrentRevisionId = p.CurrentRevisionId;
            if (p.CurrentRevisionId.HasValue)
                dto.CurrentRevision = await _uow.Set<MfNcRevision>().AsNoTracking().Where(r => r.Id == p.CurrentRevisionId.Value).Select(r => (int?)r.Revision).FirstOrDefaultAsync(ct);
            MfMachineConnection c = await _uow.Set<MfMachineConnection>().AsNoTracking().FirstOrDefaultAsync(x => x.MachineId == query.MachineId && !x.Deleted, ct);
            dto.Required = c != null && c.IsActive && c.RequireConfirmedProgram;
            var gate = await MfDncGate.CheckStartAsync(_uow, op, query.MachineId, MfBookingKind.Run, ct);
            dto.CanStart = gate.Error == null;
            dto.Problem = gate.Error;
            // review finding 11: the problem text and "can start" never disagree; a program without an approved revision on a machine that does not require one is shown, not blocking
            if (dto.Problem == null && !p.CurrentRevisionId.HasValue && dto.Required) dto.Problem = "این برنامه هنوز نسخه تأییدشده ندارد.";
            MfNcTransfer last = await _uow.Set<MfNcTransfer>().AsNoTracking()
                .Where(t => !t.Deleted && t.MachineId == query.MachineId && t.ProgramId == p.Id && t.Direction == MfNcTransferDirection.ToMachine).OrderByDescending(t => t.Id).FirstOrDefaultAsync(ct);
            if (last != null) dto.LastTransfer = (await TransferDtosAsync(new List<MfNcTransfer> { last }, ct))[0];
            return ResponseDtoHelper.Ok(dto);
        }

        // ================================================================================== edge gateway

        private static ResponseDto<T> Unauthorized<T>() => ResponseDtoHelper.Unauthorized<T>("کلید یا نشانی گیت‌وی پذیرفته نشد.");

        private async Task<MfMachineConnection> GatewayAsync(string machine, string key, string remote, CancellationToken ct)
        {
            if (!int.TryParse(machine, NumberStyles.None, CultureInfo.InvariantCulture, out int machineId) || string.IsNullOrEmpty(key)) return null;
            MfMachineConnection c = await _uow.Set<MfMachineConnection>().FirstOrDefaultAsync(x => x.MachineId == machineId && !x.Deleted, ct);
            if (c == null || !c.IsActive || c.LinkKind != MfNcLinkKind.EdgeGateway || string.IsNullOrEmpty(c.GatewayKeyHash)) return null;
            byte[] given = Encoding.UTF8.GetBytes(HashKey(key)), stored = Encoding.UTF8.GetBytes(c.GatewayKeyHash);
            if (given.Length != stored.Length || !CryptographicOperations.FixedTimeEquals(given, stored)) return null;
            if (!DeviceAddress.Allows(c.AllowedAddresses, remote)) return null;
            await TouchAsync(c, ct);
            return c;
        }

        /// <summary>
        /// Writes the last-seen time of a gateway without touching the row version of the link (review finding 7: a poll every few seconds must not turn every save of the link editor into a concurrency
        /// error). SQL Server: one UPDATE of that column; the in-memory test database: the tracked row.
        /// </summary>
        private async Task TouchAsync(MfMachineConnection c, CancellationToken ct)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            if (_uow is DbContext db && db.Database.IsRelational())
            {
                int id = c.Id;
                await db.Set<MfMachineConnection>().Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LastSeenAt, now), ct);
                return;
            }
            c.LastSeenAt = now;
            await SaveQuietAsync(ct);
        }

        public async Task<ResponseDto<List<GatewayJobDto>>> GatewayJobsAsync(string machine, string key, string remote, CancellationToken ct)
        {
            MfMachineConnection c = await GatewayAsync(machine, key, remote, ct);
            if (c == null) return Unauthorized<List<GatewayJobDto>>();
            var rows = await _uow.Set<MfNcTransfer>().AsNoTracking()
                .Where(t => !t.Deleted && t.MachineId == c.MachineId && t.LinkKind == MfNcLinkKind.EdgeGateway && (t.Status == MfNcTransferStatus.Queued || t.Status == MfNcTransferStatus.Sending))
                .OrderBy(t => t.Id).Take(20).ToListAsync(ct);
            var jobs = new List<GatewayJobDto>();
            bool cancelled = false;
            foreach (MfNcTransfer t in rows)
            {
                if (await StaleReasonAsync(t, ct) is string stale)
                {
                    MfNcTransfer row = await _uow.Set<MfNcTransfer>().FirstAsync(x => x.Id == t.Id, ct);
                    CancelBySystem(row, stale, DateTimeOffset.Now);
                    cancelled = true;
                    continue;
                }
                var job = new GatewayJobDto { TransferId = t.Id, Direction = t.Direction, TargetName = t.TargetName };
                if (t.Direction == MfNcTransferDirection.ToMachine && t.RevisionId.HasValue)
                {
                    byte[] bytes = Outgoing(await ContentAsync(t.RevisionId.Value, ct), c);
                    job.SizeBytes = bytes.Length;
                    job.Sha256 = MfNcRules.Sha256Hex(bytes);
                }
                jobs.Add(job);
            }
            if (cancelled) await SaveQuietAsync(ct);
            return ResponseDtoHelper.Ok(jobs);
        }

        private async Task<(MfNcTransfer Transfer, MfMachineConnection Connection, ResponseDto<T> Refusal)> GatewayTransferAsync<T>(string machine, string key, string remote, int transferId, CancellationToken ct)
        {
            MfMachineConnection c = await GatewayAsync(machine, key, remote, ct);
            if (c == null) return (null, null, Unauthorized<T>());
            MfNcTransfer t = await _uow.Set<MfNcTransfer>().FirstOrDefaultAsync(x => x.Id == transferId && !x.Deleted && x.MachineId == c.MachineId && x.LinkKind == MfNcLinkKind.EdgeGateway, ct);
            if (t == null) return (null, c, MfText.NotFound<T>("کار پیدا نشد."));
            return (t, c, null);
        }

        public async Task<ResponseDto<NcFileDto>> GatewayContentAsync(string machine, string key, string remote, int transferId, CancellationToken ct)
        {
            var g = await GatewayTransferAsync<NcFileDto>(machine, key, remote, transferId, ct);
            if (g.Refusal != null) return g.Refusal;
            MfNcTransfer t = g.Transfer;
            if (t.Direction != MfNcTransferDirection.ToMachine || (t.Status != MfNcTransferStatus.Queued && t.Status != MfNcTransferStatus.Sending)) return Bad<NcFileDto>("این کار ارسالی در جریان نیست.");
            if (await StaleReasonAsync(t, ct) is string stale)
            {
                CancelBySystem(t, stale, DateTimeOffset.Now);
                await SaveQuietAsync(ct);
                return Bad<NcFileDto>(stale + " این کار لغو شد.");
            }
            MfNcRevision r = await _uow.Set<MfNcRevision>().AsNoTracking().FirstAsync(x => x.Id == t.RevisionId, ct);
            byte[] content = await ContentAsync(r.Id, ct);
            if (MfNcRules.Sha256Hex(content) != r.Sha256) return Bad<NcFileDto>("محتوای نسخه با اثر انگشت آن نمی‌خواند.");
            byte[] bytes = Outgoing(content, g.Connection);
            DateTimeOffset now = DateTimeOffset.Now;
            if (t.Status == MfNcTransferStatus.Queued) { t.Status = MfNcTransferStatus.Sending; t.Attempts++; }
            t.SentSha256 = MfNcRules.Sha256Hex(bytes); t.SentBytes = bytes.Length; t.ModifyDate = now;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<NcFileDto>(MfText.Concurrency); }
            return ResponseDtoHelper.Ok(new NcFileDto { FileName = t.TargetName, Bytes = bytes, Sha256 = t.SentSha256 });
        }

        public async Task<ResponseDto<NcTransferDto>> GatewayResultAsync(string machine, string key, string remote, GatewayResultDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<NcTransferDto>(MfText.InvalidData);
            var g = await GatewayTransferAsync<NcTransferDto>(machine, key, remote, dto.TransferId, ct);
            if (g.Refusal != null) return g.Refusal;
            MfNcTransfer t = g.Transfer;
            if (t.Direction != MfNcTransferDirection.ToMachine || t.Status != MfNcTransferStatus.Sending) return Bad<NcTransferDto>("این کار ارسالی در جریان نیست (ابتدا محتوا را بگیرید).");
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Success) { t.Status = MfNcTransferStatus.Delivered; t.DeliveredAt = now; t.LastError = null; t.ModifyDate = now; }
            else Fail(t, Clean(dto.Error) ?? "گیت‌وی خطا گزارش کرد.", now, final: true);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<NcTransferDto>(MfText.Concurrency); }
            if (t.Status == MfNcTransferStatus.Failed) await DncGates.TransferFailedAsync(_notify, t, null, ct);
            return ResponseDtoHelper.Ok(await TransferDtoAsync(t.Id, ct));
        }

        public async Task<ResponseDto<NcTransferDto>> GatewayUploadAsync(string machine, string key, string remote, GatewayUploadDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.ContentBase64)) return Bad<NcTransferDto>(MfText.InvalidData);
            var g = await GatewayTransferAsync<NcTransferDto>(machine, key, remote, dto.TransferId, ct);
            if (g.Refusal != null) return g.Refusal;
            MfNcTransfer t = g.Transfer;
            if (t.Direction != MfNcTransferDirection.FromMachine || (t.Status != MfNcTransferStatus.Queued && t.Status != MfNcTransferStatus.Sending)) return Bad<NcTransferDto>("درخواست دریافتی در جریان نیست.");
            if (dto.ContentBase64.Length > (MfNcRules.GlobalMaxBytes / 3 + 1) * 4 + 8) return Bad<NcTransferDto>("حجم فایل بیش از ۴ مگابایت است.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(dto.ContentBase64.Trim()); }
            catch (FormatException) { return Bad<NcTransferDto>("محتوای فایل به‌درستی ارسال نشده است."); }
            t.Attempts++;
            string error = await AcceptFromMachineAsync(t, bytes, ct);
            if (error != null) Fail(t, error, DateTimeOffset.Now, final: true);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<NcTransferDto>("نسخه هم‌زمان توسط کاربر دیگری ثبت شد."); }
            if (error != null) return Bad<NcTransferDto>(error);
            return ResponseDtoHelper.Ok(await TransferDtoAsync(t.Id, ct));
        }
    }
}
