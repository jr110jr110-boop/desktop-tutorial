using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Sales;
using ViewModels.NasbNiro.SalesDtos;
using static Services.NasbNiro.Ceo.Drills.DrillText;

namespace Services.NasbNiro.Ceo.Drills
{
    /// <summary>The sales drills: the pipeline, inquiries, proposals, quotations, orders, customers and the win / loss statistics.</summary>
    internal static class SalesDrills
    {
        private const string M = CeoModules.Sales;

        /// <summary>The stages of an open inquiry (the same as the sales dashboard).</summary>
        private static readonly SlInquiryStatus[] OpenStages = { SlInquiryStatus.Received, SlInquiryStatus.UnderReview, SlInquiryStatus.Bidding, SlInquiryStatus.Submitted };

        /// <summary>The stages in which the proposal is being prepared.</summary>
        private static readonly SlInquiryStatus[] PreparingStages = { SlInquiryStatus.Received, SlInquiryStatus.UnderReview, SlInquiryStatus.Bidding };

        public static IEnumerable<DrillDefinition> Build()
        {
            yield return PipelineByStage();
            yield return InquiriesOpen();
            yield return ProposalsWaiting();
            yield return ProposalsDue();
            yield return QuotationsExpiring();
            yield return OrdersOpen();
            yield return OrdersWaitingCredit();
            yield return CustomersTop();
            yield return HitRateByMonth();
            yield return LostByReason();
        }

        private static int? DaysTo(DateTime? date, DateTime today) => date.HasValue ? (int)(date.Value.Date - today).TotalDays : (int?)null;

        // ------------------------------------------------------------------------------------------ pipeline

        private static DrillDefinition PipelineByStage() => new DrillDefinition
        {
            Key = "sl.pipeline_by_stage", Module = M, Title = "خط لوله‌ی فروش به تفکیک مرحله",
            Description = "استعلام‌های باز (دریافت‌شده، در حال بررسی، در حال تهیه پیشنهاد، ارسال‌شده) با تعداد و ارزش هر مرحله؛ ارزش هر استعلام آخرین پیشنهاد فعال و در نبود آن آخرین برآورد قیمت‌دار است (هم‌پایه با داشبورد فروش). می‌شود به‌جای مرحله، به تفکیک نوع محصول هم خواست.",
            Link = "/sales/inquiries",
            Parameters = new[] { Prm.Str("by", "گروه‌بندی: stage (مرحله) یا product (نوع محصول)؛ پیش‌فرض stage", "stage", new[] { "stage", "product" }) },
            Columns = new[] { Col.Text("name", "گروه"), Col.Num("count", "تعداد استعلام"), Col.Rial("value", "ارزش"), Col.Pct("sharePercent", "سهم از ارزش") },
            Run = async (c, ct) =>
            {
                Guard.Require(c, SalesRoles.Readers);
                SlDashboardDto dash = Take(await c.Get<ISlDashboardService>().GetAsync(c.Today, ct));
                bool byProduct = c.Args.Str("by") == "product";
                List<Row> rows = (byProduct ? dash.PipelineByProduct : dash.PipelineByStage)
                    .Select(g => new Row { ["name"] = g.Name, ["count"] = g.Count, ["value"] = g.Value, ["sharePercent"] = Percent(g.Value, dash.PipelineValue) }).ToList();
                return DrillResult.Slice(rows, c, "ارزش خط لوله جمع آخرین پیشنهاد فعال (یا برآورد) استعلام‌های باز است.",
                    new Dictionary<string, object> { ["openInquiries"] = dash.OpenInquiries, ["pipelineValue"] = dash.PipelineValue });
            },
        };

