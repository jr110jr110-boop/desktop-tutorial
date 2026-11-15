using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.AfterSales;
using ViewModels.NasbNiro.ServiceDtos;

namespace Services.NasbNiro.Agents.Steps
{
    // Station 12 (after-sales service). The customer's request is registered by a person (not a task). The service agent drafts the service order of an open request of an installed unit; people plan the engineers
    // and start it (physical), do the visit, record the hours, the measurements and attach the signed report (physical), decide the warranty (a second service person, never the one who made or edited the order),
    // the CEO decides goodwill, a person closes the order (the closing stamps the person who closed it), and the agent retries the draft invoice of a closed billable order. A second service person confirms the
    // installed unit that the system drafted from a delivered shipment.

    public static class ServiceSteps
    {
        public static IServiceCollection AddServiceSteps(this IServiceCollection services)
        {
            services.AddScoped<IAgentStep, SvUnitConfirmStep>();
            services.AddScoped<IAgentStep, SvOrderDraftStep>();
            services.AddScoped<IAgentStep, SvOrderStartStep>();
            services.AddScoped<IAgentStep, SvOrderFieldworkStep>();
            services.AddScoped<IAgentStep, SvGoodwillDecideStep>();
            services.AddScoped<IAgentStep, SvWarrantyDecideStep>();
            services.AddScoped<IAgentStep, SvOrderCloseStep>();
            services.AddScoped<IAgentStep, SvInvoiceDraftStep>();
            services.AddScoped<IAgentStep, SvRequestAssignStep>();
            services.AddScoped<IAgentStep, SvRequestResolveStep>();
            services.AddScoped<IAgentStep, SvRequestCloseStep>();
            return services;
        }
    }

    internal abstract class SvStep : ProcStep
    {
        public override int Station => 12;
        public override string AgentCode => AgentCodes.Service;

        /// <summary>A unit or a request older than this (days) is not started by the agents.</summary>
        public const int LiveDays = 400;

        protected static string UnitLink(int id) => "/service/units/" + id.ToString(CultureInfo.InvariantCulture);
        protected static string RequestLink(int id) => "/service/requests/" + id.ToString(CultureInfo.InvariantCulture);
        protected static string OrderLink(int id) => "/service/orders/" + id.ToString(CultureInfo.InvariantCulture);

        /// <summary>The task goes to the person who owns the request (the assignee) when there is one; the roles stay as the fallback.</summary>
        protected static AgentWorkItem Named(AgentWorkItem item, int? userId)
        {
            if (userId.HasValue && userId.Value > 0) { item.GateUserId = userId; item.GateRoles = null; }
            return item;
        }

        protected static DateTime Since(DateTime today) => today.Date.AddDays(-LiveDays);

        protected static IQueryable<SvServiceOrder> Orders(IUnitOfWork uow, DateTime today)
        {
            DateTimeOffset since = new DateTimeOffset(DateTime.SpecifyKind(Since(today), DateTimeKind.Utc));
            return uow.Set<SvServiceOrder>().AsNoTracking().Where(o => !o.Deleted && o.CreateDate >= since);
        }

        protected static AgentWorkItem OrderItem(string number, int id, int? contractId, string summary, string roles = null) =>
            Item("SvServiceOrder", id, number, contractId, null, summary, roles);
    }

