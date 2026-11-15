using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.ContractsSystem;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.ContractsSystem;
using Services.NasbNiro.Finance;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.ProcurementDtos;

namespace Services.NasbNiro.Agents.Steps
{
    // Station 13 (finance and Sepidar). The finance agent drafts what the system already knows: the progress statement of an order (the work the unit accepted, not yet claimed) and of a client contract
    // (the earned value of the CBS items from the progress of their WBS nodes, not yet claimed), prepares it (the creator prepares: it is the commercial maker step of the service), requests the payment
    // of an authorised supplier statement and drafts the Sepidar package of a closed month that has posted sales invoices and no package. People decide at the gates the service defines: the technical
    // office, the supervision, the project manager of the contract (a named person), the finance manager (or the CEO) for a package, the accountant for a sales invoice that the milestone job drafted.
    // People do the physical work: send the statement to the client, hand the package over in Sepidar (outside the system), record the answer.

    public static class FinanceSteps
    {
        public static IServiceCollection AddFinanceSteps(this IServiceCollection services)
        {
            services.AddScoped<IAgentStep, FinStatementDraftStep>();
            services.AddScoped<IAgentStep, FinStatementPrepareStep>();
            services.AddScoped<IAgentStep, FinStatementTechnicalStep>();
            services.AddScoped<IAgentStep, FinStatementSupervisionStep>();
            services.AddScoped<IAgentStep, FinStatementAuthoriseStep>();
            services.AddScoped<IAgentStep, FinStatementPayRequestStep>();
            services.AddScoped<IAgentStep, FinStatementSendStep>();
            services.AddScoped<IAgentStep, FinStatementPaidStep>();
            services.AddScoped<IAgentStep, FinStatementFixStep>();
            services.AddScoped<IAgentStep, FinInvoiceApproveStep>();
            services.AddScoped<IAgentStep, FinSepidarGenerateStep>();
            services.AddScoped<IAgentStep, FinSepidarApproveStep>();
            services.AddScoped<IAgentStep, FinSepidarHandoverStep>();
            services.AddScoped<IAgentStep, FinSepidarResultStep>();
            return services;
        }
    }

    /// <summary>The roles of the actions, copied from the services (the tests compare them with the actions).</summary>
    internal static class FinRoles
    {
        /// <summary>SaveStatementAsync, "prepare", "request-payment", "send": PsRoles.Preparers.</summary>
        public const string Commercial = PsRoles.Preparers;

        /// <summary>"technical-approve".</summary>
        public const string Technical = PsRoles.TechnicalOffice;

        /// <summary>"supervision-approve".</summary>
        public const string Supervision = PsRoles.Supervision;

        /// <summary>"authorise-payment" when the contract has no project manager: the PMO deputy.</summary>
        public const string AuthoriseFallback = "PMO";

        /// <summary>RecordPaymentAsync.</summary>
        public const string Payment = PsRoles.PaymentRecorders;

        /// <summary>A returned statement is edited by its creator or by the PMO who also holds the commercial role (SaveStatementAsync).</summary>
        public const string Fix = "COMMERCIAL,PMO";

        /// <summary>The approval action of InvoiceActionAsync: the accountants (not the one who made or edited the draft).</summary>
        public const string Invoice = FinanceRoles.Accountants;

        /// <summary>SepidarOutboxService.CanApprove.</summary>
        public const string PackageApprove = FinanceRoles.ManagersOrCeo;

        /// <summary>SepidarOutboxService.CanPrepare for a tax package (send, mark-sent, mark-accepted, mark-rejected).</summary>
        public const string PackagePrepare = FinanceRoles.Accountants;

        /// <summary>SepidarOutboxService.CanPrepare for a payroll list.</summary>
        public const string PayrollPrepare = FinanceRoles.PayrollUsers;
    }

    internal abstract class FinStep : ProcStep
    {
        public override int Station => 13;
        public override string AgentCode => AgentCodes.Fin;

        protected const string StatementsLink = "/procurement/statements";
        protected const string InvoicesLink = "/fn/receivables/invoices";
        protected const string SepidarLink = "/fn/sepidar";

        protected static string StatementLink(int id) => StatementsLink + "?id=" + id.ToString(CultureInfo.InvariantCulture);

        protected static string Money(decimal v) => v.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>The statements in the given status (of the given side) that also match <paramref name="extra"/>, oldest first.</summary>
        protected static async Task<List<StatementRow>> StatementsAsync(IUnitOfWork uow, PsStatus status, System.Linq.Expressions.Expression<Func<PsStatement, bool>> extra, CancellationToken ct)
        {
            IQueryable<PsStatement> q = uow.Set<PsStatement>().AsNoTracking().Where(s => !s.Deleted && s.Status == status);
            if (extra != null) q = q.Where(extra);
            return await q.OrderBy(s => s.Id).Take(ProcStepCommon.Cap).Select(s => new StatementRow
            {
                Id = s.Id, Number = s.Number, Side = s.Side, ContractId = s.ContractId, PurchaseOrderId = s.PurchaseOrderId, Payable = s.PayableThisPeriod, PaymentRequestId = s.PaymentRequestId,
            }).ToListAsync(ct);
        }

