using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ViewModels.NasbNiro.AiDtos;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProcurementDtos;
using ViewModels.NasbNiro.ProductionDtos;
using ViewModels.NasbNiro.ProjectControlDtos;
using ViewModels.NasbNiro.QualityDtos;
using Fin = Services.NasbNiro.Finance;
using Inv = Services.NasbNiro.Inventory;
using Proc = Services.NasbNiro.Procurement;
using Pc = Services.NasbNiro.ProjectControl;
using Qa = Services.NasbNiro.Quality;
using Sl = Services.NasbNiro.Sales;
using Sv = Services.NasbNiro.AfterSales;
using Mf = Services.NasbNiro.Production;
using static Services.NasbNiro.Ai.AiToolShaper;

namespace Services.NasbNiro.Ai
{
    /// <summary>
    /// The A1 tools (all read only). The required roles are the ones the front controllers and the services already demand for the same data;
    /// the delegates call the existing services in the request scope, so their own checks and row filters still apply.
    /// </summary>
    public static class AiTools
    {
        private const string ObjectSchema = "{\"type\":\"object\",\"properties\":{}}";

        private static string Schema(params (string Name, string Type, string Description)[] properties)
        {
            var props = new JsonObject();
            foreach (var p in properties) props[p.Name] = new JsonObject { ["type"] = p.Type, ["description"] = p.Description };
            return new JsonObject { ["type"] = "object", ["properties"] = props }.ToJsonString();
        }

        // ------------------------------------------------------------------------------------------ argument helpers