    /// <summary>SV.UNIT.CONFIRM (gate): the installed unit that the system drafted from a delivered shipment is confirmed by a service person who did not edit it (tag, customer, site and the warranty dates are theirs to check).</summary>
    internal sealed class SvUnitConfirmStep : SvStep
    {
        public override string Code => "SV.UNIT.CONFIRM";
        public override string Title => "تایید واحد نصب‌شده (نفر دوم خدمات)";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            DateTimeOffset since = new DateTimeOffset(DateTime.SpecifyKind(Since(today), DateTimeKind.Utc));
            var rows = await uow.Set<SvInstalledUnit>().AsNoTracking().Where(u => !u.Deleted && !u.IsConfirmed && !u.Decommissioned && u.CreateDate >= since)
                .OrderBy(u => u.Id).Take(ProcStepCommon.Cap).Select(u => new { u.Id, u.Tag, u.Title, u.ContractId, u.CustomerId, u.SiteId, u.WarrantyEnd }).ToListAsync(ct);
            return rows.Select(u =>
            {
                var left = new List<string>();
                if (string.IsNullOrWhiteSpace(u.Tag)) left.Add("شماره tag");
                if (!u.CustomerId.HasValue) left.Add("مشتری");
                if (!u.SiteId.HasValue) left.Add("سایت نصب");
                return Item("SvInstalledUnit", u.Id, u.Tag ?? u.Title, u.ContractId, null,
                    $"واحد {u.Title} از تحویل محموله پیش‌نویس شده؛ گارانتی تا {(u.WarrantyEnd.HasValue ? u.WarrantyEnd.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "نامشخص")}. "
                    + (left.Count > 0 ? "پیش از تایید باید تکمیل شود: " + string.Join("، ", left) + "؛ " : "") + "تایید با یک نفر دیگر از خدمات است (نه ویرایش‌کننده واحد).", SvRoles.Writers);
            }).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SvRoles.Writers, Link = UnitLink(item.TargetId), Title = "تایید واحد نصب‌شده " + item.TargetNumber };
    }

    /// <summary>SV.ORDER.DRAFT (Agent): the service order of an open request that needs a visit: unit, kind and title from the request; the engineers and the dates are the people's.</summary>
    internal sealed class SvOrderDraftStep : SvStep
    {
        public override string Code => "SV.ORDER.DRAFT";
        public override string Title => "ایجنت خدمات: پیش‌نویس دستور خدمات از درخواست مشتری";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => SvRoles.Writers;

        private static string TypeText(SvRequestType t) => t switch
        {
            SvRequestType.Breakdown => "رفع عیب",
            SvRequestType.Inspection => "بازدید",
            SvRequestType.Commissioning => "راه‌اندازی",
            SvRequestType.Overhaul => "تعمیرات اساسی",
            SvRequestType.Complaint => "بررسی شکایت",
            _ => "خدمات",
        };

        private static IQueryable<SvServiceRequest> Candidates(IUnitOfWork uow, DateTime today) =>
            uow.Set<SvServiceRequest>().AsNoTracking().Where(r => !r.Deleted && r.UnitId != null && r.ReceivedAt >= Since(today)
                && (r.Status == SvRequestStatus.Open || r.Status == SvRequestStatus.Assigned || r.Status == SvRequestStatus.InProgress)
                && r.Type != SvRequestType.SpareParts && r.Type != SvRequestType.TechnicalQuery
                && !uow.Set<SvServiceOrder>().Any(o => o.RequestId == r.Id && !o.Deleted && o.Status != SvOrderStatus.Cancelled));

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await (from r in Candidates(uow, today)
                    join u in uow.Set<SvInstalledUnit>().AsNoTracking() on r.UnitId equals (int?)u.Id
                    where !u.Deleted && !u.Decommissioned
                    orderby r.Id
                    select new { r.Id, r.Number, r.Type, u.Title, u.ContractId }).Take(ProcStepCommon.Cap).ToListAsync(ct))
                .Select(r => Item("SvServiceRequest", r.Id, r.Number, r.ContractId, null, $"درخواست {r.Number} ({TypeText(r.Type)}) برای واحد {r.Title} هنوز دستور خدمات ندارد.")).ToList();

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            var request = await Candidates(uow, DateTime.Today).Where(r => r.Id == item.TargetId).Select(r => new { r.Id, r.Number, r.Type, r.UnitId, r.Description }).FirstOrDefaultAsync(ct);
            if (request == null) return AgentStepResult.Waiting("درخواستی که دستور خدمات نداشته باشد پیدا نشد.");
            string unitTitle = await uow.Set<SvInstalledUnit>().AsNoTracking().Where(u => u.Id == request.UnitId.Value).Select(u => u.Title).FirstOrDefaultAsync(ct);
            string title = $"{TypeText(request.Type)} {unitTitle} (درخواست {request.Number})";
            var dto = new SvSaveOrderDto
            {
                RequestId = request.Id, UnitId = request.UnitId.Value, Kind = request.Type == SvRequestType.Overhaul ? SvOrderKind.Workshop : SvOrderKind.Field,
                Title = title.Length > SvLimits.Title ? title.Substring(0, SvLimits.Title) : title, PlannedStart = DateTime.Today, Findings = null,
            };
            ResponseDto<SvOrderDto> r = await services.GetRequiredService<ISvOrderService>().SaveOrderAsync(dto, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            return AgentStepResult.Done("SvServiceOrder", r.Data.Id, r.Data.Number, $"ایجنت خدمات دستور {r.Data.Number} را از درخواست {request.Number} ساخت؛ مهندسان را خدمات تعیین می‌کند.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SvRoles.Writers, Link = RequestLink(item.TargetId), Title = "دستور خدمات برای درخواست " + item.TargetNumber };
    }

    /// <summary>SV.ORDER.START (Physical): the engineers are chosen and sent to the site; the person starts the order when the work begins.</summary>
    internal sealed class SvOrderStartStep : SvStep
    {
        public override string Code => "SV.ORDER.START";
        public override string Title => "تعیین مهندسان و شروع دستور خدمات";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await Orders(uow, today).Where(o => o.Status == SvOrderStatus.Planned).OrderBy(o => o.Id).Take(ProcStepCommon.Cap)
                .Select(o => new { o.Id, o.Number, o.ContractId, Engineers = o.Engineers.Count() }).ToListAsync(ct))
                .Select(o => OrderItem(o.Number, o.Id, o.ContractId, o.Engineers == 0 ? $"دستور {o.Number} مهندس ندارد؛ مهندسان را تعیین کنید و هنگام شروع کار دستور را شروع کنید." : $"دستور {o.Number} برنامه‌ریزی شده؛ هنگام شروع کار آن را شروع کنید.", SvRoles.Writers)).ToList();

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SvRoles.Writers, Link = OrderLink(item.TargetId), Title = "شروع دستور خدمات " + item.TargetNumber };
    }

    /// <summary>SV.ORDER.FIELDWORK (Physical): the visit itself: the hours, the measurements and the report signed by the customer exist only on site.</summary>
    internal sealed class SvOrderFieldworkStep : SvStep
    {
        public override string Code => "SV.ORDER.FIELDWORK";
        public override string Title => "ثبت ساعت کار، اندازه‌گیری‌ها و گزارش امضاشده مشتری";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await Orders(uow, today).Where(o => o.Status == SvOrderStatus.InProgress && (!o.TimeEntries.Any() || o.ReportDocumentId == null)).OrderBy(o => o.Id).Take(ProcStepCommon.Cap)
                .Select(o => new { o.Id, o.Number, o.ContractId, Hours = o.TimeEntries.Any(), Report = o.ReportDocumentId != null }).ToListAsync(ct))
                .Select(o => OrderItem(o.Number, o.Id, o.ContractId, $"دستور {o.Number} در حال انجام است؛ باقی‌مانده: " + string.Join("، ", new[] { o.Hours ? null : "ساعت‌های کار و سفر", "اندازه‌گیری‌ها (در صورت وجود)", o.Report ? null : "پیوست گزارش امضاشده مشتری" }.Where(x => x != null)) + ".", SvRoles.Writers)).ToList();

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SvRoles.Writers, Link = OrderLink(item.TargetId), Title = "ثبت کار میدانی دستور " + item.TargetNumber };
    }

    /// <summary>SV.GOODWILL.DECIDE (gate): service asked for goodwill (the company pays without a warranty): the CEO alone decides.</summary>
    internal sealed class SvGoodwillDecideStep : SvStep
    {
        public override string Code => "SV.GOODWILL.DECIDE";
        public override string Title => "تصمیم مدیرعامل درباره حسن نیت";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await Orders(uow, today).Where(o => o.Status == SvOrderStatus.InProgress && o.GoodwillRequestedAt != null && o.Decision != SvWarrantyDecision.Goodwill).OrderBy(o => o.Id).Take(ProcStepCommon.Cap)
                .Select(o => new { o.Id, o.Number, o.ContractId, o.GoodwillReason }).ToListAsync(ct))
                .Select(o => OrderItem(o.Number, o.Id, o.ContractId, $"خدمات برای دستور {o.Number} حسن نیت خواسته است: {o.GoodwillReason}. تصمیم با مدیرعامل است.", SvRoles.GoodwillDeciders)).ToList();

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SvRoles.GoodwillDeciders, Link = OrderLink(item.TargetId), Title = "حسن نیت دستور خدمات " + item.TargetNumber };
    }

    /// <summary>SV.WARRANTY.DECIDE (gate): warranty or billable, by a service person who neither made nor edited the order; the summary says what the warranty dates of the unit imply.</summary>
    internal sealed class SvWarrantyDecideStep : SvStep
    {
        public override string Code => "SV.WARRANTY.DECIDE";
        public override string Title => "تصمیم گارانتی دستور خدمات (گارانتی یا قابل‌فاکتور)";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await Orders(uow, today).Where(o => o.Status == SvOrderStatus.InProgress && o.Decision == SvWarrantyDecision.None && o.LtsaContractId == null && o.GoodwillRequestedAt == null && o.TimeEntries.Any())
                .OrderBy(o => o.Id).Take(ProcStepCommon.Cap).Select(o => new { o.Id, o.Number, o.ContractId, o.UnitId, o.WarrantyTestedOn }).ToListAsync(ct);
            int[] unitIds = rows.Select(r => r.UnitId).Distinct().ToArray();
            Dictionary<int, (DateTime? Start, DateTime? End, bool Confirmed)> units = unitIds.Length == 0 ? new Dictionary<int, (DateTime? Start, DateTime? End, bool Confirmed)>()
                : (await uow.Set<SvInstalledUnit>().AsNoTracking().Where(u => unitIds.Contains(u.Id)).Select(u => new { u.Id, u.WarrantyStart, u.WarrantyEnd, u.IsConfirmed }).ToListAsync(ct)).ToDictionary(u => u.Id, u => (Start: u.WarrantyStart, End: u.WarrantyEnd, Confirmed: u.IsConfirmed));
            return rows.Select(r =>
            {
                bool known = units.TryGetValue(r.UnitId, out var u);
                bool covered = known && SvWarranty.Covers(u.Start, u.End, r.WarrantyTestedOn ?? today);
                string hint = !known ? "واحد پیدا نشد" : covered ? "تاریخ خرابی در بازهٔ گارانتی واحد است" + (u.Confirmed ? "" : "؛ ولی گارانتی واحد هنوز تایید نشده و تا آن زمان گارانتی داده نمی‌شود") : "تاریخ خرابی خارج از گارانتی واحد است (قابل‌فاکتور یا حسن نیت)";
                return OrderItem(r.Number, r.Id, r.ContractId, $"کار میدانی دستور {r.Number} ثبت شده؛ {hint}. تصمیم با نفر دومِ خدمات است (نه سازنده یا ویرایش‌کنندهٔ دستور).", SvRoles.Writers);
            }).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SvRoles.Writers, Link = OrderLink(item.TargetId), Title = "تصمیم گارانتی دستور " + item.TargetNumber };
    }

    /// <summary>SV.ORDER.CLOSE (gate): a person closes the order (the closing posts the cost and stamps the person who closed it; a seat never does).</summary>
    internal sealed class SvOrderCloseStep : SvStep
    {
        public override string Code => "SV.ORDER.CLOSE";
        public override string Title => "بستن دستور خدمات";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await Orders(uow, today).Where(o => o.Status == SvOrderStatus.InProgress && o.ReportDocumentId != null && o.TimeEntries.Any() && (o.Decision != SvWarrantyDecision.None || o.LtsaContractId != null))
                .OrderBy(o => o.Id).Take(ProcStepCommon.Cap).Select(o => new { o.Id, o.Number, o.ContractId, o.Decision }).ToListAsync(ct))
                .Select(o => OrderItem(o.Number, o.Id, o.ContractId, $"دستور {o.Number} گزارش امضاشده، ساعت کار و تصمیم گارانتی دارد؛ بستن آن هزینه را ثبت می‌کند" + (o.Decision == SvWarrantyDecision.Billable ? " و پیش‌نویس فاکتور را می‌سازد." : "."), SvRoles.Writers)).ToList();

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SvRoles.Writers, Link = OrderLink(item.TargetId), Title = "بستن دستور خدمات " + item.TargetNumber };
    }

    /// <summary>SV.INVOICE.DRAFT (Agent): a closed billable order that has no invoice (the closing could not draft it: no rate, no VAT rate, a part without a price) is tried again; the refusal of the service says what is missing.</summary>
    internal sealed class SvInvoiceDraftStep : SvStep
    {
        public override string Code => "SV.INVOICE.DRAFT";
        public override string Title => "ایجنت خدمات: پیش‌نویس فاکتور دستور قابل‌فاکتور";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => SvRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await Orders(uow, today).Where(o => o.Status == SvOrderStatus.Closed && o.Decision == SvWarrantyDecision.Billable && o.InvoiceId == null).OrderBy(o => o.Id).Take(ProcStepCommon.Cap)
                .Select(o => new { o.Id, o.Number, o.ContractId, o.DraftNote }).ToListAsync(ct))
                .Select(o => OrderItem(o.Number, o.Id, o.ContractId, $"دستور بسته‌شدهٔ قابل‌فاکتور {o.Number} هنوز پیش‌نویس فاکتور ندارد" + (string.IsNullOrWhiteSpace(o.DraftNote) ? "." : ": " + o.DraftNote))).ToList();

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<SvOrderDto> r = await services.GetRequiredService<ISvOrderService>().DraftInvoiceAsync(new SvDraftInvoiceDto { Id = item.TargetId }, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            return AgentStepResult.Done("SvServiceOrder", r.Data.Id, r.Data.Number, $"ایجنت خدمات پیش‌نویس فاکتور دستور {r.Data.Number} را ساخت؛ تایید فاکتور با حسابدار است.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SvRoles.Writers, Link = OrderLink(item.TargetId), Title = "پیش‌نویس فاکتور دستور " + item.TargetNumber };
    }

    /// <summary>SV.REQUEST.ASSIGN (Input): an open request that nobody is responsible for yet: the service person names the responsible user (the request rules stay as they are).</summary>
    internal sealed class SvRequestAssignStep : SvStep
    {
        public override string Code => "SV.REQUEST.ASSIGN";
        public override string Title => "تعیین مسئول درخواست خدمات";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await uow.Set<SvServiceRequest>().AsNoTracking().Where(r => !r.Deleted && r.Status == SvRequestStatus.Open && r.AssignedToUserId == null && r.ReceivedAt >= Since(today))
                .OrderBy(r => r.Id).Take(ProcStepCommon.Cap).Select(r => new { r.Id, r.Number }).ToListAsync(ct))
                .Select(r => Item("SvServiceRequest", r.Id, r.Number, null, null, $"درخواست {r.Number} مسئول ندارد؛ مسئول پیگیری را تعیین کنید.", SvRoles.Writers)).ToList();

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SvRoles.Writers, Link = RequestLink(item.TargetId), Title = "تعیین مسئول درخواست " + item.TargetNumber };
    }

    /// <summary>SV.REQUEST.RESOLVE (Input): every service order of the request is closed or cancelled (at least one closed) but the request is still open: the person writes the resolution.</summary>
    internal sealed class SvRequestResolveStep : SvStep
    {
        public override string Code => "SV.REQUEST.RESOLVE";
        public override string Title => "ثبت راه‌حل درخواست خدمات پس از بسته شدن دستورها";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await uow.Set<SvServiceRequest>().AsNoTracking().Where(r => !r.Deleted && r.Status != SvRequestStatus.Resolved && r.Status != SvRequestStatus.Closed && r.ReceivedAt >= Since(today)
                    // a request that needs no service order (spare parts, a technical question) is pushed to its resolution once somebody has taken it
                    && (uow.Set<SvServiceOrder>().Any(o => o.RequestId == r.Id && !o.Deleted && o.Status == SvOrderStatus.Closed)
                        || ((r.Type == SvRequestType.SpareParts || r.Type == SvRequestType.TechnicalQuery) && r.Status != SvRequestStatus.Open))
                    && !uow.Set<SvServiceOrder>().Any(o => o.RequestId == r.Id && !o.Deleted && o.Status != SvOrderStatus.Closed && o.Status != SvOrderStatus.Cancelled))
                .OrderBy(r => r.Id).Take(ProcStepCommon.Cap).Select(r => new { r.Id, r.Number, r.AssignedToUserId, r.Type }).ToListAsync(ct))
                .Select(r => Named(Item("SvServiceRequest", r.Id, r.Number, null, null,
                    r.Type == SvRequestType.SpareParts || r.Type == SvRequestType.TechnicalQuery ? $"درخواست {r.Number} نیازی به دستور خدمات ندارد؛ شرح پاسخ یا راه‌حل را ثبت کنید تا درخواست رفع شود." : $"دستورهای خدمات درخواست {r.Number} بسته شده‌اند؛ شرح راه‌حل را ثبت کنید تا درخواست رفع شود.", SvRoles.Writers), r.AssignedToUserId)).ToList();

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = item.GateUserId.HasValue ? null : SvRoles.Writers, UserId = item.GateUserId, Link = RequestLink(item.TargetId), Title = "ثبت راه‌حل درخواست " + item.TargetNumber };
    }

    /// <summary>SV.REQUEST.CLOSE (gate): a resolved request is closed by a person (only a resolved request can be closed).</summary>
    internal sealed class SvRequestCloseStep : SvStep
    {
        public override string Code => "SV.REQUEST.CLOSE";
        public override string Title => "بستن درخواست خدمات رفع‌شده";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await uow.Set<SvServiceRequest>().AsNoTracking().Where(r => !r.Deleted && r.Status == SvRequestStatus.Resolved && r.ReceivedAt >= Since(today))
                .OrderBy(r => r.Id).Take(ProcStepCommon.Cap).Select(r => new { r.Id, r.Number, r.AssignedToUserId }).ToListAsync(ct))
                .Select(r => Named(Item("SvServiceRequest", r.Id, r.Number, null, null, $"درخواست {r.Number} رفع شده است؛ پس از اطمینان از رضایت مشتری آن را ببندید.", SvRoles.Writers), r.AssignedToUserId)).ToList();

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = item.GateUserId.HasValue ? null : SvRoles.Writers, UserId = item.GateUserId, Link = RequestLink(item.TargetId), Title = "بستن درخواست " + item.TargetNumber };
    }
}
