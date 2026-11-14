using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.ProcurementDtos;

namespace Services.NasbNiro.Agents.Steps
{
    // The invoice and the payment: registered by finance or the buyer, approved by finance (or the PMO deputy when the price differs), the payment request drafted by the procurement agent, its deductions
    // confirmed by finance, approved by the PMO deputy or the CEO, and paid by finance (the treasury posts it).

    internal static class PayableText
    {
        public static string InvoiceLink(AgentWorkItem item) => "/procurement/invoices?id=" + item.TargetId.ToString(CultureInfo.InvariantCulture);
        public static string PaymentLink(AgentWorkItem item) => "/procurement/payments?id=" + item.TargetId.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>PROC.INV.REGISTER: an order line was received and not invoiced and no invoice is waiting: finance or the buyer registers the supplier invoice.</summary>
    internal sealed class RegisterInvoiceStep : ProcStep
    {
        public override string Code => "PROC.INV.REGISTER";
        public override string AgentCode => AgentCodes.Fin;
        public override string Title => "ثبت فاکتور تامین‌کننده";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<SupplierInvoice> invoices = uow.Set<SupplierInvoice>().AsNoTracking();
            var rows = await uow.Set<PurchaseOrder>().AsNoTracking()
                .Where(o => !o.Deleted && (o.Status == PurchaseOrderStatus.Released || o.Status == PurchaseOrderStatus.PartlyReceived || o.Status == PurchaseOrderStatus.Received)
                            && o.Lines.Any(l => l.ReceivedQuantity > l.InvoicedQuantity)
                            && !invoices.Any(i => i.PurchaseOrderId == o.Id && !i.Deleted && (i.Status == SupplierInvoiceStatus.Draft || i.Status == SupplierInvoiceStatus.Matched || i.Status == SupplierInvoiceStatus.NeedsApproval)))
                .OrderBy(o => o.Id).Take(ProcStepCommon.Cap).Select(o => new { o.Id, o.Number, o.ContractId, o.WorkOrderId, Open = o.Lines.Count(l => l.ReceivedQuantity > l.InvoicedQuantity) }).ToListAsync(ct);
            return rows.Select(o => Item("PurchaseOrder", o.Id, o.Number, o.ContractId, o.WorkOrderId, $"{o.Open} ردیف دریافت‌شده سفارش هنوز فاکتور نشده است.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ProcStepCommon.Roles(ProcurementRoles.FinanceStaff, ProcurementRoles.Commercial), Link = "/procurement/invoices", Title = "ثبت فاکتور تامین‌کننده سفارش " + item.TargetNumber };
    }

    /// <summary>PROC.INV.APPROVE: finance approves a matched invoice; the PMO deputy or the CEO approves one whose price differs from the order (never the person who registered it).</summary>
    internal sealed class ApproveInvoiceStep : ProcStep
    {
        public override string Code => "PROC.INV.APPROVE";
        public override string AgentCode => AgentCodes.Fin;
        public override string Title => "تایید فاکتور تامین‌کننده";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<SupplierInvoice>().AsNoTracking().Where(i => !i.Deleted && (i.Status == SupplierInvoiceStatus.Matched || i.Status == SupplierInvoiceStatus.NeedsApproval))
                .OrderBy(i => i.Id).Take(ProcStepCommon.Cap).Select(i => new { i.Id, i.Number, i.Status, i.TotalAmount, i.PriceVariance, ContractId = i.PurchaseOrder.ContractId, i.PurchaseOrder.WorkOrderId }).ToListAsync(ct);
            return rows.Select(i => Item("SupplierInvoice", i.Id, i.Number, i.ContractId, i.WorkOrderId,
                i.Status == SupplierInvoiceStatus.NeedsApproval ? $"قیمت فاکتور با سفارش فرق دارد (اختلاف {ProcStepCommon.Num(i.PriceVariance)} ریال)؛ تایید معاونت PMO یا مدیرعامل لازم است." : $"فاکتور به مبلغ {ProcStepCommon.Num(i.TotalAmount)} ریال با سفارش و رسید تطبیق دارد.",
                i.Status == SupplierInvoiceStatus.NeedsApproval ? ProcurementRoles.OrderApproval : ProcurementRoles.Finance)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = item.GateRoles ?? ProcurementRoles.Finance, Link = PayableText.InvoiceLink(item), Title = "تایید فاکتور تامین‌کننده " + item.TargetNumber, HasOfficeInboxItem = true };
    }

    /// <summary>PROC.PAY.DRAFT: an approved invoice with an unpaid balance and no open payment request gets its payment request (the whole balance) from the procurement agent.</summary>
    internal sealed class DraftPaymentStep : ProcStep
    {
        public override string Code => "PROC.PAY.DRAFT";
        public override string AgentCode => AgentCodes.Proc;
        public override string Title => "ایجنت تدارکات: تهیه درخواست پرداخت از فاکتور تاییدشده";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => ProcurementRoles.Commercial;

        /// <summary>What can still be requested for an invoice: the total, less the posted credit notes, plus the posted debit notes, less every payment request that was not rejected (PayablesService.SavePaymentAsync).</summary>
        public static async Task<decimal> BalanceAsync(IUnitOfWork uow, SupplierInvoice invoice, CancellationToken ct)
        {
            decimal requested = await uow.Set<PaymentRequest>().AsNoTracking().Where(p => p.InvoiceId == invoice.Id && !p.Deleted && p.Status != PaymentRequestStatus.Rejected).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
            decimal credits = await uow.Set<FnSupplierNote>().AsNoTracking().Where(n => n.SupplierInvoiceId == invoice.Id && n.Kind == FnSupplierNoteKind.Credit && n.Status == FnSettlementStatus.Posted).SumAsync(n => (decimal?)n.TotalAmount, ct) ?? 0;
            decimal debits = await uow.Set<FnSupplierNote>().AsNoTracking().Where(n => n.SupplierInvoiceId == invoice.Id && n.Kind == FnSupplierNoteKind.Debit && n.Status == FnSettlementStatus.Posted).SumAsync(n => (decimal?)n.TotalAmount, ct) ?? 0;
            return invoice.TotalAmount - credits + debits - requested;
        }

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<PaymentRequest> payments = uow.Set<PaymentRequest>().AsNoTracking();
            IQueryable<FnSupplierNote> notes = uow.Set<FnSupplierNote>().AsNoTracking();
            var rows = await uow.Set<SupplierInvoice>().AsNoTracking()
                .Where(i => !i.Deleted && i.Status == SupplierInvoiceStatus.Approved
                            && !payments.Any(p => p.InvoiceId == i.Id && !p.Deleted && (p.Status == PaymentRequestStatus.Draft || p.Status == PaymentRequestStatus.Approved))
                            && i.TotalAmount
                               - (notes.Where(n => n.SupplierInvoiceId == i.Id && n.Kind == FnSupplierNoteKind.Credit && n.Status == FnSettlementStatus.Posted).Sum(n => (decimal?)n.TotalAmount) ?? 0)
                               + (notes.Where(n => n.SupplierInvoiceId == i.Id && n.Kind == FnSupplierNoteKind.Debit && n.Status == FnSettlementStatus.Posted).Sum(n => (decimal?)n.TotalAmount) ?? 0)
                               - (payments.Where(p => p.InvoiceId == i.Id && !p.Deleted && p.Status != PaymentRequestStatus.Rejected).Sum(p => (decimal?)p.Amount) ?? 0) > 0)
                .OrderBy(i => i.Id).Take(ProcStepCommon.Cap).Select(i => new { i.Id, i.Number, i.TotalAmount, ContractId = i.PurchaseOrder.ContractId, i.PurchaseOrder.WorkOrderId }).ToListAsync(ct);
            return rows.Select(i => Item("SupplierInvoice", i.Id, i.Number, i.ContractId, i.WorkOrderId, $"فاکتور تاییدشده به مبلغ {ProcStepCommon.Num(i.TotalAmount)} ریال مانده قابل پرداخت دارد و درخواست پرداخت باز ندارد.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            SupplierInvoice invoice = await uow.Set<SupplierInvoice>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == item.TargetId && !i.Deleted, ct);
            if (invoice == null || invoice.Status != SupplierInvoiceStatus.Approved) return AgentStepResult.Waiting("وضعیت فاکتور تغییر کرده است.");
            decimal balance = await BalanceAsync(uow, invoice, ct);
            if (balance <= 0) return AgentStepResult.Waiting("مانده قابل پرداختی برای این فاکتور نیست.");
            ResponseDto<PaymentRequestDto> saved = await services.GetRequiredService<IPayablesService>().SavePaymentAsync(new SavePaymentRequestDto
            {
                Type = PaymentRequestType.Invoice, InvoiceId = invoice.Id, PurchaseOrderId = invoice.PurchaseOrderId, RequestDate = DateTime.Today, Amount = balance,
                Description = "درخواست پرداخت ساخته‌شده توسط ایجنت تدارکات از فاکتور تاییدشده " + invoice.Number,
            }, ct);
            if (!saved.Success) return ProcStepCommon.Refusal(saved);
            return AgentStepResult.Done("PaymentRequest", saved.Data.Id, saved.Data.Number, $"ایجنت تدارکات درخواست پرداخت {saved.Data.Number} را به مبلغ {ProcStepCommon.Num(balance)} ریال از فاکتور {invoice.Number} تهیه کرد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = PayableText.InvoiceLink(item), Title = "تهیه درخواست پرداخت از فاکتور " + item.TargetNumber };
    }

    /// <summary>PROC.PAY.DEDUCT: finance confirms the late penalty and the retention that the system proposed from the order terms (before the PMO deputy approves).</summary>
    internal sealed class ConfirmDeductionsStep : ProcStep
    {
        public override string Code => "PROC.PAY.DEDUCT";
        public override string AgentCode => AgentCodes.Fin;
        public override string Title => "تایید کسورات پیشنهادی درخواست پرداخت";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<PaymentRequest>().AsNoTracking().Where(p => !p.Deleted && p.Status == PaymentRequestStatus.Draft && p.DeductionsProposedAt != null && p.DeductionsConfirmedAt == null)
                .OrderBy(p => p.Id).Take(ProcStepCommon.Cap).Select(p => new { p.Id, p.Number, p.Deductions, ContractId = p.PurchaseOrder.ContractId, WorkOrderId = p.PurchaseOrder.WorkOrderId }).ToListAsync(ct);
            return rows.Select(p => Item("PaymentRequest", p.Id, p.Number, p.ContractId, p.WorkOrderId, $"جریمه تاخیر یا تضمین حسن انجام کار پیشنهادی (کسورات {ProcStepCommon.Num(p.Deductions)} ریال) منتظر تایید مالی است.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ProcurementRoles.DeductionConfirmation, Link = PayableText.PaymentLink(item), Title = "تایید کسورات درخواست پرداخت " + item.TargetNumber, HasOfficeInboxItem = true };
    }

    /// <summary>PROC.PAY.APPROVE: the PMO deputy or the CEO approves a payment request whose deductions are confirmed or absent (never its maker or the person who confirmed the deductions).</summary>
    internal sealed class ApprovePaymentStep : ProcStep
    {
        public override string Code => "PROC.PAY.APPROVE";
        public override string AgentCode => AgentCodes.Fin;
        public override string Title => "تایید درخواست پرداخت";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<PaymentRequest>().AsNoTracking().Where(p => !p.Deleted && p.Status == PaymentRequestStatus.Draft && (p.DeductionsProposedAt == null || p.DeductionsConfirmedAt != null))
                .OrderBy(p => p.Id).Take(ProcStepCommon.Cap).Select(p => new { p.Id, p.Number, p.Amount, p.Deductions, ContractId = p.PurchaseOrder.ContractId, WorkOrderId = p.PurchaseOrder.WorkOrderId }).ToListAsync(ct);
            return rows.Select(p => Item("PaymentRequest", p.Id, p.Number, p.ContractId, p.WorkOrderId, $"درخواست پرداخت به مبلغ {ProcStepCommon.Num(p.Amount)} ریال (کسورات {ProcStepCommon.Num(p.Deductions)} ریال) منتظر تایید است.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ProcurementRoles.OrderApproval, Link = PayableText.PaymentLink(item), Title = "تایید درخواست پرداخت " + item.TargetNumber, HasOfficeInboxItem = true };
    }

    /// <summary>PROC.PAY.PAID: finance pays an approved payment request (the bank reference is entered by the person; the treasury posts it).</summary>
    internal sealed class PayStep : ProcStep
    {
        public override string Code => "PROC.PAY.PAID";
        public override string AgentCode => AgentCodes.Fin;
        public override string Title => "پرداخت درخواست پرداخت تاییدشده";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<PaymentRequest>().AsNoTracking().Where(p => !p.Deleted && p.Status == PaymentRequestStatus.Approved)
                .OrderBy(p => p.Id).Take(ProcStepCommon.Cap).Select(p => new { p.Id, p.Number, p.Amount, p.Deductions, ContractId = p.PurchaseOrder.ContractId, WorkOrderId = p.PurchaseOrder.WorkOrderId }).ToListAsync(ct);
            return rows.Select(p => Item("PaymentRequest", p.Id, p.Number, p.ContractId, p.WorkOrderId, $"مبلغ خالص قابل پرداخت {ProcStepCommon.Num(p.Amount - p.Deductions)} ریال است؛ شماره سند پرداخت یا پیگیری بانک را ثبت کنید.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ProcurementRoles.Finance, Link = PayableText.PaymentLink(item), Title = "پرداخت درخواست " + item.TargetNumber };
    }
}
