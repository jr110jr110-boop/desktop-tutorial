using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.InventoryDtos;
using static Services.NasbNiro.Ceo.Drills.DrillText;

namespace Services.NasbNiro.Ceo.Drills
{
    /// <summary>The warehouse drills: items below the minimum, stock value, slow-moving stock, the busiest items, the quarantine and the open material requests.</summary>
    internal static class InventoryDrills
    {
        private const string M = CeoModules.Inventory;

        public static IEnumerable<DrillDefinition> Build()
        {
            yield return ItemsBelowMinimum();
            yield return StockValueByWarehouse();
            yield return SlowMoving();
            yield return TopMovements();
            yield return Quarantine();
            yield return MaterialRequestsOpen();
        }

        private static string KardexLink(int itemId) => "/inventory/kardex?itemId=" + Id(itemId);

        private static string WarehouseKindText(WarehouseKind kind) => kind switch
        {
            WarehouseKind.General => "عمومی",
            WarehouseKind.RawMaterial => "مواد اولیه",
            WarehouseKind.Parts => "قطعات",
            WarehouseKind.WorkInProgress => "در جریان ساخت",
            WarehouseKind.FinishedGoods => "کالای ساخته‌شده",
            WarehouseKind.Quarantine => "قرنطینه",
            WarehouseKind.Scrap => "ضایعات",
            WarehouseKind.Subcontractor => "نزد پیمانکار",
            WarehouseKind.CustomerProperty => "اموال مشتری",
            WarehouseKind.Patterns => "مدل‌های ریخته‌گری",
            _ => kind.ToString(),
        };

        private static string RequestState(MaterialRequestStatus status) => status switch
        {
            MaterialRequestStatus.Submitted => "ارسال‌شده، منتظر تأیید",
            MaterialRequestStatus.Approved => "تأییدشده، منتظر صدور حواله",
            MaterialRequestStatus.Draft => "پیش‌نویس",
            MaterialRequestStatus.Rejected => "ردشده",
            _ => "بسته‌شده",
        };

        private static string YesNo(bool value) => value ? "بله" : "خیر";

        // ------------------------------------------------------------------------------------------ minimum stock

        private static DrillDefinition ItemsBelowMinimum() => new DrillDefinition
        {
            Key = "inv.items_below_minimum", Module = M, Title = "کالاهای زیر حداقل موجودی",
            Description = "کالاهای فعالی که حداقل موجودی دارند و موجودی کل‌شان بدون انبار ضایعات از آن کمتر است (هم‌پایه با شمارنده‌ی داشبورد)؛ کمترین پوشش اول. کمبود = حداقل منهای موجودی؛ پوشش = موجودی به حداقل.",
            Link = "/inventory/stock", RecordType = "Item", RecordLabel = "کالا",
            Columns = new[]
            {
                Col.Text("code", "کد کالا"), Col.Text("title", "کالا"), Col.Text("group", "گروه"), Col.Text("unit", "واحد"), Col.Num("onHand", "موجودی"), Col.Num("minStock", "حداقل موجودی"),
                Col.Num("shortage", "کمبود"), Col.Pct("coverPercent", "پوشش"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                var uow = c.Get<IUnitOfWork>();
                // the rule of the counter of the dashboard (CeoDashboardService): the active items with a minimum whose stock, scrap warehouses left out, is below it
                IQueryable<StockBalance> balances = uow.Set<StockBalance>().AsNoTracking().Where(b => b.Warehouse.Kind != WarehouseKind.Scrap);
                var low = await uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && i.IsActive && i.MinStock > 0)
                    .Select(i => new
                    {
                        i.Id, i.Code, i.Title, i.Unit, Group = i.ItemGroup.Title, i.MinStock,
                        OnHand = balances.Where(b => b.ItemId == i.Id).Sum(b => (decimal?)b.Quantity) ?? 0m,
                    })
                    .Where(x => x.OnHand < x.MinStock).ToListAsync(ct);
                var ordered = low.Select(i => new { Item = i, Cover = Percent(Math.Max(i.OnHand, 0m), i.MinStock) })
                    .OrderBy(x => x.Cover).ThenByDescending(x => x.Item.MinStock - x.Item.OnHand).ThenBy(x => x.Item.Code, StringComparer.Ordinal).ToList();
                List<Row> rows = ordered.Select(x => new Row(KardexLink(x.Item.Id))
                {
                    ["id"] = x.Item.Id, ["code"] = x.Item.Code, ["title"] = x.Item.Title, ["group"] = x.Item.Group, ["unit"] = x.Item.Unit, ["onHand"] = x.Item.OnHand,
                    ["minStock"] = x.Item.MinStock, ["shortage"] = x.Item.MinStock - x.Item.OnHand, ["coverPercent"] = x.Cover,
                }).ToList();
                return DrillResult.Slice(rows, c, "موجودی، جمع همه‌ی انبارها به‌جز انبار ضایعات است؛ کالای غیرفعال یا بدون حداقل موجودی تعریف‌شده نمی‌آید.", new Dictionary<string, object>
                {
                    ["outOfStock"] = ordered.Count(x => x.Item.OnHand <= 0m),
                });
            },
        };

