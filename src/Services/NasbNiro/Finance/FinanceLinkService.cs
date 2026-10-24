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
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Finance
{
    public interface IFinanceLinkService
    {
        Task<ResponseDto<PagedList<AccountingEventDto>>> GetEventsAsync(AccountingEventQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<List<AccountMappingDto>>> GetAccountMappingsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<AccountMappingDto>> SaveAccountMappingAsync(AccountMappingDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<CostCenterDto>>> GetCostCentersAsync(CancellationToken cancellationToken);
        Task<ResponseDto<CostCenterDto>> SaveCostCenterAsync(CostCenterDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FinanceCheckDto>> CheckAsync(CancellationToken cancellationToken);
        Task<(byte[] Content, string FileName, string Error)> ExportEventsAsync(ExportEventsDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<int>> MarkEventsAsync(MarkEventsDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<WorkOrderCostDto>>> GetWorkOrderCostsAsync(int? contractId, CancellationToken cancellationToken);
        Task<ResponseDto<ValuationDto>> GetValuationAsync(string contains, CancellationToken cancellationToken);
    }

    /// <summary>
    /// The finance side of PMIS: accounting events waiting for Sepidar, the account and cost-centre mapping,
    /// the cost of every work order and project, the stock valuation and its reconciliation with the events.
    /// Until the Sepidar web-service documentation is available the events are exported as a voucher file;
    /// the same events (with their unique key) will be sent by the API client.
    /// </summary>
    public class FinanceLinkService : IFinanceLinkService
    {
        private readonly IUnitOfWork _uow;
        private readonly ICostingService _costing;

        public FinanceLinkService(IUnitOfWork uow, ICostingService costing)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _costing = costing ?? throw new ArgumentNullException(nameof(costing));
        }

        private static readonly AccountingEventStatus[] Waiting = { AccountingEventStatus.Pending, AccountingEventStatus.Failed };

        public async Task<ResponseDto<PagedList<AccountingEventDto>>> GetEventsAsync(AccountingEventQuery query, CancellationToken cancellationToken)
        {
            query ??= new AccountingEventQuery();
            IQueryable<AccountingEvent> events = _uow.Set<AccountingEvent>();
            if (query.Status.HasValue) events = events.Where(e => e.Status == query.Status.Value);
            else if (!query.IncludeNotRequired) events = events.Where(e => e.Status != AccountingEventStatus.NotRequired);
            if (query.Type.HasValue) events = events.Where(e => e.Type == query.Type.Value);
            if (query.From.HasValue) events = events.Where(e => e.EventDate >= query.From.Value.Date);
            if (query.To.HasValue) events = events.Where(e => e.EventDate < query.To.Value.Date.AddDays(1));
            string text = query.Contains?.Trim();
            if (!string.IsNullOrEmpty(text)) events = events.Where(e => e.SourceNumber.Contains(text) || e.Description.Contains(text));
            PagedList<AccountingEventDto> page = await PagedList<AccountingEventDto>.CreateAsync(
                events.OrderByDescending(e => e.Id).Select(e => new AccountingEventDto
                {
                    Id = e.Id, Type = e.Type, EventDate = e.EventDate, Status = e.Status, SourceType = e.SourceType, SourceId = e.SourceId,
                    SourceNumber = e.SourceNumber, Description = e.Description, Key = e.Key, SentAt = e.SentAt,
                    ExternalReference = e.ExternalReference, Error = e.Error, ReversesEventId = e.ReversesEventId,
                    Amount = e.Lines.Sum(l => (decimal?)l.Debit) ?? 0,
                    Lines = e.Lines.OrderBy(l => l.RowNumber).Select(l => new AccountingEventLineDto
                    {
                        RowNumber = l.RowNumber, AccountRole = l.AccountRole, Debit = l.Debit, Credit = l.Credit,
                        ProviderId = l.ProviderId, ProviderName = l.Provider.Name, WorkOrderId = l.WorkOrderId, ContractId = l.ContractId,
                        CostCenter = l.CostCenter, Description = l.Description,
                    }).ToList(),
                }), query.PageNumber, query.PageSize, cancellationToken);
            return ResponseDtoHelper.Ok(page);
        }

        public async Task<ResponseDto<List<AccountMappingDto>>> GetAccountMappingsAsync(CancellationToken cancellationToken)
        {
            await _costing.EnsureAccountRolesAsync(cancellationToken);
            List<AccountMappingDto> list = await _uow.Set<AccountMapping>().OrderBy(m => m.Role)
                .Select(m => new AccountMappingDto { Id = m.Id, Role = m.Role, Title = m.Title, SepidarAccountCode = m.SepidarAccountCode, Description = m.Description, IsActive = m.IsActive })
                .ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<AccountMappingDto>> SaveAccountMappingAsync(AccountMappingDto dto, CancellationToken cancellationToken)
        {
            string role = dto?.Role?.Trim();
            if (string.IsNullOrEmpty(role)) return ResponseDtoHelper.BadRequest<AccountMappingDto>("نقش حساب را وارد کنید.");
            AccountMapping mapping = await _uow.Set<AccountMapping>().FirstOrDefaultAsync(m => m.Role == role, cancellationToken);
            if (mapping == null)
            {
                mapping = new AccountMapping { Role = role };
                _uow.Set<AccountMapping>().Add(mapping);
            }
            mapping.Title = dto.Title?.Trim() ?? mapping.Title ?? role;
            mapping.SepidarAccountCode = string.IsNullOrWhiteSpace(dto.SepidarAccountCode) ? null : dto.SepidarAccountCode.Trim();
            mapping.Description = dto.Description?.Trim();
            mapping.IsActive = dto.IsActive;
            await _uow.SaveChangesAsync(cancellationToken);
            dto.Id = mapping.Id;
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<List<CostCenterDto>>> GetCostCentersAsync(CancellationToken cancellationToken)
        {
            List<CostCenterDto> list = await _uow.Set<CostCenter>().OrderBy(c => c.Code)
                .Select(c => new CostCenterDto { Id = c.Id, Code = c.Code, Title = c.Title, SepidarCode = c.SepidarCode, IsActive = c.IsActive, IsProduction = c.IsProduction })
                .ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<CostCenterDto>> SaveCostCenterAsync(CostCenterDto dto, CancellationToken cancellationToken)
        {
            string code = dto?.Code?.Trim();
            string title = dto?.Title?.Trim();
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(title)) return ResponseDtoHelper.BadRequest<CostCenterDto>("کد و عنوان مرکز هزینه را وارد کنید.");
            if (await _uow.Set<CostCenter>().AnyAsync(c => c.Code == code && c.Id != (dto.Id ?? 0), cancellationToken))
                return ResponseDtoHelper.BadRequest<CostCenterDto>("این کد قبلاً ثبت شده است: " + code);
            CostCenter center = dto.Id.HasValue ? await _uow.Set<CostCenter>().FirstOrDefaultAsync(c => c.Id == dto.Id.Value, cancellationToken) : null;
            if (dto.Id.HasValue && center == null) return ResponseDtoHelper.NotFound<CostCenterDto>("پیدا نشد.");
            if (center == null) { center = new CostCenter(); _uow.Set<CostCenter>().Add(center); }
            center.Code = code;
            center.Title = title;
            center.SepidarCode = string.IsNullOrWhiteSpace(dto.SepidarCode) ? null : dto.SepidarCode.Trim();
            center.IsActive = dto.IsActive;
            center.IsProduction = dto.IsProduction;
            await _uow.SaveChangesAsync(cancellationToken);
            dto.Id = center.Id;
            return ResponseDtoHelper.Ok(dto);
        }

        private static string Lookup(Dictionary<string, string> accounts, string role)
        {
            if (accounts.TryGetValue(role, out string code) && code != null) return code;
            int dot = role.IndexOf('.');
            return dot > 0 && accounts.TryGetValue(role.Substring(0, dot), out code) ? code : null; // Inventory.02 -> Inventory
        }

        public async Task<ResponseDto<FinanceCheckDto>> CheckAsync(CancellationToken cancellationToken)
        {
            await _costing.EnsureAccountRolesAsync(cancellationToken);
            var result = new FinanceCheckDto();
            Dictionary<string, string> accounts = await _uow.Set<AccountMapping>().Where(m => m.IsActive)
                .ToDictionaryAsync(m => m.Role, m => m.SepidarAccountCode, StringComparer.OrdinalIgnoreCase, cancellationToken);
            var waitingLines = await _uow.Set<AccountingEventLine>()
                .Where(l => Waiting.Contains(l.Event.Status))
                .Select(l => new { l.AccountRole, l.ProviderId, l.WorkOrderId, l.ContractId, l.ItemId }).ToListAsync(cancellationToken);
            result.PendingEvents = await _uow.Set<AccountingEvent>().CountAsync(e => Waiting.Contains(e.Status), cancellationToken);
            result.UnmappedRoles = waitingLines.Select(l => l.AccountRole).Distinct().Where(r => Lookup(accounts, r) == null).OrderBy(r => r).ToList();

            List<SepidarMapping> mappings = await _uow.Set<SepidarMapping>().ToListAsync(cancellationToken);
            List<int> providers = waitingLines.Where(l => l.ProviderId.HasValue).Select(l => l.ProviderId.Value).Distinct().ToList();
            var providerNames = await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>()
                .Where(p => providers.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToListAsync(cancellationToken);
            result.UnmappedProviders = providerNames
                .Where(p => !mappings.Any(m => m.EntityType == SepidarLinkService.ProviderType && m.EntityId == p.Id)).Select(p => p.Name).ToList();
            List<int> workOrders = waitingLines.Where(l => l.WorkOrderId.HasValue).Select(l => l.WorkOrderId.Value).Distinct().ToList();
            List<int> contracts = waitingLines.Where(l => l.ContractId.HasValue).Select(l => l.ContractId.Value).Distinct().ToList();
            result.UnmappedProjects = workOrders.Where(w => !mappings.Any(m => m.EntityType == SepidarLinkService.WorkOrderType && m.EntityId == w)).Count()
                                    + contracts.Where(c => !mappings.Any(m => m.EntityType == SepidarLinkService.ContractType && m.EntityId == c)).Count();
            result.ItemsWithoutSepidarCode = await _uow.Set<InventoryItem>().CountAsync(i => !i.Deleted && i.IsActive && i.SepidarCode == null, cancellationToken);
            result.WarehousesWithoutSepidarCode = await _uow.Set<Warehouse>().CountAsync(w => !w.Deleted && w.IsActive && w.SepidarCode == null, cancellationToken);
            result.SepidarApiConfigured = false;
            result.Ready = result.UnmappedRoles.Count == 0 && result.UnmappedProviders.Count == 0 && result.UnmappedProjects == 0;
            return ResponseDtoHelper.Ok(result);
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        public async Task<(byte[] Content, string FileName, string Error)> ExportEventsAsync(ExportEventsDto dto, CancellationToken cancellationToken)
        {
            dto ??= new ExportEventsDto();
            IQueryable<AccountingEvent> query = _uow.Set<AccountingEvent>().Include(e => e.Lines).ThenInclude(l => l.Provider);
            query = dto.EventIds != null && dto.EventIds.Count > 0
                ? query.Where(e => dto.EventIds.Contains(e.Id) && e.Status != AccountingEventStatus.NotRequired)
                : query.Where(e => Waiting.Contains(e.Status));
            List<AccountingEvent> events = await query.OrderBy(e => e.EventDate).ThenBy(e => e.Id).AsSplitQuery().ToListAsync(cancellationToken);
            if (events.Count == 0) return (null, null, "رویدادی برای ارسال به سپیدار نیست.");

            await _costing.EnsureAccountRolesAsync(cancellationToken);
            Dictionary<string, string> accounts = await _uow.Set<AccountMapping>().Where(m => m.IsActive)
                .ToDictionaryAsync(m => m.Role, m => m.SepidarAccountCode, StringComparer.OrdinalIgnoreCase, cancellationToken);
            Dictionary<string, string> titles = await _uow.Set<AccountMapping>().ToDictionaryAsync(m => m.Role, m => m.Title, StringComparer.OrdinalIgnoreCase, cancellationToken);
            List<SepidarMapping> mappings = await _uow.Set<SepidarMapping>().ToListAsync(cancellationToken);
            string Map(string type, int? id) => id.HasValue ? mappings.FirstOrDefault(m => m.EntityType == type && m.EntityId == id.Value)?.SepidarCode : null;

            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", new[]
            {
                "کلید رویداد", "شماره رویداد", "نوع", "تاریخ", "سند مبدا", "شرح سند", "ردیف", "نقش حساب", "عنوان حساب", "کد حساب سپیدار",
                "بدهکار", "بستانکار", "تفصیلی طرف حساب", "طرف حساب", "تفصیلی پروژه", "مرکز هزینه", "شرح ردیف",
            }));
            foreach (AccountingEvent e in events)
            {
                foreach (AccountingEventLine l in e.Lines.OrderBy(x => x.RowNumber))
                {
                    csv.AppendLine(string.Join(",", new[]
                    {
                        e.Key.ToString(), e.Id.ToString(CultureInfo.InvariantCulture), ((int)e.Type).ToString(CultureInfo.InvariantCulture),
                        InventoryText.PersianDate(e.EventDate), Csv(e.SourceNumber), Csv(e.Description), l.RowNumber.ToString(CultureInfo.InvariantCulture),
                        Csv(l.AccountRole), Csv(titles.TryGetValue(l.AccountRole, out string t) ? t : null), Csv(Lookup(accounts, l.AccountRole)),
                        l.Debit.ToString("0", CultureInfo.InvariantCulture), l.Credit.ToString("0", CultureInfo.InvariantCulture),
                        Csv(Map(SepidarLinkService.ProviderType, l.ProviderId)), Csv(l.Provider?.Name),
                        Csv(Map(SepidarLinkService.WorkOrderType, l.WorkOrderId) ?? Map(SepidarLinkService.ContractType, l.ContractId)),
                        Csv(l.CostCenter), Csv(l.Description),
                    }));
                }
            }
            if (dto.MarkExported)
            {
                DateTimeOffset now = DateTimeOffset.Now;
                foreach (AccountingEvent e in events.Where(x => Waiting.Contains(x.Status)))
                {
                    e.Status = AccountingEventStatus.Exported;
                    e.SentAt = now;
                    e.Attempts++;
                }
                await _uow.SaveChangesAsync(cancellationToken);
            }
            byte[] bom = Encoding.UTF8.GetPreamble();
            return (bom.Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), $"sepidar-vouchers-{FiscalCalendars.Current.Stamp(DateTime.Now)}-{DateTime.Now:HHmm}.csv", null);
        }

        public async Task<ResponseDto<int>> MarkEventsAsync(MarkEventsDto dto, CancellationToken cancellationToken)
        {
            if (dto?.EventIds == null || dto.EventIds.Count == 0) return ResponseDtoHelper.BadRequest<int>("رویدادی انتخاب نشده است.");
            if (dto.Status != AccountingEventStatus.Sent && dto.Status != AccountingEventStatus.Pending && dto.Status != AccountingEventStatus.Exported)
                return ResponseDtoHelper.BadRequest<int>("وضعیت نامعتبر است.");
            // an event that already has a voucher in the ledger cannot be marked again (it would be posted a second time or leave a cancelled document in the books)
            List<int> withVoucher = await _uow.Set<FnVoucher>().Where(v => v.AccountingEventId != null && dto.EventIds.Contains(v.AccountingEventId.Value)).Select(v => v.AccountingEventId.Value).ToListAsync(cancellationToken);
            List<AccountingEvent> events = await _uow.Set<AccountingEvent>().Where(e => dto.EventIds.Contains(e.Id) && e.Status != AccountingEventStatus.NotRequired && !withVoucher.Contains(e.Id))
                .ToListAsync(cancellationToken);
            foreach (AccountingEvent e in events)
            {
                e.Status = dto.Status;
                e.ExternalReference = string.IsNullOrWhiteSpace(dto.ExternalReference) ? e.ExternalReference : dto.ExternalReference.Trim();
                if (dto.Status == AccountingEventStatus.Sent) e.SentAt ??= DateTimeOffset.Now;
            }
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(events.Count);
        }

        public async Task<ResponseDto<List<WorkOrderCostDto>>> GetWorkOrderCostsAsync(int? contractId, CancellationToken cancellationToken)
        {
            IQueryable<CostEntry> entries = _uow.Set<CostEntry>();
            if (contractId.HasValue) entries = entries.Where(c => c.ContractId == contractId.Value);
            var rows = await entries.GroupBy(c => new { c.ContractId, c.WorkOrderId, c.CostCenter, c.Kind })
                .Select(g => new { g.Key.ContractId, g.Key.WorkOrderId, g.Key.CostCenter, g.Key.Kind, Amount = g.Sum(c => c.Amount) })
                .ToListAsync(cancellationToken);
            List<int> woIds = rows.Where(r => r.WorkOrderId.HasValue).Select(r => r.WorkOrderId.Value).Distinct().ToList();
            List<int> cIds = rows.Where(r => r.ContractId.HasValue).Select(r => r.ContractId.Value).Distinct().ToList();
            var workOrders = await _uow.Set<WorkOrder>().Where(w => woIds.Contains(w.Id)).Select(w => new { w.Id, w.Code, w.Title }).ToDictionaryAsync(w => w.Id, cancellationToken);
            var contracts = await _uow.Set<ContractEntity>().Where(c => cIds.Contains(c.Id)).Select(c => new { c.Id, c.Code, c.Title }).ToDictionaryAsync(c => c.Id, cancellationToken);
            List<WorkOrderCostDto> result = rows.GroupBy(r => new { r.ContractId, r.WorkOrderId, r.CostCenter }).Select(g => new WorkOrderCostDto
            {
                ContractId = g.Key.ContractId,
                ContractCode = g.Key.ContractId.HasValue && contracts.TryGetValue(g.Key.ContractId.Value, out var c) ? c.Code : null,
                ContractTitle = g.Key.ContractId.HasValue && contracts.TryGetValue(g.Key.ContractId.Value, out var c2) ? c2.Title : null,
                WorkOrderId = g.Key.WorkOrderId,
                WorkOrderCode = g.Key.WorkOrderId.HasValue && workOrders.TryGetValue(g.Key.WorkOrderId.Value, out var w) ? w.Code : null,
                WorkOrderTitle = g.Key.WorkOrderId.HasValue && workOrders.TryGetValue(g.Key.WorkOrderId.Value, out var w2) ? w2.Title : null,
                CostCenter = g.Key.CostCenter,
                Material = g.Where(x => x.Kind == CostKind.Material).Sum(x => x.Amount),
                Subcontract = g.Where(x => x.Kind == CostKind.Subcontract).Sum(x => x.Amount),
                Services = g.Where(x => x.Kind == CostKind.PurchasedService).Sum(x => x.Amount),
                Labour = g.Where(x => x.Kind == CostKind.Labour).Sum(x => x.Amount),
                Overhead = g.Where(x => x.Kind == CostKind.Overhead).Sum(x => x.Amount),
                Total = g.Sum(x => x.Amount),
            }).OrderBy(r => r.ContractCode).ThenBy(r => r.WorkOrderCode).ToList();
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<ValuationDto>> GetValuationAsync(string contains, CancellationToken cancellationToken)
        {
            IQueryable<ItemCost> costs = _uow.Set<ItemCost>().Where(c => c.Quantity != 0 || c.Value != 0);
            if (!string.IsNullOrWhiteSpace(contains))
            {
                string text = contains.Trim();
                costs = costs.Where(c => c.Item.Code.Contains(text) || c.Item.Title.Contains(text));
            }
            var result = new ValuationDto
            {
                Items = await costs.OrderBy(c => c.Item.Code).Select(c => new ItemValuationDto
                {
                    ItemId = c.ItemId, ItemCode = c.Item.Code, ItemTitle = c.Item.Title, Unit = c.Item.Unit,
                    Quantity = c.Quantity, AverageCost = c.AverageCost, Value = c.Value, LastPurchasePrice = c.LastPurchasePrice,
                }).Take(5000).ToListAsync(cancellationToken),
            };
            var allCosts = await _uow.Set<ItemCost>().Select(c => new { c.Item.Code, c.Value }).ToListAsync(cancellationToken);
            result.TotalValue = allCosts.Sum(c => c.Value);

            // reconciliation: stock sub-ledger (item values) against the inventory lines of the accounting events
            var ledger = await _uow.Set<AccountingEventLine>()
                .Where(l => (l.AccountRole == AccountRoles.Inventory || l.AccountRole.StartsWith(AccountRoles.Inventory + ".")) && l.Event.Status != AccountingEventStatus.NotRequired)
                .GroupBy(l => l.AccountRole).Select(g => new { Role = g.Key, Balance = g.Sum(l => l.Debit - l.Credit) }).ToListAsync(cancellationToken);
            var subLedger = allCosts.GroupBy(c => AccountRoles.InventoryOf(c.Code)).ToDictionary(g => g.Key, g => g.Sum(c => c.Value));
            foreach (string role in subLedger.Keys.Union(ledger.Select(l => l.Role)).OrderBy(r => r))
            {
                decimal sub = subLedger.TryGetValue(role, out decimal v) ? v : 0;
                decimal events = ledger.FirstOrDefault(l => l.Role == role)?.Balance ?? 0;
                result.Reconciliation.Add(new ReconciliationRowDto { AccountRole = role, StockValue = sub, EventsBalance = events, Difference = sub - events });
            }
            // value moved between groups by rounding/revaluation is booked on "Inventory": compare totals too
            result.TotalEventsBalance = ledger.Sum(l => l.Balance);
            return ResponseDtoHelper.Ok(result);
        }
    }
}
