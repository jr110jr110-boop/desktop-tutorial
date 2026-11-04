using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Office;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.ProcurementDtos;

namespace Services.NasbNiro.Inventory
{
    public interface IWarehouseAlertService
    {
        /// <summary>PB5 (items 2 and 3): in-app alerts for consignment returns (7 and 3 days before and when overdue) and for incoming inspections waiting too long (3 and 5 working days).</summary>
        Task<ResponseDto<AlertRunResultDto>> RunAsync(DateTime? asOf, CancellationToken cancellationToken);
    }

    /// <summary>In-app notifications only: the SMS and e-mail gateways stay switched off for this module.</summary>
    public class WarehouseAlertService : IWarehouseAlertService
    {
        public const string StoreRoles = "WAREHOUSE,WHSUPERVISOR,PLANNING";

        private readonly IUnitOfWork _uow;
        private readonly INotificationService _notify;

        public WarehouseAlertService(IUnitOfWork uow, INotificationService notify)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _notify = notify ?? throw new ArgumentNullException(nameof(notify));
        }

        public async Task<ResponseDto<AlertRunResultDto>> RunAsync(DateTime? asOf, CancellationToken ct)
        {
            DateTime today = (asOf ?? DateTime.Today).Date;
            var result = new AlertRunResultDto { AsOf = today };
            Dictionary<string, decimal> settings = await Quality.QualitySettingKeys.LoadAsync(_uow, ct);

            // ---------------------------------------------------------------- consignment
            int d1 = (int)settings[Quality.QualitySettingKeys.ConsignmentAlertDays1], d2 = (int)settings[Quality.QualitySettingKeys.ConsignmentAlertDays2];
            var receipts = await _uow.Set<InventoryDocument>().AsNoTracking()
                .Where(d => !d.Deleted && d.Type == InventoryDocumentType.ConsignmentReceipt && d.Status == InventoryDocumentStatus.Confirmed && d.ConsignmentDueDate != null)
                .Select(d => new { d.Id, d.Number, Due = d.ConsignmentDueDate.Value, Provider = d.Provider.Name }).ToListAsync(ct);
            List<int> receiptIds = receipts.Select(r => r.Id).ToList();
            Dictionary<int, decimal> received = (await _uow.Set<InventoryDocumentLine>().AsNoTracking().Where(l => receiptIds.Contains(l.DocumentId))
                .GroupBy(l => l.DocumentId).Select(g => new { g.Key, Q = g.Sum(x => x.Quantity) }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.Q);
            Dictionary<int, decimal> returned = (await _uow.Set<InventoryDocumentLine>().AsNoTracking()
                .Where(l => l.Document.ConsignmentOfDocumentId != null && receiptIds.Contains(l.Document.ConsignmentOfDocumentId.Value) && !l.Document.Deleted
                            && l.Document.Type == InventoryDocumentType.ConsignmentIssue && l.Document.Status == InventoryDocumentStatus.Confirmed)
                .GroupBy(l => l.Document.ConsignmentOfDocumentId.Value).Select(g => new { g.Key, Q = g.Sum(x => x.Quantity) }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.Q);
            foreach (var r in receipts)
            {
                decimal open = (received.TryGetValue(r.Id, out decimal a) ? a : 0m) - (returned.TryGetValue(r.Id, out decimal b) ? b : 0m);
                if (open <= 0) continue;
                result.ConsignmentOpen++;
                int level = ConsignmentRules.Level(r.Due, today, d1, d2);
                if (level == 0) continue;
                int daysLeft = (int)(r.Due.Date - today).TotalDays;
                string text = level == 3 ? $"دیرکرد عودت کالای امانی {r.Number}: {-daysLeft} روز از سررسید گذشته است."
                    : $"عودت کالای امانی {r.Number} {daysLeft} روز دیگر سررسید می‌شود.";
                await _notify.NotifyAsync(new NotifyRequest
                {
                    SourceKey = $"cn:{r.Id}:{level}", Module = "inventory", SourceType = "consignment", SourceId = r.Id, Title = text + (r.Provider != null ? " (" + r.Provider + ")" : ""),
                    Link = "/inventory/documents/" + r.Id.ToString(CultureInfo.InvariantCulture), Kind = OfInboxKind.Alert, Priority = level == 3 ? OfUrgency.Urgent : OfUrgency.Normal,
                    DueDate = r.Due, Roles = StoreRoles,
                }, ct);
                result.ConsignmentAlerts++;
            }

            // ---------------------------------------------------------------- incoming inspections waiting
            int wait1 = (int)settings[Quality.QualitySettingKeys.QcPendingWorkDays], wait2 = (int)settings[Quality.QualitySettingKeys.QcEscalateWorkDays];
            WorkCalendar calendar = await WorkCalendar.LoadAsync(_uow, ct);
            var pending = await _uow.Set<QcInspection>().AsNoTracking().Where(q => !q.Deleted && q.Status == InspectionStatus.Pending)
                .Select(q => new { q.Id, q.Number, q.CreateDate, Item = q.Item.Code, Receipt = q.ReceiptDocument.Number }).ToListAsync(ct);
            foreach (var q in pending)
            {
                result.QcPending++;
                int days = calendar.Between(q.CreateDate.Date, today);
                if (days >= wait1)
                {
                    await _notify.NotifyAsync(new NotifyRequest
                    {
                        SourceKey = $"qci:{q.Id}:1", Module = "inventory", SourceType = "inspection", SourceId = q.Id,
                        Title = $"بازرسی ورودی {q.Number} ({q.Item}، رسید {q.Receipt}) {days} روز کاری است که منتظر نتیجه است.",
                        Link = "/inventory/inspections", Kind = OfInboxKind.Alert, Priority = OfUrgency.Urgent, Roles = "QC",
                    }, ct);
                    result.QcAlerts++;
                }
                if (days >= wait2)
                {
                    await _notify.NotifyAsync(new NotifyRequest
                    {
                        SourceKey = $"qci:{q.Id}:2", Module = "inventory", SourceType = "inspection", SourceId = q.Id,
                        Title = $"تشدید: بازرسی ورودی {q.Number} ({q.Item}) بیش از {wait2 - 1} روز کاری بی‌نتیجه مانده است؛ مدیر تضمین کیفیت و برنامه‌ریزی مطلع شوند.",
                        Link = "/inventory/inspections", Kind = OfInboxKind.Alert, Priority = OfUrgency.Immediate, Roles = "QA,PLANNING",
                    }, ct);
                    result.QcEscalations++;
                }
            }
            return ResponseDtoHelper.Ok(result);
        }
    }

    /// <summary>Daily run of the warehouse alerts. Off with <c>Warehouse:Jobs=false</c> (environment variable <c>Warehouse__Jobs</c>); the CI sets it so that its figures do not depend on the clock.</summary>
    public class WarehouseHostedService : Microsoft.Extensions.Hosting.BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly Microsoft.Extensions.Logging.ILogger<WarehouseHostedService> _logger;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
        private DateTime _doneOn = DateTime.MinValue;

        public WarehouseHostedService(IServiceScopeFactory scopes, Microsoft.Extensions.Logging.ILogger<WarehouseHostedService> logger, Microsoft.Extensions.Configuration.IConfiguration configuration = null)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _logger = logger;
            _configuration = configuration;
        }