        // ------------------------------------------------------------------------------------------ stock value

        private static DrillDefinition StockValueByWarehouse() => new DrillDefinition
        {
            Key = "inv.stock_value_by_warehouse", Module = M, Title = "ارزش موجودی به تفکیک انبار",
            Description = "ارزش ریالی موجودی هر انبار = مقدار مثبت هر کالا در آن انبار × بهای میانگین موزون کالا (همان تابعی که نمودار «ارزش موجودی به تفکیک انبار» داشبورد از آن می‌آید)؛ بیشترین ارزش اول. اموال مشتری ارزش‌گذاری نمی‌شود و نمی‌آید.",
            Link = "/inventory/stock", RecordType = "Warehouse", RecordLabel = "انبار",
            Columns = new[]
            {
                Col.Text("code", "کد انبار"), Col.Text("title", "انبار"), Col.Text("kind", "نوع"), Col.Num("items", "تعداد کالا"), Col.Rial("value", "ارزش موجودی"), Col.Pct("sharePercent", "سهم از کل"),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, FinanceRoles.Readers);
                var uow = c.Get<IUnitOfWork>();
                // the single function behind the chart of the dashboard: positive balances only, customer property left out, the biggest warehouse first
                List<CeoWarehouseValue> values = await CeoFigures.StockValueByWarehouseAsync(uow, ct);
                List<int> ids = values.Select(v => v.WarehouseId).ToList();
                Dictionary<int, WarehouseKind> kinds = await uow.Set<Warehouse>().AsNoTracking().Where(w => ids.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Kind, ct);
                var itemPairs = await uow.Set<StockBalance>().AsNoTracking().Where(b => ids.Contains(b.WarehouseId) && b.Quantity > 0 && (b.LotId == null || !b.Lot.IsCustomerProperty))
                    .Join(uow.Set<ItemCost>().AsNoTracking(), b => b.ItemId, x => x.ItemId, (b, x) => new { b.WarehouseId, b.ItemId })
                    .GroupBy(x => new { x.WarehouseId, x.ItemId }).Select(g => new { g.Key.WarehouseId, g.Key.ItemId }).ToListAsync(ct);
                Dictionary<int, int> itemCounts = itemPairs.GroupBy(x => x.WarehouseId).ToDictionary(g => g.Key, g => g.Count());
                decimal dashboardValue = await uow.Set<ItemCost>().AsNoTracking().SumAsync(x => (decimal?)x.Value, ct) ?? 0m;

                decimal allocated = values.Sum(v => v.Value);
                List<Row> rows = values.Select(v => new Row
                {
                    ["id"] = v.WarehouseId, ["code"] = v.Code, ["title"] = v.Title, ["kind"] = kinds.TryGetValue(v.WarehouseId, out WarehouseKind kind) ? WarehouseKindText(kind) : null,
                    ["items"] = itemCounts.TryGetValue(v.WarehouseId, out int count) ? count : 0, ["value"] = Round(v.Value, 0), ["sharePercent"] = Percent(v.Value, allocated),
                }).ToList();
                return DrillResult.Slice(rows, c, "ارزش هر انبار با بهای میانگین موزون کالا حساب می‌شود؛ تفاوت با ارزش کل دفتر موجودی (موجودی منفی یا کالای بدون بها) در «تفاوت» می‌آید.", new Dictionary<string, object>
                {
                    ["totalValue"] = Round(dashboardValue, 0), ["allocatedValue"] = Round(allocated, 0), ["difference"] = Round(dashboardValue - allocated, 0),
                });
            },
        };

