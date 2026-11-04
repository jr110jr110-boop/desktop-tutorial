using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.ProjectBalance;
using Services.NasbNiro.Trace;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.ProcurementDtos;
using ViewModels.NasbNiro.TraceDtos;

namespace Services.NasbNiro.Inventory
{
    public interface IWarehouseReportService
    {
        ResponseDto<List<WarehouseReportCatalogDto>> GetCatalog();
        Task<ResponseDto<WarehouseReportDto>> GetAsync(WarehouseReportQuery query, CancellationToken cancellationToken);

        /// <summary>The same table as an Excel file (info sheet with the filters, then the data).</summary>
        Task<ResponseDto<byte[]>> ExportAsync(WarehouseReportQuery query, CancellationToken cancellationToken);
    }

    public static class WarehouseReportRoles
    {
        public const string Readers = "PMO,CEO,PLANNING,COMMERCIAL,FINANCIAL,FINANCEMANAGER,TREASURY,AUDITOR,WAREHOUSE,WHSUPERVISOR,QC,QA,ENGINEERING,PROJECTMANAGER,PRODUCTIONMANAGER,CONSTRUCTION";
    }

    /// <summary>
    /// The one place that turns a <see cref="ReportTableDto"/> into an Excel file: used by the warehouse reports and by the exports of the inventory lists, on top of the shared <see cref="SimpleXlsx"/> writer.
    /// </summary>
    public static class ReportExcel
    {
        public static byte[] Build(ReportTableDto table, string filters, DateTimeOffset generatedAt)
        {
            var info = new XlsxSheet { Name = "اطلاعات", Header = new List<string> { "مورد", "مقدار" } };
            info.Rows.Add(new object[] { "گزارش", table.Title });
            info.Rows.Add(new object[] { "تاریخ تهیه", generatedAt.DateTime });
            info.Rows.Add(new object[] { "فیلترها", string.IsNullOrWhiteSpace(filters) ? "بدون فیلتر" : filters });
            info.Rows.Add(new object[] { "تعداد ردیف", table.Rows.Count });
            foreach (var t in table.Totals) info.Rows.Add(new object[] { "جمع " + (table.Columns.FirstOrDefault(c => c.Key == t.Key)?.Title ?? t.Key), t.Value });

            var data = new XlsxSheet { Name = table.Title, Header = table.Columns.Select(c => c.Title).ToList() };
            foreach (var row in table.Rows)
                data.Rows.Add(table.Columns.Select(c => row.TryGetValue(c.Key, out object v) ? v : null).ToArray());
            return SimpleXlsx.Build(new List<XlsxSheet> { data, info }, true);
        }
    }

    public class WarehouseReportService : IWarehouseReportService
    {
        private static readonly string[] Keys =
        {
            "stock", "kardex", "kardex-summary", "open-issues", "in-transit", "project-consumption", "stagnant", "ceilings", "scrap", "purchases-by-provider",
            "purchases-by-project", "petty-cash", "contracts", "count-discrepancy", "po-receipt-invoice", "item-card", "request-trace", "consignment", "qc-pending", "freight", "assets", "reorder",
        };

        private static readonly string[] Titles =
        {
            "موجودی لحظه‌ای انبار", "کاردکس کالا با ارزش ریالی", "خلاصه کاردکس (مواد، در جریان ساخت، کالای ساخته‌شده)", "درخواست‌های کالای باز (حواله‌نشده)", "اقلام در راه (سفارش تاییدشده و دریافت‌نشده)",
            "مصرف واقعی پروژه در برابر استاندارد", "موجودی راکد", "سقف مصرف کالا", "ضایعات به تفکیک انبار", "خرید به تفکیک پیمانکار و تامین‌کننده",
            "خرید به تفکیک پروژه", "تنخواه باز", "قراردادهای جاری", "مغایرت انبارگردانی (شمارش با دفتر)", "مغایرت سفارش، رسید و فاکتور", "کارت استاندارد کالا", "ردیابی شماره درخواست", "کالاهای امانی باز", "بازرسی‌های ورودی در انتظار", "کرایه حمل و بهای تمام‌شده نهایی", "فهرست دارایی و ابزار", "فهرست نقطه سفارش مجدد",
        };

        private static readonly string[][] FilterNames =
        {
            new[] { "warehouse", "kind", "project", "group", "item", "platform" },
            new[] { "item", "warehouse", "date" },
            new[] { "date", "warehouse" },
            new[] { "date", "warehouse", "project" },
            new[] { "provider", "project", "date" },
            new[] { "project", "date" },
            new[] { "warehouse", "group", "bucket" },
            new[] { "item", "project" },
            new[] { "date", "warehouse" },
            new[] { "date", "provider" },
            new[] { "date", "project" },
            new string[0],
            new string[0],
            new[] { "date", "warehouse" },
            new[] { "date", "provider" },
            new[] { "item" },
            new[] { "request" },
            new[] { "warehouse", "provider" },
            new string[0],
            new[] { "date", "provider" },
            new[] { "warehouse" },
            new[] { "group", "platform" },
        };

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IInventoryStockService _stock;
        private readonly IProjectBalanceService _balance;
        private readonly ITraceCenterService _trace;
        private readonly IWarehouseReorderService _reorder;

        public WarehouseReportService(IUnitOfWork uow, IInventoryUserContext user, IInventoryStockService stock, IProjectBalanceService balance, ITraceCenterService trace, IWarehouseReorderService reorder)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _stock = stock ?? throw new ArgumentNullException(nameof(stock));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _trace = trace ?? throw new ArgumentNullException(nameof(trace));
            _reorder = reorder ?? throw new ArgumentNullException(nameof(reorder));
        }

        public ResponseDto<List<WarehouseReportCatalogDto>> GetCatalog() =>
            ResponseDtoHelper.Ok(Keys.Select((k, i) => new WarehouseReportCatalogDto { Key = k, Title = Titles[i], Filters = FilterNames[i].ToList() }).ToList());

        private static ReportColumnDto C(string key, string title, string type = "text") => new ReportColumnDto { Key = key, Title = title, Type = type };
        private static decimal R(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);

        /// <summary>Raw material / work in progress / finished goods, by the kind of the warehouse (★ the company confirms the grouping).</summary>
        public static string Category(WarehouseKind kind)
        {
            switch (kind)
            {
                case WarehouseKind.RawMaterial: case WarehouseKind.Material: case WarehouseKind.Parts: case WarehouseKind.StandardPart:
                case WarehouseKind.Consumable: case WarehouseKind.ProjectConsumable: case WarehouseKind.Tool: case WarehouseKind.Instrument: case WarehouseKind.Patterns:
                    return "raw";
                case WarehouseKind.WorkInProgress: case WarehouseKind.Project: case WarehouseKind.PointOfUse: case WarehouseKind.Subcontractor:
                    return "wip";
                case WarehouseKind.FinishedGoods:
                    return "finished";
                default:
                    return "other";
            }
        }

        private static string CategoryTitle(string c) => c == "raw" ? "مواد و قطعات" : c == "wip" ? "در جریان ساخت و پروژه" : c == "finished" ? "کالای ساخته‌شده" : "سایر (قرنطینه، ضایعات، اموال مشتری، عمومی)";

        public async Task<ResponseDto<WarehouseReportDto>> GetAsync(WarehouseReportQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(WarehouseReportRoles.Readers)) return ResponseDtoHelper.BadRequest<WarehouseReportDto>("شما مجوز گزارش‌های انبار را ندارید.");
            query ??= new WarehouseReportQuery();
            int index = Array.IndexOf(Keys, (query.Report ?? "").Trim().ToLowerInvariant());
            if (index < 0) return ResponseDtoHelper.BadRequest<WarehouseReportDto>("گزارش نامعتبر است.");
            var t = new WarehouseReportDto { Key = Keys[index], Title = Titles[index], GeneratedAt = DateTimeOffset.Now, Filters = await DescribeFiltersAsync(query, ct) };
            string error = await FillAsync(t, query, ct);
            if (error != null) return ResponseDtoHelper.BadRequest<WarehouseReportDto>(error);