        internal static bool Enabled(Microsoft.Extensions.Configuration.IConfiguration configuration) => !string.Equals(configuration?["Warehouse:Jobs"], "false", StringComparison.OrdinalIgnoreCase);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(4), stoppingToken); }
            catch (OperationCanceledException) { return; }
            while (!stoppingToken.IsCancellationRequested)
            {
                DateTime now = DateTime.Now;
                if (Enabled(_configuration) && now.Hour >= 6 && _doneOn < now.Date)
                {
                    try
                    {
                        using (IServiceScope scope = _scopes.CreateScope())
                        {
                            IWarehouseAlertService alerts = scope.ServiceProvider.GetRequiredService<IWarehouseAlertService>();
                            await alerts.RunAsync(null, stoppingToken);
                        }
                        _doneOn = now.Date;
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { _logger?.LogWarning(ex, "Warehouse alerts failed; they will retry."); }
                }
                try { await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    public interface IWarehouseReorderService
    {
        Task<ResponseDto<List<ReorderRowDto>>> GetAsync(ReorderQuery query, CancellationToken cancellationToken);

        /// <summary>A draft purchase request (never submitted) for the chosen items; a person reviews and submits it.</summary>
        Task<ResponseDto<DraftReorderResultDto>> DraftAsync(DraftReorderRequestDto dto, CancellationToken cancellationToken);
    }

    /// <summary>PB5 (item 5, R26): the reorder-point list. ROP = average daily use x lead time + safety stock (the minimum); the quantity follows the policy of the item or of its group.</summary>
    public class WarehouseReorderService : IWarehouseReorderService
    {
        private readonly IUnitOfWork _uow;
        private readonly IProcurementService _procurement;
        private readonly IInventoryUserContext _user;

        public WarehouseReorderService(IUnitOfWork uow, IProcurementService procurement, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _procurement = procurement ?? throw new ArgumentNullException(nameof(procurement));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        public async Task<ResponseDto<List<ReorderRowDto>>> GetAsync(ReorderQuery query, CancellationToken ct)
        {
            query ??= new ReorderQuery();
            Dictionary<string, decimal> settings = await Quality.QualitySettingKeys.LoadAsync(_uow, ct);
            int useDays = Math.Max(1, (int)settings[Quality.QualitySettingKeys.ReorderUseDays]);
            DateTime since = DateTime.Today.AddDays(-useDays);

            IQueryable<InventoryItem> items = _uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && i.IsActive);
            if (query.ItemGroupId.HasValue) items = items.Where(i => i.ItemGroupId == query.ItemGroupId.Value);
            string platform = InventoryText.Clean(query.Platform);
            if (platform != null) items = items.Where(i => i.Platform == platform);
            string text = InventoryText.Clean(query.Contains);
            if (text != null) items = items.Where(i => i.Code.Contains(text) || i.Title.Contains(text));

            var list = await items.Select(i => new
            {
                i.Id, i.Code, i.Title, i.Unit, i.MinStock, i.MaxStock, i.ReorderQuantity, i.LeadTimeDays, i.ItemGroupId, GroupTitle = i.ItemGroup.Title,
            }).ToListAsync(ct);
            List<int> ids = list.Select(i => i.Id).ToList();

            // what is on hand in company stock (customer property, scrap and contractor stock are not usable)
            Dictionary<int, decimal> onHand = (await _uow.Set<StockBalance>().AsNoTracking()
                .Where(b => ids.Contains(b.ItemId) && b.Warehouse.Kind != WarehouseKind.CustomerProperty && b.Warehouse.Kind != WarehouseKind.Scrap && b.Warehouse.Kind != WarehouseKind.Subcontractor)
                .GroupBy(b => b.ItemId).Select(g => new { g.Key, Q = g.Sum(x => x.Quantity) }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.Q);
            // average daily use: issues of the last days (reversals net out)
            Dictionary<int, decimal> used = (await _uow.Set<StockTransaction>().AsNoTracking()
                .Where(t => ids.Contains(t.ItemId) && t.Document.Type == InventoryDocumentType.Issue && t.TransactionDate >= since)
                .GroupBy(t => t.ItemId).Select(g => new { g.Key, Q = -g.Sum(x => x.Quantity) }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.Q);
            var onOrderRows = await _uow.Set<PurchaseOrderLine>().AsNoTracking()
                .Where(l => l.ItemId != null && ids.Contains(l.ItemId.Value) && !l.PurchaseOrder.Deleted
                            && (l.PurchaseOrder.Status == PurchaseOrderStatus.Draft || l.PurchaseOrder.Status == PurchaseOrderStatus.Approved
                                || l.PurchaseOrder.Status == PurchaseOrderStatus.Released || l.PurchaseOrder.Status == PurchaseOrderStatus.PartlyReceived))
                .Select(l => new { ItemId = l.ItemId.Value, Left = l.Quantity - l.ReceivedQuantity }).ToListAsync(ct);
            Dictionary<int, decimal> onOrder = onOrderRows.Where(x => x.Left > 0).GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Left));
            HashSet<int> openRequest = (await _uow.Set<PurchaseRequestLine>().AsNoTracking()
                .Where(l => l.ItemId != null && ids.Contains(l.ItemId.Value) && !l.PurchaseRequest.Deleted
                            && (l.PurchaseRequest.Status == PurchaseRequestStatus.Draft || l.PurchaseRequest.Status == PurchaseRequestStatus.Submitted
                                || l.PurchaseRequest.Status == PurchaseRequestStatus.Approved || l.PurchaseRequest.Status == PurchaseRequestStatus.Ordered))
                .Select(l => l.ItemId.Value).Distinct().ToListAsync(ct)).ToHashSet();

            var groups = await _uow.Set<ItemGroup>().AsNoTracking().Where(g => !g.Deleted)
                .Select(g => new ReorderRules.GroupDefaults { Id = g.Id, ParentId = g.ParentId, Min = g.DefaultMinStock, Max = g.DefaultMaxStock, Quantity = g.DefaultReorderQuantity, LeadDays = g.DefaultLeadTimeDays }).ToListAsync(ct);
            Dictionary<int, ReorderRules.GroupDefaults> effective = ReorderRules.Effective(groups);

            var rows = new List<ReorderRowDto>();
            foreach (var i in list)
            {
                ReorderRules.GroupDefaults g = i.ItemGroupId.HasValue && effective.TryGetValue(i.ItemGroupId.Value, out ReorderRules.GroupDefaults x) ? x : null;
                ReorderRules.Policy policy = ReorderRules.For(i.MinStock, i.MaxStock, i.ReorderQuantity, i.LeadTimeDays, g);
                decimal daily = used.TryGetValue(i.Id, out decimal u) ? Math.Max(0, u) / useDays : 0m;
                int lead = policy.LeadDays ?? 0;
                decimal rop = ReorderRules.ReorderPoint(policy.Min, daily, lead);
                if (rop <= 0) continue;
                decimal have = onHand.TryGetValue(i.Id, out decimal h) ? h : 0m;
                decimal ordered = onOrder.TryGetValue(i.Id, out decimal o) ? o : 0m;
                decimal suggested = ReorderRules.SuggestedQuantity(rop, policy.Max, policy.Quantity, have, ordered);
                if (suggested <= 0) continue;
                rows.Add(new ReorderRowDto
                {
                    ItemId = i.Id, ItemCode = i.Code, ItemTitle = i.Title, Unit = i.Unit, GroupTitle = i.GroupTitle, OnHand = have, OnOrder = ordered, OpenRequest = openRequest.Contains(i.Id),
                    SafetyStock = policy.Min, DailyUse = Math.Round(daily, 4), LeadDays = lead, ReorderPoint = rop, SuggestedQuantity = suggested, FromGroupDefault = policy.FromGroup,
                });
            }
            return ResponseDtoHelper.Ok(rows.OrderBy(r => r.ItemCode, StringComparer.Ordinal).ToList());
        }

        public async Task<ResponseDto<DraftReorderResultDto>> DraftAsync(DraftReorderRequestDto dto, CancellationToken ct)
        {
            if (dto == null || dto.ItemIds == null || dto.ItemIds.Count == 0) return ResponseDtoHelper.BadRequest<DraftReorderResultDto>("کالاهای فهرست سفارش مجدد را انتخاب کنید.");
            var all = await GetAsync(new ReorderQuery(), ct);
            if (!all.Success) return ResponseDtoHelper.BadRequest<DraftReorderResultDto>(all.Message);
            List<ReorderRowDto> chosen = all.Data.Where(r => dto.ItemIds.Contains(r.ItemId) && !r.OpenRequest).Take(200).ToList();
            if (chosen.Count == 0) return ResponseDtoHelper.BadRequest<DraftReorderResultDto>("هیچ‌کدام از کالاهای انتخابی نیاز به درخواست خرید جدید ندارد (مقدار پیشنهادی صفر است یا درخواست باز دارد).");
            var request = new SavePurchaseRequestDto
            {
                Source = PurchaseRequestSource.ReorderPoint, Kind = PurchaseKind.Goods, RequestDate = DateTime.Today, Submit = false,
                Description = "پیش‌نویس از فهرست نقطه سفارش مجدد انبار. بررسی، ویرایش و ارسال با واحد برنامه‌ریزی است.",
            };
            foreach (ReorderRowDto r in chosen)
                request.Lines.Add(new SavePurchaseRequestLineDto
                {
                    ItemId = r.ItemId, Unit = r.Unit, Quantity = r.SuggestedQuantity,
                    Description = $"{r.ItemTitle}: موجودی {r.OnHand.ToString("0.####", CultureInfo.InvariantCulture)} {r.Unit}، نقطه سفارش {r.ReorderPoint.ToString("0.####", CultureInfo.InvariantCulture)}",
                });
            var saved = await _procurement.SaveRequestAsync(request, ct);
            if (!saved.Success) return ResponseDtoHelper.BadRequest<DraftReorderResultDto>(saved.Message);
            return ResponseDtoHelper.Ok(new DraftReorderResultDto { RequestId = saved.Data.Id, RequestNumber = saved.Data.Number, Lines = chosen.Count });
        }
    }
}