        // ------------------------------------------------------------------------------------------ slow moving

        private static DrillDefinition SlowMoving() => new DrillDefinition
        {
            Key = "inv.slow_moving", Module = M, Title = "کالاهای کم‌گردش",
            Description = "کالاهایی که موجودی دارند ولی دست‌کم به تعداد روز داده‌شده هیچ گردشی (ورود یا خروج) نداشته‌اند؛ بیشترین سرمایه‌ی خوابیده اول. پیش‌فرض ۱۸۰ روز.",
            Link = "/inventory/stock", RecordType = "Item", RecordLabel = "کالا",
            Parameters = new[] { Prm.Days(180, 3650, "کالاهایی که دست‌کم این تعداد روز گردش نداشته‌اند") },
            Columns = new[]
            {
                Col.Text("code", "کد کالا"), Col.Text("title", "کالا"), Col.Text("group", "گروه"), Col.Text("unit", "واحد"), Col.Num("quantity", "موجودی"), Col.Rial("value", "ارزش موجودی"),
                Col.Date("lastMovement", "آخرین گردش"), Col.Num("daysSince", "روز از آخرین گردش"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, FinanceRoles.Readers);
                int days = c.Args.Int("days");
                // the movements of the last <days> days exist or not: a last movement before the day after the cutoff day is a slow item
                DateTime beforeDay = c.Today.AddDays(-days).AddDays(1);
                var uow = c.Get<IUnitOfWork>();
                IQueryable<StockTransaction> moves = uow.Set<StockTransaction>().AsNoTracking().Where(t => !t.IsReversal && t.Document.Status == InventoryDocumentStatus.Confirmed);
                var slow = (await uow.Set<ItemCost>().AsNoTracking().Where(x => x.Quantity > 0)
                        .Select(x => new
                        {
                            x.ItemId, Code = x.Item.Code, Title = x.Item.Title, Unit = x.Item.Unit, Group = x.Item.ItemGroup.Title, x.Quantity, x.Value,
                            Last = moves.Where(t => t.ItemId == x.ItemId).Max(t => (DateTime?)t.TransactionDate),
                        })
                        .Where(x => x.Last == null || x.Last < beforeDay).ToListAsync(ct))
                    .OrderByDescending(x => x.Value).ThenBy(x => x.Code, StringComparer.Ordinal).ToList();

                List<Row> rows = slow.Select(x => new Row(KardexLink(x.ItemId))
                {
                    ["id"] = x.ItemId, ["code"] = x.Code, ["title"] = x.Title, ["group"] = x.Group, ["unit"] = x.Unit, ["quantity"] = x.Quantity,
                    ["value"] = Round(x.Value, 0), ["lastMovement"] = DrillDates.Text(x.Last), ["daysSince"] = x.Last.HasValue ? DaysBetween(x.Last.Value, c.Today) : (int?)null,
                }).ToList();
                return DrillResult.Slice(rows, c, "گردش یعنی هر ورود یا خروج تأییدشده‌ی انبار (سند لغوشده شمرده نمی‌شود)؛ کالای بدون هیچ گردش ثبت‌شده هم می‌آید.", new Dictionary<string, object>
                {
                    ["value"] = Round(slow.Sum(x => x.Value), 0),
                });
            },
        };

        // ------------------------------------------------------------------------------------------ the busiest items

        /// <summary>The longest range of inv.top_movements (the movements of the range are grouped in memory).</summary>
        public const int MaxMovementDays = 366;

        /// <summary>The kinds of movement of inv.top_movements: the purchase receipts with their returns, the issues to production with their returns, or all but transfers and stage conversions.</summary>
        public static readonly string[] MovementKinds = { "receipt", "issue", "all" };