        /// <summary>The value of an open inquiry: the latest live proposal, else the latest estimate with a price, else zero (the rule of the sales dashboard).</summary>
        private static async Task<Dictionary<int, decimal>> InquiryValuesAsync(IUnitOfWork uow, List<int> inquiryIds, CancellationToken ct)
        {
            var result = new Dictionary<int, decimal>();
            if (inquiryIds.Count == 0) return result;
            var proposals = await uow.Set<SlProposal>().AsNoTracking()
                .Where(p => !p.Deleted && inquiryIds.Contains(p.InquiryId) && (p.Status == SlProposalStatus.Draft || p.Status == SlProposalStatus.Approved || p.Status == SlProposalStatus.Submitted))
                .Select(p => new { p.Id, p.InquiryId, p.Price }).ToListAsync(ct);
            var estimates = await uow.Set<SlEstimate>().AsNoTracking()
                .Where(e => !e.Deleted && inquiryIds.Contains(e.InquiryId) && e.Status != SlEstimateStatus.Superseded && e.Price != null)
                .Select(e => new { e.Id, e.InquiryId, e.Price }).ToListAsync(ct);
            foreach (int id in inquiryIds)
            {
                var proposal = proposals.Where(x => x.InquiryId == id).OrderByDescending(x => x.Id).FirstOrDefault();
                if (proposal != null) { result[id] = proposal.Price; continue; }
                var estimate = estimates.Where(x => x.InquiryId == id).OrderByDescending(x => x.Id).FirstOrDefault();
                result[id] = estimate?.Price ?? 0m;
            }
            return result;
        }

        private static async Task<List<SlInquiryDto>> ReadInquiriesAsync(DrillContext c, IEnumerable<SlInquiryStatus> stages, CancellationToken ct)
        {
            var pipeline = c.Get<ISlPipelineService>();
            var all = new List<SlInquiryDto>();
            foreach (SlInquiryStatus stage in stages)
                all.AddRange(await DrillRead.AllAsync(n => pipeline.GetInquiriesAsync(new SlInquiryQuery { Status = stage, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct)));
            return all;
        }

        private static DrillDefinition InquiriesOpen() => new DrillDefinition
        {
            Key = "sl.inquiries_open", Module = M, Title = "استعلام‌های باز",
            Description = "استعلام‌های دریافت‌شده، در حال بررسی، در حال تهیه پیشنهاد و ارسال‌شده با ارزش هر استعلام؛ بیشترین ارزش اول.",
            Link = "/sales/inquiries", RecordType = "Inquiry", RecordLabel = "استعلام",
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("customerName", "مشتری"), Col.Text("title", "عنوان"), Col.Text("product", "محصول"), Col.Status("status", "وضعیت"),
                Col.Date("receivedDate", "تاریخ دریافت"), Col.Date("proposalDueDate", "مهلت پیشنهاد"), Col.Num("daysToDue", "روز تا مهلت (منفی: گذشته)"), Col.Rial("value", "ارزش"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, SalesRoles.Readers);
                List<SlInquiryDto> open = await ReadInquiriesAsync(c, OpenStages, ct);
                Dictionary<int, decimal> values = await InquiryValuesAsync(c.Get<IUnitOfWork>(), open.Select(i => i.Id).ToList(), ct);
                List<Row> rows = open.OrderByDescending(i => values[i.Id]).ThenBy(i => i.Number, StringComparer.Ordinal).Select(i => new Row("/sales/inquiries/" + Id(i.Id))
                {
                    ["id"] = i.Id, ["number"] = i.Number, ["customerName"] = i.CustomerName, ["title"] = Short(i.Title), ["product"] = i.ProductTypeText, ["status"] = i.StatusText,
                    ["receivedDate"] = i.ReceivedDateText, ["proposalDueDate"] = i.ProposalDueDateText, ["daysToDue"] = DaysTo(i.ProposalDueDate, c.Today), ["value"] = values[i.Id],
                }).ToList();
                return DrillResult.Slice(rows, c, "ارزش: آخرین پیشنهاد فعال، در نبود آن آخرین برآورد قیمت‌دار، وگرنه صفر.", new Dictionary<string, object> { ["value"] = values.Values.Sum() });
            },
        };

        // ------------------------------------------------------------------------------------------ proposals

