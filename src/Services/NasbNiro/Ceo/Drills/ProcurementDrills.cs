using System;
using System.Collections.Generic;
using System.Linq;
using Entities.Model.NasbNiro.Procurement;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.ProcurementDtos;
using static Services.NasbNiro.Ceo.Drills.DrillText;

namespace Services.NasbNiro.Ceo.Drills
{
    /// <summary>The procurement drills: purchase requests, orders, late lines, supplier delivery performance, spend, invoices, payments and blocked suppliers.</summary>
    internal static class ProcurementDrills
    {
        private const string M = CeoModules.Procurement;

        /// <summary>An order is open from its approval until everything is received (the same set as the order counter of the CEO dashboard).</summary>
        private static readonly PurchaseOrderStatus[] OpenOrderStatuses = { PurchaseOrderStatus.Approved, PurchaseOrderStatus.Released, PurchaseOrderStatus.PartlyReceived };

        /// <summary>Orders that were sent to the supplier and still have something to receive.</summary>
        private static readonly PurchaseOrderStatus[] ReleasedOrderStatuses = { PurchaseOrderStatus.Released, PurchaseOrderStatus.PartlyReceived };

        public static IEnumerable<DrillDefinition> Build()
        {
            yield return RequestsOpen();
            yield return OrdersOpen();
            yield return LateLines();
            yield return SupplierOnTime();
            yield return SpendBySupplier();
            yield return InvoicesWaiting();
            yield return PaymentsWaiting();
            yield return BlockedSuppliers();
        }

        // ------------------------------------------------------------------------------------------ labels

        private static string KindText(PurchaseKind kind) => kind switch
        {
            PurchaseKind.Goods => "کالا",
            PurchaseKind.Service => "خدمات",
            _ => "پیمانکاری",
        };

        private static string SourceText(PurchaseRequestSource source) => source switch
        {
            PurchaseRequestSource.Bom => "از BOM",
            PurchaseRequestSource.ReorderPoint => "نقطه‌ی سفارش",
            PurchaseRequestSource.MaterialShortage => "کمبود مواد",
            _ => "موردی",
        };

        private static string RequestState(PurchaseRequestStatus status) => status switch
        {
            PurchaseRequestStatus.Draft => "پیش‌نویس",
            PurchaseRequestStatus.Submitted => "ارسال‌شده، منتظر تایید",
            PurchaseRequestStatus.Approved => "تاییدشده، منتظر سفارش",
            PurchaseRequestStatus.Ordered => "سفارش‌داده‌شده",
            PurchaseRequestStatus.Closed => "بسته‌شده",
            _ => "ردشده",
        };

        private static string OrderState(PurchaseOrderStatus status) => status switch
        {
            PurchaseOrderStatus.Draft => "پیش‌نویس",
            PurchaseOrderStatus.Approved => "تاییدشده، ابلاغ‌نشده",
            PurchaseOrderStatus.Released => "ابلاغ‌شده",
            PurchaseOrderStatus.PartlyReceived => "تحویل بخشی",
            PurchaseOrderStatus.Received => "تحویل کامل",
            PurchaseOrderStatus.Closed => "بسته‌شده",
            _ => "لغوشده",
        };

        private static string InvoiceState(SupplierInvoiceStatus status) => status switch
        {
            SupplierInvoiceStatus.Draft => "پیش‌نویس",
            SupplierInvoiceStatus.Matched => "مطابق، منتظر تایید",
            SupplierInvoiceStatus.NeedsApproval => "مغایرت دارد، منتظر تایید مدیریت",
            SupplierInvoiceStatus.Approved => "تاییدشده",
            _ => "لغوشده",
        };

        private static string PaymentTypeText(PaymentRequestType type) => type switch
        {
            PaymentRequestType.Advance => "پیش‌پرداخت",
            PaymentRequestType.Progress => "صورت‌وضعیت",
            PaymentRequestType.Invoice => "فاکتور",
            _ => "تسویه‌ی نهایی",
        };

