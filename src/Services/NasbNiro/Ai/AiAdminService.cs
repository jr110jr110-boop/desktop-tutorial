using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Ai;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.AiDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Ai
{
    public interface IAiAdminService
    {
        Task<ResponseDto<List<AiEndpointDto>>> GetEndpointsAsync(CancellationToken ct);
        Task<ResponseDto<AiEndpointDto>> SaveEndpointAsync(AiSaveEndpointDto dto, CancellationToken ct);

        /// <summary>Pings every enabled endpoint now and records the health on the rows.</summary>
        Task<ResponseDto<List<AiEndpointDto>>> HealthCheckAsync(CancellationToken ct);

        Task<ResponseDto<List<AiPromptVersionDto>>> GetPromptsAsync(string code, CancellationToken ct);
        Task<ResponseDto<AiPromptVersionDto>> NewPromptAsync(AiNewPromptDto dto, CancellationToken ct);
        Task<ResponseDto<AiPromptVersionDto>> ActivatePromptAsync(AiActivatePromptDto dto, CancellationToken ct);
        Task<ResponseDto<PagedList<AiAuditEventDto>>> GetAuditAsync(AiAuditQuery query, CancellationToken ct);
        Task<ResponseDto<AiIndexRebuildResultDto>> RebuildIndexAsync(CancellationToken ct);
    }

    /// <summary>Endpoints, prompt versions, the audit and the index. Writes need PMO, reads PMO or CEO. Every write is audited (kind admin).</summary>
    public class AiAdminService : IAiAdminService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IAiAudit _audit;
        private readonly IHttpClientFactory _http;
        private readonly IRetrievalService _retrieval;

        public AiAdminService(IUnitOfWork uow, IInventoryUserContext user, IAiAudit audit, IHttpClientFactory http, IRetrievalService retrieval)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _retrieval = retrieval ?? throw new ArgumentNullException(nameof(retrieval));
        }

        private int Me => _user.UserId;

        private static AiEndpointDto ToDto(AiModelEndpoint e) => new AiEndpointDto
        {
            Id = e.Id, Name = e.Name, Kind = e.Kind, Url = e.Url, ModelName = e.ModelName, Priority = e.Priority, Enabled = e.Enabled, TimeoutSeconds = e.TimeoutSeconds,
            LastHealthAt = e.LastHealthAt, LastHealthOk = e.LastHealthOk, LastError = e.LastError,
        };

        private static AiPromptVersionDto ToDto(AiPromptVersion p) => new AiPromptVersionDto
        {
            Id = p.Id, Code = p.Code, Version = p.Version, Text = p.Text, IsActive = p.IsActive, CreatedAt = p.CreatedAt, CreatedByUserId = p.CreatedByUserId,
            ActivatedAt = p.ActivatedAt, ActivatedByUserId = p.ActivatedByUserId,
        };

        /// <summary>http or https, a host, no user name or password, no query or fragment. Returns the cleaned URL or null.</summary>
        public static string CleanUrl(string url) => CleanUrl(url, out _);

        /// <summary>
        /// As above, and the host must be on the company's own network (the module works offline): a loopback address, a private address (10/8, 172.16/12, 192.168/16, fc00::/7),
        /// a single-label name (no dot) or a name ending in .local. <paramref name="external"/> is true when only that last rule refused the address.
        /// </summary>
        public static string CleanUrl(string url, out bool external)
        {
            external = false;
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
            if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || string.IsNullOrEmpty(uri.Host)) return null;
            if (!IsInternalHost(uri))
            {
                external = true;
                return null;
            }
            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }

        public static bool IsInternalHost(Uri uri)
        {
            string host = uri.Host.Trim('[', ']');
            if (System.Net.IPAddress.TryParse(host, out System.Net.IPAddress ip))
            {
                if (System.Net.IPAddress.IsLoopback(ip)) return true;
                byte[] b = ip.GetAddressBytes();
                if (b.Length == 4) return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
                return (b[0] & 0xFE) == 0xFC;
            }
            return !host.Contains('.') || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------------------------------ endpoints

        public async Task<ResponseDto<List<AiEndpointDto>>> GetEndpointsAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(AiRoles.Readers)) return AiText.Bad<List<AiEndpointDto>>(AiText.NotAllowed);
            List<AiModelEndpoint> list = await _uow.Set<AiModelEndpoint>().AsNoTracking().OrderBy(e => e.Kind).ThenBy(e => e.Priority).ThenBy(e => e.Id).ToListAsync(ct);
            return ResponseDtoHelper.Ok(list.Select(ToDto).ToList());
        }

        public async Task<ResponseDto<AiEndpointDto>> SaveEndpointAsync(AiSaveEndpointDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(AiRoles.Admins)) return AiText.Bad<AiEndpointDto>(AiText.NotAllowed);
            if (dto == null || AiText.Clean(dto.Name) == null || AiText.Clean(dto.ModelName) == null) return AiText.Bad<AiEndpointDto>(AiText.InvalidData);
            if (!Enum.IsDefined(typeof(AiEndpointKind), dto.Kind)) return AiText.Bad<AiEndpointDto>(AiText.InvalidData);
            string url = CleanUrl(dto.Url, out bool external);
            if (external) return AiText.Bad<AiEndpointDto>("نشانی باید به شبکه‌ی داخلی شرکت اشاره کند: آدرس loopback یا خصوصی (10، 172.16 تا 172.31، 192.168)، نام بدون نقطه یا نام با پسوند local.");
            if (url == null) return AiText.Bad<AiEndpointDto>("نشانی باید با http یا https شروع شود، میزبان داشته باشد و نام کاربری، گذرواژه، پرس‌وجو یا بخش # نداشته باشد.");
            if (dto.Name.Trim().Length > 200 || dto.ModelName.Trim().Length > 200 || url.Length > 1000) return AiText.Bad<AiEndpointDto>("نام، نام مدل یا نشانی بیش از حد طولانی است.");
            if (dto.Priority < 0 || dto.Priority > 1000) return AiText.Bad<AiEndpointDto>("اولویت باید بین ۰ و ۱۰۰۰ باشد.");
            if (dto.TimeoutSeconds < 1 || dto.TimeoutSeconds > 600) return AiText.Bad<AiEndpointDto>("مهلت پاسخ باید بین ۱ و ۶۰۰ ثانیه باشد.");

            AiModelEndpoint e;
            if (dto.Id.HasValue)
            {
                e = await _uow.Set<AiModelEndpoint>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (e == null) return AiText.NotFound<AiEndpointDto>("نقطه‌ی اتصال پیدا نشد.");
            }
            else
            {
                e = new AiModelEndpoint();
                _uow.Set<AiModelEndpoint>().Add(e);
            }
            bool moved = e.Url != url || e.ModelName != dto.ModelName.Trim() || e.Kind != dto.Kind;
            e.Name = dto.Name.Trim();
            e.Kind = dto.Kind;
            e.Url = url;
            e.ModelName = dto.ModelName.Trim();
            e.Priority = dto.Priority;
            e.Enabled = dto.Enabled;
            e.TimeoutSeconds = dto.TimeoutSeconds;
            if (moved)
            {
                // the old health says nothing about a new address
                e.LastHealthAt = null;
                e.LastHealthOk = null;
                e.LastError = null;
            }
            _audit.Add(AiAuditKinds.Admin, $"endpoint {(dto.Id.HasValue ? "changed" : "added")}: {e.Name}, {e.Kind}, {e.Url}, model {e.ModelName}, priority {e.Priority}, enabled {e.Enabled.ToString().ToLowerInvariant()}, timeout {e.TimeoutSeconds}");
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(e));
        }

        public async Task<ResponseDto<List<AiEndpointDto>>> HealthCheckAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(AiRoles.Admins)) return AiText.Bad<List<AiEndpointDto>>(AiText.NotAllowed);
            List<AiModelEndpoint> endpoints = await _uow.Set<AiModelEndpoint>().Where(e => e.Enabled).OrderBy(e => e.Kind).ThenBy(e => e.Priority).ToListAsync(ct);
            foreach (AiModelEndpoint e in endpoints)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, e.TimeoutSeconds)));
                try
                {
                    HttpClient client = _http.CreateClient(AiEndpointRunner.ClientName);
                    switch (e.Kind)
                    {
                        case AiEndpointKind.Chat:
                            var chat = new JsonObject { ["model"] = e.ModelName, ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "ping" }), ["temperature"] = 0, ["max_tokens"] = 1 };
                            LlmClient.Parse(await AiEndpointRunner.PostJsonAsync(client, AiEndpointRunner.Join(e, "/v1/chat/completions"), chat.ToJsonString(), timeout.Token));
                            break;
                        case AiEndpointKind.Embeddings:
                            var emb = new JsonObject { ["model"] = e.ModelName, ["input"] = new JsonArray("ping") };
                            await AiEndpointRunner.PostJsonAsync(client, AiEndpointRunner.Join(e, "/v1/embeddings"), emb.ToJsonString(), timeout.Token);
                            break;
                        default:
                            // speech servers: reachable is enough (any answer below 500 from /health)
                            using (HttpResponseMessage response = await client.GetAsync(AiEndpointRunner.Join(e, "/health"), timeout.Token))
                                if ((int)response.StatusCode >= 500) throw new AiEndpointException("HTTP " + (int)response.StatusCode);
                            break;
                    }
                    e.LastHealthOk = true;
                    e.LastError = null;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is AiEndpointException || ex is System.Text.Json.JsonException || ex is InvalidOperationException || ex is KeyNotFoundException)
                {
                    e.LastHealthOk = false;
                    e.LastError = AiText.Cut(ex is OperationCanceledException ? "timeout" : ex.Message, 1000);
                }
                e.LastHealthAt = DateTimeOffset.Now;
            }
            _audit.Add(AiAuditKinds.Admin, $"health check: {endpoints.Count(x => x.LastHealthOk == true)} ok, {endpoints.Count(x => x.LastHealthOk == false)} failed");
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(endpoints.Select(ToDto).ToList());
        }

        // ------------------------------------------------------------------------------------------ prompts

        public async Task<ResponseDto<List<AiPromptVersionDto>>> GetPromptsAsync(string code, CancellationToken ct)
        {
            if (!_user.IsInRole(AiRoles.Readers)) return AiText.Bad<List<AiPromptVersionDto>>(AiText.NotAllowed);
            await AiChatService.ActivePromptAsync(_uow, AiChatService.ChatPromptCode, Me, ct);
            IQueryable<AiPromptVersion> q = _uow.Set<AiPromptVersion>().AsNoTracking();
            if (AiText.Clean(code) != null) q = q.Where(p => p.Code == code.Trim());
            List<AiPromptVersion> list = await q.OrderBy(p => p.Code).ThenByDescending(p => p.Version).ToListAsync(ct);
            return ResponseDtoHelper.Ok(list.Select(ToDto).ToList());
        }

        public async Task<ResponseDto<AiPromptVersionDto>> NewPromptAsync(AiNewPromptDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(AiRoles.Admins)) return AiText.Bad<AiPromptVersionDto>(AiText.NotAllowed);
            string code = AiText.Clean(dto?.Code) ?? AiChatService.ChatPromptCode;
            string text = AiText.Clean(dto?.Text);
            if (text == null) return AiText.Bad<AiPromptVersionDto>("متن prompt را وارد کنید.");
            if (code.Length > 50) return AiText.Bad<AiPromptVersionDto>(AiText.InvalidData);
            code = code.ToUpperInvariant();
            await AiChatService.ActivePromptAsync(_uow, code, Me, ct);
            int last = await _uow.Set<AiPromptVersion>().Where(p => p.Code == code).MaxAsync(p => (int?)p.Version, ct) ?? 0;
            var p = new AiPromptVersion { Code = code, Version = last + 1, Text = text, IsActive = false, CreatedAt = DateTimeOffset.Now, CreatedByUserId = Me };
            _uow.Set<AiPromptVersion>().Add(p);
            _audit.Add(AiAuditKinds.Admin, $"prompt {code} version {p.Version} created ({text.Length} characters, hash {AiText.Sha256Hex(text)})");
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(p));
        }

        public async Task<ResponseDto<AiPromptVersionDto>> ActivatePromptAsync(AiActivatePromptDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(AiRoles.Admins)) return AiText.Bad<AiPromptVersionDto>(AiText.NotAllowed);
            if (dto == null || dto.Id <= 0) return AiText.Bad<AiPromptVersionDto>(AiText.InvalidData);
            AiPromptVersion target = await _uow.Set<AiPromptVersion>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.Id, ct);
            if (target == null) return AiText.NotFound<AiPromptVersionDto>("نسخه‌ی prompt پیدا نشد.");
            // one statement: the previous version is switched off and this one on together, so the filtered unique index (one active version per code) is never violated in between
            _uow.ExecuteSqlCommand(
                "UPDATE AiPromptVersions SET IsActive = CASE WHEN Id = {0} THEN 1 ELSE 0 END, ActivatedAt = CASE WHEN Id = {0} THEN {1} ELSE ActivatedAt END, ActivatedByUserId = CASE WHEN Id = {0} THEN {2} ELSE ActivatedByUserId END WHERE Code = {3} AND (Id = {0} OR IsActive = 1)",
                target.Id, DateTimeOffset.Now, Me, target.Code);
            target = await _uow.Set<AiPromptVersion>().AsNoTracking().FirstAsync(p => p.Id == dto.Id, ct);
            _audit.Add(AiAuditKinds.Admin, $"prompt {target.Code} version {target.Version} activated", promptVersionId: target.Id);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(target));
        }

        // ------------------------------------------------------------------------------------------ audit and index

        public async Task<ResponseDto<PagedList<AiAuditEventDto>>> GetAuditAsync(AiAuditQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(AiRoles.Readers)) return AiText.Bad<PagedList<AiAuditEventDto>>(AiText.NotAllowed);
            query ??= new AiAuditQuery();
            IQueryable<AiAuditEvent> q = _uow.Set<AiAuditEvent>().AsNoTracking();
            string kind = AiText.Clean(query.Kind);
            if (kind != null) q = q.Where(e => e.Kind == kind);
            if (query.UserId.HasValue) q = q.Where(e => e.UserId == query.UserId.Value);
            if (query.From.HasValue) { DateTimeOffset from = new DateTimeOffset(query.From.Value.Date, TimeZoneInfo.Local.GetUtcOffset(query.From.Value.Date)); q = q.Where(e => e.At >= from); }
            if (query.To.HasValue) { DateTime end = query.To.Value.Date.AddDays(1); DateTimeOffset to = new DateTimeOffset(end, TimeZoneInfo.Local.GetUtcOffset(end)); q = q.Where(e => e.At < to); }
            IQueryable<AiAuditEvent> ordered = q.OrderByDescending(e => e.At).ThenByDescending(e => e.Id);
            PagedList<AiAuditEvent> page = await AiText.PageAsync(ordered, query.PageNumber, query.PageSize, ct);
            PagedList<AiAuditEventDto> dtos = await PagedList<AiAuditEventDto>.CreateAsync(page.Select(e => new AiAuditEventDto
            {
                Id = e.Id, At = e.At, AtText = AiText.Stamp(e.At), UserId = e.UserId, SeatCode = e.SeatCode, Kind = e.Kind, PromptVersionId = e.PromptVersionId,
                Model = e.Model, LatencyMs = e.LatencyMs, Detail = e.Detail, PromptHash = e.PromptHash,
            }).ToList(), 1, 0, ct);
            dtos.TotalCount = page.TotalCount;
            dtos.CurrentPage = page.CurrentPage;
            dtos.PageSize = page.PageSize;
            dtos.TotalPages = page.TotalPages;
            return ResponseDtoHelper.Ok(dtos);
        }

        public async Task<ResponseDto<AiIndexRebuildResultDto>> RebuildIndexAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(AiRoles.Admins)) return AiText.Bad<AiIndexRebuildResultDto>(AiText.NotAllowed);
            AiIndexRebuildResultDto result = await _retrieval.RebuildAllAsync(ct);
            await _audit.LogAsync(AiAuditKinds.Admin, $"index rebuilt: {result.ControlledDocuments} controlled documents, {result.MeetingMinutes} minutes, {result.Chunks} chunks, {result.Failed} failed", ct);
            return ResponseDtoHelper.Ok(result);
        }
    }
}
