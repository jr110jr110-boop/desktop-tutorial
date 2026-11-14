using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.ProcurementDtos;

namespace Services.NasbNiro.Agents.Steps
{
    // The order: drafted from the approved inquiry by the procurement agent, completed by the buyer where the service asks for more, approved by the PMO deputy or the CEO, released by the buyer,
    // received by the warehouse (goods) or accepted by the unit (services).

    internal static class OrderText
    {
        public static string Link(AgentWorkItem item) => "/procurement/orders/" + item.TargetId.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>PROC.PO.DRAFT: an approved inquiry that has no live order gets its draft order from the procurement agent.</summary>
    internal sealed class DraftOrderStep : ProcStep
    {
        public override string Code => "PROC.PO.DRAFT";
        public override string AgentCode => AgentCodes.Proc;
        public override string Title => "ایجنت تدارکات: تهیه پیش‌نویس سفارش خرید از استعلام تاییدشده";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => ProcurementRoles.Commercial;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<PurchaseOrder> orders = uow.Set<PurchaseOrder>().AsNoTracking();
            var rows = await uow.Set<PurchaseInquiry>().AsNoTracking()
                .Where(i => !i.Deleted && i.Status == InquiryStatus.Approved && !orders.Any(o => o.InquiryId == i.Id && !o.Deleted && o.Status != PurchaseOrderStatus.Cancelled))
                .OrderBy(i => i.Id).Take(ProcStepCommon.Cap).Select(i => new { i.Id, i.Number, ContractId = i.PurchaseRequest.ContractId, i.PurchaseRequest.WorkOrderId }).ToListAsync(ct);
            return rows.Select(r => Item("PurchaseInquiry", r.Id, r.Number, r.ContractId, r.WorkOrderId, "انتخاب برنده تایید شده و سفارشی از این استعلام صادر نشده است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<PurchaseOrderDto> order = await services.GetRequiredService<IProcurementService>().CreateOrderFromInquiryAsync(item.TargetId, ct);
            if (!order.Success) return ProcStepCommon.Refusal(order);
            return AgentStepResult.Done("PurchaseOrder", order.Data.Id, order.Data.Number, $"ایجنت تدارکات پیش‌نویس سفارش {order.Data.Number} را از استعلام {item.TargetNumber} تهیه کرد؛ تکمیل و تایید با انسان است.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = InquiryText.Link(item), Title = "تهیه سفارش خرید از استعلام " + item.TargetNumber };
    }

    internal static class OrderRules
    {
        /// <summary>What a draft order still needs before it can be approved (the order action of ProcurementService): the technical requirements of a non-service order and the signed contract from the contract limit.</summary>
        public static string Missing(PurchaseKind kind, string requirements, decimal total, string contractNumber, DateTime? signedAt, decimal contractFrom)
        {
            var missing = new List<string>();
            if (kind != PurchaseKind.Service && requirements == null) missing.Add("الزامات فنی و کیفی سفارش (مشخصات، گواهی مواد، بازرسی)");
            if (total >= contractFrom && (contractNumber == null || !signedAt.HasValue)) missing.Add("شماره و تاریخ قرارداد امضاشده (مبلغ از سقف قرارداد بالاتر است)");
            return missing.Count == 0 ? null : string.Join(" و ", missing);
        }
    }

    /// <summary>PROC.PO.COMPLETE: the buyer adds what the draft order lacks (requirements, the signed contract) so that the PMO deputy can approve it.</summary>
    internal sealed class CompleteOrderStep : ProcStep
    {
        public override string Code => "PROC.PO.COMPLETE";
        public override string AgentCode => AgentCodes.Proc;
        public override string Title => "تکمیل سفارش خرید (الزامات فنی یا قرارداد)";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            decimal contractFrom = (await ProcStepCommon.SettingsAsync(uow, ct))[ProcurementSettingKeys.ContractFrom];
            var rows = await uow.Set<PurchaseOrder>().AsNoTracking()
                .Where(o => !o.Deleted && o.Status == PurchaseOrderStatus.Draft
                            && ((o.Kind != PurchaseKind.Service && o.Requirements == null) || (o.TotalAmount >= contractFrom && (o.ContractNumber == null || o.ContractSignedAt == null))))
                .OrderBy(o => o.Id).Take(ProcStepCommon.Cap).Select(o => new { o.Id, o.Number, o.ContractId, o.WorkOrderId, o.Kind, o.Requirements, o.TotalAmount, o.ContractNumber, o.ContractSignedAt }).ToListAsync(ct);
            return rows.Select(o => Item("PurchaseOrder", o.Id, o.Number, o.ContractId, o.WorkOrderId, "کم است: " + OrderRules.Missing(o.Kind, o.Requirements, o.TotalAmount, o.ContractNumber, o.ContractSignedAt, contractFrom))).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ProcurementRoles.Commercial, Link = OrderText.Link(item), Title = "تکمیل سفارش خرید " + item.TargetNumber };
    }

    /// <summary>PROC.PO.APPROVE: the PMO deputy or the CEO approves a complete draft order (never the buyer or the last editor).</summary>
    internal sealed class ApproveOrderStep : ProcStep
    {
        public override string Code => "PROC.PO.APPROVE";
        public override string AgentCode => AgentCodes.Proc;
        public override string Title => "تایید سفارش خرید";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            decimal contractFrom = (await ProcStepCommon.SettingsAsync(uow, ct))[ProcurementSettingKeys.ContractFrom];
            var rows = await uow.Set<PurchaseOrder>().AsNoTracking()
                .Where(o => !o.Deleted && o.Status == PurchaseOrderStatus.Draft
                            && (o.Kind == PurchaseKind.Service || o.Requirements != null) && (o.TotalAmount < contractFrom || (o.ContractNumber != null && o.ContractSignedAt != null)))
                .OrderBy(o => o.Id).Take(ProcStepCommon.Cap).Select(o => new { o.Id, o.Number, o.ContractId, o.WorkOrderId, o.TotalAmount }).ToListAsync(ct);
            return rows.Select(o => Item("PurchaseOrder", o.Id, o.Number, o.ContractId, o.WorkOrderId, $"سفارش خرید به مبلغ {ProcStepCommon.Num(o.TotalAmount)} ریال کامل است و منتظر تایید است.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ProcurementRoles.OrderApproval, Link = OrderText.Link(item), Title = "تایید سفارش خرید " + item.TargetNumber, HasOfficeInboxItem = true };
    }

    /// <summary>PROC.PO.RELEASE: the buyer sends the approved order to the supplier and records the notice (a physical act).</summary>
    internal sealed class ReleaseOrderStep : ProcStep
    {
        public override string Code => "PROC.PO.RELEASE";
        public override string AgentCode => AgentCodes.Proc;
        public override string Title => "ارسال سفارش به تامین‌کننده و ثبت ابلاغ";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<PurchaseOrder>().AsNoTracking().Where(o => !o.Deleted && o.Status == PurchaseOrderStatus.Approved)
                .OrderBy(o => o.Id).Take(ProcStepCommon.Cap).Select(o => new { o.Id, o.Number, o.ContractId, o.WorkOrderId }).ToListAsync(ct);
            return rows.Select(o => Item("PurchaseOrder", o.Id, o.Number, o.ContractId, o.WorkOrderId, "سفارش تایید شده است؛ آن را به تامین‌کننده بدهید و ابلاغ را ثبت کنید.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ProcurementRoles.Commercial, Link = OrderText.Link(item), Title = "ارسال سفارش " + item.TargetNumber + " به تامین‌کننده و ثبت ابلاغ", HasOfficeInboxItem = true };
    }

    /// <summary>WH.PO.RECEIVE: the warehouse receives the goods of a released order (a physical act: the receipt is made on the warehouse page).</summary>
    internal sealed class ReceiveGoodsStep : ProcStep
    {
        public override string Code => "WH.PO.RECEIVE";
        public override string AgentCode => AgentCodes.Wh;
        public override string Title => "دریافت کالای سفارش خرید در انبار";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<PurchaseOrder>().AsNoTracking()
                .Where(o => !o.Deleted && o.Kind == PurchaseKind.Goods && (o.Status == PurchaseOrderStatus.Released || o.Status == PurchaseOrderStatus.PartlyReceived) && o.Lines.Any(l => l.Quantity > l.ReceivedQuantity))
                .OrderBy(o => o.Id).Take(ProcStepCommon.Cap).Select(o => new { o.Id, o.Number, o.ContractId, o.WorkOrderId, Open = o.Lines.Count(l => l.Quantity > l.ReceivedQuantity) }).ToListAsync(ct);
            return rows.Select(o => Item("PurchaseOrder", o.Id, o.Number, o.ContractId, o.WorkOrderId, $"{o.Open} ردیف سفارش هنوز به‌طور کامل دریافت نشده است.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = "WAREHOUSE,WHSUPERVISOR", Link = "/inventory/documents", Title = "دریافت کالای سفارش " + item.TargetNumber + " و ثبت رسید انبار" };
    }

    /// <summary>PROC.PO.SERVICE: the unit accepts the work of a released service or subcontract order (not the buyer, not the approver).</summary>
    internal sealed class AcceptServiceStep : ProcStep
    {
        public override string Code => "PROC.PO.SERVICE";
        public override string AgentCode => AgentCodes.Proc;
        public override string Title => "تایید انجام خدمت یا کار پیمانکاری";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<PurchaseOrder>().AsNoTracking()
                .Where(o => !o.Deleted && o.Kind != PurchaseKind.Goods && (o.Status == PurchaseOrderStatus.Released || o.Status == PurchaseOrderStatus.PartlyReceived) && o.Lines.Any(l => l.Quantity > l.ReceivedQuantity))
                .OrderBy(o => o.Id).Take(ProcStepCommon.Cap).Select(o => new { o.Id, o.Number, o.ContractId, o.WorkOrderId, Open = o.Lines.Count(l => l.Quantity > l.ReceivedQuantity) }).ToListAsync(ct);
            return rows.Select(o => Item("PurchaseOrder", o.Id, o.Number, o.ContractId, o.WorkOrderId, $"{o.Open} ردیف خدمت یا پیمانکاری هنوز تایید انجام نشده است.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ProcurementRoles.ServiceAcceptance, Link = OrderText.Link(item), Title = "تایید انجام خدمت سفارش " + item.TargetNumber };
    }
}