        protected static AgentWorkItem StatementItem(StatementRow s, string summary, string roles = null) =>
            Item("PsStatement", s.Id, s.Number, s.ContractId, null, summary, roles);

        internal sealed class StatementRow
        {
            public int Id { get; set; }
            public string Number { get; set; }
            public PsSide Side { get; set; }
            public int? ContractId { get; set; }
            public int? PurchaseOrderId { get; set; }
            public decimal Payable { get; set; }
            public int? PaymentRequestId { get; set; }
        }
    }

    /// <summary>What the statement drafting derives: the claim of every order line (supplier side) and of every CBS item (client side).</summary>
    internal static class FinClaims
    {
        public sealed class Claim
        {
            public string Title { get; set; }
            public decimal Quantity { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal Amount { get; set; }
            public int? OrderLineId { get; set; }
            public int? CbsItemId { get; set; }
            public decimal? ProgressPercent { get; set; }
        }

        private static decimal R(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);

        /// <summary>
        /// The orders that a statement can be drafted for: a service or subcontract order in rial, released, whose accepted value (the service acceptances of its lines x the price, which is what the payment
        /// request of a statement is capped by) is higher than everything already claimed in the statements of the order (every statement that was not rejected). An order whose statement was rejected is left
        /// alone until the order itself changes.
        /// </summary>
        public static async Task<Dictionary<int, (string Number, int? ContractId, List<Claim> Claims)>> SupplierAsync(IUnitOfWork uow, int? onlyOrder, CancellationToken ct)
        {
            var result = new Dictionary<int, (string, int?, List<Claim>)>();
            IQueryable<PurchaseOrder> q = uow.Set<PurchaseOrder>().AsNoTracking().Where(o => !o.Deleted && o.Kind != PurchaseKind.Goods && (o.Currency == null || o.Currency == "")
                && (o.Status == PurchaseOrderStatus.Released || o.Status == PurchaseOrderStatus.PartlyReceived || o.Status == PurchaseOrderStatus.Received)
                && uow.Set<ServiceAcceptance>().Where(a => !a.Deleted && a.PurchaseOrderLine.PurchaseOrderId == o.Id).Sum(a => (decimal?)(a.Quantity * a.PurchaseOrderLine.UnitPrice)) > (uow.Set<PsStatement>().Where(s => s.PurchaseOrderId == o.Id && !s.Deleted && s.Status != PsStatus.Rejected).Sum(s => (decimal?)s.GrossThisPeriod) ?? 0m)
                && !uow.Set<PsStatement>().Any(s => s.PurchaseOrderId == o.Id && !s.Deleted && s.Status == PsStatus.Rejected && s.ModifyDate >= o.ModifyDate));
            if (onlyOrder.HasValue) q = q.Where(o => o.Id == onlyOrder.Value);
            var orders = await q.OrderBy(o => o.Id).Take(ProcStepCommon.Cap).Select(o => new { o.Id, o.Number, o.ContractId }).ToListAsync(ct);
            if (orders.Count == 0) return result;
            int[] ids = orders.Select(o => o.Id).ToArray();
            var lines = await uow.Set<PurchaseOrderLine>().AsNoTracking().Where(l => ids.Contains(l.PurchaseOrderId) && l.UnitPrice > 0)
                .OrderBy(l => l.PurchaseOrderId).ThenBy(l => l.RowNumber).Select(l => new { l.Id, l.PurchaseOrderId, l.RowNumber, l.Description, l.Quantity, l.UnitPrice, ItemTitle = l.Item.Title }).ToListAsync(ct);
            var acceptedRows = await uow.Set<ServiceAcceptance>().AsNoTracking().Where(a => !a.Deleted && ids.Contains(a.PurchaseOrderLine.PurchaseOrderId)).Select(a => new { Line = a.PurchaseOrderLineId, a.Quantity }).ToListAsync(ct);
            Dictionary<int, decimal> accepted = acceptedRows.GroupBy(a => a.Line).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
            var claimed = (await uow.Set<PsStatementLine>().AsNoTracking().Where(l => l.OrderLineId != null && l.Statement.PurchaseOrderId != null && ids.Contains(l.Statement.PurchaseOrderId.Value)
                    && !l.Statement.Deleted && l.Statement.Status != PsStatus.Rejected).Select(l => new { Line = l.OrderLineId.Value, l.ThisPeriodAmount }).ToListAsync(ct))
                .GroupBy(l => l.Line).ToDictionary(g => g.Key, g => g.Sum(x => x.ThisPeriodAmount));
            foreach (var o in orders)
            {
                var claims = new List<Claim>();
                foreach (var l in lines.Where(x => x.PurchaseOrderId == o.Id && accepted.ContainsKey(x.Id)))
                {
                    decimal quantity = accepted[l.Id];
                    decimal amount = R(quantity * l.UnitPrice) - (claimed.TryGetValue(l.Id, out decimal c) ? c : 0m);
                    if (amount < 1m) continue;
                    claims.Add(new Claim
                    {
                        Title = (l.Description ?? l.ItemTitle ?? ("ردیف " + l.RowNumber.ToString(CultureInfo.InvariantCulture))) + " (تایید انجام: " + quantity.ToString("0.####", CultureInfo.InvariantCulture) + " از " + l.Quantity.ToString("0.####", CultureInfo.InvariantCulture) + ")",
                        Quantity = quantity, UnitPrice = l.UnitPrice, Amount = amount, OrderLineId = l.Id,
                    });
                }
                if (claims.Count > 0) result[o.Id] = (o.Number, o.ContractId, claims);
            }
            return result;
        }

