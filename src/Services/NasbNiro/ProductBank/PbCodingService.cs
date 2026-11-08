using Entities.Model.NasbNiro.Integrity;
using Services.NasbNiro.Integrity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProductBankDtos;

namespace Services.NasbNiro.ProductBank
{
    public interface IPbCodingService
    {
        Task<ResponseDto<List<PbStageDto>>> GetStagesAsync(bool includeInactive, CancellationToken ct);
        Task<ResponseDto<PbStageDto>> SaveStageAsync(SavePbStageDto dto, CancellationToken ct);
        Task<ResponseDto<List<PbSettingDto>>> GetSettingsAsync(CancellationToken ct);
        Task<ResponseDto<PbSettingDto>> SaveSettingAsync(SavePbSettingDto dto, CancellationToken ct);

        Task<ResponseDto<PbCodePreviewDto>> PreviewAsync(string partNumber, string stageCode2, string title, CancellationToken ct);
        Task<ResponseDto<InventoryItemDto>> CreateItemAsync(PbCreateItemDto dto, CancellationToken ct);

        Task<ResponseDto<PagedList<PbMappingDto>>> GetMappingsAsync(PbMappingQuery query, CancellationToken ct);
        Task<ResponseDto<PbImportMappingsResultDto>> ImportMappingsAsync(List<PbImportMappingRowDto> rows, CancellationToken ct);
        Task<ResponseDto<PbImportMappingsResultDto>> SuggestMappingsAsync(CancellationToken ct);
        Task<ResponseDto<PbMappingDto>> SaveMappingAsync(SavePbMappingDto dto, CancellationToken ct);
        Task<ResponseDto<PbMappingDto>> DecideMappingAsync(PbDecideDto dto, CancellationToken ct);
        Task<ResponseDto<PbAdoptResultDto>> AdoptPartNumbersAsync(CancellationToken ct);
    }

    /// <summary>
    /// PB1 part 1: item coding by BOM part number. The code of a part is its part number; the item of a part at an OPC stage is "part number + separator + two-letter stage code".
    /// The 14-digit warehouse codes stay as LegacyCode; a mapping (imported, or suggested from the descriptions) says which part number and stage an old code stands for, and engineering approves it.
    /// </summary>
    public class PbCodingService : PbServiceBase, IPbCodingService
    {
        private static readonly Regex LegacyShape = new Regex(@"^\d{14}$", RegexOptions.Compiled);
        private readonly IInventoryMasterService _master;

        public PbCodingService(IUnitOfWork uow, IInventoryUserContext user, IInventoryMasterService master) : base(uow, user)
        {
            _master = master ?? throw new ArgumentNullException(nameof(master));
        }

        // -------------------------------------------------------------------------------- stages and settings

        private static PbStageDto Dto(PbStage s) => new PbStageDto
        {
            Id = s.Id, Code = s.Code, TitleFa = s.TitleFa, TitleEn = s.TitleEn, Kind = s.Kind, IsStockable = s.IsStockable, SortOrder = s.SortOrder, IsActive = s.IsActive,
        };

        public async Task<ResponseDto<List<PbStageDto>>> GetStagesAsync(bool includeInactive, CancellationToken ct)
        {
            List<PbStage> list = await LoadStagesAsync(false, ct);
            return ResponseDtoHelper.Ok(list.Where(s => includeInactive || s.IsActive).Select(Dto).ToList());
        }

        public async Task<ResponseDto<PbStageDto>> SaveStageAsync(SavePbStageDto dto, CancellationToken ct)
        {
            if (!InRole(PbRoles.Planning)) return ResponseDtoHelper.BadRequest<PbStageDto>(PbMessages.NotAllowed);
            if (dto == null) return ResponseDtoHelper.BadRequest<PbStageDto>(PbMessages.InvalidData);
            string code = PbText.Stage(dto.Code);
            if (code == null || !Regex.IsMatch(code, "^[A-Z]{2}$")) return ResponseDtoHelper.BadRequest<PbStageDto>("کد مرحله دو حرف انگلیسی است.");
            string fa = PbText.Clean(dto.TitleFa);
            if (fa == null) return ResponseDtoHelper.BadRequest<PbStageDto>("عنوان فارسی مرحله را وارد کنید.");
            if (!Enum.IsDefined(typeof(PbStageKind), dto.Kind)) return ResponseDtoHelper.BadRequest<PbStageDto>("نوع مرحله نامعتبر است.");
            List<PbStage> all = await LoadStagesAsync(true, ct);
            if (all.Any(s => s.Code == code && s.Id != (dto.Id ?? 0))) return ResponseDtoHelper.BadRequest<PbStageDto>("این کد مرحله قبلاً تعریف شده است: " + code);
            PbStage row;
            if (dto.Id.HasValue)
            {
                row = all.FirstOrDefault(s => s.Id == dto.Id.Value);
                if (row == null) return ResponseDtoHelper.NotFound<PbStageDto>(PbMessages.NotFound);
                // a code that items already use is not renamed: the item codes would no longer follow the rule
                if (row.Code != code && await Uow.Set<InventoryItem>().AnyAsync(i => !i.Deleted && i.StageCode2 == row.Code, ct))
                    return ResponseDtoHelper.BadRequest<PbStageDto>("کدی که کالایی با آن ساخته شده تغییر نمی‌کند؛ آن را غیرفعال کنید و کد تازه بسازید.");
            }
            else
            {
                row = new PbStage();
                Uow.Set<PbStage>().Add(row);
            }
            row.Code = code; row.TitleFa = fa; row.TitleEn = PbText.Clean(dto.TitleEn); row.Kind = dto.Kind; row.IsStockable = dto.IsStockable;
            row.SortOrder = dto.SortOrder > 0 ? dto.SortOrder : (all.Count == 0 ? 1 : all.Max(s => s.SortOrder) + 1);
            row.IsActive = dto.IsActive;
            Stamp(row, !dto.Id.HasValue);
            await Uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(Dto(row));
        }