        private static DrillDefinition ProposalsWaiting() => new DrillDefinition
        {
            Key = "sl.proposals_waiting", Module = M, Title = "پیشنهادهای منتظر تأیید",
            Description = "پیشنهادهای پیش‌نویس که منتظر تأیید هستند (زیر قیمت تمام‌شده و نیازمند تأیید مدیرعامل اول، سپس بیشترین مبلغ).",
            Link = "/sales/proposals", RecordType = "Proposal", RecordLabel = "پیشنهاد",
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Num("revision", "بازنگری"), Col.Text("inquiryNumber", "استعلام"), Col.Text("customerName", "مشتری"), Col.Rial("price", "مبلغ (بدون مالیات)"),
                Col.Rial("estimateTotalCost", "قیمت تمام‌شده‌ی برآورد"), Col.Status("flags", "هشدار"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, SalesRoles.CommercialReaders);
                var proposals = c.Get<ISlProposalService>();
                List<SlProposalDto> draft = await DrillRead.AllAsync(n => proposals.GetProposalsAsync(new SlProposalQuery { Status = SlProposalStatus.Draft, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct));
                List<Row> rows = draft.OrderByDescending(p => p.BelowCost).ThenByDescending(p => p.NeedsCeo).ThenByDescending(p => p.Price).ThenBy(p => p.Number, StringComparer.Ordinal)
                    .Select(p => new Row("/sales/proposals/" + Id(p.Id))
                    {
                        ["id"] = p.Id, ["number"] = p.Number, ["revision"] = p.Revision, ["inquiryNumber"] = p.InquiryNumber, ["customerName"] = p.CustomerName, ["price"] = p.Price,
                        ["estimateTotalCost"] = p.EstimateTotalCost, ["flags"] = p.BelowCost ? "زیر قیمت تمام‌شده" : p.NeedsCeo ? "نیازمند تأیید مدیرعامل" : "عادی",
                    }).ToList();
                return DrillResult.Slice(rows, c, "پیشنهاد زیر قیمت تمام‌شده یا بالاتر از آستانه را فقط مدیرعامل تأیید می‌کند.",
                    new Dictionary<string, object> { ["price"] = draft.Sum(p => p.Price), ["belowCost"] = draft.Count(p => p.BelowCost), ["needsCeo"] = draft.Count(p => p.NeedsCeo) });
            },
        };

        private static DrillDefinition ProposalsDue() => new DrillDefinition
        {
            Key = "sl.proposals_due", Module = M, Title = "مهلت ارسال پیشنهاد",
            Description = "استعلام‌هایی که پیشنهادشان هنوز ارسال نشده و مهلت ارسال تا چند روز آینده می‌رسد یا گذشته است (هم‌پایه با «پیشنهاد در آستانه‌ی مهلت» داشبورد مدیرعامل)؛ نزدیک‌ترین مهلت اول.",
            Link = "/sales/inquiries", RecordType = "Inquiry", RecordLabel = "استعلام",
            Parameters = new[] { Prm.Days(3, 90, "مهلت ارسال پیشنهاد تا این تعداد روز آینده (مهلت‌های گذشته هم می‌آید)") },
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("customerName", "مشتری"), Col.Text("title", "عنوان"), Col.Status("status", "وضعیت"), Col.Date("proposalDueDate", "مهلت پیشنهاد"),
                Col.Num("daysToDue", "روز تا مهلت (منفی: گذشته)"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, SalesRoles.Readers);
                DateTime limit = c.Today.AddDays(c.Args.Int("days"));
                List<SlInquiryDto> due = (await ReadInquiriesAsync(c, PreparingStages, ct)).Where(i => i.ProposalDueDate.HasValue && i.ProposalDueDate.Value.Date <= limit)
                    .OrderBy(i => i.ProposalDueDate.Value.Date).ThenBy(i => i.Number, StringComparer.Ordinal).ToList();
                List<Row> rows = due.Select(i => new Row("/sales/inquiries/" + Id(i.Id))
                {
                    ["id"] = i.Id, ["number"] = i.Number, ["customerName"] = i.CustomerName, ["title"] = Short(i.Title), ["status"] = i.StatusText, ["proposalDueDate"] = i.ProposalDueDateText,
                    ["daysToDue"] = DaysTo(i.ProposalDueDate, c.Today),
                }).ToList();
                return DrillResult.Slice(rows, c, "فقط استعلام‌های دریافت‌شده، در حال بررسی و در حال تهیه پیشنهاد شمرده می‌شود.",
                    new Dictionary<string, object> { ["overdue"] = due.Count(i => i.ProposalDueDate.Value.Date < c.Today) });
            },
        };