        /// <summary>
        /// The client contracts that a statement can be drafted for: a contract that is running, whose CBS items sit on WBS nodes with progress. The claim of an item is its earned value (amount x the weighted
        /// progress of its WBS node, as the statement service computes it) less what the earlier statements claimed (not rejected, not returned). A contract with a draft or returned client statement waits for it.
        /// </summary>
        public static async Task<Dictionary<int, (string Number, List<Claim> Claims)>> ClientAsync(IUnitOfWork uow, DateTime today, int? onlyContract, CancellationToken ct)
        {
            var result = new Dictionary<int, (string, List<Claim>)>();
            IQueryable<int> live = ConFacts.Live(uow, today).Select(c => c.Id);
            IQueryable<CtCbsItem> items = uow.Set<CtCbsItem>().AsNoTracking().Where(c => !c.Deleted && c.ContractId != null && c.WbsNodeId != null && c.Amount > 0 && live.Contains(c.ContractId.Value)
                && !uow.Set<PsStatement>().Any(s => s.ContractId == c.ContractId && s.Side == PsSide.Client && !s.Deleted && (s.Status == PsStatus.Draft || s.Status == PsStatus.Returned)));
            if (onlyContract.HasValue) items = items.Where(c => c.ContractId == onlyContract.Value);
            // a bounded number of contracts per pass (the progress of a contract reads its whole WBS)
            int[] contracts = (await items.Select(c => c.ContractId.Value).Distinct().OrderBy(id => id).Take(20).ToListAsync(ct)).ToArray();
            if (contracts.Length == 0) return result;
            var cbs = await uow.Set<CtCbsItem>().AsNoTracking().Where(c => !c.Deleted && c.WbsNodeId != null && c.Amount > 0 && c.ContractId != null && contracts.Contains(c.ContractId.Value)).OrderBy(c => c.Id)
                .Select(c => new { c.Id, ContractId = c.ContractId.Value, c.Code, c.Title, c.Amount, WbsNodeId = c.WbsNodeId.Value }).Take(ProcStepCommon.Cap * 5).ToListAsync(ct);
            var nodes = await uow.Set<PcWbsNode>().AsNoTracking().Where(n => !n.Deleted && contracts.Contains(n.ContractId)).Select(n => new { n.Id, n.ParentId, n.ContractId }).ToListAsync(ct);
            var acts = await uow.Set<PcActivity>().AsNoTracking().Where(a => !a.Deleted && a.WbsNodeId != null && contracts.Contains(a.ContractId)).Select(a => new { WbsNodeId = a.WbsNodeId.Value, a.Weight, a.PercentCache }).ToListAsync(ct);
            var claimed = (await uow.Set<PsStatementLine>().AsNoTracking().Where(l => l.CbsItemId != null && l.Statement.ContractId != null && contracts.Contains(l.Statement.ContractId.Value) && l.Statement.Side == PsSide.Client
                    && !l.Statement.Deleted && l.Statement.Status != PsStatus.Rejected && l.Statement.Status != PsStatus.Returned)
                .Select(l => new { Item = l.CbsItemId.Value, Amount = l.AfterDeliveryQcAmount + l.GoodPerformanceAmount + l.AfterFinalTestsAmount }).ToListAsync(ct))
                .GroupBy(l => l.Item).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
            var contractNumbers = await uow.Set<Entities.Model.NasbNiro.Contracts.ContractEntity>().AsNoTracking().Where(c => contracts.Contains(c.Id)).Select(c => new { c.Id, c.Code }).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            foreach (int contract in contracts)
            {
                var childrenOf = nodes.Where(n => n.ContractId == contract && n.ParentId.HasValue).GroupBy(n => n.ParentId.Value).ToDictionary(g => g.Key, g => g.Select(n => n.Id).ToList());
                var claims = new List<Claim>();
                foreach (var item in cbs.Where(c => c.ContractId == contract))
                {
                    // the subtree of the node, and the weighted progress of its activities (a weight of zero counts as one: the service does the same)
                    var subtree = new HashSet<int> { item.WbsNodeId };
                    var stack = new Stack<int>(subtree);
                    while (stack.Count > 0)
                        if (childrenOf.TryGetValue(stack.Pop(), out List<int> kids))
                            foreach (int k in kids) if (subtree.Add(k)) stack.Push(k);
                    var inTree = acts.Where(a => subtree.Contains(a.WbsNodeId)).ToList();
                    if (inTree.Count == 0) continue;
                    decimal weight = inTree.Sum(a => a.Weight <= 0 ? 1 : a.Weight);
                    decimal progress = Math.Round(inTree.Sum(a => (a.Weight <= 0 ? 1 : a.Weight) * a.PercentCache) / weight, 2);
                    decimal amount = R(item.Amount * progress / 100m) - (claimed.TryGetValue(item.Id, out decimal c) ? c : 0m);
                    if (amount < 1m) continue;
                    claims.Add(new Claim { Title = (item.Code + " " + item.Title).Trim() + " (پیشرفت " + progress.ToString("0.##", CultureInfo.InvariantCulture) + "٪)", Quantity = 1, UnitPrice = item.Amount, Amount = amount, CbsItemId = item.Id, ProgressPercent = progress });
                }
                if (claims.Count > 0) result[contract] = (contractNumbers.TryGetValue(contract, out string code) ? code : contract.ToString(CultureInfo.InvariantCulture), claims);
            }
            return result;
        }
    }