        private static DrillDefinition TopMovements() => new DrillDefinition
        {
            Key = "inv.top_movements", Module = M, Title = "پرگردش‌ترین کالاها",
            Description = "کالاهایی که در بازه بیشترین ارزش گردش انبار (ورود + خروج به بهای میانگین موزون) را داشته‌اند؛ انتقال بین انبارها و تبدیل مرحله شمرده نمی‌شود. می‌شود فقط ورودها (رسید خرید و برگشت به تأمین‌کننده؛ همان «رسید خرید» نمودار جریان موجودی داشبورد) یا فقط خروج‌ها (خروج برای مصرف و برگشت از تولید؛ همان «مصرف تولید» نمودار) را خواست. بازه حداکثر ۳۶۶ روز؛ پیش‌فرض ۳۰ روز اخیر.",
            Link = "/inventory/kardex", RecordType = "Item", RecordLabel = "کالا",
            Parameters = Prm.Last30Days().Concat(new[]
            {
                Prm.Str("kind", "نوع حرکت: receipt (رسید خرید)، issue (مصرف تولید) یا all (همه)؛ پیش‌فرض all", "all", MovementKinds),
            }).ToArray(),
            Columns = new[]
            {
                Col.Text("code", "کد کالا"), Col.Text("title", "کالا"), Col.Text("unit", "واحد"), Col.Num("receivedQuantity", "ورود"), Col.Num("issuedQuantity", "خروج"), Col.Rial("receivedValue", "ارزش ورود"),
                Col.Rial("issuedValue", "ارزش خروج"), Col.Rial("totalValue", "جمع ارزش گردش"), Col.Num("documents", "تعداد سند"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, FinanceRoles.Readers);
                DateTime from = c.Args.Date("from").Date, to = c.Args.Date("to").Date, end = to.AddDays(1);
                if ((to - from).TotalDays + 1 > MaxMovementDays)
                    throw new DrillFailureException("بازه‌ی گزارش نباید بیش از " + MaxMovementDays.ToString(CultureInfo.InvariantCulture) + " روز باشد؛ بازه‌ی کوتاه‌تری بدهید.");
                string kind = c.Args.Str("kind") ?? "all";
                var uow = c.Get<IUnitOfWork>();
                IQueryable<StockTransaction> moves = uow.Set<StockTransaction>().AsNoTracking()
                    .Where(t => !t.IsReversal && t.TransactionDate >= from && t.TransactionDate < end && t.Document.Status == InventoryDocumentStatus.Confirmed &&
                                t.Document.Type != InventoryDocumentType.Transfer && t.Document.Type != InventoryDocumentType.StageConversion);
                if (kind == "receipt") moves = moves.Where(t => t.Document.Type == InventoryDocumentType.PurchaseReceipt || t.Document.Type == InventoryDocumentType.SupplierReturn);
                else if (kind == "issue") moves = moves.Where(t => t.Document.Type == InventoryDocumentType.Issue || t.Document.Type == InventoryDocumentType.ProductionReturn);

                var perDocument = await moves
                    .GroupBy(t => new { t.ItemId, t.DocumentId })
                    .Select(g => new
                    {
                        g.Key.ItemId, g.Key.DocumentId,
                        InQuantity = g.Sum(t => t.Quantity > 0 ? t.Quantity : 0m), OutQuantity = g.Sum(t => t.Quantity < 0 ? -t.Quantity : 0m),
                        InValue = g.Sum(t => t.Quantity > 0 ? (t.CostAmount ?? 0m) : 0m), OutValue = g.Sum(t => t.Quantity < 0 ? -(t.CostAmount ?? 0m) : 0m),
                    }).ToListAsync(ct);
                var perItem = perDocument.GroupBy(x => x.ItemId).Select(g => new
                {
                    ItemId = g.Key, InQuantity = g.Sum(x => x.InQuantity), OutQuantity = g.Sum(x => x.OutQuantity), InValue = g.Sum(x => x.InValue), OutValue = g.Sum(x => x.OutValue), Documents = g.Count(),
                }).OrderByDescending(x => x.InValue + x.OutValue).ThenBy(x => x.ItemId).ToList();

                // the item names are read only for the page that is shown
                var page = perItem.Skip(Math.Max(0, c.Page - 1) * c.Top).Take(c.Top).ToList();
                List<int> ids = page.Select(x => x.ItemId).ToList();
                Dictionary<int, (string Code, string Title, string Unit)> items = (await uow.Set<InventoryItem>().AsNoTracking().Where(i => ids.Contains(i.Id))
                        .Select(i => new { i.Id, i.Code, i.Title, i.Unit }).ToListAsync(ct))
                    .ToDictionary(i => i.Id, i => (i.Code, i.Title, i.Unit));
                var rows = new List<Dictionary<string, object>>();
                foreach (var x in page)
                {
                    items.TryGetValue(x.ItemId, out var item);
                    rows.Add(new Row(KardexLink(x.ItemId))
                    {
                        ["id"] = x.ItemId, ["code"] = item.Code, ["title"] = item.Title, ["unit"] = item.Unit, ["receivedQuantity"] = x.InQuantity, ["issuedQuantity"] = x.OutQuantity,
                        ["receivedValue"] = Round(x.InValue, 0), ["issuedValue"] = Round(x.OutValue, 0), ["totalValue"] = Round(x.InValue + x.OutValue, 0), ["documents"] = x.Documents,
                    });
                }
                decimal receivedValue = perItem.Sum(x => x.InValue), issuedValue = perItem.Sum(x => x.OutValue);
                var totals = new Dictionary<string, object> { ["receivedValue"] = Round(receivedValue, 0), ["issuedValue"] = Round(issuedValue, 0) };
                // the figure of the chart: receipts net of the returns to the supplier, or the consumption net of the returns from production
                if (kind == "receipt") totals["netValue"] = Round(receivedValue - issuedValue, 0);
                else if (kind == "issue") totals["netValue"] = Round(issuedValue - receivedValue, 0);
                return new DrillResult
                {
                    Rows = rows,
                    TotalCount = perItem.Count,
                    Note = "ارزش هر حرکت همان بهای میانگین موزون ثبت‌شده در حرکت است؛ حرکت‌های قبل از هزینه‌یابی ارزش صفر دارند." +
                           (kind == "all" ? string.Empty : " «ارزش خالص» در جمع‌ها = " + (kind == "receipt" ? "ورود منهای برگشت به تأمین‌کننده" : "خروج منهای برگشت از تولید") + "، همان عدد نمودار."),
                    Totals = totals,
                };
            },
        };

