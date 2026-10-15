using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.InventoryDtos;

namespace Services.NasbNiro.Inventory
{
    public interface IOpeningStockService
    {
        /// <summary>
        /// Turns cleaned rows of the old warehouse data into one draft "opening balance" document per warehouse.
        /// The drafts then need the warehouse supervisor and the planning manager before they are confirmed.
        /// </summary>
        Task<ResponseDto<OpeningImportResultDto>> ImportAsync(OpeningImportDto dto, CancellationToken cancellationToken);
    }

    public class OpeningStockService : IOpeningStockService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IInventoryDocumentService _documents;

        public OpeningStockService(IUnitOfWork uow, IInventoryUserContext user, IInventoryDocumentService documents)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        }

        public async Task<ResponseDto<OpeningImportResultDto>> ImportAsync(OpeningImportDto dto, CancellationToken cancellationToken)
        {
            var result = new OpeningImportResultDto();
            List<OpeningImportRowDto> rows = (dto?.Rows ?? new List<OpeningImportRowDto>()).Where(r => r != null).ToList();
            if (rows.Count == 0) return ResponseDtoHelper.BadRequest<OpeningImportResultDto>("فایل هیچ ردیفی ندارد.");
            if (rows.Count > 20000) return ResponseDtoHelper.BadRequest<OpeningImportResultDto>("حداکثر ۲۰٬۰۰۰ ردیف در هر بار.");
            DateTime date = dto.DocumentDate == default ? DateTime.Today : dto.DocumentDate.Date;

            List<InventoryItem> items = await _uow.Set<InventoryItem>().Where(i => !i.Deleted).ToListAsync(cancellationToken);
            var byCode = items.GroupBy(i => i.Code, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var byLegacy = items.Where(i => !string.IsNullOrWhiteSpace(i.LegacyCode))
                .GroupBy(i => i.LegacyCode, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            List<Warehouse> warehouses = await _uow.Set<Warehouse>().Include(w => w.Locations).Where(w => !w.Deleted).ToListAsync(cancellationToken);

            var lines = new Dictionary<int, List<SaveDocumentLineDto>>();
            var serialsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < rows.Count; index++)
            {
                OpeningImportRowDto row = rows[index];
                string label = $"ردیف {index + 2}: ";
                string code = InventoryText.Clean(row.ItemCode);
                if (code == null && InventoryText.Clean(row.WarehouseCode) == null) continue;

                InventoryItem item = null;
                if (code != null && !byCode.TryGetValue(code, out item) && byLegacy.TryGetValue(code, out List<InventoryItem> legacy))
                {
                    if (legacy.Count == 1) item = legacy[0];
                    else { result.Errors.Add(label + $"کد قدیم {code} به چند کالا وصل است؛ کد جدید را بنویسید."); continue; }
                }
                if (item == null) { result.Errors.Add(label + $"کالای {code} تعریف نشده است؛ اول کالاها را وارد کنید."); continue; }

                Warehouse warehouse = warehouses.FirstOrDefault(w => string.Equals(w.Code, InventoryText.Clean(row.WarehouseCode), StringComparison.OrdinalIgnoreCase));
                if (warehouse == null) { result.Errors.Add(label + $"انبار «{row.WarehouseCode}» تعریف نشده است."); continue; }

                int? locationId = null;
                string locationCode = InventoryText.Clean(row.LocationCode)?.ToUpperInvariant();
                if (locationCode != null)
                {
                    WarehouseLocation location = warehouse.Locations.FirstOrDefault(l => !l.Deleted && string.Equals(l.Code, locationCode, StringComparison.OrdinalIgnoreCase));
                    if (location == null)
                    {
                        if (!dto.CreateLocations) { result.Errors.Add(label + $"محل {locationCode} در انبار {warehouse.Code} نیست."); continue; }
                        location = new WarehouseLocation
                        {
                            WarehouseId = warehouse.Id, Code = locationCode, Title = locationCode, IsActive = true,
                            CreateDate = DateTimeOffset.Now, CreatorUserId = _user.UserId, ModifyDate = DateTimeOffset.Now, ModifierUserId = _user.UserId,
                        };
                        _uow.Set<WarehouseLocation>().Add(location);
                        warehouse.Locations.Add(location);
                        await _uow.SaveChangesAsync(cancellationToken);
                        result.LocationsCreated++;
                    }
                    locationId = location.Id;
                }

                List<string> serials = (row.SerialNumbers ?? new List<string>()).Select(InventoryText.Clean).Where(s => s != null).Distinct().ToList();
                decimal quantity = row.Quantity;
                if (item.TrackingMode == ItemTrackingMode.Serial)
                {
                    if (serials.Count == 0) { result.Errors.Add(label + $"کالای {item.Code} سریالی است؛ سریال (برچسب) را بنویسید."); continue; }
                    string dup = serials.FirstOrDefault(s => !serialsSeen.Add(item.Id + "|" + s));
                    if (dup != null) { result.Errors.Add(label + $"سریال {dup} تکراری است."); continue; }
                    quantity = serials.Count;
                }
                else
                {
                    serials.Clear();
                }
                if (quantity <= 0) { result.Skipped++; continue; } // zero balance: nothing to bring in
                string lot = InventoryText.Clean(row.LotNumber);
                if (item.TrackingMode == ItemTrackingMode.Lot && lot == null)
                {
                    // one label = one package: the label number is the lot when no heat number is known
                    lot = serials.FirstOrDefault() ?? InventoryText.Clean(row.LabelNumber);
                    if (lot == null) { result.Errors.Add(label + $"کالای {item.Code} بچ‌دار است؛ شماره بچ یا برچسب را بنویسید."); continue; }
                }

                if (!lines.TryGetValue(warehouse.Id, out List<SaveDocumentLineDto> list)) lines[warehouse.Id] = list = new List<SaveDocumentLineDto>();
                list.Add(new SaveDocumentLineDto
                {
                    ItemId = item.Id,
                    Quantity = quantity,
                    LocationId = locationId,
                    LotNumber = item.TrackingMode == ItemTrackingMode.Lot ? lot : null,
                    HeatNumber = InventoryText.Clean(row.HeatNumber),
                    CertificateNumber = InventoryText.Clean(row.CertificateNumber),
                    UnitPrice = row.UnitPrice,
                    Description = InventoryText.Clean(row.Description) ?? (InventoryText.Clean(row.LabelNumber) != null ? "برچسب " + row.LabelNumber : null),
                    SerialNumbers = serials,
                });
            }

            if (result.Errors.Count > 0 && !dto.ImportValidRows)
                return ResponseDtoHelper.Ok(result); // nothing created: fix the file first (or ask to import the valid rows)

            foreach (var pair in lines)
            {
                // lines of the same item, location and lot are merged (quantity items only)
                var merged = pair.Value
                    .GroupBy(l => new { l.ItemId, l.LocationId, l.LotNumber, Serial = l.SerialNumbers.Count > 0 })
                    .SelectMany(g => g.Key.Serial ? g.ToList() : new List<SaveDocumentLineDto>
                    {
                        new SaveDocumentLineDto
                        {
                            ItemId = g.Key.ItemId, LocationId = g.Key.LocationId, LotNumber = g.Key.LotNumber, Quantity = g.Sum(l => l.Quantity),
                            HeatNumber = g.First().HeatNumber, CertificateNumber = g.First().CertificateNumber, UnitPrice = g.First().UnitPrice,
                            Description = string.Join("، ", g.Select(l => l.Description).Where(d => d != null).Distinct().Take(5)),
                        },
                    }).ToList();
                Warehouse warehouse = warehouses.First(w => w.Id == pair.Key);
                for (int start = 0; start < merged.Count; start += 500)
                {
                    ResponseDto<DocumentDto> saved = await _documents.SaveDraftAsync(new SaveDocumentDto
                    {
                        Type = InventoryDocumentType.OpeningBalance,
                        DocumentDate = date,
                        WarehouseId = warehouse.Id,
                        ReferenceNumber = InventoryText.Clean(dto.Reference) ?? "موجودی اول دوره",
                        Description = $"ورود موجودی اول دوره از داده انبار قبلی ({warehouse.Title})",
                        Lines = merged.Skip(start).Take(500).ToList(),
                    }, cancellationToken);
                    if (!saved.Success) { result.Errors.Add($"انبار {warehouse.Code}: {saved.Message}"); continue; }
                    result.DocumentIds.Add(saved.Data.Id);
                    result.Lines += saved.Data.Lines.Count;
                }
            }
            return ResponseDtoHelper.Ok(result);
        }
    }
}