    /// <summary>FIN.STATEMENT.DRAFT: the agent drafts a progress statement from what the system already holds (accepted work of an order, earned value of a client contract), without deductions.</summary>
    internal sealed class FinStatementDraftStep : FinStep
    {
        public override string Code => "FIN.STATEMENT.DRAFT";
        public override string Title => "ایجنت مالی: پیش‌نویس صورت‌وضعیت از کار تایید‌شده یا پیشرفت پروژه";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => FinRoles.Commercial;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var result = new List<AgentWorkItem>();
            foreach (var o in await FinClaims.SupplierAsync(uow, null, ct))
                result.Add(Item("PurchaseOrder", o.Key, o.Value.Number, o.Value.ContractId, null, $"کار تایید‌شده سفارش {o.Value.Number} به مبلغ {Money(o.Value.Claims.Sum(c => c.Amount))} ریال هنوز در صورت‌وضعیتی مطالبه نشده است."));
            foreach (var c in await FinClaims.ClientAsync(uow, today, null, ct))
                result.Add(Item("Contract", c.Key, c.Value.Number, c.Key, null, $"پیشرفت پروژه قرارداد {c.Value.Number} به مبلغ {Money(c.Value.Claims.Sum(x => x.Amount))} ریال هنوز در صورت‌وضعیت کارفرما مطالبه نشده است."));
            return result;
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            bool supplier = item.TargetType == "PurchaseOrder";
            List<FinClaims.Claim> claims;
            var dto = new SaveStatementDto { PeriodTo = DateTime.Today };
            int year = PersianCal.Year(DateTime.Today), month = PersianCal.Month(DateTime.Today);
            dto.PeriodFrom = PersianCal.Start(year, month);
            if (supplier)
            {
                var found = await FinClaims.SupplierAsync(uow, item.TargetId, ct);
                if (!found.TryGetValue(item.TargetId, out var o)) return AgentStepResult.Waiting("کار تایید‌شده‌ای که هنوز مطالبه نشده باشد برای این سفارش نیست.");
                claims = o.Claims;
                dto.Side = PsSide.Supplier; dto.PurchaseOrderId = item.TargetId; dto.ContractId = o.ContractId;
                dto.Subject = "صورت‌وضعیت خودکار سفارش " + o.Number + " بر پایه کار تایید‌شده";
                dto.SupportingDocuments = "تایید انجام کار (مقدار تاییدشده هر ردیف سفارش در سامانه)";
            }
            else
            {
                var found = await FinClaims.ClientAsync(uow, DateTime.Today, item.TargetId, ct);
                if (!found.TryGetValue(item.TargetId, out var c)) return AgentStepResult.Waiting("پیشرفتی که هنوز مطالبه نشده باشد برای این قرارداد نیست.");
                claims = c.Claims;
                dto.Side = PsSide.Client; dto.ContractId = item.TargetId;
                dto.Subject = "صورت‌وضعیت خودکار قرارداد " + c.Number + " بر پایه پیشرفت پروژه";
                dto.SupportingDocuments = "پیشرفت وزنی گره‌های WBS مربوط به بندهای CBS (برنامه پروژه)";
            }
            foreach (FinClaims.Claim c in claims)
                dto.Lines.Add(new SaveStatementLineDto { Title = c.Title, Quantity = c.Quantity, UnitPrice = c.UnitPrice, AfterDeliveryQcAmount = c.Amount, OrderLineId = c.OrderLineId, CbsItemId = c.CbsItemId });
            ResponseDto<StatementDto> saved = await services.GetRequiredService<IProgressStatementService>().SaveStatementAsync(dto, ct);
            if (!saved.Success) return ProcStepCommon.Refusal(saved);
            return AgentStepResult.Done("PsStatement", saved.Data.Id, saved.Data.Number, $"ایجنت مالی صورت‌وضعیت {saved.Data.Number} را با {claims.Count.ToString(CultureInfo.InvariantCulture)} ردیف و مبلغ {Money(saved.Data.GrossThisPeriod)} ریال پیش‌نویس کرد (بدون کسورات؛ تایید با دفتر فنی، نظارت و مدیر پروژه است).");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = FinRoles.Commercial, Link = StatementsLink, Title = "پیش‌نویس صورت‌وضعیت " + item.TargetNumber };
    }