            // R29: money is for the roles of the price list only
            bool sees = InventoryPriceAccess.SeesValues(_user);
            if (!sees)
            {
                List<string> money = t.Columns.Where(c => c.Type == "money").Select(c => c.Key).ToList();
                if (money.Count > 0)
                {
                    t.PricesMasked = true;
                    t.Columns.RemoveAll(c => money.Contains(c.Key));
                    foreach (var row in t.Rows) foreach (string k in money) row.Remove(k);
                    foreach (string k in money) t.Totals.Remove(k);
                }
            }
            return ResponseDtoHelper.Ok(t);
        }

        public async Task<ResponseDto<byte[]>> ExportAsync(WarehouseReportQuery query, CancellationToken ct)
        {
            ResponseDto<WarehouseReportDto> r = await GetAsync(query, ct);
            if (!r.Success || r.Data == null) return ResponseDtoHelper.BadRequest<byte[]>(r.Message);
            return ResponseDtoHelper.Ok(ReportExcel.Build(r.Data, r.Data.Filters, r.Data.GeneratedAt));
        }

        private async Task<string> DescribeFiltersAsync(WarehouseReportQuery q, CancellationToken ct)
        {
            var parts = new List<string>();
            if (q.DateFrom.HasValue) parts.Add("از " + FiscalCalendars.Current.Text(q.DateFrom.Value));
            if (q.DateTo.HasValue) parts.Add("تا " + FiscalCalendars.Current.Text(q.DateTo.Value));
            if (q.WarehouseId.HasValue) parts.Add("انبار: " + await _uow.Set<Warehouse>().AsNoTracking().Where(w => w.Id == q.WarehouseId.Value).Select(w => w.Title).FirstOrDefaultAsync(ct));
            if (q.WorkOrderId.HasValue) parts.Add("پروژه: " + await _uow.Set<WorkOrder>().AsNoTracking().Where(w => w.Id == q.WorkOrderId.Value).Select(w => w.Code).FirstOrDefaultAsync(ct));
            if (q.ItemId.HasValue) parts.Add("کالا: " + await _uow.Set<InventoryItem>().AsNoTracking().Where(w => w.Id == q.ItemId.Value).Select(w => w.Code).FirstOrDefaultAsync(ct));
            if (q.ProviderId.HasValue) parts.Add("تامین‌کننده: " + await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(w => w.Id == q.ProviderId.Value).Select(w => w.Name).FirstOrDefaultAsync(ct));
            if (!string.IsNullOrWhiteSpace(q.Platform)) parts.Add("پلتفرم: " + q.Platform);
            if (!string.IsNullOrWhiteSpace(q.EquipmentType)) parts.Add("نوع تجهیز: " + q.EquipmentType);
            if (!string.IsNullOrWhiteSpace(q.ValveModel)) parts.Add("مدل شیر: " + q.ValveModel);
            if (q.Kind.HasValue) parts.Add("نوع انبار: " + q.Kind.Value);
            if (q.Bucket.HasValue) parts.Add("گروه راکد: " + StagnantRules.Title(q.Bucket.Value));
            if (!string.IsNullOrWhiteSpace(q.RequestNumber)) parts.Add("شماره درخواست: " + q.RequestNumber);
            return string.Join(" | ", parts);
        }

        private static string Day(DateTime? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

        private async Task<string> FillAsync(WarehouseReportDto t, WarehouseReportQuery q, CancellationToken ct)
        {
            DateTime? from = q.DateFrom?.Date, to = q.DateTo?.Date;
            DateTime? toEnd = to?.AddDays(1);
            switch (t.Key)
            {
                case "stock":
                {
                    t.Columns.AddRange(new[] { C("code", "کد کالا"), C("title", "عنوان"), C("unit", "واحد"), C("warehouse", "انبار"), C("quantity", "مقدار", "number"), C("unitCost", "بهای میانگین (ریال)", "money"), C("value", "ارزش (ریال)", "money") });
                    IQueryable<StockBalance> b = _uow.Set<StockBalance>().AsNoTracking().Where(x => x.Quantity != 0);
                    if (q.WarehouseId.HasValue) b = b.Where(x => x.WarehouseId == q.WarehouseId.Value);
                    if (q.Kind.HasValue) b = b.Where(x => x.Warehouse.Kind == q.Kind.Value);
                    if (q.WorkOrderId.HasValue) b = b.Where(x => x.Warehouse.WorkOrderId == q.WorkOrderId.Value);
                    if (q.ItemGroupId.HasValue) b = b.Where(x => x.Item.ItemGroupId == q.ItemGroupId.Value);
                    if (q.ItemId.HasValue) b = b.Where(x => x.ItemId == q.ItemId.Value);
                    if (!string.IsNullOrWhiteSpace(q.Platform)) b = b.Where(x => x.Item.Platform == q.Platform);
                    if (!string.IsNullOrWhiteSpace(q.EquipmentType)) b = b.Where(x => x.Item.EquipmentType == q.EquipmentType);
                    if (!string.IsNullOrWhiteSpace(q.ValveModel)) b = b.Where(x => x.Item.ValveModel == q.ValveModel);
                    string text = InventoryText.Clean(q.Contains);
                    if (text != null) b = b.Where(x => x.Item.Code.Contains(text) || x.Item.Title.Contains(text));
                    var rows = await b.GroupBy(x => new { x.ItemId, x.Item.Code, x.Item.Title, x.Item.Unit, x.WarehouseId, W = x.Warehouse.Title, K = x.Warehouse.Kind })
                        .Select(g => new { g.Key.ItemId, g.Key.Code, g.Key.Title, g.Key.Unit, g.Key.WarehouseId, g.Key.W, g.Key.K, Q = g.Sum(z => z.Quantity) })
                        .OrderBy(g => g.Code).ThenBy(g => g.W).Take(5000).ToListAsync(ct);
                    List<int> ids = rows.Select(r => r.ItemId).Distinct().ToList();
                    Dictionary<int, decimal> avg = await _uow.Set<ItemCost>().AsNoTracking().Where(c => ids.Contains(c.ItemId)).ToDictionaryAsync(c => c.ItemId, c => c.AverageCost, ct);
                    decimal total = 0;
                    foreach (var r in rows)
                    {
                        decimal cost = r.K == WarehouseKind.CustomerProperty ? 0m : avg.TryGetValue(r.ItemId, out decimal a) ? a : 0m;
                        decimal value = R(r.Q * cost);
                        total += value;
                        t.Rows.Add(new Dictionary<string, object> { ["code"] = r.Code, ["title"] = r.Title, ["unit"] = r.Unit, ["warehouse"] = r.W, ["quantity"] = r.Q, ["unitCost"] = cost, ["value"] = value });
                    }
                    t.Totals["value"] = total;
                    return null;
                }
                case "kardex":
                {
                    if (!q.ItemId.HasValue) return "کالا را انتخاب کنید.";
                    t.Columns.AddRange(new[] { C("date", "تاریخ", "date"), C("document", "سند"), C("warehouse", "انبار"), C("lot", "بچ"), C("in", "ورود", "number"), C("out", "خروج", "number"), C("balance", "مانده", "number"),
                        C("unitCost", "بهای واحد (ریال)", "money"), C("amount", "مبلغ (ریال)", "money"), C("runningValue", "ارزش مانده (ریال)", "money"), C("supplier", "تامین‌کننده"), C("receipt", "شماره رسید"), C("note", "توضیح") });
                    var k = await _stock.GetKardexAsync(new KardexQuery { ItemId = q.ItemId.Value, WarehouseId = q.WarehouseId, From = from, To = to }, ct);
                    if (!k.Success) return k.Message;
                    foreach (KardexRowDto r in k.Data.Rows)
                        t.Rows.Add(new Dictionary<string, object>
                        {
                            ["date"] = r.Date, ["document"] = r.DocumentNumber, ["warehouse"] = r.WarehouseTitle, ["lot"] = r.LotNumber, ["in"] = r.In, ["out"] = r.Out, ["balance"] = r.Balance,
                            ["unitCost"] = r.UnitCost, ["amount"] = r.Amount, ["runningValue"] = r.RunningValue, ["supplier"] = r.Supplier, ["receipt"] = r.ReceiptNumber, ["note"] = r.Note,
                        });
                    t.Note = $"کالا: {k.Data.ItemCode} {k.Data.ItemTitle}؛ مانده اول دوره {k.Data.OpeningBalance.ToString("0.####", CultureInfo.InvariantCulture)}؛ مانده پایان {k.Data.ClosingBalance.ToString("0.####", CultureInfo.InvariantCulture)}";
                    if (k.Data.ClosingValue.HasValue) t.Totals["runningValue"] = k.Data.ClosingValue.Value;
                    return null;
                }
                case "kardex-summary":
                {
                    t.Columns.AddRange(new[] { C("category", "گروه"), C("openingQty", "مقدار اول دوره", "number"), C("inQty", "ورود (مقدار)", "number"), C("outQty", "خروج (مقدار)", "number"), C("closingQty", "مقدار پایان", "number"),
                        C("openingValue", "ارزش اول دوره (ریال)", "money"), C("inValue", "ارزش ورود (ریال)", "money"), C("outValue", "ارزش خروج (ریال)", "money"), C("closingValue", "ارزش پایان (ریال)", "money") });
                    IQueryable<StockTransaction> tx = _uow.Set<StockTransaction>().AsNoTracking();
                    if (q.WarehouseId.HasValue) tx = tx.Where(x => x.WarehouseId == q.WarehouseId.Value);
                    if (toEnd.HasValue) tx = tx.Where(x => x.TransactionDate < toEnd.Value);
                    var rows = await tx.GroupBy(x => new { x.Warehouse.Kind, Before = from.HasValue && x.TransactionDate < from.Value })
                        .Select(g => new { g.Key.Kind, g.Key.Before, QIn = g.Sum(z => z.Quantity > 0 ? z.Quantity : 0), QOut = g.Sum(z => z.Quantity < 0 ? -z.Quantity : 0), VIn = g.Sum(z => (z.CostAmount ?? 0) > 0 ? (z.CostAmount ?? 0) : 0), VOut = g.Sum(z => (z.CostAmount ?? 0) < 0 ? -(z.CostAmount ?? 0) : 0) })
                        .ToListAsync(ct);
                    var order = new[] { "raw", "wip", "finished", "other" };
                    decimal sumClosing = 0;
                    foreach (string cat in order)
                    {
                        var mine = rows.Where(r => Category(r.Kind) == cat).ToList();
                        decimal openQ = mine.Where(r => r.Before).Sum(r => r.QIn - r.QOut), openV = mine.Where(r => r.Before).Sum(r => r.VIn - r.VOut);
                        decimal inQ = mine.Where(r => !r.Before).Sum(r => r.QIn), outQ = mine.Where(r => !r.Before).Sum(r => r.QOut);
                        decimal inV = mine.Where(r => !r.Before).Sum(r => r.VIn), outV = mine.Where(r => !r.Before).Sum(r => r.VOut);
                        sumClosing += openV + inV - outV;
                        t.Rows.Add(new Dictionary<string, object>
                        {
                            ["category"] = CategoryTitle(cat), ["openingQty"] = openQ, ["inQty"] = inQ, ["outQty"] = outQ, ["closingQty"] = openQ + inQ - outQ,
                            ["openingValue"] = R(openV), ["inValue"] = R(inV), ["outValue"] = R(outV), ["closingValue"] = R(openV + inV - outV),
                        });
                    }
                    t.Totals["closingValue"] = R(sumClosing);
                    t.Note = "مقدارها با واحدهای مختلف جمع شده‌اند؛ مبنای مقایسه ارزش ریالی است. گروه‌بندی بر اساس نوع انبار است.";
                    return null;
                }
                case "open-issues":
                {
                    t.Columns.AddRange(new[] { C("number", "شماره درخواست"), C("date", "تاریخ", "date"), C("project", "پروژه"), C("warehouse", "انبار"), C("state", "وضعیت"), C("requested", "مقدار درخواستی", "number"), C("issued", "حواله‌شده", "number"), C("left", "مانده", "number") });
                    IQueryable<MaterialRequest> m = _uow.Set<MaterialRequest>().AsNoTracking().Where(r => !r.Deleted && (r.Status == MaterialRequestStatus.Submitted || r.Status == MaterialRequestStatus.Approved));
                    if (from.HasValue) m = m.Where(r => r.RequestDate >= from.Value);
                    if (to.HasValue) m = m.Where(r => r.RequestDate <= to.Value);
                    if (q.WarehouseId.HasValue) m = m.Where(r => r.WarehouseId == q.WarehouseId.Value);
                    if (q.WorkOrderId.HasValue) m = m.Where(r => r.WorkOrderId == q.WorkOrderId.Value);
                    var rows = await m.OrderBy(r => r.RequestDate).Take(3000).Select(r => new
                    {
                        r.Number, r.RequestDate, r.Status, Project = r.WorkOrder.Code, W = r.Warehouse.Title,
                        Req = r.Lines.Sum(l => (decimal?)l.Quantity) ?? 0, Iss = r.Lines.Sum(l => (decimal?)l.IssuedQuantity) ?? 0,
                    }).ToListAsync(ct);
                    foreach (var r in rows.Where(x => x.Status == MaterialRequestStatus.Submitted || x.Req - x.Iss > 0))
                        t.Rows.Add(new Dictionary<string, object>
                        {
                            ["number"] = r.Number, ["date"] = r.RequestDate, ["project"] = r.Project, ["warehouse"] = r.W,
                            ["state"] = r.Status == MaterialRequestStatus.Submitted ? "در انتظار تایید" : r.Iss > 0 ? "بخشی حواله شده" : "تایید شده، حواله نشده",
                            ["requested"] = r.Req, ["issued"] = r.Iss, ["left"] = Math.Max(0, r.Req - r.Iss),
                        });
                    return null;
                }
                case "in-transit":
                {
                    t.Columns.AddRange(new[] { C("order", "سفارش خرید"), C("provider", "تامین‌کننده"), C("item", "کالا"), C("unit", "واحد"), C("ordered", "سفارش‌شده", "number"), C("received", "دریافت‌شده", "number"), C("left", "در راه", "number"), C("delivery", "تاریخ تحویل", "date"), C("value", "ارزش در راه (ریال)", "money") });
                    IQueryable<PurchaseOrderLine> l = _uow.Set<PurchaseOrderLine>().AsNoTracking().Where(x => !x.PurchaseOrder.Deleted && (x.PurchaseOrder.Status == PurchaseOrderStatus.Approved || x.PurchaseOrder.Status == PurchaseOrderStatus.Released || x.PurchaseOrder.Status == PurchaseOrderStatus.PartlyReceived) && x.Quantity > x.ReceivedQuantity);
                    if (q.ProviderId.HasValue) l = l.Where(x => x.PurchaseOrder.ProviderId == q.ProviderId.Value);
                    if (q.WorkOrderId.HasValue) l = l.Where(x => (x.WorkOrderId ?? x.PurchaseOrder.WorkOrderId) == q.WorkOrderId.Value);
                    if (from.HasValue) l = l.Where(x => x.PurchaseOrder.OrderDate >= from.Value);
                    if (to.HasValue) l = l.Where(x => x.PurchaseOrder.OrderDate <= to.Value);
                    var rows = await l.OrderBy(x => x.DeliveryDate).Take(3000).Select(x => new { Order = x.PurchaseOrder.Number, P = x.PurchaseOrder.Provider.Name, Item = x.Item != null ? x.Item.Code : x.Description, x.Unit, x.Quantity, x.ReceivedQuantity, x.DeliveryDate, x.UnitPrice }).ToListAsync(ct);
                    foreach (var r in rows)
                        t.Rows.Add(new Dictionary<string, object> { ["order"] = r.Order, ["provider"] = r.P, ["item"] = r.Item, ["unit"] = r.Unit, ["ordered"] = r.Quantity, ["received"] = r.ReceivedQuantity, ["left"] = r.Quantity - r.ReceivedQuantity, ["delivery"] = r.DeliveryDate, ["value"] = R((r.Quantity - r.ReceivedQuantity) * r.UnitPrice) });
                    t.Totals["value"] = rows.Sum(r => R((r.Quantity - r.ReceivedQuantity) * r.UnitPrice));
                    return null;
                }
                case "project-consumption":
                {
                    if (!q.WorkOrderId.HasValue) return "پروژه (دستور کار) را انتخاب کنید.";
                    t.Columns.AddRange(new[] { C("code", "کد کالا"), C("title", "عنوان"), C("unit", "واحد"), C("standard", "مصرف استاندارد (BOM پایه)", "number"), C("actual", "مصرف واقعی", "number"), C("variance", "انحراف", "number"), C("percent", "درصد انحراف", "number"), C("value", "ارزش انحراف (ریال)", "money") });
                    var latest = await _balance.GetLatestAsync(q.WorkOrderId.Value, ct);
                    if (!latest.Success || latest.Data == null) return "برای این پروژه هنوز محاسبه تراز مواد (BOM پایه) انجام نشده است.";
                    IQueryable<StockTransaction> tx = _uow.Set<StockTransaction>().AsNoTracking().Where(x => x.Document.WorkOrderId == q.WorkOrderId.Value && (x.Document.Type == InventoryDocumentType.Issue || x.Document.Type == InventoryDocumentType.ProductionReturn));
                    if (from.HasValue) tx = tx.Where(x => x.TransactionDate >= from.Value);
                    if (toEnd.HasValue) tx = tx.Where(x => x.TransactionDate < toEnd.Value);
                    var actual = (await tx.GroupBy(x => x.ItemId).Select(g => new { g.Key, Q = -g.Sum(z => z.Quantity), V = -g.Sum(z => z.CostAmount ?? 0) }).ToListAsync(ct)).ToDictionary(x => x.Key);
                    var standard = latest.Data.Lines.Where(x => x.ItemId.HasValue).GroupBy(x => x.ItemId.Value).ToDictionary(g => g.Key, g => new { Code = g.First().ItemCode, Title = g.First().ItemTitle, Unit = g.First().Unit, Q = g.Sum(z => z.GrossQty + z.ScrapQty) });
                    List<int> itemIds = standard.Keys.Union(actual.Keys).ToList();
                    var names = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => new { i.Code, i.Title, i.Unit }, ct);
                    Dictionary<int, decimal> avg = await _uow.Set<ItemCost>().AsNoTracking().Where(c => itemIds.Contains(c.ItemId)).ToDictionaryAsync(c => c.ItemId, c => c.AverageCost, ct);
                    decimal totalVar = 0;
                    foreach (int id in itemIds.OrderBy(i => names.TryGetValue(i, out var n) ? n.Code : "", StringComparer.Ordinal))
                    {
                        decimal std = standard.TryGetValue(id, out var s) ? s.Q : 0m, act = actual.TryGetValue(id, out var a) ? a.Q : 0m;
                        var n2 = names.TryGetValue(id, out var nn) ? nn : null;
                        decimal variance = act - std;
                        decimal value = R(variance * (avg.TryGetValue(id, out decimal c) ? c : 0m));
                        totalVar += value;
                        t.Rows.Add(new Dictionary<string, object>
                        {
                            ["code"] = n2?.Code ?? s?.Code, ["title"] = n2?.Title ?? s?.Title, ["unit"] = n2?.Unit ?? s?.Unit, ["standard"] = std, ["actual"] = act, ["variance"] = variance,
                            ["percent"] = std == 0 ? (decimal?)null : Math.Round(variance / std * 100m, 1), ["value"] = value,
                        });
                    }
                    t.Totals["value"] = totalVar;
                    t.Note = $"مبنای استاندارد: آخرین تراز مواد پروژه (BOM پایه {latest.Data.BomCode} ویرایش {latest.Data.BomRevision}) با احتساب پرت.";
                    return null;
                }
                case "stagnant":
                {
                    t.Columns.AddRange(new[] { C("code", "کد کالا"), C("title", "عنوان"), C("unit", "واحد"), C("onHand", "موجودی", "number"), C("lastMove", "آخرین گردش", "date"), C("days", "روز بدون گردش", "number"), C("bucket", "گروه"), C("value", "ارزش (ریال)", "money") });
                    Dictionary<string, decimal> s = await Quality.QualitySettingKeys.LoadAsync(_uow, ct);
                    int slow = (int)s[Quality.QualitySettingKeys.StagnantSlowDays], stag = (int)s[Quality.QualitySettingKeys.StagnantDays], dead = (int)s[Quality.QualitySettingKeys.StagnantDeadDays];
                    IQueryable<StockBalance> b = _uow.Set<StockBalance>().AsNoTracking().Where(x => x.Quantity > 0 && x.Warehouse.Kind != WarehouseKind.CustomerProperty && x.Warehouse.Kind != WarehouseKind.Scrap);
                    if (q.WarehouseId.HasValue) b = b.Where(x => x.WarehouseId == q.WarehouseId.Value);
                    if (q.ItemGroupId.HasValue) b = b.Where(x => x.Item.ItemGroupId == q.ItemGroupId.Value);
                    var onHand = await b.GroupBy(x => new { x.ItemId, x.Item.Code, x.Item.Title, x.Item.Unit }).Select(g => new { g.Key.ItemId, g.Key.Code, g.Key.Title, g.Key.Unit, Q = g.Sum(z => z.Quantity) }).ToListAsync(ct);
                    List<int> ids = onHand.Select(x => x.ItemId).ToList();
                    IQueryable<StockTransaction> mv = _uow.Set<StockTransaction>().AsNoTracking().Where(x => ids.Contains(x.ItemId) && !x.IsReversal && x.Quantity < 0);
                    if (q.WarehouseId.HasValue) mv = mv.Where(x => x.WarehouseId == q.WarehouseId.Value);
                    // "movement" = the last time the item left a warehouse (issue, transfer or sale); a receipt alone does not make stock move
                    Dictionary<int, DateTime> last = (await mv.GroupBy(x => x.ItemId).Select(g => new { g.Key, D = g.Max(z => z.TransactionDate) }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.D);
                    Dictionary<int, DateTime> firstIn = (await _uow.Set<StockTransaction>().AsNoTracking().Where(x => ids.Contains(x.ItemId) && x.Quantity > 0).GroupBy(x => x.ItemId).Select(g => new { g.Key, D = g.Max(z => z.TransactionDate) }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.D);
                    Dictionary<int, decimal> avg = await _uow.Set<ItemCost>().AsNoTracking().Where(c => ids.Contains(c.ItemId)).ToDictionaryAsync(c => c.ItemId, c => c.AverageCost, ct);
                    DateTime today = (to ?? DateTime.Today).Date;
                    decimal total = 0;
                    foreach (var r in onHand.OrderBy(x => x.Code, StringComparer.Ordinal))
                    {
                        DateTime? lm = last.TryGetValue(r.ItemId, out DateTime d1) ? d1 : (firstIn.TryGetValue(r.ItemId, out DateTime d2) ? d2 : (DateTime?)null);
                        if (lm == null) continue;
                        int days = (int)(today - lm.Value.Date).TotalDays;
                        int bucket = StagnantRules.Bucket(days, slow, stag, dead);
                        if (q.Bucket.HasValue ? bucket != q.Bucket.Value : bucket == 0) continue;
                        decimal value = R(r.Q * (avg.TryGetValue(r.ItemId, out decimal a) ? a : 0m));
                        total += value;
                        t.Rows.Add(new Dictionary<string, object> { ["code"] = r.Code, ["title"] = r.Title, ["unit"] = r.Unit, ["onHand"] = r.Q, ["lastMove"] = lm, ["days"] = days, ["bucket"] = StagnantRules.Title(bucket), ["value"] = value });
                    }
                    t.Totals["value"] = total;
                    t.Note = $"آستانه‌ها: {slow} / {stag} / {dead} روز (تنظیم شرکت).";
                    return null;
                }
                case "ceilings":
                {
                    t.Columns.AddRange(new[] { C("code", "کد کالا"), C("title", "عنوان"), C("project", "پروژه"), C("max", "سقف مصرف", "number"), C("used", "مصرف تاکنون", "number"), C("left", "مانده تا سقف", "number"), C("state", "وضعیت") });
                    IQueryable<InventoryConsumptionCeiling> c = _uow.Set<InventoryConsumptionCeiling>().AsNoTracking().Where(x => !x.Deleted);
                    if (q.ItemId.HasValue) c = c.Where(x => x.ItemId == q.ItemId.Value);
                    if (q.WorkOrderId.HasValue) c = c.Where(x => x.WorkOrderId == q.WorkOrderId.Value);
                    var list = await c.Select(x => new { x.ItemId, x.Item.Code, x.Item.Title, x.WorkOrderId, P = x.WorkOrder.Code, x.MaxQuantity }).ToListAsync(ct);
                    List<int> ids = list.Select(x => x.ItemId).Distinct().ToList();
                    var tx = await _uow.Set<StockTransaction>().AsNoTracking().Where(x => ids.Contains(x.ItemId) && (x.Document.Type == InventoryDocumentType.Issue || x.Document.Type == InventoryDocumentType.ProductionReturn))
                        .GroupBy(x => new { x.ItemId, W = x.Document.WorkOrderId }).Select(g => new { g.Key.ItemId, g.Key.W, Q = -g.Sum(z => z.Quantity) }).ToListAsync(ct);
                    foreach (var r in list.OrderBy(x => x.Code, StringComparer.Ordinal))
                    {
                        decimal used = tx.Where(x => x.ItemId == r.ItemId && (r.WorkOrderId == null || x.W == r.WorkOrderId)).Sum(x => x.Q);
                        t.Rows.Add(new Dictionary<string, object> { ["code"] = r.Code, ["title"] = r.Title, ["project"] = r.P ?? "کل شرکت", ["max"] = r.MaxQuantity, ["used"] = used, ["left"] = r.MaxQuantity - used, ["state"] = used > r.MaxQuantity ? "فراتر از سقف" : used >= r.MaxQuantity * 0.9m ? "نزدیک سقف" : "در محدوده" });
                    }
                    return null;
                }
                case "scrap":
                {
                    t.Columns.AddRange(new[] { C("warehouse", "انبار ضایعات"), C("received", "ضایعات دریافتی (مقدار)", "number"), C("receivedValue", "ارزش دریافتی (ریال)", "money"), C("sold", "فروخته‌شده (مقدار)", "number"), C("onHand", "مانده فعلی", "number") });
                    IQueryable<StockTransaction> tx = _uow.Set<StockTransaction>().AsNoTracking().Where(x => x.Warehouse.Kind == WarehouseKind.Scrap);
                    if (q.WarehouseId.HasValue) tx = tx.Where(x => x.WarehouseId == q.WarehouseId.Value);
                    var onHand = await tx.GroupBy(x => new { x.WarehouseId, x.Warehouse.Title }).Select(g => new { g.Key.WarehouseId, g.Key.Title, Q = g.Sum(z => z.Quantity) }).ToListAsync(ct);
                    if (from.HasValue) tx = tx.Where(x => x.TransactionDate >= from.Value);
                    if (toEnd.HasValue) tx = tx.Where(x => x.TransactionDate < toEnd.Value);
                    var moves = await tx.Where(x => x.Document.Type == InventoryDocumentType.ScrapReceipt || x.Document.Type == InventoryDocumentType.ScrapSale || x.Quantity > 0)
                        .GroupBy(x => new { x.WarehouseId, Sale = x.Document.Type == InventoryDocumentType.ScrapSale })
                        .Select(g => new { g.Key.WarehouseId, g.Key.Sale, Q = g.Sum(z => z.Quantity), V = g.Sum(z => z.CostAmount ?? 0) }).ToListAsync(ct);
                    foreach (var w in onHand.OrderBy(x => x.Title))
                        t.Rows.Add(new Dictionary<string, object>
                        {
                            ["warehouse"] = w.Title, ["received"] = moves.Where(m => m.WarehouseId == w.WarehouseId && !m.Sale).Sum(m => Math.Max(0, m.Q)),
                            ["receivedValue"] = R(moves.Where(m => m.WarehouseId == w.WarehouseId && !m.Sale).Sum(m => Math.Max(0, m.V))),
                            ["sold"] = -moves.Where(m => m.WarehouseId == w.WarehouseId && m.Sale).Sum(m => m.Q), ["onHand"] = w.Q,
                        });
                    return null;
                }
                case "purchases-by-provider":
                case "purchases-by-project":
                {
                    bool byProvider = t.Key == "purchases-by-provider";
                    t.Columns.AddRange(new[] { C(byProvider ? "provider" : "project", byProvider ? "تامین‌کننده / پیمانکار" : "پروژه"), C("kind", "نوع خرید"), C("orders", "تعداد سفارش", "number"), C("total", "مبلغ سفارش (ریال)", "money"), C("received", "ارزش دریافت‌شده (ریال)", "money") });
                    IQueryable<PurchaseOrder> o = _uow.Set<PurchaseOrder>().AsNoTracking().Where(x => !x.Deleted && x.Status != PurchaseOrderStatus.Cancelled && x.Status != PurchaseOrderStatus.Draft);
                    if (from.HasValue) o = o.Where(x => x.OrderDate >= from.Value);
                    if (to.HasValue) o = o.Where(x => x.OrderDate <= to.Value);
                    if (q.ProviderId.HasValue) o = o.Where(x => x.ProviderId == q.ProviderId.Value);
                    if (q.WorkOrderId.HasValue) o = o.Where(x => x.WorkOrderId == q.WorkOrderId.Value);
                    var rows = await o.Select(x => new { x.Id, x.ProviderId, P = x.Provider.Name, x.WorkOrderId, W = x.WorkOrder.Code, x.Kind, x.TotalAmount, Rec = x.Lines.Sum(l => (decimal?)(l.ReceivedQuantity * l.UnitPrice)) ?? 0 }).Take(10000).ToListAsync(ct);
                    foreach (var g in rows.GroupBy(x => new { Key = byProvider ? (object)x.ProviderId : (object)(x.WorkOrderId ?? 0), x.Kind }).OrderByDescending(g => g.Sum(x => x.TotalAmount)))
                        t.Rows.Add(new Dictionary<string, object>
                        {
                            [byProvider ? "provider" : "project"] = byProvider ? g.First().P : (g.First().W ?? "بدون پروژه"), ["kind"] = g.Key.Kind == PurchaseKind.Goods ? "کالا" : "خدمات و پیمانکاری",
                            ["orders"] = g.Count(), ["total"] = R(g.Sum(x => x.TotalAmount)), ["received"] = R(g.Sum(x => x.Rec)),
                        });
                    t.Totals["total"] = R(rows.Sum(x => x.TotalAmount));
                    return null;
                }
                case "petty-cash":
                {
                    t.Columns.AddRange(new[] { C("holder", "عنوان تنخواه"), C("ceiling", "سقف (ریال)", "money"), C("balance", "مانده (ریال)", "money"), C("openSummaries", "خلاصه‌های تسویه‌نشده", "number"), C("openAmount", "مبلغ تسویه‌نشده (ریال)", "money") });
                    var cash = await _uow.Set<FnPettyCash>().AsNoTracking().Where(x => x.IsActive).Select(x => new { x.Id, x.HolderTitle, x.Ceiling, x.Balance }).ToListAsync(ct);
                    var open = await _uow.Set<FnPettyCashSummary>().AsNoTracking().Where(x => !x.Deleted && x.Status != FnSummaryStatus.Final).GroupBy(x => x.PettyCashId).Select(g => new { g.Key, N = g.Count(), A = g.Sum(z => z.TotalAmount) }).ToListAsync(ct);
                    foreach (var c in cash.OrderBy(x => x.HolderTitle))
                    {
                        var o = open.FirstOrDefault(x => x.Key == c.Id);
                        t.Rows.Add(new Dictionary<string, object> { ["holder"] = c.HolderTitle, ["ceiling"] = c.Ceiling, ["balance"] = c.Balance, ["openSummaries"] = o?.N ?? 0, ["openAmount"] = o?.A ?? 0m });
                    }
                    t.Totals["balance"] = cash.Sum(x => x.Balance);
                    t.Totals["openAmount"] = open.Sum(x => x.A);
                    return null;
                }
                case "contracts":
                {
                    t.Columns.AddRange(new[] { C("code", "کد"), C("title", "عنوان"), C("employer", "کارفرما"), C("start", "شروع", "date"), C("end", "پایان", "date"), C("price", "مبلغ (ریال)", "money"), C("workOrders", "تعداد دستور کار", "number") });
                    var rows = await _uow.Set<ContractEntity>().AsNoTracking().Where(x => !x.Deleted && x.Status == ContractStatus.Current).OrderBy(x => x.Code)
                        .Select(x => new { x.Code, x.Title, x.Employer, x.StartDate, x.EndDate, x.Price, W = x.WorkOrders.Count() }).Take(2000).ToListAsync(ct);
                    foreach (var r in rows)
                        t.Rows.Add(new Dictionary<string, object> { ["code"] = r.Code, ["title"] = r.Title, ["employer"] = r.Employer, ["start"] = r.StartDate.DateTime, ["end"] = r.EndDate.DateTime, ["price"] = r.Price ?? 0m, ["workOrders"] = r.W });
                    t.Totals["price"] = rows.Sum(x => x.Price ?? 0m);
                    return null;
                }
                case "count-discrepancy":
                {
                    t.Columns.AddRange(new[] { C("count", "شماره شمارش"), C("date", "تاریخ", "date"), C("warehouse", "انبار"), C("code", "کد کالا"), C("title", "عنوان"), C("system", "موجودی دفتر", "number"), C("counted", "شمارش‌شده", "number"), C("diff", "مغایرت", "number"), C("value", "ارزش مغایرت (ریال)", "money") });
                    IQueryable<StockCountLine> l = _uow.Set<StockCountLine>().AsNoTracking().Where(x => !x.StockCount.Deleted && x.StockCount.Status != StockCountStatus.Cancelled && (x.RecountedQuantity ?? x.CountedQuantity) != null);
                    if (q.WarehouseId.HasValue) l = l.Where(x => x.StockCount.WarehouseId == q.WarehouseId.Value);
                    if (from.HasValue) l = l.Where(x => x.StockCount.CountDate >= from.Value);
                    if (to.HasValue) l = l.Where(x => x.StockCount.CountDate <= to.Value);
                    var rows = await l.Select(x => new { x.StockCount.Number, x.StockCount.CountDate, W = x.StockCount.Warehouse.Title, x.ItemId, x.Item.Code, x.Item.Title, x.SystemQuantity, Counted = (x.RecountedQuantity ?? x.CountedQuantity).Value })
                        .Where(x => x.Counted != x.SystemQuantity).OrderByDescending(x => x.CountDate).Take(5000).ToListAsync(ct);
                    List<int> ids = rows.Select(x => x.ItemId).Distinct().ToList();
                    Dictionary<int, decimal> avg = await _uow.Set<ItemCost>().AsNoTracking().Where(c => ids.Contains(c.ItemId)).ToDictionaryAsync(c => c.ItemId, c => c.AverageCost, ct);
                    foreach (var r in rows)
                        t.Rows.Add(new Dictionary<string, object> { ["count"] = r.Number, ["date"] = r.CountDate, ["warehouse"] = r.W, ["code"] = r.Code, ["title"] = r.Title, ["system"] = r.SystemQuantity, ["counted"] = r.Counted, ["diff"] = r.Counted - r.SystemQuantity, ["value"] = R((r.Counted - r.SystemQuantity) * (avg.TryGetValue(r.ItemId, out decimal a) ? a : 0m)) });
                    t.Totals["value"] = t.Rows.Sum(r => (decimal)r["value"]);
                    return null;
                }
                case "po-receipt-invoice":
                {
                    t.Columns.AddRange(new[] { C("order", "سفارش"), C("provider", "تامین‌کننده"), C("item", "کالا"), C("ordered", "سفارش‌شده", "number"), C("received", "دریافت‌شده", "number"), C("invoiced", "فاکتورشده", "number"),
                        C("orderPrice", "قیمت سفارش (ریال)", "money"), C("invoicePrice", "قیمت فاکتور (ریال)", "money"), C("flag", "مغایرت") });
                    IQueryable<PurchaseOrderLine> l = _uow.Set<PurchaseOrderLine>().AsNoTracking().Where(x => !x.PurchaseOrder.Deleted && x.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled && x.PurchaseOrder.Status != PurchaseOrderStatus.Draft);
                    if (from.HasValue) l = l.Where(x => x.PurchaseOrder.OrderDate >= from.Value);
                    if (to.HasValue) l = l.Where(x => x.PurchaseOrder.OrderDate <= to.Value);
                    if (q.ProviderId.HasValue) l = l.Where(x => x.PurchaseOrder.ProviderId == q.ProviderId.Value);
                    var lines = await l.OrderByDescending(x => x.PurchaseOrderId).Take(4000).Select(x => new { x.Id, Order = x.PurchaseOrder.Number, P = x.PurchaseOrder.Provider.Name, Item = x.Item != null ? x.Item.Code : x.Description, x.Quantity, x.ReceivedQuantity, x.UnitPrice }).ToListAsync(ct);
                    List<int> lineIds = lines.Select(x => x.Id).ToList();
                    var inv = await _uow.Set<SupplierInvoiceLine>().AsNoTracking().Where(x => lineIds.Contains(x.OrderLineId) && x.Invoice.Status != SupplierInvoiceStatus.Cancelled && !x.Invoice.Deleted)
                        .GroupBy(x => x.OrderLineId).Select(g => new { g.Key, Q = g.Sum(z => z.Quantity), V = g.Sum(z => z.Quantity * z.UnitPrice) }).ToListAsync(ct);
                    foreach (var r in lines)
                    {
                        var i = inv.FirstOrDefault(x => x.Key == r.Id);
                        decimal invQ = i?.Q ?? 0m, invPrice = i != null && i.Q > 0 ? Math.Round(i.V / i.Q, 2) : 0m;
                        var flags = new List<string>();
                        if (r.ReceivedQuantity != r.Quantity) flags.Add(r.ReceivedQuantity < r.Quantity ? "دریافت ناقص" : "دریافت بیش از سفارش");
                        if (invQ != r.ReceivedQuantity) flags.Add(invQ == 0 ? "بدون فاکتور" : "مقدار فاکتور با رسید یکی نیست");
                        if (i != null && invPrice != r.UnitPrice) flags.Add("قیمت فاکتور با سفارش فرق دارد");
                        if (flags.Count == 0) continue;
                        t.Rows.Add(new Dictionary<string, object> { ["order"] = r.Order, ["provider"] = r.P, ["item"] = r.Item, ["ordered"] = r.Quantity, ["received"] = r.ReceivedQuantity, ["invoiced"] = invQ, ["orderPrice"] = r.UnitPrice, ["invoicePrice"] = invPrice, ["flag"] = string.Join("؛ ", flags) });
                    }
                    return null;
                }
                case "item-card":
                {
                    if (!q.ItemId.HasValue) return "کالا را انتخاب کنید.";
                    t.Columns.AddRange(new[] { C("label", "مورد"), C("value", "مقدار") });
                    var item = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == q.ItemId.Value && !i.Deleted).Select(i => new
                    {
                        i.Id, i.Code, i.Title, i.Unit, Group = i.ItemGroup.Title, i.TechnicalCode, i.Material, i.Standard, i.Specification, i.Weight, i.MinStock, i.MaxStock, i.MescCode, i.LegacyCode, i.PartNumber,
                        i.Platform, i.EquipmentType, i.ValveModel, i.IranCode, i.RequiresIncomingInspection, i.ShelfLifeDays,
                    }).FirstOrDefaultAsync(ct);
                    if (item == null) return InventoryMessages.ItemNotFound;
                    void Add(string label, object value) { if (value != null && !(value is string s && s.Length == 0)) t.Rows.Add(new Dictionary<string, object> { ["label"] = label, ["value"] = value is decimal d ? d.ToString("0.####", CultureInfo.InvariantCulture) : value.ToString() }); }
                    Add("کد کالا", item.Code); Add("عنوان", item.Title); Add("واحد", item.Unit); Add("گروه", item.Group); Add("کد فنی", item.TechnicalCode); Add("کد MESC", item.MescCode);
                    Add("کد قدیم", item.LegacyCode); Add("شماره قطعه", item.PartNumber); Add("ایران کد", item.IranCode); Add("جنس", item.Material); Add("استاندارد", item.Standard); Add("مشخصات", item.Specification);
                    Add("وزن واحد (کیلوگرم)", item.Weight); Add("پلتفرم", item.Platform); Add("نوع تجهیز", item.EquipmentType); Add("مدل شیر", item.ValveModel);
                    Add("حداقل موجودی", item.MinStock); Add("حداکثر موجودی", item.MaxStock); Add("بازرسی ورودی", item.RequiresIncomingInspection ? "دارد" : "ندارد"); Add("عمر مفید (روز)", item.ShelfLifeDays);
                    var stock = await _uow.Set<StockBalance>().AsNoTracking().Where(b => b.ItemId == item.Id && b.Quantity != 0).GroupBy(b => b.Warehouse.Title).Select(g => new { W = g.Key, Q = g.Sum(z => z.Quantity) }).ToListAsync(ct);
                    foreach (var s in stock.OrderBy(x => x.W)) Add("موجودی " + s.W, s.Q);
                    if (InventoryPriceAccess.SeesValues(_user))
                    {
                        ItemCost cost = await _uow.Set<ItemCost>().AsNoTracking().FirstOrDefaultAsync(c => c.ItemId == item.Id, ct);
                        if (cost != null) { Add("بهای میانگین (ریال)", cost.AverageCost); Add("ارزش موجودی (ریال)", cost.Value); }
                    }
                    t.Note = "کارت استاندارد کالا برای چاپ";
                    return null;
                }
                case "request-trace":
                {
                    string number = InventoryText.Clean(q.RequestNumber);
                    if (number == null) return "شماره درخواست (خرید یا کالا) را وارد کنید.";
                    t.Columns.AddRange(new[] { C("kind", "نوع"), C("code", "کد / شماره"), C("title", "عنوان"), C("detail", "توضیح") });
                    string kind = number.StartsWith("MR", StringComparison.OrdinalIgnoreCase) ? "mr" : "pr";
                    var trace = await _trace.ReverseAsync(new ReverseTraceQuery { Q = number, Kind = kind, Depth = 6 }, ct);
                    if (!trace.Success || trace.Data == null) return trace.Message ?? "درخواستی با این شماره پیدا نشد.";
                    foreach (ReverseRootDto r in trace.Data.Roots)
                        t.Rows.Add(new Dictionary<string, object> { ["kind"] = "ریشه", ["code"] = r.ItemCode, ["title"] = r.ItemTitle, ["detail"] = $"بچ {r.LotNumber} | سفارش {r.PurchaseOrderNumber} | رسید {r.ReceiptNumber} | درخواست {r.PurchaseRequestNumber}" });
                    foreach (ReverseItemDto i in trace.Data.Items)
                        t.Rows.Add(new Dictionary<string, object> { ["kind"] = "قطعه / مجموعه مصرف‌کننده", ["code"] = i.Code, ["title"] = i.Title, ["detail"] = $"{i.NodeCount} گره، سطح {i.MinLevel} تا {i.MaxLevel}" });
                    foreach (ReverseProjectDto p in trace.Data.Projects)
                        t.Rows.Add(new Dictionary<string, object> { ["kind"] = "پروژه", ["code"] = p.WorkOrderCode, ["title"] = p.WorkOrderTitle, ["detail"] = $"قرارداد {p.ContractCode}" });
                    foreach (ReverseDeliveryDto d in trace.Data.Deliveries)
                        t.Rows.Add(new Dictionary<string, object> { ["kind"] = "تحویل", ["code"] = d.DocumentNumber, ["title"] = d.ItemCode, ["detail"] = $"{FiscalCalendars.Current.Text(d.DocumentDate)} | {d.Quantity.ToString("0.####", CultureInfo.InvariantCulture)}" });
                    return null;
                }
                case "consignment":
                {
                    t.Columns.AddRange(new[] { C("number", "شماره رسید امانی"), C("date", "تاریخ", "date"), C("provider", "صاحب کالا"), C("warehouse", "انبار"), C("due", "سررسید عودت", "date"), C("open", "مانده عودت‌نشده", "number"), C("level", "هشدار") });
                    Dictionary<string, decimal> s = await Quality.QualitySettingKeys.LoadAsync(_uow, ct);
                    IQueryable<InventoryDocument> d = _uow.Set<InventoryDocument>().AsNoTracking().Where(x => !x.Deleted && x.Type == InventoryDocumentType.ConsignmentReceipt && x.Status == InventoryDocumentStatus.Confirmed);
                    if (q.WarehouseId.HasValue) d = d.Where(x => x.WarehouseId == q.WarehouseId.Value);
                    if (q.ProviderId.HasValue) d = d.Where(x => x.ProviderId == q.ProviderId.Value);
                    var docs = await d.Select(x => new { x.Id, x.Number, x.DocumentDate, P = x.Provider.Name, W = x.Warehouse.Title, x.ConsignmentDueDate }).ToListAsync(ct);
                    List<int> ids = docs.Select(x => x.Id).ToList();
                    var rec = (await _uow.Set<InventoryDocumentLine>().AsNoTracking().Where(l => ids.Contains(l.DocumentId)).GroupBy(l => l.DocumentId).Select(g => new { g.Key, Q = g.Sum(z => z.Quantity) }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.Q);
                    var ret = (await _uow.Set<InventoryDocumentLine>().AsNoTracking().Where(l => l.Document.ConsignmentOfDocumentId != null && ids.Contains(l.Document.ConsignmentOfDocumentId.Value) && !l.Document.Deleted && l.Document.Type == InventoryDocumentType.ConsignmentIssue && l.Document.Status == InventoryDocumentStatus.Confirmed)
                        .GroupBy(l => l.Document.ConsignmentOfDocumentId.Value).Select(g => new { g.Key, Q = g.Sum(z => z.Quantity) }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.Q);
                    foreach (var x in docs.OrderBy(z => z.ConsignmentDueDate))
                    {
                        decimal open = (rec.TryGetValue(x.Id, out decimal a) ? a : 0m) - (ret.TryGetValue(x.Id, out decimal b) ? b : 0m);
                        if (open <= 0) continue;
                        int level = x.ConsignmentDueDate.HasValue ? ConsignmentRules.Level(x.ConsignmentDueDate.Value, DateTime.Today, (int)s[Quality.QualitySettingKeys.ConsignmentAlertDays1], (int)s[Quality.QualitySettingKeys.ConsignmentAlertDays2]) : 0;
                        t.Rows.Add(new Dictionary<string, object> { ["number"] = x.Number, ["date"] = x.DocumentDate, ["provider"] = x.P, ["warehouse"] = x.W, ["due"] = x.ConsignmentDueDate, ["open"] = open, ["level"] = level == 3 ? "دیرکرد" : level == 2 ? "کمتر از سه روز" : level == 1 ? "کمتر از هفت روز" : "عادی" });
                    }
                    return null;
                }
                case "qc-pending":
                {
                    t.Columns.AddRange(new[] { C("number", "بازرسی"), C("receipt", "رسید"), C("item", "کالا"), C("quantity", "مقدار", "number"), C("since", "از تاریخ", "date"), C("workDays", "روز کاری انتظار", "number"), C("level", "وضعیت") });
                    Dictionary<string, decimal> s = await Quality.QualitySettingKeys.LoadAsync(_uow, ct);
                    WorkCalendar cal = await WorkCalendar.LoadAsync(_uow, ct);
                    var rows = await _uow.Set<QcInspection>().AsNoTracking().Where(x => !x.Deleted && x.Status == InspectionStatus.Pending).OrderBy(x => x.CreateDate).Take(2000)
                        .Select(x => new { x.Number, R = x.ReceiptDocument.Number, I = x.Item.Code, x.Quantity, x.CreateDate }).ToListAsync(ct);
                    foreach (var r in rows)
                    {
                        int days = cal.Between(r.CreateDate.Date, DateTime.Today);
                        t.Rows.Add(new Dictionary<string, object> { ["number"] = r.Number, ["receipt"] = r.R, ["item"] = r.I, ["quantity"] = r.Quantity, ["since"] = r.CreateDate.DateTime, ["workDays"] = days,
                            ["level"] = days >= (int)s[Quality.QualitySettingKeys.QcEscalateWorkDays] ? "تشدید به مدیر تضمین کیفیت و برنامه‌ریزی" : days >= (int)s[Quality.QualitySettingKeys.QcPendingWorkDays] ? "هشدار به کنترل کیفیت" : "در مهلت" });
                    }
                    return null;
                }
                case "freight":
                {
                    t.Columns.AddRange(new[] { C("number", "شماره"), C("date", "تاریخ", "date"), C("receipt", "رسید"), C("vehicle", "پلاک"), C("goods", "نوع کالا"), C("bol", "بارنامه"), C("carrier", "حمل‌کننده"), C("amount", "مبلغ (ریال)", "money"), C("status", "وضعیت") });
                    IQueryable<InventoryFreight> f = _uow.Set<InventoryFreight>().AsNoTracking().Where(x => !x.Deleted);
                    if (from.HasValue) f = f.Where(x => x.FreightDate >= from.Value);
                    if (to.HasValue) f = f.Where(x => x.FreightDate <= to.Value);
                    if (q.ProviderId.HasValue) f = f.Where(x => x.CarrierProviderId == q.ProviderId.Value);
                    var rows = await f.OrderByDescending(x => x.Id).Take(3000).Select(x => new { x.Number, x.FreightDate, R = x.ReceiptDocument.Number, x.VehiclePlate, x.GoodsType, x.BillOfLadingNumber, C = x.CarrierProvider.Name, x.Amount, x.Status }).ToListAsync(ct);
                    foreach (var r in rows)
                        t.Rows.Add(new Dictionary<string, object> { ["number"] = r.Number, ["date"] = r.FreightDate, ["receipt"] = r.R, ["vehicle"] = r.VehiclePlate, ["goods"] = r.GoodsType, ["bol"] = r.BillOfLadingNumber, ["carrier"] = r.C, ["amount"] = r.Amount, ["status"] = r.Status == FreightStatus.Approved ? "تاییدشده" : r.Status == FreightStatus.Voided ? "باطل" : "پیش‌نویس" });
                    t.Totals["amount"] = rows.Where(r => r.Status == FreightStatus.Approved).Sum(r => r.Amount);
                    return null;
                }
                case "assets":
                {
                    t.Columns.AddRange(new[] { C("tag", "برچسب"), C("title", "عنوان"), C("item", "کالا"), C("warehouse", "انبار"), C("location", "محل"), C("status", "وضعیت"), C("custodian", "تحویل‌گیرنده (واحد)"), C("cost", "بها (ریال)", "money") });
                    IQueryable<InventoryAsset> a = _uow.Set<InventoryAsset>().AsNoTracking().Where(x => !x.Deleted);
                    if (q.WarehouseId.HasValue) a = a.Where(x => x.WarehouseId == q.WarehouseId.Value);
                    var rows = await a.OrderBy(x => x.Tag).Take(5000).Select(x => new { x.Tag, x.Title, I = x.Item.Code, W = x.Warehouse.Title, L = x.Location.Code, x.Status, x.CustodianUnit, x.Cost }).ToListAsync(ct);
                    string[] st = { "", "در خدمت", "در تعمیر", "مفقود", "اسقاط" };
                    foreach (var r in rows)
                        t.Rows.Add(new Dictionary<string, object> { ["tag"] = r.Tag, ["title"] = r.Title, ["item"] = r.I, ["warehouse"] = r.W, ["location"] = r.L, ["status"] = st[Math.Min(4, Math.Max(0, (int)r.Status))], ["custodian"] = r.CustodianUnit, ["cost"] = r.Cost ?? 0m });
                    t.Totals["cost"] = rows.Sum(r => r.Cost ?? 0m);
                    return null;
                }
                default: // reorder
                {
                    t.Columns.AddRange(new[] { C("code", "کد کالا"), C("title", "عنوان"), C("unit", "واحد"), C("onHand", "موجودی", "number"), C("onOrder", "در راه", "number"), C("safety", "موجودی ایمنی", "number"), C("daily", "مصرف روزانه", "number"), C("lead", "زمان تامین (روز)", "number"), C("rop", "نقطه سفارش", "number"), C("suggested", "مقدار پیشنهادی", "number"), C("request", "درخواست باز") });
                    var r = await _reorder.GetAsync(new ReorderQuery { ItemGroupId = q.ItemGroupId, Platform = q.Platform, Contains = q.Contains }, ct);
                    if (!r.Success) return r.Message;
                    foreach (ReorderRowDto x in r.Data)
                        t.Rows.Add(new Dictionary<string, object> { ["code"] = x.ItemCode, ["title"] = x.ItemTitle, ["unit"] = x.Unit, ["onHand"] = x.OnHand, ["onOrder"] = x.OnOrder, ["safety"] = x.SafetyStock, ["daily"] = x.DailyUse, ["lead"] = x.LeadDays, ["rop"] = x.ReorderPoint, ["suggested"] = x.SuggestedQuantity, ["request"] = x.OpenRequest ? "دارد" : "" });
                    return null;
                }
            }
        }
    }
}