        private static string PaymentState(PaymentRequestStatus status) => status switch
        {
            PaymentRequestStatus.Draft => "پیش‌نویس، منتظر تایید",
            PaymentRequestStatus.Approved => "تاییدشده، منتظر پرداخت",
            PaymentRequestStatus.Paid => "پرداخت‌شده",
            _ => "ردشده",
        };

        private static string GradeText(SupplierGrade grade) => grade switch
        {
            SupplierGrade.A => "A",
            SupplierGrade.B => "B",
            SupplierGrade.C => "C",
            SupplierGrade.D => "D",
            _ => "ارزیابی‌نشده",
        };

        private static string YesNo(bool value) => value ? "بله" : "خیر";

        /// <summary>The day a line is due: its own delivery date, else the one of the order.</summary>
        private static DateTime? DueOf(PurchaseOrderLineDto line, PurchaseOrderDto order) => line.DeliveryDate ?? order.DeliveryDate;

        private static async System.Threading.Tasks.Task<List<PurchaseOrderDto>> OrdersAsync(IProcurementService procurement, IEnumerable<PurchaseOrderStatus> statuses, System.Threading.CancellationToken ct)
        {
            var orders = new List<PurchaseOrderDto>();
            foreach (PurchaseOrderStatus status in statuses)
                orders.AddRange(Take(await procurement.GetOrdersAsync(new PurchaseOrderQuery { Status = status, PageSize = 0 }, ct)));
            return orders;
        }

        // ------------------------------------------------------------------------------------------ requests

        private static DrillDefinition RequestsOpen() => new DrillDefinition
        {
            Key = "pr.requests_open", Module = M, Title = "درخواست‌های خرید باز",
            Description = "درخواست‌های خرید ارسال‌شده (منتظر تایید برنامه‌ریزی یا مدیریت) و تاییدشده (منتظر سفارش)؛ هم‌پایه با شمارنده‌ی «درخواست‌های خرید باز» داشبورد؛ قدیمی‌ترین اول.",
            Link = "/procurement/requests", RecordType = "PurchaseRequest", RecordLabel = "درخواست خرید",
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Date("requestDate", "تاریخ"), Col.Status("status", "وضعیت"), Col.Text("kind", "نوع"), Col.Text("source", "منبع"), Col.Text("projectCode", "پروژه / دستور کار"),
                Col.Text("description", "شرح"), Col.Num("lines", "تعداد ردیف"), Col.Rial("estimatedAmount", "برآورد"), Col.Date("neededBy", "نیاز تا"), Col.Text("approval", "نیازمند تایید"),
                Col.Num("waitingDays", "روز انتظار"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProcurementRoles.Readers);
                var procurement = c.Get<IProcurementService>();
                var open = new List<PurchaseRequestDto>();
                foreach (PurchaseRequestStatus status in new[] { PurchaseRequestStatus.Submitted, PurchaseRequestStatus.Approved })
                    open.AddRange(Take(await procurement.GetRequestsAsync(new PurchaseRequestQuery { Status = status, PageSize = 0 }, ct)));
                int Waiting(PurchaseRequestDto r) => Math.Max(0, DaysBetween(r.SubmittedAt.HasValue ? r.SubmittedAt.Value.LocalDateTime : r.RequestDate, c.Today));
                List<PurchaseRequestDto> ordered = open.OrderByDescending(Waiting).ThenBy(r => r.Number, StringComparer.Ordinal).ToList();
                List<Row> rows = ordered.Select(r => new Row("/procurement/requests/" + Id(r.Id))
                {
                    ["id"] = r.Id, ["number"] = r.Number, ["requestDate"] = DrillDates.Text(r.RequestDate), ["status"] = RequestState(r.Status), ["kind"] = KindText(r.Kind), ["source"] = SourceText(r.Source),
                    ["projectCode"] = r.ContractCode ?? r.WorkOrderCode, ["description"] = Short(r.Description), ["lines"] = r.Lines.Count, ["estimatedAmount"] = r.EstimatedAmount,
                    ["neededBy"] = DrillDates.Text(r.Lines.Where(l => l.NeededBy.HasValue).Select(l => l.NeededBy).Min()),
                    ["approval"] = r.NeedsBoard ? "هیئت‌مدیره" : r.NeedsManagement ? "مدیریت" : "برنامه‌ریزی", ["waitingDays"] = Waiting(r),
                }).ToList();
                return DrillResult.Slice(rows, c, "روز انتظار از ارسال درخواست (و برای درخواست‌های ارسال‌نشده از تاریخ درخواست) شمرده می‌شود.", new Dictionary<string, object>
                {
                    ["waitingApproval"] = ordered.Count(r => r.Status == PurchaseRequestStatus.Submitted), ["approved"] = ordered.Count(r => r.Status == PurchaseRequestStatus.Approved),
                    ["estimatedAmount"] = ordered.Sum(r => r.EstimatedAmount),
                });
            },
        };

