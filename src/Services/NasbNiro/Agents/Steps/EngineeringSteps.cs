using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.ProductBank;
using ViewModels.NasbNiro.ProductBankDtos;

namespace Services.NasbNiro.Agents.Steps
{
    // Station 5 (engineering and the product bank). A repeat product (the equipment of the work order has a product with a released BOM) is instantiated by the engineering agent: the baseline, the exploded project BOM,
    // the approved routings and the WBS come out of the bank without anybody typing. A NEW product has no design yet: the BOM lines and the routing (OPC) steps are engineering content that exists only in the engineers'
    // drawings and heads, so those two are Input tasks that say exactly what is missing. People sign the four BOM signatures, release the BOM, check and approve the routing and publish it to the shop floor system.
    //
    // ENG.BOM.REVISION (an agent copying a released BOM into a new revision) is deliberately NOT a step: a revision exists to carry a design change, the revision of an approved engineering change order names a draft
    // that is already edited by its preparer, and a draft made by the agent could be edited, submitted or deleted by nobody else (the service lets only the preparer do it). The change flow has its own screens and inbox.

    public static class EngineeringSteps
    {
        public static IServiceCollection AddEngineeringSteps(this IServiceCollection services)
        {
            services.AddScoped<IAgentStep, EngInstantiateStep>();
            services.AddScoped<IAgentStep, EngBomInputStep>();
            services.AddScoped<IAgentStep, EngOpcInputStep>();
            services.AddScoped<IAgentStep, EngBomSignStep>();
            services.AddScoped<IAgentStep, EngBomReleaseStep>();
            services.AddScoped<IAgentStep, EngOpcCheckStep>();
            services.AddScoped<IAgentStep, EngOpcApproveStep>();
            services.AddScoped<IAgentStep, EngOpcPublishStep>();
            return services;
        }
    }

    /// <summary>The roles of the actions, taken from the services themselves (the tests compare them with the actions).</summary>
    internal static class EngRoles
    {
        public const string Writers = PbRoles.Writers;
        public const string Release = PbRoles.ReleaseRoles;
        public const string OpcCheck = PbRoles.CheckRoles;
        public const string OpcApprove = PbRoles.ApproveRoles;
        public const string Publish = PbRoutingService.PublishRoles;
    }

    internal abstract class EngStep : ProcStep, IOfficeDeadlineStep
    {
        /// <summary>The older engineering steps are engineering work too: a waiting or failed task of them is under the 72 office hour rule (a pure agent step is not affected while it runs).</summary>
        public virtual int OfficeDeadlineHours => global::Services.NasbNiro.Office.OfficeClock.StopLimitHours;

        public override int Station => 5;
        public override string AgentCode => AgentCodes.Eng;

        protected const string ProductsLink = "/product-bank/products";
        protected const string BomsLink = "/product-bank/boms";
        protected const string RoutingsLink = "/product-bank/routings";

        protected static string BomLink(int id) => BomsLink + "/" + id.ToString(CultureInfo.InvariantCulture);
        protected static string RoutingLink(int id) => RoutingsLink + "/" + id.ToString(CultureInfo.InvariantCulture);
        protected static string ProductLink(int id) => ProductsLink + "/" + id.ToString(CultureInfo.InvariantCulture);
        protected static string Rev(int revision) => revision.ToString("00", CultureInfo.InvariantCulture);

        /// <summary>The ids of the contracts that passed the contract review and are live (the agents do not start old contracts).</summary>
        protected static IQueryable<int> LiveAccepted(IUnitOfWork uow, DateTime today)
        {
            IQueryable<int> accepted = ConFacts.AcceptedContracts(uow);
            return ConFacts.Live(uow, today).Where(c => accepted.Contains(c.Id)).Select(c => c.Id);
        }

        /// <summary>The work orders of live accepted contracts that have no current baseline yet.</summary>
        protected static IQueryable<WorkOrder> AwaitingBaseline(IUnitOfWork uow, DateTime today)
        {
            IQueryable<int> contracts = LiveAccepted(uow, today);
            IQueryable<int> baselined = uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => !b.Deleted && b.IsCurrent).Select(b => b.WorkOrderId);
            return uow.Set<WorkOrder>().AsNoTracking().Where(w => !w.Deleted && contracts.Contains(w.ContractId) && !baselined.Contains(w.Id));
        }