        public async Task<ResponseDto<List<PbSettingDto>>> GetSettingsAsync(CancellationToken ct)
        {
            Dictionary<string, string> values = await PbSettingKeys.LoadAsync(Uow, ct);
            return ResponseDtoHelper.Ok(PbSettingKeys.Defaults().Select(d => new PbSettingDto { Key = d.Key, Value = values[d.Key], Title = d.Title, Source = d.Source }).ToList());
        }

        public async Task<ResponseDto<PbSettingDto>> SaveSettingAsync(SavePbSettingDto dto, CancellationToken ct)
        {
            if (!InRole(PbRoles.Planning)) return ResponseDtoHelper.BadRequest<PbSettingDto>(PbMessages.NotAllowed);
            PbSetting def = dto == null ? null : PbSettingKeys.Defaults().FirstOrDefault(d => d.Key == dto.Key);
            if (def == null) return ResponseDtoHelper.NotFound<PbSettingDto>("تنظیم پیدا نشد.");
            string value = dto.Value?.Trim();
            if (def.Key == PbSettingKeys.StageSeparator && (string.IsNullOrEmpty(value) || value.Length > 3 || value.Any(char.IsLetterOrDigit) || value.Any(char.IsWhiteSpace)))
                return ResponseDtoHelper.BadRequest<PbSettingDto>("جداکننده یک تا سه نشانه است (مثل - یا _) و حرف، عدد یا فاصله نیست.");
            if (def.Key == PbSettingKeys.MinSuggestConfidence && (!decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal v) || v < 0 || v > 100))
                return ResponseDtoHelper.BadRequest<PbSettingDto>("عددی بین صفر و صد وارد کنید.");
            if ((def.Key == PbSettingKeys.DistinctReviewers || def.Key == PbSettingKeys.DemoData) && !PbSettingKeys.IsBool(value))
                return ResponseDtoHelper.BadRequest<PbSettingDto>("مقدار این تنظیم فقط true یا false است.");
            if (def.Key == PbSettingKeys.DemoData && !InRole(PbRoles.Admin)) return ResponseDtoHelper.BadRequest<PbSettingDto>(PbMessages.NotAllowed);
            PbSetting row = await Uow.Set<PbSetting>().FirstOrDefaultAsync(s => s.Key == def.Key, ct);
            if (SafetyProfile.Refusal(SafetyStore.ProductBank, def.Key) != null && !SafetyProfile.SameValue(row?.Value ?? def.Value, value)) return ResponseDtoHelper.BadRequest<PbSettingDto>(SafetyProfile.RefusalText);   // PD: two-person change
            if (row == null)
            {
                row = new PbSetting { Key = def.Key, Title = def.Title, Source = def.Source };
                Uow.Set<PbSetting>().Add(row);
            }
            // the separator is part of every stage item code: it is fixed once such an item exists
            if (def.Key == PbSettingKeys.StageSeparator && row.Value != value && await Uow.Set<InventoryItem>().AnyAsync(i => !i.Deleted && i.StageCode2 != null, ct))
                return ResponseDtoHelper.BadRequest<PbSettingDto>("کالای مرحله‌ای با این جداکننده ساخته شده است و جداکننده دیگر تغییر نمی‌کند.");
            row.Value = value;
            await Uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new PbSettingDto { Key = row.Key, Value = row.Value, Title = row.Title, Source = row.Source });
        }

        // -------------------------------------------------------------------------------- item coding

        public async Task<ResponseDto<PbCodePreviewDto>> PreviewAsync(string partNumber, string stageCode2, string title, CancellationToken ct)
        {
            var preview = new PbCodePreviewDto { PartNumber = PbText.PartNumber(partNumber), StageCode2 = PbText.Stage(stageCode2) };
            if (!CheckShape(preview.PartNumber, preview.Errors)) return ResponseDtoHelper.Ok(preview);
            List<PbStage> stages = await LoadStagesAsync(false, ct);
            PbStage stage = null;
            if (preview.StageCode2 != null)
            {
                stage = stages.FirstOrDefault(s => s.Code == preview.StageCode2 && s.IsActive);
                if (stage == null) { preview.Errors.Add(PbMessages.StageUnknown); return ResponseDtoHelper.Ok(preview); }
                preview.StageTitle = stage.TitleFa;
            }
            string separator = await SeparatorAsync(ct);
            preview.Code = stage == null ? preview.PartNumber : PbCodeRules.StageItemCode(preview.PartNumber, stage.Code, separator);
            InventoryItem baseItem = await FindPartItemAsync(preview.PartNumber, false, ct);
            string partTitle = PbText.Clean(title) ?? baseItem?.Title ?? await TitleFromBomAsync(preview.PartNumber, ct);
            preview.Title = stage == null ? partTitle : PbCodeRules.StageItemTitle(partTitle, stage);
            InventoryItem existing = await Uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => !i.Deleted && i.Code == preview.Code, ct);
            if (existing != null)
            {
                preview.Exists = true; preview.ItemId = existing.Id;
                bool adoptable = existing.PartNumber == null && existing.StageCode2 == null && stage == null;
                if (adoptable) preview.Warnings.Add("کالایی با همین کد وجود دارد و با ثبت، به‌عنوان کالای این قطعه پذیرفته می‌شود (شماره قطعه روی آن ثبت می‌شود).");
                else if (existing.PartNumber != preview.PartNumber || existing.StageCode2 != preview.StageCode2)
                    preview.Errors.Add("این کد برای کالای دیگری به کار رفته است: " + existing.Title);
            }
            else if (baseItem != null && baseItem.PartNumber == null && stage == null)
                preview.Warnings.Add("کالای موجود با کد مسک " + baseItem.Code + " به‌عنوان کالای این قطعه پذیرفته می‌شود.");
            if (baseItem == null && stage != null) preview.Warnings.Add("کالای خود قطعه هنوز ثبت نشده است؛ هنگام ساخت همراه آن ساخته می‌شود.");
            await NearDuplicateWarningsAsync(preview, ct);
            return ResponseDtoHelper.Ok(preview);
        }

        private static bool CheckShape(string pn, List<string> errors)
        {
            string error = PbCodeRules.ShapeError(pn);
            if (error != null) errors.Add(error);
            return error == null;
        }

        private async Task<string> TitleFromBomAsync(string partNumber, CancellationToken ct) =>
            await Uow.Set<PbBomLine>().AsNoTracking().Where(l => !l.Deleted && l.PartNumber == partNumber && l.PartName != null).OrderByDescending(l => l.Id).Select(l => l.PartName).FirstOrDefaultAsync(ct);

        /// <summary>R3: the same title under another part number, or the same part number under another title, is shown before an item is made.</summary>
        private async Task NearDuplicateWarningsAsync(PbCodePreviewDto preview, CancellationToken ct)
        {
            if (preview.Title == null) return;
            string title = preview.Title;
            List<string> sameTitle = await Uow.Set<InventoryItem>().AsNoTracking()
                .Where(i => !i.Deleted && i.Title == title && (i.PartNumber != preview.PartNumber || i.PartNumber == null) && i.Code != preview.Code).Select(i => i.Code).Take(5).ToListAsync(ct);
            if (sameTitle.Count > 0) preview.Warnings.Add("کالایی با همین عنوان و شماره قطعه دیگر وجود دارد (احتمال تکرار): " + string.Join("، ", sameTitle));
        }

        public async Task<ResponseDto<InventoryItemDto>> CreateItemAsync(PbCreateItemDto dto, CancellationToken ct)
        {
            if (!InRole(PbRoles.Coding)) return ResponseDtoHelper.BadRequest<InventoryItemDto>(PbMessages.NotAllowed);
            if (dto == null) return ResponseDtoHelper.BadRequest<InventoryItemDto>(PbMessages.InvalidData);
            var errors = new List<string>();
            string pn = PbText.PartNumber(dto.PartNumber);
            if (!CheckShape(pn, errors)) return ResponseDtoHelper.BadRequest<InventoryItemDto>(errors[0]);
            string stageCode = PbText.Stage(dto.StageCode2);
            PbStage stage = null;
            if (stageCode != null)
            {
                stage = (await LoadStagesAsync(false, ct)).FirstOrDefault(s => s.Code == stageCode && s.IsActive);
                if (stage == null) return ResponseDtoHelper.BadRequest<InventoryItemDto>(PbMessages.StageUnknown);
            }
            string separator = await SeparatorAsync(ct);
            string code = stage == null ? pn : PbCodeRules.StageItemCode(pn, stage.Code, separator);

            InventoryItem existing = await Uow.Set<InventoryItem>().FirstOrDefaultAsync(i => !i.Deleted && (i.Code == code || (i.PartNumber == pn && i.StageCode2 == stageCode)), ct);
            // an item that already carries the code (or the MESC code) of the part but has no part number yet is adopted, never duplicated
            bool adoptable = stage == null && existing != null && existing.PartNumber == null && existing.StageCode2 == null;
            if (existing != null && !adoptable) return ResponseDtoHelper.BadRequest<InventoryItemDto>(InventoryMessages.DuplicateCode + existing.Code);

            InventoryItem baseItem = await FindPartItemAsync(pn, true, ct);
            string partTitle = PbText.Clean(dto.PartTitle) ?? baseItem?.Title ?? await TitleFromBomAsync(pn, ct);
            if (partTitle == null) return ResponseDtoHelper.BadRequest<InventoryItemDto>("عنوان قطعه را وارد کنید (قطعه هنوز در هیچ BOM و کالایی نیست).");

            string title = stage == null ? partTitle : PbCodeRules.StageItemTitle(partTitle, stage);
            if (!dto.ConfirmNearDuplicate && (stage != null || baseItem == null) && await Uow.Set<InventoryItem>().AnyAsync(i => !i.Deleted && i.Title == title && i.Code != code, ct))
                return ResponseDtoHelper.BadRequest<InventoryItemDto>("کالایی با همین عنوان و شماره قطعه دیگر وجود دارد (احتمال تکرار). اگر مطمئن هستید با تایید تکرار دوباره بفرستید.");

            if (baseItem == null)
            {
                var created = await FindOrCreatePartItemAsync(pn, partTitle, dto.Unit, dto.Material, dto.Standard, ct, dto.ItemGroupId, dto.TrackingMode);
                if (created.Error != null) return ResponseDtoHelper.BadRequest<InventoryItemDto>(created.Error);
                baseItem = created.Item;
            }
            if (stage == null)
            {
                await Uow.SaveChangesAsync(ct);
                return await _master.GetItemAsync(baseItem.Id, ct);
            }
            if (Uow.Entry(baseItem).State == EntityState.Added) await Uow.SaveChangesAsync(ct);   // the stage item points at the saved key
            InventoryItem stageItem = NewItem(pn, stage.Code, title, dto, baseItem);
            stageItem.Code = code;
            Uow.Set<InventoryItem>().Add(stageItem);
            await Uow.SaveChangesAsync(ct);
            return await _master.GetItemAsync(stageItem.Id, ct);
        }

        private InventoryItem NewItem(string pn, string stageCode, string title, PbCreateItemDto dto, InventoryItem partItem) =>
            NewPartItem(pn, stageCode, title, dto.Unit, dto.Material, dto.Standard, dto.ItemGroupId, dto.TrackingMode, partItem);

        // -------------------------------------------------------------------------------- one-time upgrade step

        /// <summary>
        /// Upgrade backfill, run once by the PMO (a marker row in the settings records it; a second call changes nothing): every existing item whose MESC code (or whose code, when it is a MESC code) is a part number
        /// gets that PartNumber, so the BOM finds it instead of making a second item. The changes are audited like any other edit of an item. Items that would collide are listed, not changed.
        /// </summary>
        public async Task<ResponseDto<PbAdoptResultDto>> AdoptPartNumbersAsync(CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbAdoptResultDto>(PbMessages.AiNeverApproves);
            if (!InRole(PbRoles.Admin)) return ResponseDtoHelper.BadRequest<PbAdoptResultDto>(PbMessages.NotAllowed);
            PbSetting marker = await Uow.Set<PbSetting>().FirstOrDefaultAsync(x => x.Key == PbSettingKeys.AdoptMarker, ct);
            if (marker != null) return ResponseDtoHelper.Ok(new PbAdoptResultDto { AlreadyDone = true, Marker = marker.Value });

            var result = new PbAdoptResultDto();
            List<InventoryItem> candidates = await Uow.Set<InventoryItem>().Where(i => !i.Deleted && i.PartNumber == null && i.StageCode2 == null && (i.MescCode != null || i.Code != null)).OrderBy(i => i.Id).ToListAsync(ct);
            var taken = (await Uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && i.PartNumber != null && i.StageCode2 == null).Select(i => i.PartNumber).ToListAsync(ct)).ToHashSet();
            var byPn = new Dictionary<string, List<InventoryItem>>();
            foreach (InventoryItem i in candidates)
            {
                string pn = PbText.PartNumber(!string.IsNullOrWhiteSpace(i.MescCode) ? i.MescCode : (ItemCodeRules.IsMesc(i.Code) ? i.Code : null));
                if (pn == null || PbCodeRules.ShapeError(pn) != null) continue;
                if (!byPn.TryGetValue(pn, out List<InventoryItem> list)) byPn[pn] = list = new List<InventoryItem>();
                list.Add(i);
            }
            foreach (var kv in byPn.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (taken.Contains(kv.Key)) { result.Conflicts.Add(kv.Key + ": شماره قطعه برای کالای دیگری ثبت شده است"); continue; }
                InventoryItem chosen = kv.Value.FirstOrDefault(i => i.Code == kv.Key) ?? kv.Value[0];
                chosen.PartNumber = kv.Key;
                if (string.IsNullOrEmpty(chosen.BaseCode)) chosen.BaseCode = chosen.Code;
                Stamp(chosen, false);
                result.Adopted++;
                foreach (InventoryItem other in kv.Value.Where(i => i != chosen)) result.Conflicts.Add(kv.Key + ": کالای " + other.Code + " هم همین کد مسک را دارد و تغییر نکرد");
            }
            marker = new PbSetting { Key = PbSettingKeys.AdoptMarker, Value = DateTimeOffset.Now.ToString("o", System.Globalization.CultureInfo.InvariantCulture) + "|" + result.Adopted, Title = "ثبت شماره قطعه برای کالاهای دارای کد مسک (یک‌بار)", Source = "مرحلهٔ ارتقا" };
            Uow.Set<PbSetting>().Add(marker);
            await Uow.SaveChangesAsync(ct);
            result.Marker = marker.Value;
            return ResponseDtoHelper.Ok(result);
        }

        // -------------------------------------------------------------------------------- legacy code mapping

        private PbMappingDto Dto(PbLegacyMapping m) => new PbMappingDto
        {
            Id = m.Id, LegacyCode = m.LegacyCode, LegacyTitle = m.LegacyTitle, LegacyMaterial = m.LegacyMaterial, LegacyItemId = m.LegacyItemId,
            PartNumber = m.PartNumber, StageCode2 = m.StageCode2, PartTitle = m.PartTitle, Source = m.Source, Confidence = m.Confidence, Status = m.Status,
            ProposedByUserId = m.ProposedByUserId, ReviewedByUserId = m.ReviewedByUserId, ReviewedAt = m.ReviewedAt, ReviewNote = m.ReviewNote, AppliedAt = m.AppliedAt,
            CanDecide = !IsAi && m.Status == PbMappingStatus.Draft && InRole(PbRoles.Engineering) && m.ProposedByUserId != User.UserId && m.CreatorUserId != User.UserId,
        };

        public async Task<ResponseDto<PagedList<PbMappingDto>>> GetMappingsAsync(PbMappingQuery query, CancellationToken ct)
        {
            query ??= new PbMappingQuery();
            IQueryable<PbLegacyMapping> q = Uow.Set<PbLegacyMapping>().AsNoTracking().Where(m => !m.Deleted);
            if (query.Status.HasValue) q = q.Where(m => m.Status == query.Status.Value);
            if (query.MinConfidence.HasValue) q = q.Where(m => m.Confidence >= query.MinConfidence.Value);
            string text = PbText.Clean(query.Contains);
            if (text != null) q = q.Where(m => m.LegacyCode.Contains(text) || m.LegacyTitle.Contains(text) || m.PartNumber.Contains(text) || m.PartTitle.Contains(text));
            IQueryable<PbMappingDto> projected = q.OrderByDescending(m => m.Confidence).ThenBy(m => m.LegacyCode).Select(m => new PbMappingDto
            {
                Id = m.Id, LegacyCode = m.LegacyCode, LegacyTitle = m.LegacyTitle, LegacyMaterial = m.LegacyMaterial, LegacyItemId = m.LegacyItemId,
                PartNumber = m.PartNumber, StageCode2 = m.StageCode2, PartTitle = m.PartTitle, Source = m.Source, Confidence = m.Confidence, Status = m.Status,
                ProposedByUserId = m.ProposedByUserId, ReviewedByUserId = m.ReviewedByUserId, ReviewedAt = m.ReviewedAt, ReviewNote = m.ReviewNote, AppliedAt = m.AppliedAt,
                CanDecide = m.CreatorUserId != User.UserId && m.ProposedByUserId != User.UserId,
            });
            PagedList<PbMappingDto> page = await PagedList<PbMappingDto>.CreateAsync(projected, query.PageNumber, query.PageSize, ct);
            bool mayDecide = !IsAi && InRole(PbRoles.Engineering);
            foreach (PbMappingDto d in page) d.CanDecide = mayDecide && d.Status == PbMappingStatus.Draft && d.CanDecide;
            return ResponseDtoHelper.Ok(page);
        }

        private async Task<string> PartTitleAsync(string pn, CancellationToken ct) =>
            (await Uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && i.PartNumber == pn && i.StageCode2 == null).Select(i => i.Title).FirstOrDefaultAsync(ct)) ?? await TitleFromBomAsync(pn, ct);

        public async Task<ResponseDto<PbImportMappingsResultDto>> ImportMappingsAsync(List<PbImportMappingRowDto> rows, CancellationToken ct)
        {
            if (!InRole(PbRoles.Coding)) return ResponseDtoHelper.BadRequest<PbImportMappingsResultDto>(PbMessages.NotAllowed);
            if (rows == null || rows.Count == 0) return ResponseDtoHelper.BadRequest<PbImportMappingsResultDto>(PbMessages.InvalidData);
            if (rows.Count > 5000) return ResponseDtoHelper.BadRequest<PbImportMappingsResultDto>("حداکثر ۵۰۰۰ سطر در هر بار.");
            var result = new PbImportMappingsResultDto();
            List<PbStage> stages = await LoadStagesAsync(false, ct);
            var live = (await Uow.Set<PbLegacyMapping>().AsNoTracking().Where(m => !m.Deleted && m.Status != PbMappingStatus.Rejected).Select(m => m.LegacyCode).ToListAsync(ct)).ToHashSet();

            // the rows are validated first; the items and titles they refer to are read in two queries, not per row
            var valid = new List<(string Legacy, string Pn, string Stage, PbImportMappingRowDto Row)>();
            int line = 0;
            foreach (PbImportMappingRowDto row in rows)
            {
                line++;
                string legacy = ItemCodeRules.Normalize(row?.LegacyCode);
                string pn = PbText.PartNumber(row?.PartNumber);
                string stage = PbText.Stage(row?.StageCode2);
                if (legacy == null || pn == null) { result.Errors.Add($"سطر {line}: کد قدیم و شماره قطعه لازم است."); result.Skipped++; continue; }
                if (legacy.Length > 100) { result.Errors.Add($"سطر {line}: کد قدیم بلندتر از ۱۰۰ نشانه است."); result.Skipped++; continue; }
                if (PbCodeRules.ShapeError(pn) != null) { result.Errors.Add($"سطر {line}: شماره قطعه نامعتبر است ({pn})."); result.Skipped++; continue; }
                if (stage != null && !stages.Any(s => s.Code == stage && s.IsActive)) { result.Errors.Add($"سطر {line}: کد مرحله {stage} در فهرست نیست."); result.Skipped++; continue; }
                if (!live.Add(legacy)) { result.Errors.Add($"سطر {line}: برای کد {legacy} نگاشت فعال وجود دارد."); result.Skipped++; continue; }
                valid.Add((legacy, pn, stage, row));
            }
            var legacyCodes = valid.Select(v => v.Legacy).Distinct().ToList();
            var partNumbers = valid.Select(v => v.Pn).Distinct().ToList();
            var legacyIds = new Dictionary<string, int>();
            foreach (var x in await Uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && legacyCodes.Contains(i.Code)).Select(i => new { i.Code, i.Id }).ToListAsync(ct)) legacyIds[x.Code] = x.Id;
            var titles = new Dictionary<string, string>();
            foreach (var x in await Uow.Set<PbBomLine>().AsNoTracking().Where(l => !l.Deleted && partNumbers.Contains(l.PartNumber) && l.PartName != null).OrderBy(l => l.Id).Select(l => new { l.PartNumber, l.PartName }).ToListAsync(ct)) titles[x.PartNumber] = x.PartName;
            foreach (var x in await Uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && i.StageCode2 == null && partNumbers.Contains(i.PartNumber)).Select(i => new { i.PartNumber, i.Title }).ToListAsync(ct)) titles[x.PartNumber] = x.Title;
            foreach (var v in valid)
            {
                var m = new PbLegacyMapping
                {
                    LegacyCode = v.Legacy, LegacyTitle = PbText.Clean(v.Row.LegacyTitle), LegacyMaterial = PbText.Clean(v.Row.LegacyMaterial), LegacyItemId = legacyIds.TryGetValue(v.Legacy, out int id) ? id : (int?)null,
                    PartNumber = v.Pn, StageCode2 = v.Stage, PartTitle = titles.TryGetValue(v.Pn, out string t) ? t : null, Source = PbMappingSource.Imported, Confidence = 100m,
                    Status = PbMappingStatus.Draft, ProposedByUserId = User.UserId,
                };
                Stamp(m, true);
                Uow.Set<PbLegacyMapping>().Add(m);
                result.Created++;
            }
            await Uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(result);
        }

        /// <summary>Stage the description of an old item points at (raw casting, milling, final machining ...), or null.</summary>
        internal static string StageFromDescription(string title)
        {
            string t = (title ?? "").ToLowerInvariant();
            if (t.Contains("قطعه نهایی") || t.Contains("final part")) return "FN";
            if (t.Contains("قطعه خام") || t.Contains("raw")) return "RW";
            if (t.Contains("final machining")) return "FM";
            if (t.Contains("milling") || t.Contains("فرز")) return "ML";
            if (t.Contains("lathing") || t.Contains("تراش")) return "LA";
            if (t.Contains("assy")) return "AS";
            return null;
        }

        public async Task<ResponseDto<PbImportMappingsResultDto>> SuggestMappingsAsync(CancellationToken ct)
        {
            if (!InRole(PbRoles.Coding)) return ResponseDtoHelper.BadRequest<PbImportMappingsResultDto>(PbMessages.NotAllowed);
            var result = new PbImportMappingsResultDto();
            decimal min = decimal.Parse((await PbSettingKeys.LoadAsync(Uow, ct))[PbSettingKeys.MinSuggestConfidence], System.Globalization.CultureInfo.InvariantCulture);
            List<PbStage> stages = await LoadStagesAsync(false, ct);

            // the parts to match: the base items by part number and the lines of the newest revision of each BOM
            var parts = new Dictionary<string, (string Name, string Material)>();
            foreach (var i in await Uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && i.PartNumber != null && i.StageCode2 == null).Select(i => new { i.PartNumber, i.Title, i.Material }).ToListAsync(ct))
                parts[i.PartNumber] = (i.Title, i.Material);
            foreach (var l in await Uow.Set<PbBomLine>().AsNoTracking().Where(l => !l.Deleted && !l.IsHeading && l.PartNumber != null && l.PartName != null).OrderBy(l => l.Id).Select(l => new { l.PartNumber, l.PartName, l.Material }).ToListAsync(ct))
                parts[l.PartNumber] = (l.PartName, l.Material);
            if (parts.Count == 0) return ResponseDtoHelper.Ok(result);

            var live = (await Uow.Set<PbLegacyMapping>().AsNoTracking().Where(m => !m.Deleted && m.Status != PbMappingStatus.Rejected).Select(m => m.LegacyCode).ToListAsync(ct)).ToHashSet();
            List<InventoryItem> legacy = await Uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && i.PartNumber == null).ToListAsync(ct);
            foreach (InventoryItem item in legacy)
            {
                string code = item.LegacyCode ?? item.Code;
                if (!LegacyShape.IsMatch(code ?? "")) continue;
                if (live.Contains(code)) { result.Skipped++; continue; }
                string bestPn = null; decimal best = 0;
                foreach (var kv in parts)
                {
                    decimal score = PbText.Similarity(item.Title, kv.Value.Name, item.Material, kv.Value.Material);
                    if (score > best) { best = score; bestPn = kv.Key; }
                }
                if (bestPn == null || best < min) { result.Skipped++; continue; }
                string stage = StageFromDescription(item.Title);
                if (stage != null && !stages.Any(s => s.Code == stage && s.IsActive)) stage = null;
                var m = new PbLegacyMapping
                {
                    LegacyCode = code, LegacyTitle = item.Title, LegacyMaterial = item.Material, LegacyItemId = item.Id, PartNumber = bestPn, StageCode2 = stage,
                    PartTitle = parts[bestPn].Name, Source = PbMappingSource.Suggested, Confidence = best, Status = PbMappingStatus.Draft, ProposedByUserId = User.UserId,
                };
                Stamp(m, true);
                Uow.Set<PbLegacyMapping>().Add(m);
                live.Add(code);
                result.Created++;
            }
            await Uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<PbMappingDto>> SaveMappingAsync(SavePbMappingDto dto, CancellationToken ct)
        {
            if (!InRole(PbRoles.Coding)) return ResponseDtoHelper.BadRequest<PbMappingDto>(PbMessages.NotAllowed);
            if (dto == null) return ResponseDtoHelper.BadRequest<PbMappingDto>(PbMessages.InvalidData);
            string pn = PbText.PartNumber(dto.PartNumber);
            string stage = PbText.Stage(dto.StageCode2);
            var errors = new List<string>();
            if (!CheckShape(pn, errors)) return ResponseDtoHelper.BadRequest<PbMappingDto>(errors[0]);
            if (stage != null && !(await LoadStagesAsync(false, ct)).Any(s => s.Code == stage && s.IsActive)) return ResponseDtoHelper.BadRequest<PbMappingDto>(PbMessages.StageUnknown);
            PbLegacyMapping m;
            if (dto.Id.HasValue)
            {
                m = await Uow.Set<PbLegacyMapping>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (m == null) return ResponseDtoHelper.NotFound<PbMappingDto>(PbMessages.NotFound);
                if (m.Status != PbMappingStatus.Draft) return ResponseDtoHelper.BadRequest<PbMappingDto>("فقط نگاشت پیش‌نویس ویرایش می‌شود.");
            }
            else
            {
                string legacy = ItemCodeRules.Normalize(dto.LegacyCode);
                if (legacy == null) return ResponseDtoHelper.BadRequest<PbMappingDto>("کد قدیم را وارد کنید.");
                if (await Uow.Set<PbLegacyMapping>().AnyAsync(x => !x.Deleted && x.LegacyCode == legacy && x.Status != PbMappingStatus.Rejected, ct))
                    return ResponseDtoHelper.BadRequest<PbMappingDto>("برای این کد قدیم نگاشت فعال وجود دارد.");
                if (legacy.Length > 100) return ResponseDtoHelper.BadRequest<PbMappingDto>("کد قدیم بلندتر از ۱۰۰ نشانه است.");
                var legacyItem = await Uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && i.Code == legacy).Select(i => new { i.Id, i.Title, i.Material }).FirstOrDefaultAsync(ct);
                m = new PbLegacyMapping { LegacyCode = legacy, LegacyItemId = legacyItem?.Id, LegacyTitle = legacyItem?.Title, LegacyMaterial = legacyItem?.Material, Source = PbMappingSource.Manual, ProposedByUserId = User.UserId };
                Uow.Set<PbLegacyMapping>().Add(m);
                Stamp(m, true);
            }
            m.PartNumber = pn; m.StageCode2 = stage; m.PartTitle = await PartTitleAsync(pn, ct); m.Confidence = 100m;
            // the person who changes the draft is a proposer too: he does not approve it
            if (dto.Id.HasValue) m.ProposedByUserId = User.UserId;
            Stamp(m, false);
            await Uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(Dto(m));
        }

        public async Task<ResponseDto<PbMappingDto>> DecideMappingAsync(PbDecideDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbMappingDto>(PbMessages.AiNeverApproves);
            if (!InRole(PbRoles.Engineering)) return ResponseDtoHelper.BadRequest<PbMappingDto>(PbMessages.NotAllowed);
            PbLegacyMapping m = dto == null ? null : await Uow.Set<PbLegacyMapping>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (m == null) return ResponseDtoHelper.NotFound<PbMappingDto>(PbMessages.NotFound);
            if (m.Status != PbMappingStatus.Draft) return ResponseDtoHelper.BadRequest<PbMappingDto>("این نگاشت قبلاً تصمیم‌گیری شده است.");
            if (m.ProposedByUserId == User.UserId || m.CreatorUserId == User.UserId)
                return ResponseDtoHelper.BadRequest<PbMappingDto>("نگاشت را مهندسی تایید می‌کند و کسی که آن را پیشنهاد یا وارد کرده نمی‌تواند تایید کند.");
            if (!dto.Approve)
            {
                if (PbText.Clean(dto.Note) == null) return ResponseDtoHelper.BadRequest<PbMappingDto>("دلیل رد را بنویسید.");
                m.Status = PbMappingStatus.Rejected;
            }
            else
            {
                if (m.PartNumber == null) return ResponseDtoHelper.BadRequest<PbMappingDto>("شماره قطعه نگاشت خالی است.");
                // The target item (the part, or the part at the stage) exists after the approval. The mapping table stays the source of truth: every approved code
                // is a row of it (the item search reads it); the item keeps only its first legacy code in LegacyCode, nothing is joined.
                InventoryItem target = await Uow.Set<InventoryItem>().FirstOrDefaultAsync(i => !i.Deleted && i.PartNumber == m.PartNumber && i.StageCode2 == m.StageCode2, ct);
                if (target == null)
                {
                    ResponseDto<InventoryItemDto> created = await CreateItemAsAsync(m, ct);
                    if (!created.Success) return ResponseDtoHelper.BadRequest<PbMappingDto>(created.Message);
                    target = await Uow.Set<InventoryItem>().FirstAsync(i => i.Id == created.Data.Id, ct);
                }
                if (string.IsNullOrEmpty(target.LegacyCode)) target.LegacyCode = m.LegacyCode;
                Stamp(target, false);
                m.Status = PbMappingStatus.Approved;
                m.AppliedAt = DateTimeOffset.Now;
            }
            m.ReviewedByUserId = User.UserId;
            m.ReviewedAt = DateTimeOffset.Now;
            m.ReviewNote = PbText.Clean(dto.Note);
            Stamp(m, false);
            await Uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(Dto(m));
        }

        /// <summary>The approval of a mapping may need the item of the part (and its stage item); the approving engineer is not a coding role by himself, so the item is made on his approval. Everything is checked before anything is written.</summary>
        private async Task<ResponseDto<InventoryItemDto>> CreateItemAsAsync(PbLegacyMapping m, CancellationToken ct)
        {
            string pn = PbText.PartNumber(m.PartNumber);
            var errors = new List<string>();
            if (!CheckShape(pn, errors)) return ResponseDtoHelper.BadRequest<InventoryItemDto>(errors[0]);
            string title = PbText.Clean(m.PartTitle) ?? PbText.Clean(m.LegacyTitle);
            InventoryItem baseItem = await FindPartItemAsync(pn, true, ct);
            if (baseItem == null && title == null) return ResponseDtoHelper.BadRequest<InventoryItemDto>("عنوان قطعه معلوم نیست.");
            PbStage stage = null;
            string stageCode = null;
            if (m.StageCode2 != null)
            {
                stage = (await LoadStagesAsync(false, ct)).FirstOrDefault(s => s.Code == m.StageCode2 && s.IsActive);
                if (stage == null) return ResponseDtoHelper.BadRequest<InventoryItemDto>(PbMessages.StageUnknown);
                stageCode = PbCodeRules.StageItemCode(pn, stage.Code, await SeparatorAsync(ct));
                if (await Uow.Set<InventoryItem>().AnyAsync(i => !i.Deleted && i.Code == stageCode, ct)) return ResponseDtoHelper.BadRequest<InventoryItemDto>(InventoryMessages.DuplicateCode + stageCode);
            }
            if (baseItem == null)
            {
                var made = await FindOrCreatePartItemAsync(pn, title, null, m.LegacyMaterial, null, ct);
                if (made.Error != null) return ResponseDtoHelper.BadRequest<InventoryItemDto>(made.Error);
                baseItem = made.Item;
            }
            if (stage == null)
            {
                await Uow.SaveChangesAsync(ct);
                return await _master.GetItemAsync(baseItem.Id, ct);
            }
            if (Uow.Entry(baseItem).State == EntityState.Added) await Uow.SaveChangesAsync(ct);
            var args = new PbCreateItemDto { PartNumber = pn, StageCode2 = stage.Code, Material = m.LegacyMaterial };
            InventoryItem stageItem = NewItem(pn, stage.Code, PbCodeRules.StageItemTitle(baseItem.Title, stage), args, baseItem);
            stageItem.Code = stageCode;
            Uow.Set<InventoryItem>().Add(stageItem);
            await Uow.SaveChangesAsync(ct);
            return await _master.GetItemAsync(stageItem.Id, ct);
        }
    }
}