        // ------------------------------------------------------------------------------------------ quarantine

        private static DrillDefinition Quarantine() => new DrillDefinition
        {
            Key = "inv.quarantine", Module = M, Title = "موجودی در قرنطینه",
            Description = "ردیف‌های موجودی مثبت در انبارهای قرنطینه که منتظر بازرسی ورودی یا تصمیم عدم انطباق‌اند (هم‌پایه با شمارنده‌ی «ردیف‌های قرنطینه» داشبورد)؛ قدیمی‌ترین ورود اول.",
            Link = "/inventory/inspections", RecordType = "StockLine", RecordLabel = "ردیف قرنطینه",
            Columns = new[]
            {
                Col.Text("warehouse", "انبار"), Col.Text("code", "کد کالا"), Col.Text("title", "کالا"), Col.Text("unit", "واحد"), Col.Text("lot", "بچ"), Col.Text("heat", "شماره‌ی ذوب"),
                Col.Text("location", "محل"), Col.Num("quantity", "مقدار"), Col.Date("since", "آخرین ورود"), Col.Num("daysWaiting", "روز انتظار"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                List<WarehouseDto> quarantine = Take(await c.Get<IInventoryMasterService>().GetWarehousesAsync(ct)).Where(w => w.Kind == WarehouseKind.Quarantine).ToList();
                var stock = c.Get<IInventoryStockService>();
                var lines = new List<StockBalanceDto>();
                foreach (WarehouseDto w in quarantine)
                    lines.AddRange(Take(await stock.GetBalancesAsync(new StockQuery { WarehouseId = w.Id, PageSize = 0 }, ct)).Where(b => b.Quantity > 0));

                // the day the stock came in is not on the balance: the last receipt of the same item and lot into the warehouse
                List<int> warehouseIds = quarantine.Select(w => w.Id).ToList();
                var received = warehouseIds.Count == 0
                    ? new List<(int WarehouseId, int ItemId, int? LotId, DateTime Last)>()
                    : (await c.Get<IUnitOfWork>().Set<StockTransaction>().AsNoTracking()
                            .Where(t => warehouseIds.Contains(t.WarehouseId) && t.Quantity > 0 && !t.IsReversal && t.Document.Status == InventoryDocumentStatus.Confirmed)
                            .GroupBy(t => new { t.WarehouseId, t.ItemId, t.LotId }).Select(g => new { g.Key.WarehouseId, g.Key.ItemId, g.Key.LotId, Last = g.Max(t => t.TransactionDate) }).ToListAsync(ct))
                        .Select(x => (x.WarehouseId, x.ItemId, x.LotId, x.Last)).ToList();
                DateTime? Since(StockBalanceDto b) => received.Where(r => r.WarehouseId == b.WarehouseId && r.ItemId == b.ItemId && r.LotId == b.LotId).Select(r => (DateTime?)r.Last).FirstOrDefault();

                var ordered = lines.Select(b => new { Line = b, Since = Since(b) })
                    .OrderBy(x => x.Since.HasValue ? 0 : 1).ThenBy(x => x.Since).ThenBy(x => x.Line.ItemCode, StringComparer.Ordinal).ToList();
                List<Row> rows = ordered.Select(x => new Row(KardexLink(x.Line.ItemId))
                {
                    ["id"] = Id(x.Line.ItemId) + "-" + Id(x.Line.WarehouseId) + "-" + (x.Line.LotId.HasValue ? Id(x.Line.LotId.Value) : "0"), ["warehouse"] = x.Line.WarehouseTitle, ["code"] = x.Line.ItemCode,
                    ["title"] = x.Line.ItemTitle, ["unit"] = x.Line.Unit, ["lot"] = x.Line.LotNumber, ["heat"] = x.Line.HeatNumber, ["location"] = x.Line.LocationCode, ["quantity"] = x.Line.Quantity,
                    ["since"] = DrillDates.Text(x.Since), ["daysWaiting"] = x.Since.HasValue ? Math.Max(0, DaysBetween(x.Since.Value, c.Today)) : (int?)null,
                }).ToList();
                return DrillResult.Slice(rows, c, "هر ردیف یک کالا و بچ در یک محل است؛ روز انتظار از آخرین رسید همان کالا و بچ به انبار قرنطینه شمرده می‌شود.", new Dictionary<string, object>
                {
                    ["items"] = ordered.Select(x => x.Line.ItemId).Distinct().Count(),
                });
            },
        };

        // ------------------------------------------------------------------------------------------ material requests

        private static DrillDefinition MaterialRequestsOpen() => new DrillDefinition
        {
            Key = "inv.material_requests_open", Module = M, Title = "درخواست‌های مواد باز",
            Description = "درخواست‌های مواد انبار که ارسال یا تأیید شده‌اند و هنوز بسته نشده‌اند (منتظر تأیید برنامه‌ریزی و ساخت، یا منتظر صدور حواله)؛ قدیمی‌ترین اول.",
            Link = "/inventory/material-requests", RecordType = "MaterialRequest", RecordLabel = "درخواست مواد",
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Date("requestDate", "تاریخ"), Col.Status("status", "وضعیت"), Col.Text("workOrderCode", "دستور کار"), Col.Text("warehouse", "انبار"), Col.Text("purpose", "هدف"),
                Col.Num("lines", "تعداد ردیف"), Col.Text("planningApproved", "تأیید برنامه‌ریزی"), Col.Text("manufacturingApproved", "تأیید ساخت"), Col.Num("waitingDays", "روز انتظار"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                var workflow = c.Get<IInventoryWorkflowService>();
                var open = new List<MaterialRequestListItemDto>();
                foreach (MaterialRequestStatus status in new[] { MaterialRequestStatus.Submitted, MaterialRequestStatus.Approved })
                    open.AddRange(Take(await workflow.GetRequestsAsync(new MaterialRequestQuery { Status = status, PageSize = 0 }, ct)));
                List<MaterialRequestListItemDto> ordered = open.OrderBy(r => r.RequestDate).ThenBy(r => r.Number, StringComparer.Ordinal).ToList();
                List<Row> rows = ordered.Select(r => new Row("/inventory/material-requests/" + Id(r.Id))
                {
                    ["id"] = r.Id, ["number"] = r.Number, ["requestDate"] = DrillDates.Text(r.RequestDate), ["status"] = RequestState(r.Status), ["workOrderCode"] = r.WorkOrderCode,
                    ["warehouse"] = r.WarehouseTitle, ["purpose"] = Short(r.Purpose), ["lines"] = r.LinesCount, ["planningApproved"] = YesNo(r.PlanningApproved),
                    ["manufacturingApproved"] = YesNo(r.ManufacturingApproved), ["waitingDays"] = Math.Max(0, DaysBetween(r.RequestDate, c.Today)),
                }).ToList();
                return DrillResult.Slice(rows, c, null, new Dictionary<string, object>
                {
                    ["submitted"] = ordered.Count(r => r.Status == MaterialRequestStatus.Submitted), ["approved"] = ordered.Count(r => r.Status == MaterialRequestStatus.Approved),
                });
            },
        };
    }
}