        protected static async Task<Dictionary<int, string>> ContractCodesAsync(IUnitOfWork uow, DateTime today, IEnumerable<int> ids, CancellationToken ct)
        {
            int[] list = ids.Distinct().Take(ProcStepCommon.Cap).ToArray();
            if (list.Length == 0) return new Dictionary<int, string>();
            return (await ConFacts.Live(uow, today).Where(c => list.Contains(c.Id)).Select(c => new { c.Id, c.Code }).ToListAsync(ct)).ToDictionary(c => c.Id, c => c.Code);
        }
    }

    /// <summary>ENG.INSTANTIATE (Agent): the baseline of a work order whose equipment is a product with a released BOM (a repeat product). The service applies every rule (plan lock, readiness gate).</summary>
    internal sealed class EngInstantiateStep : EngStep
    {
        public override string Code => "ENG.INSTANTIATE";
        public override string Title => "ایجنت مهندسی: پایه‌ریزی دستور کار از بانک محصول (BOM منتشرشده)";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => PbRoles.Requesters;

        private static IQueryable<PbProduct> RepeatProducts(IUnitOfWork uow) =>
            uow.Set<PbProduct>().AsNoTracking().Where(p => !p.Deleted && p.IsActive && uow.Set<PbBomHeader>().Any(h => !h.Deleted && h.ProductId == p.Id && h.Status == PbBomStatus.Released));

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<PbProduct> products = RepeatProducts(uow);
            var rows = await (from w in AwaitingBaseline(uow, today)
                              from p in products
                              where p.EquipmentId == w.EquipmentId
                              orderby w.Id, p.Id
                              select new { w.Id, w.Code, w.ContractId, ProductId = p.Id, ProductCode = p.Code }).Take(ProcStepCommon.Cap).ToListAsync(ct);
            Dictionary<int, string> codes = await ContractCodesAsync(uow, today, rows.Select(r => r.ContractId), ct);
            return rows.GroupBy(r => r.Id).Select(g => g.First()).Where(r => codes.ContainsKey(r.ContractId)).Select(r =>
                Item("WorkOrder", r.Id, r.Code, r.ContractId, r.Id, $"محصول {r.ProductCode} BOM منتشرشده دارد؛ دستور کار {r.Code} قرارداد {codes[r.ContractId]} هنوز پایه‌ریزی نشده است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            int equipment = await uow.Set<WorkOrder>().AsNoTracking().Where(w => w.Id == item.TargetId && !w.Deleted).Select(w => w.EquipmentId).FirstOrDefaultAsync(ct);
            var product = await RepeatProducts(uow).Where(p => p.EquipmentId == equipment).OrderBy(p => p.Id).Select(p => new { p.Id, p.Code }).FirstOrDefaultAsync(ct);
            if (product == null) return AgentStepResult.Waiting("محصولی با BOM منتشرشده برای تجهیز این دستور کار نیست.");
            int contractId = await uow.Set<WorkOrder>().AsNoTracking().Where(w => w.Id == item.TargetId).Select(w => w.ContractId).FirstOrDefaultAsync(ct);
            string held = await global::Services.NasbNiro.Engineering.EnGates.InstantiateRefusalAsync(uow, contractId, ct);   // ENG-E1: waits for the handover accepted by the PMO
            if (held != null) return AgentStepResult.Waiting(held);
            ResponseDto<PbBaselineDto> response = await services.GetRequiredService<IPbProductService>().InstantiateAsync(new PbInstantiateDto { WorkOrderId = item.TargetId, ProductId = product.Id }, ct);
            if (!response.Success) return ProcStepCommon.Refusal(response);
            return AgentStepResult.Done("PbProjectBaseline", response.Data.Id, item.TargetNumber, $"ایجنت مهندسی دستور کار {item.TargetNumber} را از BOM منتشرشدهٔ محصول {product.Code} پایه‌ریزی کرد (BOM پروژه، مسیرهای ساخت تاییدشده و WBS از بانک آمد).");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = PbRoles.Requesters, Link = ProductsLink, Title = "پایه‌ریزی دستور کار " + item.TargetNumber + " از بانک محصول" };
    }

    /// <summary>
    /// ENG.BOM.INPUT (Input): the work order has no released BOM to be instantiated from and none is in the signature flow. Input because the bill of materials of a new product (its parts, quantities, make or buy)
    /// is design content that exists only in the engineers' drawings; the service also lets only the preparer edit and send a draft. The task also covers a draft that was never sent for review.
    /// </summary>
    internal sealed class EngBomInputStep : EngStep
    {
        public override string Code => "ENG.BOM.INPUT";
        public override string Title => "تهیه BOM محصول جدید (محتوای طراحی)";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => EngRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await AwaitingBaseline(uow, today)
                .Where(w => !uow.Set<PbBomHeader>().Any(h => !h.Deleted && h.Product.EquipmentId == w.EquipmentId && (h.Status == PbBomStatus.Released || h.Status == PbBomStatus.UnderReview || h.Status == PbBomStatus.Approved)))
                .OrderBy(w => w.Id).Take(ProcStepCommon.Cap)
                .Select(w => new
                {
                    w.Id, w.Code, w.Title, w.ContractId,
                    ProductId = uow.Set<PbProduct>().Where(p => !p.Deleted && p.IsActive && p.EquipmentId == w.EquipmentId).OrderBy(p => p.Id).Select(p => (int?)p.Id).FirstOrDefault(),
                    HasDraft = uow.Set<PbBomHeader>().Any(h => !h.Deleted && h.Status == PbBomStatus.Draft && h.Product.EquipmentId == w.EquipmentId),
                }).ToListAsync(ct);
            Dictionary<int, string> codes = await ContractCodesAsync(uow, today, rows.Select(r => r.ContractId), ct);
            return rows.Where(r => codes.ContainsKey(r.ContractId)).Select(r =>
            {
                string what = !r.ProductId.HasValue
                    ? "محصولی برای تجهیز این دستور کار در بانک محصول ثبت نشده است؛ محصول (شماره قطعه مجموعه) را ثبت کنید و فهرست قطعات (BOM) را بنویسید و برای بررسی بفرستید."
                    : r.HasDraft ? "پیش‌نویس BOM هست ولی برای بررسی فرستاده نشده؛ آن را کامل کنید و برای امضاها بفرستید."
                    : "برای محصول BOM ثبت نشده است؛ فهرست قطعات را بنویسید و برای بررسی بفرستید.";
                return Item("WorkOrder", r.Id, r.Code, r.ContractId, r.Id, $"دستور کار {r.Code} ({r.Title}) قرارداد {codes[r.ContractId]} محصول تازه است و BOM منتشرشده ندارد. " + what);
            }).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EngRoles.Writers, Link = BomsLink, Title = "تهیه BOM برای دستور کار " + item.TargetNumber };
    }

    /// <summary>
    /// ENG.OPC.INPUT (Input): the product has a released BOM but no routing chart in review or approved. Input because the operations, the work centres and the standard hours of a new product are engineering
    /// content that exists only in the process engineer's plan; the service lets only the preparer edit and send a draft.
    /// </summary>
    internal sealed class EngOpcInputStep : EngStep
    {
        public override string Code => "ENG.OPC.INPUT";
        public override string Title => "تهیه چارت مسیر ساخت (OPC) محصول جدید (محتوای مهندسی)";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => EngRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> contracts = LiveAccepted(uow, today);
            var rows = await (from p in uow.Set<PbProduct>().AsNoTracking()
                              where !p.Deleted && p.IsActive
                                    && uow.Set<PbBomHeader>().Any(h => !h.Deleted && h.ProductId == p.Id && h.Status == PbBomStatus.Released)
                                    && !uow.Set<PbOpcRouting>().Any(r => !r.Deleted && r.ProductId == p.Id && (r.Status == PbRoutingStatus.UnderReview || r.Status == PbRoutingStatus.Approved || r.Status == PbRoutingStatus.Superseded))
                                    && uow.Set<WorkOrder>().Any(w => !w.Deleted && w.EquipmentId == p.EquipmentId && contracts.Contains(w.ContractId))
                              orderby p.Id
                              select new
                              {
                                  p.Id, p.Code, p.Title,
                                  HasDraft = uow.Set<PbOpcRouting>().Any(r => !r.Deleted && r.ProductId == p.Id && r.Status == PbRoutingStatus.Draft),
                                  ContractId = uow.Set<WorkOrder>().Where(w => !w.Deleted && w.EquipmentId == p.EquipmentId && contracts.Contains(w.ContractId)).OrderBy(w => w.Id).Select(w => (int?)w.ContractId).FirstOrDefault(),
                              }).Take(ProcStepCommon.Cap).ToListAsync(ct);
            return rows.Select(r => Item("PbProduct", r.Id, r.Code, r.ContractId, null,
                $"محصول {r.Code} ({r.Title}) BOM منتشرشده دارد ولی چارت مسیر ساخت (OPC) تاییدشده یا در بررسی ندارد. "
                + (r.HasDraft ? "پیش‌نویس چارت هست ولی برای بررسی فرستاده نشده؛ مراحل و زمان استاندارد را کامل کنید و بفرستید."
                              : "مراحل ساخت هر قطعه (ایستگاه، مرکز کار، زمان استاندارد، نقطه کنترل کیفیت) را بنویسید و برای بررسی بفرستید."))).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EngRoles.Writers, Link = item.TargetId > 0 ? ProductLink(item.TargetId) : RoutingsLink, Title = "تهیه چارت OPC محصول " + item.TargetNumber };
    }

    /// <summary>ENG.BOM.SIGN (gate): the four signatures of a BOM under review, each by a different person; the task lists the roles of the signatures that are still open (the service decides the turn and the distinct persons).</summary>
    internal sealed class EngBomSignStep : EngStep
    {
        public override string Code => "ENG.BOM.SIGN";
        public override string Title => "امضاهای چهارگانه BOM (کنترل، کیفیت، ساخت، تایید)";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<PbBomHeader>().AsNoTracking().Where(h => !h.Deleted && h.Status == PbBomStatus.UnderReview).OrderBy(h => h.Id).Take(ProcStepCommon.Cap)
                .Select(h => new { h.Id, h.Code, h.Revision, Checked = h.CheckedAt != null, Qc = h.QcCheckedAt != null, Manufacturing = h.ManufacturingCheckedAt != null, Approved = h.ApprovedAt != null }).ToListAsync(ct);
            return rows.Select(r =>
            {
                var open = new List<(string Title, string Roles)>();
                if (!r.Checked) open.Add(("کنترل", PbBomService.StepRoles(PbBomSignature.Checked)));
                if (!r.Qc) open.Add(("کنترل کیفیت", PbBomService.StepRoles(PbBomSignature.QcChecked)));
                if (!r.Manufacturing) open.Add(("کنترل ساخت", PbBomService.StepRoles(PbBomSignature.ManufacturingChecked)));
                if (!r.Approved) open.Add(("تایید", PbBomService.StepRoles(PbBomSignature.Approved)));
                return Item("PbBomHeader", r.Id, $"{r.Code} ({Rev(r.Revision)})", null, null,
                    "BOM در حال بررسی است؛ هر امضا را یک نفر متفاوت می‌زند. امضای باقی‌مانده: " + string.Join("، ", open.Select(o => o.Title)) + ".",
                    ProcStepCommon.Roles(open.Select(o => o.Roles).ToArray()));
            }).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = item.GateRoles ?? PbRoles.Reviewers, Link = BomLink(item.TargetId), Title = "امضای BOM " + item.TargetNumber };
    }

    /// <summary>ENG.BOM.RELEASE (gate): the approved BOM is released by engineering or the PMO (never its preparer, never one of the four signers).</summary>
    internal sealed class EngBomReleaseStep : EngStep
    {
        public override string Code => "ENG.BOM.RELEASE";
        public override string Title => "انتشار BOM تاییدشده";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await uow.Set<PbBomHeader>().AsNoTracking().Where(h => !h.Deleted && h.Status == PbBomStatus.Approved).OrderBy(h => h.Id).Take(ProcStepCommon.Cap).Select(h => new { h.Id, h.Code, h.Revision }).ToListAsync(ct))
                .Select(h => Item("PbBomHeader", h.Id, $"{h.Code} ({Rev(h.Revision)})", null, null, "هر چهار امضا زده شده؛ BOM منتظر انتشار است (پس از انتشار پایه‌ریزی دستورکارها ممکن می‌شود).", EngRoles.Release)).ToList();

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EngRoles.Release, Link = BomLink(item.TargetId), Title = "انتشار BOM " + item.TargetNumber };
    }

    /// <summary>ENG.OPC.CHECK (gate): the routing chart under review is checked by an engineer who is not its preparer.</summary>
    internal sealed class EngOpcCheckStep : EngStep
    {
        public override string Code => "ENG.OPC.CHECK";
        public override string Title => "کنترل چارت مسیر ساخت (OPC)";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await uow.Set<PbOpcRouting>().AsNoTracking().Where(r => !r.Deleted && r.Status == PbRoutingStatus.UnderReview && r.CheckedAt == null).OrderBy(r => r.Id).Take(ProcStepCommon.Cap).Select(r => new { r.Id, r.PartNumber, r.Revision }).ToListAsync(ct))
                .Select(r => Item("PbOpcRouting", r.Id, $"{r.PartNumber} ({Rev(r.Revision)})", null, null, "چارت مسیر ساخت برای بررسی فرستاده شده و منتظر کنترل است.", EngRoles.OpcCheck)).ToList();

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EngRoles.OpcCheck, Link = RoutingLink(item.TargetId), Title = "کنترل چارت OPC " + item.TargetNumber };
    }

    /// <summary>ENG.OPC.APPROVE (gate): the checked chart is approved by a person who neither prepared nor checked it.</summary>
    internal sealed class EngOpcApproveStep : EngStep
    {
        public override string Code => "ENG.OPC.APPROVE";
        public override string Title => "تایید چارت مسیر ساخت (OPC)";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await uow.Set<PbOpcRouting>().AsNoTracking().Where(r => !r.Deleted && r.Status == PbRoutingStatus.UnderReview && r.CheckedAt != null).OrderBy(r => r.Id).Take(ProcStepCommon.Cap).Select(r => new { r.Id, r.PartNumber, r.Revision }).ToListAsync(ct))
                .Select(r => Item("PbOpcRouting", r.Id, $"{r.PartNumber} ({Rev(r.Revision)})", null, null, "چارت مسیر ساخت کنترل شده و منتظر تایید است.", EngRoles.OpcApprove)).ToList();

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EngRoles.OpcApprove, Link = RoutingLink(item.TargetId), Title = "تایید چارت OPC " + item.TargetNumber };
    }

    /// <summary>ENG.OPC.PUBLISH (gate): the approved chart is published to the shop floor system (the MES routing stays a draft there until its own approvals).</summary>
    internal sealed class EngOpcPublishStep : EngStep
    {
        public override string Code => "ENG.OPC.PUBLISH";
        public override string Title => "انتقال چارت تاییدشده به مسیر ساخت (MES)";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await uow.Set<PbOpcRouting>().AsNoTracking().Where(r => !r.Deleted && r.Status == PbRoutingStatus.Approved && r.PublishedToMesAt == null).OrderBy(r => r.Id).Take(ProcStepCommon.Cap).Select(r => new { r.Id, r.PartNumber, r.Revision }).ToListAsync(ct))
                .Select(r => Item("PbOpcRouting", r.Id, $"{r.PartNumber} ({Rev(r.Revision)})", null, null, "چارت تایید شده است و هنوز به مسیر ساخت (MES) منتقل نشده است.", EngRoles.Publish)).ToList();

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EngRoles.Publish, Link = RoutingLink(item.TargetId), Title = "انتقال چارت OPC " + item.TargetNumber + " به MES" };
    }
}