        // ------------------------------------------------------------------------------------------ quotations and orders

        private static DrillDefinition QuotationsExpiring() => new DrillDefinition
        {
            Key = "sl.quotations_expiring", Module = M, Title = "پیش‌فاکتورهای در آستانه‌ی انقضا",
            Description = "پیش‌فاکتورهای ارسال‌شده‌ای که اعتبارشان تا چند روز آینده تمام می‌شود (هم‌پایه با داشبورد فروش)؛ نزدیک‌ترین انقضا اول.",
            Link = "/sales/quotations", RecordType = "Quotation", RecordLabel = "پیش‌فاکتور",
            Parameters = new[] { Prm.Days(7, 90, "پیش‌فاکتورهایی که اعتبارشان تا این تعداد روز آینده تمام می‌شود") },
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("customerName", "مشتری"), Col.Date("quotationDate", "تاریخ"), Col.Date("validUntil", "اعتبار تا"), Col.Num("daysLeft", "روز مانده"),
                Col.Rial("netAmount", "مبلغ"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, SalesRoles.Readers);
                var orders = c.Get<ISlOrderService>();
                DateTime limit = c.Today.AddDays(c.Args.Int("days"));
                List<SlQuotationDto> sent = await DrillRead.AllAsync(n => orders.GetQuotationsAsync(new SlQuotationQuery { Status = SlQuotationStatus.Sent, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct));
                List<SlQuotationDto> expiring = sent.Where(q => q.ValidUntil.Date >= c.Today && q.ValidUntil.Date <= limit).OrderBy(q => q.ValidUntil.Date).ThenBy(q => q.Number, StringComparer.Ordinal).ToList();
                List<Row> rows = expiring.Select(q => new Row("/sales/quotations")
                {
                    ["id"] = q.Id, ["number"] = q.Number, ["customerName"] = q.CustomerName, ["quotationDate"] = q.QuotationDateText, ["validUntil"] = q.ValidUntilText,
                    ["daysLeft"] = (int)(q.ValidUntil.Date - c.Today).TotalDays, ["netAmount"] = q.NetAmount,
                }).ToList();
                return DrillResult.Slice(rows, c, "فقط پیش‌فاکتورهای «ارسال‌شده»ای که اعتبارشان هنوز تمام نشده است.", new Dictionary<string, object> { ["netAmount"] = expiring.Sum(q => q.NetAmount) });
            },
        };

        private static decimal DeliveredPercent(SlOrderDto order)
        {
            decimal total = order.Lines.Sum(l => l.Amount);
            if (total <= 0m) return 0m;
            decimal delivered = order.Lines.Sum(l => l.Quantity > 0m ? l.Amount * Math.Min(l.DeliveredQuantity, l.Quantity) / l.Quantity : 0m);
            return Percent(delivered, total);
        }

        /// <summary>What is left to invoice on an order: the sum of the uninvoiced amounts of its lines (<see cref="CeoFigures.UninvoicedAmount"/>, the function of the sales backlog of the overview).</summary>
        private static decimal Remaining(SlOrderDto order) => order.Lines.Sum(l => CeoFigures.UninvoicedAmount(l.Amount, l.Quantity, l.InvoicedQuantity));