        public static string Str(JsonElement args, string name)
        {
            if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out JsonElement v)) return null;
            string s = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : null;
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }

        public static int? Int(JsonElement args, string name)
        {
            string s = Str(args, name);
            return s != null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : (int?)null;
        }

        private static async Task<int?> ContractIdAsync(AiToolContext ctx, JsonElement args, CancellationToken ct)
        {
            int? id = Int(args, "contractId");
            if (id.HasValue) return id;
            string code = Str(args, "projectCode") ?? Str(args, "code");
            if (code == null) return null;
            IUnitOfWork uow = ctx.Get<IUnitOfWork>();
            int found = await uow.Set<ContractEntity>().AsNoTracking().Where(c => !c.Deleted && (c.Code == code || c.Code.Contains(code) || c.Title.Contains(code))).OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct);
            return found == 0 ? (int?)null : found;
        }

        private static async Task<int?> WorkOrderIdAsync(AiToolContext ctx, JsonElement args, CancellationToken ct)
        {
            int? id = Int(args, "workOrderId");
            if (id.HasValue) return id;
            string code = Str(args, "workOrderCode") ?? Str(args, "code");
            if (code == null) return null;
            IUnitOfWork uow = ctx.Get<IUnitOfWork>();
            int found = await uow.Set<WorkOrder>().AsNoTracking().Where(w => !w.Deleted && (w.Code == code || w.Code.Contains(code))).OrderBy(w => w.Id).Select(w => w.Id).FirstOrDefaultAsync(ct);
            return found == 0 ? (int?)null : found;
        }

        private static object Paged<T>(PagedList<T> page) => new { totalCount = page.TotalCount, items = page.ToList() };

        private static AiItemLinkRule Items(string type, string prefix, string template, string property = "items", string idKey = "id") =>
            new AiItemLinkRule { Property = property, Type = type, TitlePrefix = prefix, Template = template, IdKey = idKey };

        public static List<AiToolDefinition> Build()
        {
            const string finance = Fin.FinanceRoles.Readers;
            string procurement = Proc.ProcurementRoles.Readers + ",ASSEMBLER,ENGINEER";
            string projects = Pc.ProjectControlRoles.Readers;
            string quality = Qa.QualityRoles.Readers;
            var tools = new List<AiToolDefinition>();

            // ---------------------------------------------------------------------------------------- management and finance
            tools.Add(new AiToolDefinition
            {
                Name = "ceo_dashboard", Title = "داشبورد مدیرعامل", RequiredRoles = "CEO,PMO", PageLink = "/finance/ceo-dashboard",
                Description = "داشبورد مدیرعامل: ارزش موجودی، خرید و بدهی، کیفیت، پروژه‌ها، فروش، خدمات پس از فروش، تولید، مالی و فهرست هشدارها (هر هشدار پیوند منبع دارد).",
                Run = async (c, a, ct) => AiToolShaper.Unwrap(await c.Get<Fin.ICeoDashboardService>().GetAsync(ct)),
            });
            tools.Add(new AiToolDefinition
            {
                Name = "finance_dashboard", Title = "داشبورد مالی", RequiredRoles = finance, PageLink = "/fn/dashboard",
                Description = "داشبورد مالی: صندوق و بانک، چک‌های نزدیک سررسید، اسناد در انتظار، مطالبات و بدهی‌ها، سود خالص از ابتدای سال.",
                Run = async (c, a, ct) => AiToolShaper.Unwrap(await c.Get<Fin.IFinanceDashboardService>().GetAsync(ct)),
            });
            tools.Add(new AiToolDefinition
            {
                Name = "trial_balance_summary", Title = "خلاصه تراز آزمایشی", RequiredRoles = finance, PageLink = "/fn/reports/trial-balance",
                Description = "تراز آزمایشی سطح کل سال مالی جاری: جمع بدهکار و بستانکار، تراز بودن و حساب‌های اصلی.",
                SchemaJson = Schema(("fiscalYearId", "integer", "شناسه سال مالی؛ اگر نیامد سال مالی جاری")),
                Run = async (c, a, ct) =>
                {
                    var q = new LedgerReportQuery { FiscalYearId = Int(a, "fiscalYearId"), Level = 1, Columns = 4, PageSize = 0 };
                    TrialBalanceDto d = AiToolShaper.Unwrap(await c.Get<Fin.ILedgerReportService>().TrialBalanceAsync(q, ct));
                    return new { d.FiscalYearId, d.From, d.To, d.IncludesDraft, d.OpeningDebit, d.OpeningCredit, d.TotalDebit, d.TotalCredit, d.ClosingDebit, d.ClosingCredit, d.Balanced, rowCount = d.Rows.Count, rows = d.Rows };
                },
            });
            tools.Add(new AiToolDefinition
            {
                Name = "cash_position", Title = "وضعیت نقدینگی", RequiredRoles = finance, PageLink = "/fn/treasury/cash-position",
                Description = "وضعیت نقدینگی: موجودی صندوق، بانک‌ها و تنخواه، چک‌های دریافتی و صادرشده، درخواست‌های پرداخت تأییدشده و پیش‌بینی هفتگی.",
                Run = async (c, a, ct) => AiToolShaper.Unwrap(await c.Get<Fin.ITreasuryService>().GetCashPositionAsync(ct)),
            });
            tools.Add(new AiToolDefinition
            {
                Name = "receivables_aging", Title = "سنی‌سازی مطالبات", RequiredRoles = finance, PageLink = "/fn/receivables/ageing",
                Description = "سنی‌سازی مطالبات مشتریان (جاری، ۳۱ تا ۶۰، ۶۱ تا ۹۰ و بالای ۹۰ روز) به تفکیک مشتری.",
                Run = async (c, a, ct) => AiToolShaper.Unwrap(await c.Get<Fin.IReceivablesService>().GetAgeingAsync(null, ct)),
                ItemLinks = { Items("Customer", "مشتری", "/fn/receivables/customers/{customerId}/statement", "rows", "customerId") },
            });
            tools.Add(new AiToolDefinition
            {
                Name = "overdue_payables", Title = "بدهی‌ها و پرداخت‌های منتظر", RequiredRoles = finance, PageLink = "/fn/treasury/payments",
                Description = "بدهی‌های باز به تأمین‌کنندگان، بدهی سررسید ۳۰ روز آینده و درخواست‌های پرداخت منتظر پرداخت خزانه.",
                Run = async (c, a, ct) =>
                {
                    FinanceDashboardDto dash = AiToolShaper.Unwrap(await c.Get<Fin.IFinanceDashboardService>().GetAsync(ct));
                    PagedList<PayableRequestDto> waiting = AiToolShaper.Unwrap(await c.Get<Fin.ITreasuryService>().GetPayableRequestsAsync(new TreasuryQuery { PageNumber = 1, PageSize = 10 }, ct));
                    return new { dash.PayablesOpen, dash.PayablesDue30, awaitingPayment = new { totalCount = waiting.TotalCount, items = waiting.ToList() } };
                },
            });

            // ---------------------------------------------------------------------------------------- inventory
            tools.Add(new AiToolDefinition
            {
                Name = "item_stock", Title = "موجودی کالا", CitationType = "Item", PageLink = "/inventory/stock",
                Description = "موجودی یک کالا بر اساس کد یا شناسه (یا بخشی از نام): موجودی کل، حداقل موجودی و موجودی هر انبار.",
                SchemaJson = Schema(("code", "string", "کد یا بخشی از نام کالا"), ("id", "integer", "شناسه کالا")),
                ItemLinks = { Items("Item", "کالا", "/inventory/stock?itemId={itemId}", "items", "itemId") },
                Run = async (c, a, ct) =>
                {
                    var svc = c.Get<Inv.IInventoryStockService>();
                    int? id = Int(a, "id");
                    PagedList<ItemStockDto> items = AiToolShaper.Unwrap(await svc.GetItemStocksAsync(new StockQuery { ItemId = id, Contains = Str(a, "code"), PageNumber = 1, PageSize = 10 }, ct));
                    object balances = null;
                    if (items.Count == 1)
                    {
                        PagedList<StockBalanceDto> b = AiToolShaper.Unwrap(await svc.GetBalancesAsync(new StockQuery { ItemId = items[0].ItemId, PageNumber = 1, PageSize = 10 }, ct));
                        balances = new { totalCount = b.TotalCount, items = b.ToList() };
                    }
                    return new { totalCount = items.TotalCount, items = items.ToList(), warehouseBalances = balances };
                },
            });
            tools.Add(new AiToolDefinition
            {
                Name = "items_below_minimum", Title = "اقلام زیر حداقل موجودی", CitationType = "Item", PageLink = "/inventory/stock",
                Description = "فهرست اقلامی که موجودی‌شان از حداقل موجودی کمتر است.",
                ItemLinks = { Items("Item", "کالا", "/inventory/stock?itemId={itemId}", "items", "itemId") },
                Run = async (c, a, ct) => Paged(AiToolShaper.Unwrap(await c.Get<Inv.IInventoryStockService>().GetItemStocksAsync(new StockQuery { LowStockOnly = true, PageNumber = 1, PageSize = 10 }, ct))),
            });
            tools.Add(new AiToolDefinition
            {
                Name = "item_card_summary", Title = "خلاصه کاردکس کالا", CitationType = "Item", PageLink = "/inventory/kardex",
                Description = "خلاصه کاردکس یک کالا: مانده ابتدا، مانده انتها و ده گردش آخر.",
                SchemaJson = Schema(("code", "string", "کد یا بخشی از نام کالا"), ("id", "integer", "شناسه کالا")),
                Run = async (c, a, ct) =>
                {
                    var svc = c.Get<Inv.IInventoryStockService>();
                    int? id = Int(a, "id");
                    if (!id.HasValue)
                    {
                        PagedList<ItemStockDto> found = AiToolShaper.Unwrap(await svc.GetItemStocksAsync(new StockQuery { Contains = Str(a, "code"), PageNumber = 1, PageSize = 2 }, ct));
                        if (found.Count == 0) throw new AiToolFailureException("کالایی با این مشخصات پیدا نشد.");
                        if (found.Count > 1) throw new AiToolFailureException("چند کالا با این مشخصات هست؛ کد دقیق‌تری بدهید.");
                        id = found[0].ItemId;
                    }
                    KardexDto k = AiToolShaper.Unwrap(await svc.GetKardexAsync(new KardexQuery { ItemId = id.Value }, ct));
                    return new { k.ItemId, k.ItemCode, k.ItemTitle, k.Unit, k.OpeningBalance, k.ClosingBalance, rowCount = k.Rows.Count, lastRows = k.Rows.AsEnumerable().Reverse().Take(10).ToList() };
                },
            });

            // ---------------------------------------------------------------------------------------- procurement
            tools.Add(new AiToolDefinition
            {
                Name = "purchase_status", Title = "وضعیت خرید", RequiredRoles = procurement, CitationType = "PurchaseOrder", PageLink = "/procurement/orders",
                Description = "وضعیت درخواست‌ها و سفارش‌های خرید بر اساس شماره، نام تأمین‌کننده یا کالا، و کد پروژه (دستور کار).",
                SchemaJson = Schema(("text", "string", "شماره درخواست یا سفارش، نام تأمین‌کننده یا کالا"), ("projectCode", "string", "کد پروژه یا دستور کار")),
                ItemLinks =
                {
                    Items("PurchaseOrder", "سفارش خرید", "/procurement/orders/{id}", "orders"),
                    Items("PurchaseRequest", "درخواست خرید", "/procurement/requests/{id}", "requests"),
                },
                Run = async (c, a, ct) =>
                {
                    var svc = c.Get<Proc.IProcurementService>();
                    string text = Str(a, "text") ?? Str(a, "number") ?? Str(a, "supplier");
                    string project = Str(a, "projectCode");
                    int? workOrderId = project == null ? null : await WorkOrderIdAsync(c, a, ct);
                    PagedList<PurchaseRequestDto> requests = AiToolShaper.Unwrap(await svc.GetRequestsAsync(new PurchaseRequestQuery { Contains = text, WorkOrderId = workOrderId, PageNumber = 1, PageSize = 10 }, ct));
                    PagedList<PurchaseOrderDto> orders = AiToolShaper.Unwrap(await svc.GetOrdersAsync(new PurchaseOrderQuery { Contains = text, PageNumber = 1, PageSize = project == null ? 10 : 200 }, ct));
                    List<PurchaseOrderDto> orderList = orders.ToList();
                    int orderTotal = orders.TotalCount;
                    if (project != null)
                    {
                        orderList = orderList.Where(o => (o.ContractCode ?? string.Empty).Contains(project) || (o.WorkOrderCode ?? string.Empty).Contains(project)).ToList();
                        orderTotal = orderList.Count;
                    }
                    return new { requestsTotalCount = requests.TotalCount, requests = requests.ToList(), ordersTotalCount = orderTotal, orders = orderList.Take(10).ToList() };
                },
            });
            tools.Add(new AiToolDefinition
            {
                Name = "supplier_profile", Title = "پروفایل تأمین‌کننده", RequiredRoles = procurement, CitationType = "Supplier", PageLink = "/procurement/suppliers",
                Description = "پروفایل و درجه‌ی تأمین‌کنندگان: درجه، امتیاز، سفارش‌ها، درصد تحویل به‌موقع، NCR و مرجوعی‌ها؛ با نام یا بخشی از نام.",
                SchemaJson = Schema(("name", "string", "نام یا بخشی از نام تأمین‌کننده")),
                ItemLinks = { Items("Supplier", "تأمین‌کننده", "/procurement/suppliers", "items", "providerId") },
                Run = async (c, a, ct) =>
                {
                    List<SupplierProfileDto> list = AiToolShaper.Unwrap(await c.Get<Proc.IPayablesService>().GetSuppliersAsync(Str(a, "name"), ct));
                    return new { totalCount = list.Count, items = list.Take(10).ToList() };
                },
            });

            // ---------------------------------------------------------------------------------------- project control
            tools.Add(new AiToolDefinition
            {
                Name = "project_portfolio", Title = "سبد پروژه‌ها", RequiredRoles = projects, CitationType = "Project", PageLink = "/project-control/portfolio",
                Description = "سبد پروژه‌ها: پیشرفت فیزیکی و برنامه‌ای، شاخص‌های SPI و CPI، وضعیت قرمز/زرد، فعالیت‌های دارای تأخیر، تغییرات باز و تضمین‌های در آستانه انقضا.",
                ItemLinks = { Items("Project", "پروژه", "/project-control/project?contractId={contractId}", "items", "contractId"), Items("Project", "پروژه", "/project-control/project?contractId={contractId}", "worst", "contractId") },
                Run = async (c, a, ct) =>
                {
                    var svc = c.Get<Pc.IPcReportService>();
                    PcDashboardDto dash = await svc.GetDashboardAsync(ct);
                    PagedList<PcPortfolioRowDto> rows = AiToolShaper.Unwrap(await svc.GetPortfolioAsync(new PaginationParams { PageNumber = 1, PageSize = 10 }, ct));
                    return new { summary = new { dash.ActiveProjects, dash.ProjectsRed, dash.ProjectsAmber, dash.LateActivities, dash.OpenChangeRequests, dash.OverdueChangeRequests, dash.GuaranteeAlerts, dash.OverdueMilestones, dash.PenaltyExposure }, totalCount = rows.TotalCount, items = rows.ToList() };
                },
            });
            tools.Add(new AiToolDefinition
            {
                Name = "project_status", Title = "وضعیت پروژه", RequiredRoles = projects, CitationType = "Project", PageLink = "/project-control/evm",
                Description = "وضعیت یک پروژه: شاخص‌های EVM (SPI، CPI، PV، EV، AC)، فعالیت‌های دارای تأخیر و نقاط عطف بعدی؛ با شناسه یا کد پروژه.",
                SchemaJson = Schema(("contractId", "integer", "شناسه قرارداد/پروژه"), ("projectCode", "string", "کد پروژه")),
                ItemLinks = { Items("Activity", "فعالیت", "/project-control/activities", "lateActivities", "id"), Items("Milestone", "نقطه عطف", "/project-control/milestones", "nextMilestones", "id") },
                Run = async (c, a, ct) =>
                {
                    int? contractId = await ContractIdAsync(c, a, ct);
                    if (!contractId.HasValue) throw new AiToolFailureException("پروژه‌ای با این مشخصات پیدا نشد؛ شناسه یا کد پروژه را بدهید.");
                    PcEvmDto evm = AiToolShaper.Unwrap(await c.Get<Pc.IPcProgressService>().GetEvmAsync(contractId.Value, null, ct));
                    PagedList<PcActivityDto> late = AiToolShaper.Unwrap(await c.Get<Pc.IPcScheduleService>().GetActivitiesAsync(new PcActivityQuery { ContractId = contractId, OnlyLate = true, PageNumber = 1, PageSize = 10 }, ct));
                    PagedList<PcMilestoneDto> ms = AiToolShaper.Unwrap(await c.Get<Pc.IPcCommercialService>().GetMilestonesAsync(new PcContractQuery { ContractId = contractId, PageNumber = 1, PageSize = 200 }, ct));
                    List<PcMilestoneDto> next = ms.Where(m => m.Status != Entities.Model.NasbNiro.ProjectControl.PcMilestoneStatus.Paid).OrderBy(m => m.PlannedDate ?? DateTime.MaxValue).Take(5).ToList();
                    return new { contractId, evm, lateActivitiesTotalCount = late.TotalCount, lateActivities = late.ToList(), nextMilestones = next };
                },
            });
            tools.Add(new AiToolDefinition
            {
                Name = "change_requests", Title = "درخواست‌های تغییر", RequiredRoles = projects, CitationType = "ChangeRequest", PageLink = "/project-control/changes",
                Description = "درخواست‌های تغییر پروژه‌ها (شماره، وضعیت، اثر زمانی و هزینه‌ای، موعد تحلیل)؛ اختیاری: شناسه یا کد پروژه.",
                SchemaJson = Schema(("contractId", "integer", "شناسه قرارداد/پروژه"), ("projectCode", "string", "کد پروژه")),
                ItemLinks = { Items("ChangeRequest", "درخواست تغییر", "/project-control/changes?id={id}") },
                Run = async (c, a, ct) => Paged(AiToolShaper.Unwrap(await c.Get<Pc.IPcBaselineService>().GetChangesAsync(new PcChangeQuery { ContractId = await ContractIdAsync(c, a, ct), PageNumber = 1, PageSize = 10 }, ct))),
            });
            tools.Add(new AiToolDefinition
            {
                Name = "expiring_guarantees", Title = "تضمین‌های در آستانه انقضا", RequiredRoles = projects, CitationType = "Guarantee", PageLink = "/project-control/guarantees",
                Description = "ضمانت‌نامه‌هایی که تا ۳۰ روز دیگر منقضی می‌شوند یا منقضی شده‌اند.",
                SchemaJson = Schema(("contractId", "integer", "شناسه قرارداد/پروژه")),
                ItemLinks = { Items("Guarantee", "ضمانت‌نامه", "/project-control/guarantees?id={guaranteeId}", "items", "guaranteeId") },
                Run = async (c, a, ct) =>
                {
                    List<PcGuaranteeAlertDto> list = await c.Get<Pc.IPcCommercialService>().GuaranteeAlertsAsync(Int(a, "contractId"), ct);
                    return new { totalCount = list.Count, items = list.Take(10).ToList() };
                },
            });

            // ---------------------------------------------------------------------------------------- quality
            tools.Add(new AiToolDefinition
            {
                Name = "quality_dashboard", Title = "داشبورد کیفیت", RequiredRoles = quality, PageLink = "/quality/dashboard",
                Description = "داشبورد کیفیت: بازرسی‌های منتظر، NCR و CAPA باز و دیرکرد، کالیبراسیون سررسیدشده، صلاحیت‌های رو به انقضا و هشدارها.",
                Run = async (c, a, ct) => AiToolShaper.Unwrap(await c.Get<Qa.IQualityDashboardService>().GetDashboardAsync(ct)),
            });
            tools.Add(new AiToolDefinition
            {
                Name = "open_ncr_capa", Title = "NCR و CAPA باز", RequiredRoles = quality, CitationType = "Ncr", PageLink = "/quality/capa",
                Description = "گزارش‌های عدم انطباق (NCR) باز و اقدام‌های اصلاحی/پیشگیرانه (CAPA) باز و دارای دیرکرد.",
                ItemLinks = { Items("Ncr", "عدم انطباق", "/inventory/ncr/{id}", "ncrs"), Items("Capa", "اقدام اصلاحی", "/quality/capa/{id}", "capas") },
                Run = async (c, a, ct) =>
                {
                    PagedList<NcrListItemDto> ncrs = AiToolShaper.Unwrap(await c.Get<Inv.IInventoryQualityService>().GetNcrsAsync(new NcrQuery { Status = Entities.Model.NasbNiro.Inventory.NcrStatus.Open, PageNumber = 1, PageSize = 10 }, ct));
                    PagedList<CapaListDto> overdue = AiToolShaper.Unwrap(await c.Get<Qa.ICapaService>().GetOverdueAsync(new CapaQuery { PageNumber = 1, PageSize = 10 }, ct));
                    PagedList<CapaListDto> capas = AiToolShaper.Unwrap(await c.Get<Qa.ICapaService>().GetCapasAsync(new CapaQuery { PageNumber = 1, PageSize = 200 }, ct));
                    List<CapaListDto> open = capas.Where(x => x.Status != Entities.Model.NasbNiro.Quality.CapaStatus.Closed && x.Status != Entities.Model.NasbNiro.Quality.CapaStatus.Cancelled).ToList();
                    return new { openNcrTotalCount = ncrs.TotalCount, ncrs = ncrs.ToList(), openCapaTotalCount = open.Count, capas = open.Take(10).ToList(), overdueCapaTotalCount = overdue.TotalCount };
                },
            });
            tools.Add(new AiToolDefinition
            {
                Name = "calibration_due", Title = "کالیبراسیون سررسیدشده", RequiredRoles = quality, CitationType = "Equipment", PageLink = "/quality/calibration",
                Description = "تجهیزات اندازه‌گیری که کالیبراسیونشان سررسید شده یا تا ۳۰ روز آینده سررسید می‌شود.",
                SchemaJson = Schema(("days", "integer", "تعداد روز آینده؛ پیش‌فرض ۳۰")),
                ItemLinks = { Items("Equipment", "تجهیز", "/quality/calibration/{id}") },
                Run = async (c, a, ct) => Paged(AiToolShaper.Unwrap(await c.Get<Qa.ICalibrationService>().GetDueAsync(new DueQuery { Days = Int(a, "days") ?? 30, PageNumber = 1, PageSize = 10 }, ct))),
            });
            tools.Add(new AiToolDefinition
            {
                Name = "itp_status", Title = "وضعیت ITP دستور کار", RequiredRoles = quality, CitationType = "Itp", PageLink = "/quality/itp",
                Description = "وضعیت طرح بازرسی و آزمون (ITP) یک دستور کار: وضعیت، فعالیت‌ها و نقاط توقف؛ با شناسه یا کد دستور کار.",
                SchemaJson = Schema(("workOrderId", "integer", "شناسه دستور کار"), ("workOrderCode", "string", "کد دستور کار")),
                ItemLinks = { Items("Itp", "ITP", "/quality/itp/{id}", "items") },
                Run = async (c, a, ct) =>
                {
                    int? id = await WorkOrderIdAsync(c, a, ct);
                    if (!id.HasValue) throw new AiToolFailureException("دستور کاری با این مشخصات پیدا نشد.");
                    ItpDto itp = AiToolShaper.Unwrap(await c.Get<Qa.IItpService>().GetByWorkOrderAsync(id.Value, ct));
                    return new { workOrderId = id, items = new[] { itp } };
                },
            });
            tools.Add(new AiToolDefinition
            {
                Name = "mdr_status", Title = "وضعیت MDR پروژه", RequiredRoles = projects, CitationType = "Project", PageLink = "/project-control/mdr",
                Description = "وضعیت فهرست مدارک پروژه (MDR): تعداد مدارک به تفکیک وضعیت صدور و مدارک هنوز صادرنشده؛ با شناسه یا کد پروژه.",
                SchemaJson = Schema(("contractId", "integer", "شناسه قرارداد/پروژه"), ("projectCode", "string", "کد پروژه")),
                ItemLinks = { Items("Deliverable", "مدرک", "/project-control/mdr", "notIssued") },
                Run = async (c, a, ct) =>
                {
                    int? contractId = await ContractIdAsync(c, a, ct);
                    if (!contractId.HasValue) throw new AiToolFailureException("پروژه‌ای با این مشخصات پیدا نشد؛ شناسه یا کد پروژه را بدهید.");
                    PagedList<PcDeliverableDto> docs = AiToolShaper.Unwrap(await c.Get<Pc.IPcMdrService>().GetDeliverablesAsync(new PcDeliverableQuery { ContractId = contractId, PageNumber = 1, PageSize = 200 }, ct));
                    var byState = docs.GroupBy(d => d.IssueState).OrderBy(g => (int)g.Key).Select(g => new { state = g.Key.ToString(), count = g.Count() }).ToList();
                    return new { contractId, totalCount = docs.TotalCount, byState, notIssuedTotalCount = docs.Count(d => d.IssueState == Entities.Model.NasbNiro.ProjectControl.PcIssueState.NotIssued), notIssued = docs.Where(d => d.IssueState == Entities.Model.NasbNiro.ProjectControl.PcIssueState.NotIssued).Take(10).ToList() };
                },
            });

            // ---------------------------------------------------------------------------------------- sales, service, production
            tools.Add(new AiToolDefinition
            {
                Name = "sales_dashboard", Title = "داشبورد فروش", RequiredRoles = Sl.SalesRoles.Readers, PageLink = "/sales/dashboard",
                Description = "داشبورد فروش: استعلام‌های باز و ارزش خط لوله، نرخ برد، پیشنهادهای نزدیک به مهلت، تصمیم‌های منتظر و سفارش‌های باز.",
                Run = async (c, a, ct) => AiToolShaper.Unwrap(await c.Get<Sl.ISlDashboardService>().GetAsync(null, ct)),
            });
            tools.Add(new AiToolDefinition
            {
                Name = "service_dashboard", Title = "داشبورد خدمات پس از فروش", RequiredRoles = Sv.SvRoles.Readers, PageLink = "/service/dashboard",
                Description = "داشبورد خدمات پس از فروش: درخواست‌های باز، نقض SLA، واحدهای در گارانتی، رضایت مشتری.",
                Run = async (c, a, ct) => AiToolShaper.Unwrap(await c.Get<Sv.ISvDashboardService>().GetAsync(null, ct)),
            });
            tools.Add(new AiToolDefinition
            {
                Name = "production_dashboard", Title = "داشبورد تولید", RequiredRoles = Mf.MfDashboardService.Roles, PageLink = "/production/dashboard",
                Description = "داشبورد تولید: دستورهای تولید دارای تأخیر، ماشین‌های خراب، بار مراکز کار و نگهداری سررسیدشده.",
                Run = async (c, a, ct) => AiToolShaper.Unwrap(await c.Get<Mf.IMfDashboardService>().GetAsync(new DashboardQuery(), ct)),
            });

            // ---------------------------------------------------------------------------------------- documents
            tools.Add(new AiToolDefinition
            {
                Name = "search_documents", Title = "جست‌وجوی اسناد", CitationType = "ControlledDocument", PageLink = null,
                Description = "جست‌وجوی معنایی در اسناد کنترل‌شده (رویه‌ها و دستورالعمل‌های کاری) و صورت‌جلسه‌های تأییدشده؛ برای پرسش درباره‌ی روش انجام کار یا مفاد یک سند.",
                SchemaJson = Schema(("query", "string", "عبارت جست‌وجو به فارسی")),
                ItemLinks = { new AiItemLinkRule { Property = "items", Type = "ControlledDocument", TypeKey = "sourceType", Template = "{link}", IdKey = "sourceId" } },
                Run = async (c, a, ct) =>
                {
                    string query = Str(a, "query") ?? Str(a, "text");
                    if (query == null) throw new AiToolFailureException("عبارت جست‌وجو را بدهید.");
                    List<AiSearchHitDto> hits = await c.Get<IRetrievalService>().SearchAsync(query, 5, null, ct);
                    return new { totalCount = hits.Count, items = hits };
                },
            });
            tools.Add(new AiToolDefinition
            {
                Name = "explain_alert", Title = "توضیح هشدار", RequiredRoles = "CEO,PMO", PageLink = null,
                Description = "رکوردهای منبع یک هشدار داشبورد مدیرعامل را برمی‌گرداند؛ پیوند (link) هشدار را از نتیجه‌ی ceo_dashboard بدهید.",
                SchemaJson = Schema(("link", "string", "پیوند هشدار، مثلاً /inventory/stock"), ("kind", "string", "نوع هشدار (اختیاری)")),
                Run = async (c, a, ct) =>
                {
                    string link = Str(a, "link") ?? Str(a, "kind");
                    string inner = AlertSource(link);
                    if (inner == null) return new { supported = false, alertLink = link, message = "برای این هشدار منبع داده‌ی قابل‌خواندن تعریف نشده است؛ پیوند هشدار را در سامانه باز کنید." };
                    AiToolResult r = await c.Registry.ExecuteAsync(inner, "{}", ct);
                    if (!r.Allowed) throw new AiToolForbiddenException();
                    return new AiToolOutput { Data = new { alertLink = link, sourceTool = inner, result = JsonNode.Parse(r.Json) }, Citations = r.Citations };
                },
            });
            return tools;
        }

        /// <summary>The tool whose result lists the records behind a dashboard alert, by the front-end path of the alert.</summary>
        public static string AlertSource(string link)
        {
            if (string.IsNullOrWhiteSpace(link)) return null;
            string l = link.Trim().ToLowerInvariant();
            (string Prefix, string Tool)[] map =
            {
                ("/inventory/stock", "items_below_minimum"), ("/inventory/ncr", "open_ncr_capa"),
                ("/procurement/orders", "purchase_status"), ("/procurement/requests", "purchase_status"), ("/procurement/payments", "overdue_payables"), ("/procurement/suppliers", "supplier_profile"),
                ("/finance/costs", "project_portfolio"), ("/finance/events", "finance_dashboard"), ("/finance/", "finance_dashboard"), ("/fn/", "finance_dashboard"),
                ("/project-control/guarantees", "expiring_guarantees"), ("/project-control/changes", "change_requests"), ("/project-control/", "project_portfolio"),
                ("/quality/calibration", "calibration_due"), ("/quality/capa", "open_ncr_capa"), ("/quality/", "quality_dashboard"),
                ("/sales/", "sales_dashboard"), ("/service/", "service_dashboard"), ("/production/", "production_dashboard"),
            };
            foreach (var m in map) if (l.StartsWith(m.Prefix, StringComparison.Ordinal)) return m.Tool;
            return null;
        }
    }
}
