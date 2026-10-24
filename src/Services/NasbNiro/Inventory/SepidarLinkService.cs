using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Providers;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Inventory
{
    public interface ISepidarLinkService
    {
        Task<ResponseDto<PagedList<DocumentListItemDto>>> GetOutboxAsync(SepidarOutboxQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<SepidarCheckDto>> CheckAsync(CancellationToken cancellationToken);
        Task<(byte[] Content, string FileName, string Error)> ExportCsvAsync(SepidarExportDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<SepidarMappingDto>>> GetMappingsAsync(string entityType, CancellationToken cancellationToken);
        Task<ResponseDto<SepidarMappingDto>> SaveMappingAsync(SaveSepidarMappingDto dto, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Link to Sepidar accounting. Confirmed warehouse documents wait in an outbox (SepidarStatus = Pending).
    /// They are exported as a CSV file for now; the direct Sepidar web-service connection uses the same
    /// outbox and mapping once its API documentation is available.
    /// </summary>
    public class SepidarLinkService : ISepidarLinkService
    {
        public const string ProviderType = "Provider";
        public const string WorkOrderType = "WorkOrder";
        public const string ContractType = "Contract";

        private readonly IUnitOfWork _uow;

        public SepidarLinkService(IUnitOfWork uow)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
        }

        private static readonly SepidarSyncStatus[] Waiting = { SepidarSyncStatus.Pending, SepidarSyncStatus.Failed };

        public async Task<ResponseDto<PagedList<DocumentListItemDto>>> GetOutboxAsync(SepidarOutboxQuery query, CancellationToken cancellationToken)
        {
            query ??= new SepidarOutboxQuery();
            IQueryable<InventoryDocument> documents = _uow.Set<InventoryDocument>()
                .Where(d => !d.Deleted && d.Status != InventoryDocumentStatus.Draft && d.SepidarStatus != SepidarSyncStatus.NotRequired);
            if (query.Status.HasValue) documents = documents.Where(d => d.SepidarStatus == query.Status.Value);
            PagedList<DocumentListItemDto> page = await PagedList<DocumentListItemDto>.CreateAsync(
                InventoryDocumentService.ListProjection(documents.OrderBy(d => d.SepidarStatus).ThenBy(d => d.DocumentDate).ThenBy(d => d.Id)),
                query.PageNumber, query.PageSize, cancellationToken);
            return ResponseDtoHelper.Ok(page);
        }

        public async Task<ResponseDto<SepidarCheckDto>> CheckAsync(CancellationToken cancellationToken)
        {
            IQueryable<InventoryDocument> waiting = _uow.Set<InventoryDocument>().Where(d => !d.Deleted && Waiting.Contains(d.SepidarStatus));
            var result = new SepidarCheckDto { PendingDocuments = await waiting.CountAsync(cancellationToken) };

            List<int> warehouseIds = (await waiting.Select(d => new { d.WarehouseId, d.DestinationWarehouseId }).ToListAsync(cancellationToken))
                .SelectMany(d => new[] { (int?)d.WarehouseId, d.DestinationWarehouseId }).Where(x => x.HasValue).Select(x => x.Value).Distinct().ToList();
            List<string> warehouses = await _uow.Set<Warehouse>().Where(w => warehouseIds.Contains(w.Id) && w.SepidarCode == null)
                .Select(w => w.Code + " - " + w.Title).ToListAsync(cancellationToken);
            result.MissingCodes.AddRange(warehouses.Select(w => "کد سپیدار انبار: " + w));

            List<string> items = await waiting.SelectMany(d => d.Lines).Select(l => l.Item)
                .Where(i => i.SepidarCode == null).Select(i => i.Code).Distinct().Take(50).ToListAsync(cancellationToken);
            if (items.Count > 0)
                result.MissingCodes.Add("کالاهای بدون کد سپیدار (کد PMIS فرستاده می‌شود): " + string.Join("، ", items));

            List<int> providerIds = await waiting.Where(d => d.ProviderId.HasValue).Select(d => d.ProviderId.Value).Distinct().ToListAsync(cancellationToken);
            List<int> mappedProviders = await _uow.Set<SepidarMapping>().Where(m => m.EntityType == ProviderType && providerIds.Contains(m.EntityId))
                .Select(m => m.EntityId).ToListAsync(cancellationToken);
            List<string> providers = await _uow.Set<Provider>().Where(p => providerIds.Contains(p.Id) && !mappedProviders.Contains(p.Id))
                .Select(p => p.Name).ToListAsync(cancellationToken);
            result.MissingCodes.AddRange(providers.Select(p => "کد تفصیلی تامین‌کننده: " + p));
            return ResponseDtoHelper.Ok(result);
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            bool quote = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            value = value.Replace("\"", "\"\"");
            return quote ? "\"" + value + "\"" : value;
        }

        public async Task<(byte[] Content, string FileName, string Error)> ExportCsvAsync(SepidarExportDto dto, CancellationToken cancellationToken)
        {
            dto ??= new SepidarExportDto();
            IQueryable<InventoryDocument> query = _uow.Set<InventoryDocument>().Where(d => !d.Deleted && d.Status != InventoryDocumentStatus.Draft);
            if (dto.DocumentIds != null && dto.DocumentIds.Count > 0)
            {
                List<int> ids = dto.DocumentIds;
                query = query.Where(d => ids.Contains(d.Id) && d.SepidarStatus != SepidarSyncStatus.NotRequired);
            }
            else
            {
                query = query.Where(d => Waiting.Contains(d.SepidarStatus));
            }

            List<InventoryDocument> documents = await query
                .Include(d => d.Warehouse).Include(d => d.DestinationWarehouse).Include(d => d.Provider).Include(d => d.WorkOrder).Include(d => d.Contract)
                .Include(d => d.Lines).ThenInclude(l => l.Item)
                .Include(d => d.Lines).ThenInclude(l => l.Lot)
                .OrderBy(d => d.DocumentDate).ThenBy(d => d.Id)
                .AsSplitQuery()
                .ToListAsync(cancellationToken);
            if (documents.Count == 0) return (null, null, "سندی برای ارسال به سپیدار نیست.");

            List<SepidarMapping> mappings = await _uow.Set<SepidarMapping>().ToListAsync(cancellationToken);
            string Map(string type, int? id) => id.HasValue ? mappings.FirstOrDefault(m => m.EntityType == type && m.EntityId == id.Value)?.SepidarCode : null;

            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", new[]
            {
                "شماره سند", "نوع سند", "کد نوع", "تاریخ", "وضعیت", "کد انبار سپیدار", "انبار", "کد انبار مقصد سپیدار", "انبار مقصد",
                "کد تفصیلی تامین‌کننده", "تامین‌کننده", "کد پروژه / دستور ساخت", "دستور ساخت", "قرارداد", "شماره مرجع",
                "ردیف", "کد کالا سپیدار", "کد کالا PMIS", "شرح کالا", "واحد", "مقدار", "فی", "مبلغ", "بچ", "شماره ذوب", "شرح",
            }));

            foreach (InventoryDocument d in documents)
            {
                // a cancelled document that was already sent goes out as a reversal (negative quantities)
                bool reversal = d.Status == InventoryDocumentStatus.Cancelled;
                string projectCode = Map(WorkOrderType, d.WorkOrderId) ?? Map(ContractType, d.ContractId);
                foreach (InventoryDocumentLine l in d.Lines.OrderBy(x => x.RowNumber))
                {
                    decimal quantity = reversal ? -l.Quantity : l.Quantity;
                    decimal? amount = l.UnitPrice.HasValue ? quantity * l.UnitPrice.Value : (decimal?)null;
                    csv.AppendLine(string.Join(",", new[]
                    {
                        Csv(d.Number),
                        Csv(InventoryDocumentRules.Title(d.Type)),
                        ((int)d.Type).ToString(CultureInfo.InvariantCulture),
                        InventoryText.PersianDate(d.DocumentDate),
                        reversal ? "ابطال" : "تایید",
                        Csv(d.Warehouse?.SepidarCode),
                        Csv(d.Warehouse?.Title),
                        Csv(d.DestinationWarehouse?.SepidarCode),
                        Csv(d.DestinationWarehouse?.Title),
                        Csv(Map(ProviderType, d.ProviderId)),
                        Csv(d.Provider?.Name),
                        Csv(projectCode),
                        Csv(d.WorkOrder?.Code),
                        Csv(d.Contract?.Code),
                        Csv(d.ReferenceNumber),
                        l.RowNumber.ToString(CultureInfo.InvariantCulture),
                        Csv(l.Item?.SepidarCode ?? l.Item?.Code),
                        Csv(l.Item?.Code),
                        Csv(l.Item?.Title),
                        Csv(l.Item?.Unit),
                        InventoryText.Number(quantity),
                        l.UnitPrice.HasValue ? InventoryText.Number(l.UnitPrice.Value) : "",
                        amount.HasValue ? InventoryText.Number(amount.Value) : "",
                        Csv(l.Lot?.LotNumber ?? l.LotNumber),
                        Csv(l.Lot?.HeatNumber ?? l.HeatNumber),
                        Csv(l.Description),
                    }));
                }
            }

            if (dto.MarkExported)
            {
                DateTimeOffset now = DateTimeOffset.Now;
                foreach (InventoryDocument d in documents.Where(x => Waiting.Contains(x.SepidarStatus)))
                {
                    // a reversal that has been exported needs nothing more
                    d.SepidarStatus = SepidarSyncStatus.Exported;
                    d.SepidarExportedAt = now;
                }
                await _uow.SaveChangesAsync(cancellationToken);
            }

            byte[] bom = Encoding.UTF8.GetPreamble(); // Excel needs the BOM to read Persian text
            byte[] body = Encoding.UTF8.GetBytes(csv.ToString());
            string fileName = $"sepidar-export-{Services.NasbNiro.Finance.FiscalCalendars.Current.Stamp(DateTime.Now)}-{DateTime.Now:HHmm}.csv";
            return (bom.Concat(body).ToArray(), fileName, null);
        }

        public async Task<ResponseDto<List<SepidarMappingDto>>> GetMappingsAsync(string entityType, CancellationToken cancellationToken)
        {
            List<SepidarMapping> mappings = await _uow.Set<SepidarMapping>().Where(m => m.EntityType == entityType).ToListAsync(cancellationToken);
            List<SepidarMappingDto> rows;
            switch (entityType)
            {
                case ProviderType:
                    rows = await _uow.Set<Provider>().Where(p => !p.Deleted).OrderBy(p => p.Name)
                        .Select(p => new SepidarMappingDto { EntityType = ProviderType, EntityId = p.Id, Code = p.Id.ToString(), Title = p.Name })
                        .ToListAsync(cancellationToken);
                    break;
                case WorkOrderType:
                    rows = await _uow.Set<WorkOrder>().Where(w => !w.Deleted).OrderBy(w => w.Code)
                        .Select(w => new SepidarMappingDto { EntityType = WorkOrderType, EntityId = w.Id, Code = w.Code, Title = w.Title })
                        .ToListAsync(cancellationToken);
                    break;
                case ContractType:
                    rows = await _uow.Set<ContractEntity>().Where(c => !c.Deleted).OrderBy(c => c.Code)
                        .Select(c => new SepidarMappingDto { EntityType = ContractType, EntityId = c.Id, Code = c.Code, Title = c.Title })
                        .ToListAsync(cancellationToken);
                    break;
                default:
                    return ResponseDtoHelper.BadRequest<List<SepidarMappingDto>>("نوع نامعتبر است (Provider، WorkOrder یا Contract).");
            }
            foreach (SepidarMappingDto row in rows)
                row.SepidarCode = mappings.FirstOrDefault(m => m.EntityId == row.EntityId)?.SepidarCode;
            return ResponseDtoHelper.Ok(rows);
        }

        public async Task<ResponseDto<SepidarMappingDto>> SaveMappingAsync(SaveSepidarMappingDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || (dto.EntityType != ProviderType && dto.EntityType != WorkOrderType && dto.EntityType != ContractType))
                return ResponseDtoHelper.BadRequest<SepidarMappingDto>("نوع نامعتبر است (Provider، WorkOrder یا Contract).");
            bool exists = dto.EntityType == ProviderType ? await _uow.Set<Provider>().AnyAsync(p => p.Id == dto.EntityId, cancellationToken)
                : dto.EntityType == WorkOrderType ? await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.EntityId, cancellationToken)
                : await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.EntityId, cancellationToken);
            if (!exists) return ResponseDtoHelper.NotFound<SepidarMappingDto>(InventoryMessages.NotFound);

            string code = InventoryText.Clean(dto.SepidarCode);
            SepidarMapping mapping = await _uow.Set<SepidarMapping>()
                .FirstOrDefaultAsync(m => m.EntityType == dto.EntityType && m.EntityId == dto.EntityId, cancellationToken);
            if (code == null)
            {
                if (mapping != null) _uow.Set<SepidarMapping>().Remove(mapping);
            }
            else if (mapping == null)
            {
                _uow.Set<SepidarMapping>().Add(new SepidarMapping { EntityType = dto.EntityType, EntityId = dto.EntityId, SepidarCode = code });
            }
            else
            {
                mapping.SepidarCode = code;
            }
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(new SepidarMappingDto { EntityType = dto.EntityType, EntityId = dto.EntityId, SepidarCode = code });
        }
    }
}