        private static DrillDefinition OrdersOpen() => new DrillDefinition
        {
            Key = "sl.orders_open", Module = M, Title = "سفارش‌های فروش باز",
            Description = "سفارش‌های فروش تأییدشده و در حال تحویل بخشی با درصد تحویل (بر پایه‌ی مبلغ ردیف‌ها) و مانده‌ی فاکتورنشده؛ نزدیک‌ترین تاریخ تحویل درخواستی اول. جمع مانده‌ی فاکتورنشده بخش سفارش‌ها در شاخص «سفارش‌های در جریان» داشبورد مدیرعامل است.",
            Link = "/sales/orders", RecordType = "SalesOrder", RecordLabel = "سفارش فروش",
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("customerName", "مشتری"), Col.Date("orderDate", "تاریخ سفارش"), Col.Date("requestedDeliveryDate", "تحویل درخواستی"),
                Col.Num("daysToDelivery", "روز تا تحویل (منفی: گذشته)"), Col.Status("status", "وضعیت"), Col.Rial("netAmount", "مبلغ"), Col.Rial("remainingAmount", "مانده‌ی فاکتورنشده"),
                Col.Pct("deliveredPercent", "درصد تحویل"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, SalesRoles.OrderReaders);
                var orders = c.Get<ISlOrderService>();
                var open = new List<SlOrderDto>();
                foreach (SlOrderStatus status in new[] { SlOrderStatus.Confirmed, SlOrderStatus.PartlyDelivered })
                    open.AddRange(await DrillRead.AllAsync(n => orders.GetOrdersAsync(new SlOrderQuery { Status = status, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct)));
                List<Row> rows = open.OrderBy(o => o.RequestedDeliveryDate.HasValue ? 0 : 1).ThenBy(o => o.RequestedDeliveryDate).ThenBy(o => o.Number, StringComparer.Ordinal).Select(o => new Row("/sales/orders/" + Id(o.Id))
                {
                    ["id"] = o.Id, ["number"] = o.Number, ["customerName"] = o.CustomerName, ["orderDate"] = o.OrderDateText, ["requestedDeliveryDate"] = o.RequestedDeliveryDateText,
                    ["daysToDelivery"] = DaysTo(o.RequestedDeliveryDate, c.Today), ["status"] = o.StatusText, ["netAmount"] = o.NetAmount, ["remainingAmount"] = CeoFigures.RoundRial(Remaining(o)),
                    ["deliveredPercent"] = DeliveredPercent(o),
                }).ToList();
                return DrillResult.Slice(rows, c, "باز یعنی «تأییدشده» یا «تحویل بخشی» (هم‌پایه با داشبورد فروش). مانده‌ی فاکتورنشده: مبلغ هر ردیف ضرب در سهم مقدار فاکتورنشده‌ی آن؛ جمع آن یک بار گرد می‌شود و بخش سفارش‌های رقم «سفارش‌های در جریان» داشبورد است (بخش قراردادهای آن رقم در این فهرست نیست).",
                    new Dictionary<string, object> { ["netAmount"] = open.Sum(o => o.NetAmount), ["remainingAmount"] = CeoFigures.RoundRial(open.Sum(Remaining)) });
            },
        };