    /// <summary>FIN.STATEMENT.PREPARE: the draft that the agent made is prepared by its creator (the service lets only the creator prepare; this is the maker's own sign-off, the approvals follow).</summary>
    internal sealed class FinStatementPrepareStep : FinStep
    {
        public override string Code => "FIN.STATEMENT.PREPARE";
        public override string Title => "ایجنت مالی: آماده‌سازی صورت‌وضعیت برای بررسی دفتر فنی";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => FinRoles.Commercial;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            int seat = await ProcStepCommon.SeatUserIdAsync(uow, AgentCode, ct);
            if (seat == 0) return new List<AgentWorkItem>();
            List<StatementRow> rows = await StatementsAsync(uow, PsStatus.Draft, s => s.CreatorUserId == seat && s.PayableThisPeriod > 0, ct);
            return rows.Select(s => StatementItem(s, $"پیش‌نویس صورت‌وضعیت به مبلغ قابل پرداخت {Money(s.Payable)} ریال آماده ارسال برای دفتر فنی است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<StatementDto> r = await services.GetRequiredService<IProgressStatementService>().StatementActionAsync(new FlowActionDto { Id = item.TargetId, Action = "prepare" }, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            return AgentStepResult.Done("PsStatement", item.TargetId, item.TargetNumber, $"ایجنت مالی صورت‌وضعیت {item.TargetNumber} را برای بررسی دفتر فنی آماده کرد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = FinRoles.Commercial, Link = StatementLink(item.TargetId), Title = "آماده‌سازی صورت‌وضعیت " + item.TargetNumber };
    }

