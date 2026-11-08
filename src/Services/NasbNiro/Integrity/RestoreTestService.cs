using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Integrity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.IntegrityDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Integrity
{
    public interface IRestoreTestService
    {
        /// <summary>Reads the result files of deploy/restore-test.ps1 from the folder <c>RestoreTest:ResultDirectory</c> and keeps one row per run (idempotent). Never throws.</summary>
        Task<RestoreImportDto> ImportAsync(CancellationToken ct);

        /// <summary>The same for a person (PMO, CEO, QA, AUDITOR, FINANCEMANAGER): the button «خواندن نتیجه آزمون».</summary>
        Task<ResponseDto<RestoreImportDto>> ImportOnDemandAsync(CancellationToken ct);

        Task<ResponseDto<PagedList<RestoreTestDto>>> GetResultsAsync(PaginationParams query, CancellationToken ct);

        RestoreTestDto ToDto(RestoreTestResult row);
    }

    /// <summary>
    /// Package D: the monthly restore test. The test itself is deploy/restore-test.ps1 (a Windows scheduled task, run as the machine account because the application pool must not hold sysadmin): it restores the latest backup
    /// into the scratch database IRNNCO_RESTORETEST, checks it, drops it and writes a JSON result file. The application only READS that file and keeps the result for the page «سلامت سوابق» and the CEO dashboard.
    /// </summary>
    public class RestoreTestService : IRestoreTestService
    {
        public const string DirectorySetting = "RestoreTest:ResultDirectory";
        private const long MaxFileBytes = 256 * 1024;
        private const int MaxFiles = 200;

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly IUnitOfWork _uow;
        private readonly IConfiguration _configuration;
        private readonly IInventoryUserContext _user;
        private readonly ILogger<RestoreTestService> _logger;

        public RestoreTestService(IUnitOfWork uow, IConfiguration configuration, IInventoryUserContext user, ILogger<RestoreTestService> logger = null)
        {
            _uow = uow;
            _configuration = configuration;
            _user = user;
            _logger = logger;
        }

        /// <summary>The file written by the script.</summary>
        internal sealed class ResultFile
        {
            public string RunId { get; set; }
            public DateTimeOffset? StartedAt { get; set; }
            public DateTimeOffset? FinishedAt { get; set; }
            public bool Success { get; set; }
            public string BackupFile { get; set; }
            public DateTimeOffset? BackupTakenAt { get; set; }
            public bool? MigrationsMatch { get; set; }
            public bool? ChainOk { get; set; }
            public long? ChainEntriesChecked { get; set; }
            public bool? ScratchDropped { get; set; }
            public Dictionary<string, long> RowCounts { get; set; }
            public List<RestoreCheckDto> Checks { get; set; }
            public string Message { get; set; }
        }

        public async Task<ResponseDto<RestoreImportDto>> ImportOnDemandAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(IntegrityRoles.Readers)) return ResponseDtoHelper.Forbidden<RestoreImportDto>(IntegrityRoles.NotAllowed);
            return ResponseDtoHelper.Ok(await ImportAsync(ct));
        }

        public async Task<RestoreImportDto> ImportAsync(CancellationToken ct)
        {
            var result = new RestoreImportDto();
            try
            {
                string directory = _configuration?[DirectorySetting];
                if (string.IsNullOrWhiteSpace(directory)) { result.Message = "پوشه نتیجه آزمون بازیابی (RestoreTest:ResultDirectory) تنظیم نشده است."; return result; }
                if (!Directory.Exists(directory)) { result.Message = "پوشه نتیجه آزمون بازیابی وجود ندارد؛ آزمون هنوز اجرا نشده است."; return result; }
                List<FileInfo> files = new DirectoryInfo(directory).GetFiles("restore-test-*.json").OrderByDescending(f => f.Name, StringComparer.Ordinal).Take(MaxFiles).ToList();
                result.Found = files.Count;
                if (files.Count == 0) { result.Message = "هنوز فایل نتیجه‌ای نیست."; return result; }
                HashSet<string> known = (await _uow.Set<RestoreTestResult>().AsNoTracking().Select(r => r.RunId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
                foreach (FileInfo file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    ResultFile parsed = Parse(file);
                    if (parsed == null || string.IsNullOrWhiteSpace(parsed.RunId) || parsed.RunId.Length > 40 || !parsed.StartedAt.HasValue) { result.Skipped++; continue; }
                    if (known.Contains(parsed.RunId)) continue;
                    _uow.Set<RestoreTestResult>().Add(ToEntity(parsed));
                    known.Add(parsed.RunId);
                    result.Imported++;
                }
                if (result.Imported > 0) await _uow.SaveChangesAsync(ct);
                result.Message = result.Imported + " نتیجه تازه خوانده شد.";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Reading the restore test results failed.");
                result.Message = "خواندن نتیجه آزمون بازیابی ممکن نشد.";
            }
            return result;
        }

        internal static ResultFile Parse(FileInfo file)
        {
            try
            {
                if (file.Length == 0 || file.Length > MaxFileBytes) return null;
                string text = File.ReadAllText(file.FullName);
                if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
                return JsonSerializer.Deserialize<ResultFile>(text, Json);
            }
            catch (Exception) { return null; }
        }

        internal static RestoreTestResult ToEntity(ResultFile f)
        {
            string Cut(string s, int n) => s == null ? null : (s.Length <= n ? s : s.Substring(0, n));
            return new RestoreTestResult
            {
                RunId = f.RunId.Trim(), StartedAt = f.StartedAt.Value, FinishedAt = f.FinishedAt, Success = f.Success, BackupFile = Cut(f.BackupFile, 260), BackupTakenAt = f.BackupTakenAt,
                MigrationsMatch = f.MigrationsMatch, ChainOk = f.ChainOk, ChainEntriesChecked = f.ChainEntriesChecked, ScratchDropped = f.ScratchDropped,
                RowCountsJson = f.RowCounts == null ? null : JsonSerializer.Serialize(f.RowCounts), ChecksJson = f.Checks == null ? null : JsonSerializer.Serialize(f.Checks),
                Message = Cut(f.Message, 1000), ImportedAt = DateTimeOffset.Now,
            };
        }

        public async Task<ResponseDto<PagedList<RestoreTestDto>>> GetResultsAsync(PaginationParams query, CancellationToken ct)
        {
            if (!_user.IsInRole(IntegrityRoles.Readers)) return ResponseDtoHelper.Forbidden<PagedList<RestoreTestDto>>(IntegrityRoles.NotAllowed);
            query ??= new PaginationParams();
            int size = query.PageSize <= 0 ? 25 : Math.Min(query.PageSize, 200);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            IQueryable<RestoreTestResult> q = _uow.Set<RestoreTestResult>().AsNoTracking();
            int total = await q.CountAsync(ct);
            List<RestoreTestResult> rows = await q.OrderByDescending(r => r.StartedAt).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok<PagedList<RestoreTestDto>>(new IntegrityPage<RestoreTestDto>(rows.Select(ToDto).ToList(), total, number, size));
        }

        public RestoreTestDto ToDto(RestoreTestResult r)
        {
            var dto = new RestoreTestDto
            {
                Id = r.Id, RunId = r.RunId, StartedAt = r.StartedAt, StartedAtText = AuditIntegrityService.StampText(r.StartedAt), FinishedAt = r.FinishedAt, Success = r.Success, BackupFile = r.BackupFile,
                BackupTakenAt = r.BackupTakenAt, BackupTakenAtText = r.BackupTakenAt.HasValue ? AuditIntegrityService.StampText(r.BackupTakenAt.Value) : null,
                MigrationsMatch = r.MigrationsMatch, ChainOk = r.ChainOk, ChainEntriesChecked = r.ChainEntriesChecked, ScratchDropped = r.ScratchDropped, Message = r.Message,
            };
            try { if (!string.IsNullOrWhiteSpace(r.RowCountsJson)) dto.RowCounts = JsonSerializer.Deserialize<Dictionary<string, long>>(r.RowCountsJson) ?? dto.RowCounts; } catch (JsonException) { /* a damaged text shows no counts */ }
            try { if (!string.IsNullOrWhiteSpace(r.ChecksJson)) dto.Checks = JsonSerializer.Deserialize<List<RestoreCheckDto>>(r.ChecksJson, Json) ?? dto.Checks; } catch (JsonException) { /* the same */ }
            return dto;
        }
    }
}