        // ------------------------------------------------------------------------------------------ orders

        private static DrillDefinition OrdersOpen() => new DrillDefinition
        {
            Key = "pr.orders_open", Module = M, Title = "سفارش‌های خرید باز",
            Description = "سفارش‌های خرید تاییدشده، ابلاغ‌شده و در حال تحویل (هم‌پایه با شمارنده و مبلغ «سفارش‌های خرید باز» داشبورد)؛ بیشترین مبلغ اول، با درصد تحویل، مبلغ فاکتورنشده و ردیف‌های دارای تاخیر.",
            Link = "/procurement/orders", RecordType = "PurchaseOrder", RecordLabel = "سفارش خرید",
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Date("orderDate", "تاریخ"), Col.Text("providerName", "تامین‌کننده"), Col.Text("grade", "رتبه"), Col.Status("status", "وضعیت"), Col.Text("projectCode", "پروژه / دستور کار"),
                Col.Rial("amount", "مبلغ"), Col.Pct("receivedPercent", "درصد تحویل"), Col.Rial("uninvoiced", "تحویل‌شده‌ی فاکتورنشده"), Col.Rial("paid", "پرداخت‌شده"), Col.Date("deliveryDate", "تحویل"),
                Col.Num("lateLines", "ردیف دارای تاخیر"), Col.Num("daysLate", "بیشترین روز تاخیر"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProcurementRoles.Readers);
                List<PurchaseOrderDto> open = await OrdersAsync(c.Get<IProcurementService>(), OpenOrderStatuses, ct);
                int DaysLate(PurchaseOrderDto o) => o.Lines.Where(l => l.Late && DueOf(l, o).HasValue).Select(l => DaysBetween(DueOf(l, o).Value, c.Today)).DefaultIfEmpty(0).Max();
                List<PurchaseOrderDto> ordered = open.OrderByDescending(o => o.TotalAmount).ThenBy(o => o.Number, StringComparer.Ordinal).ToList();
                List<Row> rows = ordered.Select(o => new Row("/procurement/orders/" + Id(o.Id))
                {
                    ["id"] = o.Id, ["number"] = o.Number, ["orderDate"] = DrillDates.Text(o.OrderDate), ["providerName"] = o.ProviderName, ["grade"] = GradeText(o.Grade), ["status"] = OrderState(o.Status),
                    ["projectCode"] = o.ContractCode ?? o.WorkOrderCode, ["amount"] = o.TotalAmount, ["receivedPercent"] = Percent(o.ReceivedAmount, o.TotalAmount),
                    ["uninvoiced"] = Math.Max(0m, o.ReceivedAmount - o.InvoicedAmount), ["paid"] = o.PaidAmount, ["deliveryDate"] = DrillDates.Text(o.DeliveryDate),
                    ["lateLines"] = o.Lines.Count(l => l.Late), ["daysLate"] = DaysLate(o),
                }).ToList();
                return DrillResult.Slice(rows, c, "درصد تحویل بر پایه‌ی مقدار پذیرفته‌شده‌ی کنترل کیفیت و قیمت سفارش است؛ مبلغ فاکتورنشده برای همین سفارش‌های باز تخمین بر پایه‌ی قیمت سفارش است (نه حساب دفتر کل).", new Dictionary<string, object>
                {
                    ["amount"] = ordered.Sum(o => o.TotalAmount), ["lateLines"] = ordered.Sum(o => o.Lines.Count(l => l.Late)),
                    ["uninvoiced"] = ordered.Sum(o => Math.Max(0m, o.ReceivedAmount - o.InvoicedAmount)),
                });
            },
        };

        private static DrillDefinition LateLines() => new DrillDefinition
        {
            Key = "pr.late_lines", Module = M, Title = "ردیف‌های سفارش خرید دارای تاخیر",
            Description = "ردیف‌های سفارش‌های ابلاغ‌شده و در حال تحویل که تاریخ تحویل (ردیف یا سفارش) گذشته و هنوز کامل تحویل نشده‌اند (هم‌پایه با «ردیف‌های سفارش دارای تاخیر» داشبورد)؛ بیشترین روز تاخیر اول.",
            Link = "/procurement/orders", RecordType = "PurchaseOrder", RecordLabel = "سفارش خرید",
            Columns = new[]
            {
                Col.Text("number", "سفارش"), Col.Text("providerName", "تامین‌کننده"), Col.Num("row", "ردیف"), Col.Text("item", "کالا یا خدمت"), Col.Text("unit", "واحد"), Col.Num("quantity", "سفارش"),
                Col.Num("received", "تحویل‌شده"), Col.Num("openQuantity", "باقی‌مانده"), Col.Date("dueDate", "تاریخ تحویل"), Col.Num("daysLate", "روز تاخیر"), Col.Rial("openAmount", "مبلغ باقی‌مانده"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProcurementRoles.Readers);
                List<PurchaseOrderDto> released = await OrdersAsync(c.Get<IProcurementService>(), ReleasedOrderStatuses, ct);
                var late = released.SelectMany(o => o.Lines.Where(l => l.Late && DueOf(l, o).HasValue).Select(l => new { Order = o, Line = l, Due = DueOf(l, o).Value }))
                    .OrderByDescending(x => DaysBetween(x.Due, c.Today)).ThenBy(x => x.Order.Number, StringComparer.Ordinal).ThenBy(x => x.Line.RowNumber).ToList();
                List<Row> rows = late.Select(x => new Row("/procurement/orders/" + Id(x.Order.Id))
                {
                    ["id"] = x.Line.Id, ["number"] = x.Order.Number, ["providerName"] = x.Order.ProviderName, ["row"] = x.Line.RowNumber, ["item"] = Short(x.Line.ItemTitle ?? x.Line.Description, 80),
                    ["unit"] = x.Line.Unit, ["quantity"] = x.Line.Quantity, ["received"] = x.Line.ReceivedQuantity, ["openQuantity"] = x.Line.OpenQuantity, ["dueDate"] = DrillDates.Text(x.Due),
                    ["daysLate"] = DaysBetween(x.Due, c.Today), ["openAmount"] = Math.Round(x.Line.OpenQuantity * x.Line.UnitPrice, 0, MidpointRounding.AwayFromZero),
                }).ToList();
                return DrillResult.Slice(rows, c, "هر ردیف جدا شمرده می‌شود؛ تاریخ تحویل ردیف، و اگر نداشت تاریخ تحویل سفارش، ملاک است.", new Dictionary<string, object>
                {
                    ["orders"] = late.Select(x => x.Order.Id).Distinct().Count(), ["openAmount"] = late.Sum(x => Math.Round(x.Line.OpenQuantity * x.Line.UnitPrice, 0, MidpointRounding.AwayFromZero)),
                });
            },
        };

        // ------------------------------------------------------------------------------------------ suppliers

        private static DrillDefinition SupplierOnTime() => new DrillDefinition
        {
            Key = "pr.supplier_on_time", Module = M, Title = "تحویل به‌موقع تامین‌کنندگان",
            Description = "درصد ردیف‌های تحویل‌شده‌ی هر تامین‌کننده که تا تاریخ تحویل رسیده‌اند (همه‌ی دوران)، با تعداد سفارش، عدم انطباق و مرجوعی؛ کم‌ترین درصد به‌موقع اول. فقط تامین‌کنندگانی که دست‌کم یک ردیف تحویل داده‌اند.",
            Link = "/procurement/suppliers", RecordType = "Supplier", RecordLabel = "تامین‌کننده",
            Columns = new[]
            {
                Col.Text("providerName", "تامین‌کننده"), Col.Text("grade", "رتبه"), Col.Num("score", "امتیاز"), Col.Num("orders", "تعداد سفارش"), Col.Num("receiptLines", "ردیف تحویل‌شده"),
                Col.Num("lateReceiptLines", "ردیف دیرکرد"), Col.Pct("onTimePercent", "درصد به‌موقع"), Col.Num("ncrs", "عدم انطباق"), Col.Num("returns", "مرجوعی"), Col.Text("blocked", "مسدود"),
                Col.Text("evaluationDue", "ارزیابی سررسید"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProcurementRoles.Readers);
                List<SupplierProfileDto> all = Take(await c.Get<IPayablesService>().GetSuppliersAsync(null, ct));
                List<SupplierProfileDto> delivered = all.Where(s => s.ReceiptLines > 0).OrderBy(s => s.OnTimePercent ?? 100m).ThenByDescending(s => s.LateReceiptLines).ThenBy(s => s.ProviderName, StringComparer.Ordinal).ToList();
                List<Row> rows = delivered.Select(s => new Row("/procurement/suppliers")
                {
                    ["id"] = s.ProviderId, ["providerName"] = s.ProviderName, ["grade"] = GradeText(s.Grade), ["score"] = s.Score, ["orders"] = s.Orders, ["receiptLines"] = s.ReceiptLines,
                    ["lateReceiptLines"] = s.LateReceiptLines, ["onTimePercent"] = s.OnTimePercent, ["ncrs"] = s.Ncrs, ["returns"] = s.Returns, ["blocked"] = YesNo(s.Blocked), ["evaluationDue"] = YesNo(s.EvaluationDue),
                }).ToList();
                int lines = delivered.Sum(s => s.ReceiptLines), lateLines = delivered.Sum(s => s.LateReceiptLines);
                return DrillResult.Slice(rows, c, "دیرکرد یعنی تاریخ آخرین تحویل ردیف از تاریخ تحویل مقرر گذشته است؛ ارزیابی سررسید شده فقط برای تامین‌کنندگان مسدودنشده در شمارنده‌ی داشبورد می‌آید.", new Dictionary<string, object>
                {
                    ["suppliers"] = delivered.Count, ["receiptLines"] = lines, ["lateReceiptLines"] = lateLines, ["onTimePercent"] = lines == 0 ? (decimal?)null : Percent(lines - lateLines, lines),
                    ["evaluationsDue"] = all.Count(s => !s.Blocked && s.EvaluationDue),
                });
            },
        };

        private static DrillDefinition SpendBySupplier() => new DrillDefinition
        {
            Key = "pr.spend_by_supplier", Module = M, Title = "خرید به تفکیک تامین‌کننده",
            Description = "جمع فاکتورهای تاییدشده‌ی هر تامین‌کننده (با مالیات ارزش افزوده) با تاریخ فاکتور در بازه، و مبلغ خالص پرداخت‌های انجام‌شده در همان بازه؛ بیشترین خرید اول. پیش‌فرض ۱۲ ماه شمسی اخیر.",
            Link = "/procurement/invoices", RecordType = "Supplier", RecordLabel = "تامین‌کننده",
            Parameters = Prm.Last12Months(),
            Columns = new[]
            {
                Col.Text("providerName", "تامین‌کننده"), Col.Num("invoices", "تعداد فاکتور"), Col.Rial("invoiced", "جمع فاکتورهای تاییدشده"), Col.Pct("sharePercent", "سهم از کل"), Col.Rial("paid", "پرداخت‌شده در بازه"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProcurementRoles.Readers);
                DateTime from = c.Args.Date("from").Date, to = c.Args.Date("to").Date;
                var payables = c.Get<IPayablesService>();
                var invoices = Take(await payables.GetInvoicesAsync(new ProcurementListQuery { Status = (int)SupplierInvoiceStatus.Approved, PageSize = 0 }, ct))
                    .Where(i => i.InvoiceDate.Date >= from && i.InvoiceDate.Date <= to).ToList();
                var paid = Take(await payables.GetPaymentsAsync(new ProcurementListQuery { Status = (int)PaymentRequestStatus.Paid, PageSize = 0 }, ct))
                    .Where(p => p.PaidAt.HasValue && p.PaidAt.Value.LocalDateTime.Date >= from && p.PaidAt.Value.LocalDateTime.Date <= to).ToList();

                var bySupplier = new Dictionary<int, (string Name, int Invoices, decimal Invoiced, decimal Paid)>();
                foreach (SupplierInvoiceDto i in invoices)
                {
                    bySupplier.TryGetValue(i.ProviderId, out var v);
                    bySupplier[i.ProviderId] = (i.ProviderName, v.Invoices + 1, v.Invoiced + i.TotalAmount, v.Paid);
                }
                foreach (PaymentRequestDto p in paid)
                {
                    bySupplier.TryGetValue(p.ProviderId, out var v);
                    bySupplier[p.ProviderId] = (v.Name ?? p.ProviderName, v.Invoices, v.Invoiced, v.Paid + p.NetAmount);
                }
                decimal total = bySupplier.Values.Sum(v => v.Invoiced);
                List<Row> rows = bySupplier.Select(kv => new { Id = kv.Key, kv.Value.Name, kv.Value.Invoices, kv.Value.Invoiced, kv.Value.Paid })
                    .OrderByDescending(x => x.Invoiced).ThenByDescending(x => x.Paid).ThenBy(x => x.Name, StringComparer.Ordinal)
                    .Select(x => new Row("/procurement/suppliers")
                    {
                        ["id"] = x.Id, ["providerName"] = x.Name, ["invoices"] = x.Invoices, ["invoiced"] = x.Invoiced, ["sharePercent"] = Percent(x.Invoiced, total), ["paid"] = x.Paid,
                    }).ToList();
                return DrillResult.Slice(rows, c, "فاکتور بر پایه‌ی تاریخ فاکتور تامین‌کننده و پرداخت بر پایه‌ی تاریخ پرداخت شمرده می‌شود؛ پرداخت پیش‌پرداخت و صورت‌وضعیت هم در «پرداخت‌شده» می‌آید.", new Dictionary<string, object>
                {
                    ["invoiced"] = total, ["paid"] = bySupplier.Values.Sum(v => v.Paid), ["suppliers"] = rows.Count,
                });
            },
        };

        private static DrillDefinition BlockedSuppliers() => new DrillDefinition
        {
            Key = "pr.blocked_suppliers", Module = M, Title = "تامین‌کنندگان مسدود",
            Description = "تامین‌کنندگانی که از فهرست تامین‌کنندگان تاییدشده مسدود شده‌اند (هم‌پایه با شمارنده‌ی داشبورد)، با علت مسدودی، رتبه، هشدارها و میزان سفارش‌های گذشته؛ بیشترین مبلغ سفارش اول.",
            Link = "/procurement/suppliers", RecordType = "Supplier", RecordLabel = "تامین‌کننده",
            Columns = new[]
            {
                Col.Text("providerName", "تامین‌کننده"), Col.Text("grade", "رتبه"), Col.Num("score", "امتیاز"), Col.Num("warnings", "هشدار"), Col.Text("blockReason", "علت مسدودی"), Col.Num("orders", "تعداد سفارش"),
                Col.Rial("orderedAmount", "جمع سفارش‌ها"), Col.Num("ncrs", "عدم انطباق"), Col.Num("returns", "مرجوعی"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProcurementRoles.Readers);
                List<SupplierProfileDto> blocked = Take(await c.Get<IPayablesService>().GetSuppliersAsync(null, ct)).Where(s => s.Blocked)
                    .OrderByDescending(s => s.OrderedAmount).ThenBy(s => s.ProviderName, StringComparer.Ordinal).ToList();
                List<Row> rows = blocked.Select(s => new Row("/procurement/suppliers")
                {
                    ["id"] = s.ProviderId, ["providerName"] = s.ProviderName, ["grade"] = GradeText(s.Grade), ["score"] = s.Score, ["warnings"] = s.Warnings, ["blockReason"] = Short(s.BlockReason),
                    ["orders"] = s.Orders, ["orderedAmount"] = s.OrderedAmount, ["ncrs"] = s.Ncrs, ["returns"] = s.Returns,
                }).ToList();
                return DrillResult.Slice(rows, c, "رفع مسدودی تامین‌کننده‌ی رتبه‌ی D یا هشدارداده‌شده با مدیرعامل است.", new Dictionary<string, object> { ["orderedAmount"] = blocked.Sum(s => s.OrderedAmount) });
            },
        };

        // ------------------------------------------------------------------------------------------ invoices and payments

        private static DrillDefinition InvoicesWaiting() => new DrillDefinition
        {
            Key = "pr.invoices_waiting", Module = M, Title = "فاکتورهای تامین‌کننده منتظر تایید",
            Description = "فاکتورهای تامین‌کننده که با سفارش و رسید تطبیق داده شده‌اند (مطابق یا دارای مغایرت) و منتظر تایید مالی یا مدیریت‌اند (هم‌پایه با «فاکتورهای منتظر تایید» داشبورد)؛ قدیمی‌ترین اول.",
            Link = "/procurement/invoices", RecordType = "SupplierInvoice", RecordLabel = "فاکتور تامین‌کننده",
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("providerName", "تامین‌کننده"), Col.Text("invoiceNumber", "شماره‌ی فاکتور تامین‌کننده"), Col.Date("invoiceDate", "تاریخ فاکتور"), Col.Text("orderNumber", "سفارش"),
                Col.Status("status", "وضعیت"), Col.Rial("netAmount", "مبلغ"), Col.Rial("vatAmount", "مالیات"), Col.Rial("totalAmount", "جمع"), Col.Rial("priceVariance", "مغایرت قیمت"), Col.Text("matchNote", "یادداشت تطبیق"),
                Col.Num("waitingDays", "روز انتظار"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProcurementRoles.Readers);
                var payables = c.Get<IPayablesService>();
                var waiting = new List<SupplierInvoiceDto>();
                foreach (SupplierInvoiceStatus status in new[] { SupplierInvoiceStatus.Matched, SupplierInvoiceStatus.NeedsApproval })
                    waiting.AddRange(Take(await payables.GetInvoicesAsync(new ProcurementListQuery { Status = (int)status, PageSize = 0 }, ct)));
                int Waiting(SupplierInvoiceDto i) => Math.Max(0, DaysBetween(i.CreateDate.LocalDateTime, c.Today));
                List<SupplierInvoiceDto> ordered = waiting.OrderByDescending(Waiting).ThenBy(i => i.Number, StringComparer.Ordinal).ToList();
                List<Row> rows = ordered.Select(i => new Row("/procurement/invoices")
                {
                    ["id"] = i.Id, ["number"] = i.Number, ["providerName"] = i.ProviderName, ["invoiceNumber"] = i.InvoiceNumber, ["invoiceDate"] = DrillDates.Text(i.InvoiceDate), ["orderNumber"] = i.PurchaseOrderNumber,
                    ["status"] = InvoiceState(i.Status), ["netAmount"] = i.NetAmount, ["vatAmount"] = i.VatAmount, ["totalAmount"] = i.TotalAmount, ["priceVariance"] = i.PriceVariance,
                    ["matchNote"] = Short(i.MatchNote), ["waitingDays"] = Waiting(i),
                }).ToList();
                return DrillResult.Slice(rows, c, "روز انتظار از ثبت فاکتور در سامانه شمرده می‌شود.", new Dictionary<string, object>
                {
                    ["needsApproval"] = ordered.Count(i => i.Status == SupplierInvoiceStatus.NeedsApproval), ["totalAmount"] = ordered.Sum(i => i.TotalAmount),
                });
            },
        };

        private static DrillDefinition PaymentsWaiting() => new DrillDefinition
        {
            Key = "pr.payments_waiting", Module = M, Title = "درخواست‌های پرداخت منتظر",
            Description = "درخواست‌های پرداخت به تامین‌کننده که پیش‌نویس‌اند (منتظر تایید) یا تایید شده‌اند و پرداخت نشده‌اند (هم‌پایه با تعداد و مبلغ «پرداخت‌های منتظر» داشبورد)؛ قدیمی‌ترین اول. مبلغ خالص = مبلغ منهای کسورات.",
            Link = "/procurement/payments", RecordType = "PaymentRequest", RecordLabel = "درخواست پرداخت",
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("type", "نوع"), Col.Text("providerName", "تامین‌کننده"), Col.Text("orderNumber", "سفارش"), Col.Text("invoiceNumber", "فاکتور"), Col.Date("requestDate", "تاریخ درخواست"),
                Col.Status("status", "وضعیت"), Col.Rial("amount", "مبلغ"), Col.Rial("deductions", "کسورات"), Col.Rial("netAmount", "مبلغ خالص"), Col.Text("qcApproved", "تایید کنترل کیفیت"),
                Col.Num("waitingDays", "روز انتظار"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProcurementRoles.Readers);
                var payables = c.Get<IPayablesService>();
                var waiting = new List<PaymentRequestDto>();
                foreach (PaymentRequestStatus status in new[] { PaymentRequestStatus.Draft, PaymentRequestStatus.Approved })
                    waiting.AddRange(Take(await payables.GetPaymentsAsync(new ProcurementListQuery { Status = (int)status, PageSize = 0 }, ct)));
                int Waiting(PaymentRequestDto p) => Math.Max(0, DaysBetween(p.RequestDate, c.Today));
                List<PaymentRequestDto> ordered = waiting.OrderByDescending(Waiting).ThenBy(p => p.Number, StringComparer.Ordinal).ToList();
                List<Row> rows = ordered.Select(p => new Row("/procurement/payments")
                {
                    ["id"] = p.Id, ["number"] = p.Number, ["type"] = PaymentTypeText(p.Type), ["providerName"] = p.ProviderName, ["orderNumber"] = p.PurchaseOrderNumber, ["invoiceNumber"] = p.InvoiceNumber,
                    ["requestDate"] = DrillDates.Text(p.RequestDate), ["status"] = PaymentState(p.Status), ["amount"] = p.Amount, ["deductions"] = p.Deductions, ["netAmount"] = p.NetAmount,
                    ["qcApproved"] = YesNo(p.QcApproved), ["waitingDays"] = Waiting(p),
                }).ToList();
                return DrillResult.Slice(rows, c, null, new Dictionary<string, object>
                {
                    ["netAmount"] = ordered.Sum(p => p.NetAmount), ["draft"] = ordered.Count(p => p.Status == PaymentRequestStatus.Draft), ["approved"] = ordered.Count(p => p.Status == PaymentRequestStatus.Approved),
                });
            },
        };
    }
}