    /// <summary>FIN.STATEMENT.TECH (gate): the technical office confirms the amount (it may lower it); not the preparer.</summary>
    internal sealed class FinStatementTechnicalStep : FinStep
    {
        public override string Code => "FIN.STATEMENT.TECH";
        public override string Title => "تایید صورت‌وضعیت توسط دفتر فنی";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await StatementsAsync(uow, PsStatus.Prepared, null, ct)).Select(s => StatementItem(s, $"صورت‌وضعیت آماده شده؛ تایید مبلغ با دفتر فنی است (قابل پرداخت {Money(s.Payable)} ریال؛ کسورات قانونی و عدم انطباق را هم ببینید).", FinRoles.Technical)).ToList();

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = FinRoles.Technical, Link = StatementLink(item.TargetId), Title = "تایید دفتر فنی: صورت‌وضعیت " + item.TargetNumber };
    }

    /// <summary>FIN.STATEMENT.SUPERVISION (gate): the supervision confirms the amount; not the people who touched the statement.</summary>
    internal sealed class FinStatementSupervisionStep : FinStep
    {
        public override string Code => "FIN.STATEMENT.SUPERVISION";
        public override string Title => "تایید صورت‌وضعیت توسط نظارت";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await StatementsAsync(uow, PsStatus.TechnicalApproved, null, ct)).Select(s => StatementItem(s, "دفتر فنی صورت‌وضعیت را تایید کرده؛ تایید مبلغ با نظارت است.", FinRoles.Supervision)).ToList();

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = FinRoles.Supervision, Link = StatementLink(item.TargetId), Title = "تایید نظارت: صورت‌وضعیت " + item.TargetNumber };
    }

    /// <summary>FIN.STATEMENT.AUTHORISE (gate): the project manager OF THE CONTRACT authorises the payment (a named person); a contract without a project manager is authorised by the PMO.</summary>
    internal sealed class FinStatementAuthoriseStep : FinStep
    {
        public override string Code => "FIN.STATEMENT.AUTHORISE";
        public override string Title => "مجوز پرداخت صورت‌وضعیت توسط مدیر پروژه";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            List<StatementRow> rows = await StatementsAsync(uow, PsStatus.SupervisionApproved, null, ct);
            int[] contracts = rows.Where(s => s.ContractId.HasValue).Select(s => s.ContractId.Value).Distinct().ToArray();
            Dictionary<int, int> managers = contracts.Length == 0 ? new Dictionary<int, int>()
                : (await uow.Set<PcContractTerms>().AsNoTracking().Where(t => !t.Deleted && t.ProjectManagerUserId != null && contracts.Contains(t.ContractId)).Select(t => new { t.ContractId, Manager = t.ProjectManagerUserId.Value }).ToListAsync(ct))
                    .GroupBy(t => t.ContractId).ToDictionary(g => g.Key, g => g.First().Manager);
            var result = new List<AgentWorkItem>();
            foreach (StatementRow s in rows)
            {
                bool named = s.ContractId.HasValue && managers.ContainsKey(s.ContractId.Value);
                AgentWorkItem item = StatementItem(s, named ? "نظارت صورت‌وضعیت را تایید کرده؛ مجوز پرداخت فقط با مدیر پروژه همین قرارداد است." : "نظارت صورت‌وضعیت را تایید کرده؛ قرارداد مدیر پروژه ندارد و مجوز پرداخت با PMO است.",
                    named ? null : FinRoles.AuthoriseFallback);
                if (named) item.GateUserId = managers[s.ContractId.Value];
                result.Add(item);
            }
            return result;
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo
        {
            Roles = item.GateUserId.HasValue ? null : FinRoles.AuthoriseFallback, UserId = item.GateUserId, Link = StatementLink(item.TargetId), Title = "مجوز پرداخت: صورت‌وضعیت " + item.TargetNumber,
        };
    }

    /// <summary>FIN.STATEMENT.PAYREQUEST: a supplier statement whose payment was authorised becomes a payment request (progress type) through the payables service; its own approvals follow in station 6.</summary>
    internal sealed class FinStatementPayRequestStep : FinStep
    {
        public override string Code => "FIN.STATEMENT.PAYREQUEST";
        public override string Title => "ایجنت مالی: ثبت درخواست پرداخت صورت‌وضعیت مجاز";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => FinRoles.Commercial;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await StatementsAsync(uow, PsStatus.PaymentAuthorised, s => s.Side == PsSide.Supplier && s.PurchaseOrderId != null, ct))
                .Select(s => StatementItem(s, "مدیر پروژه پرداخت را مجاز کرده؛ درخواست پرداخت هنوز ثبت نشده است.")).ToList();

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<StatementDto> r = await services.GetRequiredService<IProgressStatementService>().StatementActionAsync(new FlowActionDto { Id = item.TargetId, Action = "request-payment" }, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            return AgentStepResult.Done("PaymentRequest", r.Data.PaymentRequestId, r.Data.PaymentRequestNumber, $"ایجنت مالی برای صورت‌وضعیت {item.TargetNumber} درخواست پرداخت {r.Data.PaymentRequestNumber} را ثبت کرد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = FinRoles.Commercial, Link = StatementLink(item.TargetId), Title = "درخواست پرداخت صورت‌وضعیت " + item.TargetNumber };
    }

    /// <summary>FIN.STATEMENT.SEND (physical): an authorised client statement goes to the client (a letter or a visit outside the system); the person records the sending date.</summary>
    internal sealed class FinStatementSendStep : FinStep
    {
        public override string Code => "FIN.STATEMENT.SEND";
        public override string Title => "ارسال صورت‌وضعیت به کارفرما";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await StatementsAsync(uow, PsStatus.PaymentAuthorised, s => s.Side == PsSide.Client && s.SendingDate == null, ct))
                .Select(s => StatementItem(s, "صورت‌وضعیت کارفرما تایید و مجاز شده؛ پس از ارسال برای کارفرما تاریخ ارسال ثبت شود.", FinRoles.Commercial)).ToList();

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = FinRoles.Commercial, Link = StatementLink(item.TargetId), Title = "ارسال صورت‌وضعیت " + item.TargetNumber + " به کارفرما" };
    }

    /// <summary>
    /// FIN.STATEMENT.PAID (Input, finance): the payment is recorded against the statement. Input because the method of payment and the cheque number exist only at the treasury or the bank (the payment request
    /// holds a free reference, not the method), and for a client statement the receipt of the money is outside the system. A supplier statement comes here after treasury paid the request.
    /// </summary>
    internal sealed class FinStatementPaidStep : FinStep
    {
        public override string Code => "FIN.STATEMENT.PAID";
        public override string Title => "ثبت پرداخت یا دریافت وجه صورت‌وضعیت";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> paidRequests = uow.Set<PaymentRequest>().AsNoTracking().Where(p => !p.Deleted && p.Status == PaymentRequestStatus.Paid).Select(p => p.Id);
            var result = new List<AgentWorkItem>();
            foreach (StatementRow s in await StatementsAsync(uow, PsStatus.PaymentRequested, s => s.Side == PsSide.Supplier && s.PaymentRequestId != null && paidRequests.Contains(s.PaymentRequestId.Value), ct))
                result.Add(StatementItem(s, "خزانه درخواست پرداخت را پرداخت کرده؛ روش پرداخت، شماره چک (در صورت چک) و مبلغ پرداخت‌شده برای صورت‌وضعیت ثبت شود.", FinRoles.Payment));
            foreach (StatementRow s in await StatementsAsync(uow, PsStatus.PaymentAuthorised, s => s.Side == PsSide.Client && s.SendingDate != null, ct))
                result.Add(StatementItem(s, "صورت‌وضعیت برای کارفرما ارسال شده؛ پس از دریافت وجه، روش و مبلغ دریافتی (و شماره چک) ثبت شود.", FinRoles.Payment));
            return result;
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = FinRoles.Payment, Link = StatementLink(item.TargetId), Title = "ثبت پرداخت صورت‌وضعیت " + item.TargetNumber };
    }

    /// <summary>
    /// FIN.STATEMENT.FIX (Input): a returned statement is corrected by its creator or by a PMO who also holds the commercial role (the service lets nobody else edit it). The creator here is the agent, which
    /// cannot know what the person who returned it wanted (a deduction, a different amount): the return reason is on the task and the person edits and prepares it.
    /// </summary>
    internal sealed class FinStatementFixStep : FinStep
    {
        public override string Code => "FIN.STATEMENT.FIX";
        public override string Title => "اصلاح صورت‌وضعیت برگشت‌خورده";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<PsStatement>().AsNoTracking().Where(s => !s.Deleted && s.Status == PsStatus.Returned).OrderBy(s => s.Id).Take(ProcStepCommon.Cap).Select(s => new { s.Id, s.Number, s.ContractId, s.ReturnReason }).ToListAsync(ct);
            return rows.Select(s => Item("PsStatement", s.Id, s.Number, s.ContractId, null, "صورت‌وضعیت برگشت خورده است؛ علت: " + AgentText.Cut(s.ReturnReason, 500) + " (ویرایش با تهیه‌کننده یا PMO دارای نقش بازرگانی است).", FinRoles.Fix)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = FinRoles.Fix, Link = StatementLink(item.TargetId), Title = "اصلاح صورت‌وضعیت برگشتی " + item.TargetNumber };
    }

    /// <summary>FIN.INVOICE.APPROVE (gate): the sales invoice that the milestone job drafted is approved by an accountant who did not make or edit it.</summary>
    internal sealed class FinInvoiceApproveStep : FinStep
    {
        public override string Code => "FIN.INVOICE.APPROVE";
        public override string Title => "تایید فاکتور فروش پیش‌نویس (مرحله پرداخت قرارداد)";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => i.Status == FnDocStatus.Draft).OrderBy(i => i.Id).Take(ProcStepCommon.Cap)
                .Select(i => new { i.Id, i.Number, i.ContractId, i.GrossAmount }).ToListAsync(ct);
            return rows.Select(i => Item("FnSalesInvoice", i.Id, i.Number, i.ContractId, null, $"فاکتور فروش پیش‌نویس به مبلغ {Money(i.GrossAmount)} ریال منتظر تایید است (تاییدکننده غیر از ثبت‌کننده و ویرایشگر).", FinRoles.Invoice)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = FinRoles.Invoice, Link = InvoicesLink, Title = "تایید فاکتور فروش " + item.TargetNumber };
    }

    /// <summary>The closed months that have posted sales invoices and no tax package.</summary>
    internal static class FinPeriods
    {
        /// <summary>How many closed Persian months back the agent looks.</summary>
        public const int MonthsBack = 3;

        public static async Task<List<(int Year, int Month)>> MissingAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var result = new List<(int, int)>();
            int current = PersianCal.Index(today);
            for (int back = 1; back <= MonthsBack; back++)
            {
                int index = current - back;
                int year = PersianCal.YearOfIndex(index), month = PersianCal.MonthOfIndex(index);
                DateTime from = PersianCal.Start(year, month), to = PersianCal.End(year, month);
                // a package of any state (also a rejected one) means the period was handled: the agent never replaces a package
                bool has = await uow.Set<FnOutboxPackage>().AsNoTracking().AnyAsync(p => p.Kind == FnOutboxKind.SalesInvoicesTaxpayer && p.Year == year && p.Month == month && p.Status != FnOutboxStatus.Superseded, ct);
                if (has) continue;
                bool posted = await uow.Set<FnSalesInvoice>().AsNoTracking().AnyAsync(i => i.Status == FnDocStatus.Posted && i.Date >= from && i.Date <= to, ct);
                if (posted) result.Add((year, month));
            }
            return result;
        }
    }

    /// <summary>FIN.SEPIDAR.GENERATE: a closed month with posted sales invoices and no package gets its tax package drafted.</summary>
    internal sealed class FinSepidarGenerateStep : FinStep
    {
        public override string Code => "FIN.SEPIDAR.GENERATE";
        public override string Title => "ایجنت مالی: تهیه بسته فاکتورهای فروش ماه برای سپیدار";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => FinRoles.PackagePrepare;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await FinPeriods.MissingAsync(uow, today, ct)).Select(p => Item("OutboxPeriod", p.Year * 100 + p.Month, p.Year.ToString("0000", CultureInfo.InvariantCulture) + "/" + p.Month.ToString("00", CultureInfo.InvariantCulture), null, null,
                "فاکتور فروش ثبت‌شده در این ماه هست ولی بسته‌ای برای سپیدار تهیه نشده است.")).ToList();

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            int year = item.TargetId / 100, month = item.TargetId % 100;
            ResponseDto<OutboxPackageDto> r = await services.GetRequiredService<ISepidarOutboxService>().GenerateAsync(new GenerateOutboxDto { Kind = FnOutboxKind.SalesInvoicesTaxpayer, Year = year, Month = month }, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            return AgentStepResult.Done("FnOutboxPackage", r.Data.Id, r.Data.Number, $"ایجنت مالی بسته {r.Data.Number} را برای فاکتورهای فروش {item.TargetNumber} با {r.Data.RowCount.ToString(CultureInfo.InvariantCulture)} ردیف تهیه کرد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = FinRoles.PackagePrepare, Link = SepidarLink, Title = "تهیه بسته سپیدار ماه " + item.TargetNumber };
    }

    internal abstract class FinPackageStep : FinStep
    {
        protected static bool IsPayroll(FnOutboxKind kind) => kind == FnOutboxKind.PayrollInsuranceList || kind == FnOutboxKind.PayrollTaxList;

        /// <summary>Who prepares, sends and records the package: the accountants for a tax package, the payroll users for a payroll list (SepidarOutboxService.CanPrepare).</summary>
        protected static string Prepare(FnOutboxKind kind) => IsPayroll(kind) ? FinRoles.PayrollPrepare : FinRoles.PackagePrepare;

        protected static async Task<List<AgentWorkItem>> PackagesAsync(IUnitOfWork uow, FnOutboxStatus[] statuses, string summary, Func<FnOutboxKind, string> roles, CancellationToken ct)
        {
            var rows = await uow.Set<FnOutboxPackage>().AsNoTracking().Where(p => statuses.Contains(p.Status)).OrderBy(p => p.Id).Take(ProcStepCommon.Cap).Select(p => new { p.Id, p.Number, p.Kind }).ToListAsync(ct);
            return rows.Select(p => Item("FnOutboxPackage", p.Id, p.Number, null, null, summary, roles(p.Kind))).ToList();
        }
    }

    /// <summary>FIN.SEPIDAR.APPROVE (gate): the finance manager (or the CEO) approves a package; not the one who made it (the service refuses).</summary>
    internal sealed class FinSepidarApproveStep : FinPackageStep
    {
        public override string Code => "FIN.SEPIDAR.APPROVE";
        public override string Title => "تایید بسته سپیدار";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            PackagesAsync(uow, new[] { FnOutboxStatus.Draft }, "بسته پیش‌نویس تهیه شده؛ تایید با مدیر مالی یا مدیرعامل است و تهیه‌کننده نمی‌تواند آن را تایید کند.", k => FinRoles.PackageApprove, ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = FinRoles.PackageApprove, Link = SepidarLink, Title = "تایید بسته " + item.TargetNumber };
    }

    /// <summary>FIN.SEPIDAR.HANDOVER (physical): an approved package is handed to Sepidar (the file is imported there, outside the system); the person records the reference and a note.</summary>
    internal sealed class FinSepidarHandoverStep : FinPackageStep
    {
        public override string Code => "FIN.SEPIDAR.HANDOVER";
        public override string Title => "تحویل بسته به سپیدار";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            PackagesAsync(uow, new[] { FnOutboxStatus.Ready, FnOutboxStatus.Exported }, "بسته تایید شده؛ فایل را دریافت و در سپیدار وارد کنید و سپس شماره پیگیری و یادداشت ارسال را ثبت کنید.", Prepare, ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = FinRoles.PackagePrepare + "," + FinRoles.PayrollPrepare, Link = SepidarLink, Title = "تحویل بسته " + item.TargetNumber + " به سپیدار" };
    }

    /// <summary>FIN.SEPIDAR.RESULT (physical): the answer for a delivered package (accepted or rejected, with a note) exists only in Sepidar or at the office; the person records it.</summary>
    internal sealed class FinSepidarResultStep : FinPackageStep
    {
        public override string Code => "FIN.SEPIDAR.RESULT";
        public override string Title => "ثبت نتیجه بسته در سپیدار";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            PackagesAsync(uow, new[] { FnOutboxStatus.Sent }, "بسته به سپیدار تحویل شده؛ پس از پاسخ، پذیرفته‌شدن یا ردشدن (با یادداشت) ثبت شود.", Prepare, ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = FinRoles.PackagePrepare + "," + FinRoles.PayrollPrepare, Link = SepidarLink, Title = "نتیجه بسته " + item.TargetNumber };
    }
}