        private static DrillDefinition OrdersWaitingCredit() => new DrillDefinition
        {
            Key = "sl.orders_waiting_credit", Module = M, Title = "سفارش‌های منتظر تأیید اعتبار",
            Description = "سفارش‌های پیش‌نویسی که به‌خاطر سقف اعتبار مشتری منتظر تأیید مدیر مالی هستند؛ بیشترین مبلغ مازاد بر سقف اول.",
            Link = "/sales/orders", RecordType = "SalesOrder", RecordLabel = "سفارش فروش",
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("customerName", "مشتری"), Col.Date("orderDate", "تاریخ سفارش"), Col.Rial("netAmount", "مبلغ"), Col.Rial("creditExcess", "مازاد بر سقف اعتبار"),
                Col.Text("creditReason", "دلیل"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, SalesRoles.OrderReaders);
                var orders = c.Get<ISlOrderService>();
                List<SlOrderDto> waiting = await DrillRead.AllAsync(n => orders.GetOrdersAsync(new SlOrderQuery { CreditPending = true, PageNumber = n, PageSize = DrillRead.ModulePageSize }, ct));
                List<Row> rows = waiting.OrderByDescending(o => o.CreditExcess ?? 0m).ThenByDescending(o => o.NetAmount).ThenBy(o => o.Number, StringComparer.Ordinal).Select(o => new Row("/sales/orders/" + Id(o.Id))
                {
                    ["id"] = o.Id, ["number"] = o.Number, ["customerName"] = o.CustomerName, ["orderDate"] = o.OrderDateText, ["netAmount"] = o.NetAmount, ["creditExcess"] = o.CreditExcess,
                    ["creditReason"] = Short(o.CreditReason, 160),
                }).ToList();
                return DrillResult.Slice(rows, c, "مازاد خالی یعنی مالی سقف اعتبار مشتری را هنوز تعیین نکرده است.", new Dictionary<string, object> { ["netAmount"] = waiting.Sum(o => o.NetAmount) });
            },
        };

        // ------------------------------------------------------------------------------------------ customers

        private static DrillDefinition CustomersTop() => new DrillDefinition
        {
            Key = "sl.customers_top", Module = M, Title = "مشتریان برتر",
            Description = "مشتریان به ترتیب فروش خالص (فاکتورهای قطعی‌شده منهای اصلاحیه‌ها، بدون مالیات) در بازه، همراه با مانده‌ی مطالبات باز. بازه‌ی پیش‌فرض: ۱۲ ماه شمسی اخیر.",
            Link = "/sales/customers", RecordType = "Customer", RecordLabel = "مشتری",
            Parameters = Prm.Last12Months(" فاکتور"),
            Columns = new[]
            {
                Col.Text("customerName", "مشتری"), Col.Num("invoices", "تعداد فاکتور"), Col.Rial("sales", "فروش خالص"), Col.Pct("sharePercent", "سهم از فروش"), Col.Rial("openAmount", "مطالبات باز"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, FinanceRoles.Readers);
                DateTime from = c.Args.Date("from").Date, to = c.Args.Date("to").Date;
                var uow = c.Get<IUnitOfWork>();
                var sums = await uow.Set<FnSalesInvoice>().AsNoTracking()
                    .Where(i => i.Status == FnDocStatus.Posted && i.Date >= from && i.Date <= to)
                    .GroupBy(i => i.CustomerId)
                    .Select(g => new
                    {
                        CustomerId = g.Key,
                        Invoices = g.Count(i => i.Type != FnInvoiceType.CreditNote),
                        Net = g.Sum(i => i.Type == FnInvoiceType.CreditNote ? -i.NetAmount : i.NetAmount),
                    }).ToListAsync(ct);
                sums = sums.Where(s => s.Net != 0m).OrderByDescending(s => s.Net).ToList();
                List<int> ids = sums.Select(s => s.CustomerId).ToList();
                Dictionary<int, string> names = ids.Count == 0 ? new Dictionary<int, string>() : await uow.Set<FnCustomer>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
                Dictionary<int, decimal> open = ids.Count == 0 ? new Dictionary<int, decimal>() : await SlReceivables.OpenByCustomerAsync(uow, ids, ct);
                decimal total = sums.Sum(s => s.Net);
                List<Row> rows = sums.Select(s => new Row("/sales/customers/" + Id(s.CustomerId))
                {
                    ["id"] = s.CustomerId, ["customerName"] = names.TryGetValue(s.CustomerId, out string n) ? n : null, ["invoices"] = s.Invoices, ["sales"] = s.Net,
                    ["sharePercent"] = Percent(s.Net, total), ["openAmount"] = open.TryGetValue(s.CustomerId, out decimal o) ? o : 0m,
                }).ToList();
                return DrillResult.Slice(rows, c, "فروش خالص = مبلغ خالص فاکتورهای قطعی‌شده منهای اصلاحیه‌ها (بدون مالیات).", new Dictionary<string, object> { ["sales"] = total, ["customers"] = rows.Count });
            },
        };

        // ------------------------------------------------------------------------------------------ win / loss

        private static DrillDefinition HitRateByMonth() => new DrillDefinition
        {
            Key = "sl.hit_rate_by_month", Module = M, Title = "نرخ برد پیشنهادها به تفکیک ماه",
            Description = "تعداد و ارزش پیشنهادهای برنده و بازنده و نرخ برد هر ماه شمسی (بر پایه‌ی تاریخ نتیجه). بازه‌ی پیش‌فرض: ۱۲ ماه شمسی اخیر؛ نرخ برد داشبورد روی کل سابقه است.",
            Link = "/sales/proposals",
            Parameters = Prm.Last12Months(" نتیجه"),
            Columns = new[] { Col.Text("month", "ماه"), Col.Num("won", "برنده"), Col.Num("lost", "بازنده"), Col.Pct("hitRatePercent", "نرخ برد"), Col.Rial("wonValue", "ارزش برنده"), Col.Rial("lostValue", "ارزش بازنده") },
            Run = async (c, ct) =>
            {
                Guard.Require(c, SalesRoles.CommercialReaders);
                DateTime from = c.Args.Date("from").Date, to = c.Args.Date("to").Date;
                var proposals = c.Get<ISlProposalService>();
                var rows = new List<Row>();
                int won = 0, lost = 0;
                decimal wonValue = 0m, lostValue = 0m;
                foreach (DateTime month in DrillDates.Months(from, to))
                {
                    DateTime start = month < from ? from : month;
                    DateTime end = DrillDates.MonthEnd(month);
                    if (end > to) end = to;
                    SlWinLossDto result = Take(await proposals.GetWinLossAsync(new SlWinLossQuery { From = start, To = end }, ct));
                    decimal monthWon = result.ByProduct.Sum(x => x.WonValue), monthLost = result.ByProduct.Sum(x => x.LostValue);
                    rows.Add(new Row { ["month"] = CeoText.MonthLabel(month), ["won"] = result.Won, ["lost"] = result.Lost, ["hitRatePercent"] = result.HitRatePercent, ["wonValue"] = monthWon, ["lostValue"] = monthLost });
                    won += result.Won; lost += result.Lost; wonValue += monthWon; lostValue += monthLost;
                }
                return DrillResult.Slice(rows, c, "نرخ برد = برنده / (برنده + بازنده)؛ ماه بدون نتیجه نرخ ندارد.", new Dictionary<string, object>
                {
                    ["won"] = won, ["lost"] = lost, ["hitRatePercent"] = SlProposalService.Percent(won, lost), ["wonValue"] = wonValue, ["lostValue"] = lostValue,
                });
            },
        };

        private static DrillDefinition LostByReason() => new DrillDefinition
        {
            Key = "sl.lost_by_reason", Module = M, Title = "پیشنهادهای بازنده به تفکیک دلیل",
            Description = "تعداد و ارزش پیشنهادهای بازنده به تفکیک دلیل ثبت‌شده‌ی نتیجه؛ بیشترین تعداد اول. بازه‌ی پیش‌فرض: ۱۲ ماه شمسی اخیر (بر پایه‌ی تاریخ نتیجه).",
            Link = "/sales/proposals",
            Parameters = Prm.Last12Months(" نتیجه"),
            Columns = new[] { Col.Text("reason", "دلیل"), Col.Num("lost", "بازنده"), Col.Rial("lostValue", "ارزش بازنده"), Col.Pct("sharePercent", "سهم از بازنده‌ها") },
            Run = async (c, ct) =>
            {
                Guard.Require(c, SalesRoles.CommercialReaders);
                SlWinLossDto result = Take(await c.Get<ISlProposalService>().GetWinLossAsync(new SlWinLossQuery { From = c.Args.Date("from").Date, To = c.Args.Date("to").Date }, ct));
                decimal totalLostValue = result.ByProduct.Sum(x => x.LostValue);
                List<SlWinLossRowDto> reasons = result.ByReason.Where(r => r.Lost > 0).OrderByDescending(r => r.Lost).ThenByDescending(r => r.LostValue).ThenBy(r => r.Name, StringComparer.Ordinal).ToList();
                var rows = reasons.Select(r => new Row { ["reason"] = r.Name ?? "نامشخص", ["lost"] = r.Lost, ["lostValue"] = r.LostValue, ["sharePercent"] = Percent(r.Lost, result.Lost) }).ToList();
                int withoutReason = result.Lost - reasons.Sum(r => r.Lost);
                if (withoutReason > 0)
                    rows.Add(new Row { ["reason"] = "بدون دلیل ثبت‌شده", ["lost"] = withoutReason, ["lostValue"] = totalLostValue - reasons.Sum(r => r.LostValue), ["sharePercent"] = Percent(withoutReason, result.Lost) });
                return DrillResult.Slice(rows, c, "دلیل را کاربر هنگام ثبت نتیجه‌ی پیشنهاد انتخاب می‌کند.",
                    new Dictionary<string, object> { ["lost"] = result.Lost, ["lostValue"] = totalLostValue, ["won"] = result.Won, ["hitRatePercent"] = result.HitRatePercent });
            },
        };
    }
}
