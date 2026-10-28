using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.ContractEquipments;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Equipments;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Providers;
using Entities.Model.NasbNiro.Quality;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro
{
    /// <summary>
    /// R1a: integrity guards for the legacy deletes. The legacy services only flag a row as deleted (soft delete) and cascade the flag to the
    /// legacy children (work orders, assemblies, BOMs, processes), but the newer modules (project control, procurement, finance, production,
    /// inventory, after-sales, sales, quality) keep plain integer references to contracts, work orders, providers, equipment and BOMs.
    /// Deleting a record that those modules still use would hide a contract, a supplier or a work order from reports and approvals while the
    /// money, the stock and the documents that belong to it stay. Every method here returns a Persian refusal that names what is in the way,
    /// or <c>null</c> when the delete may go on. The services call them from the single-record delete, which the collection deletes also use,
    /// so every path is covered. Soft delete itself is unchanged.
    /// R1a2 (M3) widened the lists after the security review: the project start records, the register of documents, the letters, the budget, the complaints,
    /// the processes, the provisional statements (which also stop being edited once an invoice or a payment point uses them), the legacy guaranties and the buy orders under a work order.
    /// </summary>
    public static class LegacyDeleteGuard
    {
        // ------------------------------------------------------------------------------------------ contract

        /// <summary>A contract with project control, procurement, finance, production, stock, service or quality records (its own or those of its work orders) cannot be deleted.</summary>
        public static async Task<string> ContractAsync(IUnitOfWork uow, int contractId, CancellationToken cancellationToken)
        {
            var p = new Probe(uow, cancellationToken);

            // project control: terms, baseline, activities and the records built on them
            await p.Live<PcContractTerms>("شرایط قرارداد در کنترل پروژه", x => x.ContractId == contractId);
            await p.Live<PcBaseline>("خط‌مبنای برنامه", x => x.ContractId == contractId);
            await p.Live<PcActivity>("فعالیت‌های برنامه", x => x.ContractId == contractId);
            await p.Live<PcWbsNode>("ساختار شکست کار (WBS)", x => x.ContractId == contractId);
            await p.Live<PcPaymentMilestone>("نقاط پرداخت", x => x.ContractId == contractId);
            await p.Live<PcVariationOrder>("دستورهای تغییر", x => x.ContractId == contractId);
            await p.Live<PcPenaltyAssessment>("ارزیابی‌های جریمه", x => x.ContractId == contractId);
            await p.Live<PcClaim>("ادعاها", x => x.ContractId == contractId);
            await p.Live<PcChangeRequest>("درخواست‌های تغییر", x => x.ContractId == contractId);
            await p.Live<PcContractAmendment>("الحاقیه‌های قرارداد", x => x.ContractId == contractId);
            await p.Live<PcGuaranteeInfo>("ضمانت‌نامه‌های کنترل پروژه", x => x.ContractId == contractId);
            await p.Live<PcShipment>("محموله‌ها", x => x.ContractId == contractId);

            // procurement
            await p.Any<PurchaseRequest>("درخواست‌های خرید", x => x.ContractId == contractId);
            await p.Any<PurchaseOrder>("سفارش‌های خرید", x => x.ContractId == contractId);

            // finance: invoices, revenue, guarantees, vouchers, costs and the accounting events
            await p.Any<FnSalesInvoice>("فاکتورهای فروش", x => x.ContractId == contractId);
            await p.Any<FnContractRevenue>("درآمد قرارداد", x => x.ContractId == contractId);
            await p.Any<FnRevenueRecognition>("درآمد قرارداد", x => x.ContractId == contractId);
            await p.Any<FnGuarantee>("ضمانت‌نامه و سپرده‌های مالی", x => x.ContractId == contractId);
            await p.Any<FnBankGuarantee>("ضمانت‌نامه و سپرده‌های مالی", x => x.ContractId == contractId);
            await p.Any<FnRetentionRelease>("ضمانت‌نامه و سپرده‌های مالی", x => x.ContractId == contractId);
            await p.Any<FnInsuranceClearance>("ضمانت‌نامه و سپرده‌های مالی", x => x.ContractId == contractId);
            await p.Any<FnVoucherLine>("اسناد حسابداری", x => x.ContractId == contractId);
            await p.Any<CostEntry>("هزینه‌های ثبت‌شده", x => x.ContractId == contractId);
            await p.Any<AccountingEventLine>("رویدادهای حسابداری", x => x.ContractId == contractId);

            // production, stock, after-sales, sales, quality
            await p.Live<MfProductionOrder>("دستورهای تولید", x => x.ContractId == contractId);
            await p.Any<MfCostPosting>("هزینه‌های تولید", x => x.ContractId == contractId);
            await p.Live<InventoryDocument>("اسناد انبار", x => x.ContractId == contractId);
            await p.Live<NonConformance>("گزارش‌های عدم انطباق", x => x.ContractId == contractId);
            await p.Live<SvInstalledUnit>("تجهیزات نصب‌شده نزد مشتری", x => x.ContractId == contractId);
            await p.Live<SvServiceOrder>("دستورهای خدمات پس از فروش", x => x.ContractId == contractId);
            await p.Live<SlProposal>("پیشنهادهای فروش", x => x.ContractId == contractId);
            await p.Live<Itp>("طرح‌های بازرسی و آزمون", x => x.ContractId == contractId);
            await p.Live<ReleaseNote>("برگه‌های ترخیص", x => x.ContractId == contractId);

            // R1a2 (M3): the project start, the register of engineering documents, the transmittals and reports, the lines of purchase orders and budgets, deferrals, complaints, controlled documents, letters and the statements
            await p.Live<PcProjectInitiation>("راه‌اندازی پروژه", x => x.ContractId == contractId);
            await p.Live<PcProjectCharter>("منشور پروژه", x => x.ContractId == contractId);
            await p.Live<PcProjectPlan>("برنامه اجرایی پروژه", x => x.ContractId == contractId);
            await p.Live<PcContractReview>("بازنگری قرارداد", x => x.ContractId == contractId);
            await p.Live<PcEngineeringDeliverable>("مدارک مهندسی", x => x.ContractId == contractId);
            await p.Live<PcTransmittal>("ترنسمیتال‌ها", x => x.ContractId == contractId);
            await p.Any<PcReportIssue>("گزارش‌های صادرشده", x => x.ContractId == contractId);
            await p.Live<PcDailyReport>("گزارش‌های روزانه", x => x.ContractId == contractId);
            await p.Any<PurchaseOrderLine>("سفارش‌های خرید", x => x.ContractId == contractId);
            await p.Any<FnBudgetLine>("بودجه", x => x.ContractId == contractId);
            await p.Any<FnDeferral>("پیش‌پرداخت‌ها و درآمدهای انتقالی", x => x.ContractId == contractId);
            await p.Live<CustomerComplaint>("شکایت‌های مشتری", x => x.ContractId == contractId);
            await p.Live<ControlledDocument>("مدارک کنترل‌شده", x => x.ContractId == contractId);
            await p.Live<OfLetter>("نامه‌ها", x => x.ContractId == contractId);
            await p.Live<ProvisionalStatement>("صورت‌وضعیت‌ها", x => x.ContractId == contractId);

            // the work orders of the contract are deleted with it: whatever blocks one of them blocks the contract
            List<int> workOrderIds = await uow.Set<WorkOrder>().AsNoTracking()
                .Where(w => w.ContractId == contractId && !w.Deleted)
                .Select(w => w.Id)
                .ToListAsync(cancellationToken);
            if (workOrderIds.Count > 0) await WorkOrderProbesAsync(uow, p, workOrderIds, cancellationToken);

            return p.Refusal("حذف قرارداد ممکن نیست؛ برای این قرارداد سوابق ثبت شده است", "قراردادی که سابقه کنترل پروژه، خرید، مالی، تولید، انبار یا خدمات دارد حذف نمی‌شود.");
        }

        // ------------------------------------------------------------------------------------------ work order

        /// <summary>A work order (دستور ساخت) with production orders, inventory documents, cost entries or other downstream records cannot be deleted.</summary>
        public static async Task<string> WorkOrderAsync(IUnitOfWork uow, int workOrderId, CancellationToken cancellationToken)
        {
            var p = new Probe(uow, cancellationToken);
            await WorkOrderProbesAsync(uow, p, new List<int> { workOrderId }, cancellationToken);
            return p.Refusal("حذف دستور ساخت ممکن نیست؛ برای آن سوابق ثبت شده است", "دستور ساختی که دستور تولید، سند انبار یا هزینه دارد حذف نمی‌شود.");
        }

        /// <summary>The first stages of a buy order: nothing was asked of a supplier yet (the second and third are the starts of the assembler and the constructor routes).</summary>
        private static readonly BuyOrderStatus[] BuyOrderStartStatuses = { BuyOrderStatus.NewBuyOrder, BuyOrderStatus.NewBuyOrderForAssembler, BuyOrderStatus.NewBuyOrderForConstructor };

        private static async Task WorkOrderProbesAsync(IUnitOfWork uow, Probe p, List<int> ids, CancellationToken cancellationToken)
        {
            // the three kinds the company named first: production orders, inventory documents, cost entries
            await p.Live<MfProductionOrder>("دستورهای تولید", x => ids.Contains(x.WorkOrderId));
            await p.Any<MfCostPosting>("هزینه‌های تولید", x => ids.Contains(x.WorkOrderId));
            await p.Live<InventoryDocument>("اسناد انبار", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Any<CostEntry>("هزینه‌های ثبت‌شده", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));

            // the rest of what hangs on a work order
            await p.Live<MaterialRequest>("درخواست‌های مواد از انبار", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Live<SerialUnit>("قطعات سریال‌دار", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Live<PartOperation>("ردیابی قطعات", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Live<AssemblyLink>("ردیابی قطعات", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Live<NonConformance>("گزارش‌های عدم انطباق", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Any<AccountingEventLine>("رویدادهای حسابداری", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Any<FnVoucherLine>("اسناد حسابداری", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Any<FnPettyCashSettlementLine>("هزینه‌های تنخواه", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Any<FnWorkOrderBase>("سوابق مالی دستور ساخت", x => ids.Contains(x.WorkOrderId));
            await p.Any<FnWorkOrderClose>("سوابق مالی دستور ساخت", x => ids.Contains(x.WorkOrderId));
            await p.Any<FnWorkOrderEstimate>("سوابق مالی دستور ساخت", x => ids.Contains(x.WorkOrderId));
            await p.Any<FnAttendanceWorkOrder>("هزینه نیروی انسانی", x => ids.Contains(x.WorkOrderId));
            await p.Any<FnPayslipAllocation>("هزینه نیروی انسانی", x => ids.Contains(x.WorkOrderId));
            await p.Live<PurchaseRequest>("درخواست‌های خرید", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Live<PurchaseOrder>("سفارش‌های خرید", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Live<PcShipment>("محموله‌ها", x => ids.Contains(x.WorkOrderId));
            await p.Live<PcActivity>("فعالیت‌های برنامه", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Live<PcWbsNode>("ساختار شکست کار (WBS)", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Live<PcEngineeringDeliverable>("مدارک مهندسی", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Live<QcRecord>("سوابق کنترل کیفیت", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Live<TestReport>("سوابق کنترل کیفیت", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Live<WeldLog>("سوابق کنترل کیفیت", x => ids.Contains(x.WorkOrderId));
            await p.Live<Itp>("طرح‌های بازرسی و آزمون", x => ids.Contains(x.WorkOrderId));
            await p.Live<ReleaseNote>("برگه‌های ترخیص", x => ids.Contains(x.WorkOrderId));
            await p.Live<SvInstalledUnit>("تجهیزات نصب‌شده نزد مشتری", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));

            // R1a2 (M3): payment points and penalties of the work order, its project code, the lines of purchase orders, complaints and controlled documents
            await p.Live<PcPaymentMilestone>("نقاط پرداخت", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Live<PcPenaltyAssessment>("ارزیابی‌های جریمه", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Live<PcWorkOrderInfo>("کد پروژه‌ی دستور ساخت", x => ids.Contains(x.WorkOrderId));
            await p.Any<PurchaseOrderLine>("سفارش‌های خرید", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Live<CustomerComplaint>("شکایت‌های مشتری", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));
            await p.Live<ControlledDocument>("مدارک کنترل‌شده", x => x.WorkOrderId.HasValue && ids.Contains(x.WorkOrderId.Value));

            // the work order takes its buy orders with it (the legacy project purchases): they must not have started, and no stock document or purchase request line may point to them
            List<int> buyOrderIds = await uow.Set<BuyOrder>().AsNoTracking()
                .Where(b => !b.Deleted && ids.Contains(b.WorkOrderId))
                .Select(b => b.Id)
                .ToListAsync(cancellationToken);
            if (buyOrderIds.Count > 0)
            {
                await p.Live<BuyOrder>("سفارش‌های خرید پروژه که فرایندشان شروع شده است", x => ids.Contains(x.WorkOrderId) && !BuyOrderStartStatuses.Contains(x.Status));
                await p.Live<InventoryDocument>("اسناد انبار", x => x.BuyOrderId.HasValue && buyOrderIds.Contains(x.BuyOrderId.Value));
                await p.Any<PurchaseRequestLine>("درخواست‌های خرید", x => x.BuyOrderId.HasValue && buyOrderIds.Contains(x.BuyOrderId.Value) && !x.PurchaseRequest.Deleted);
            }

            // ... and its assemblies, BOMs and processes, which the schedule and the production orders may point to
            List<int> assemblyIds = await uow.Set<AssemblyEntity>().AsNoTracking()
                .Where(a => !a.Deleted && ids.Contains(a.WorkOrderId))
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);
            if (assemblyIds.Count > 0)
            {
                await p.Live<PcWbsNode>("ساختار شکست کار (WBS)", x => x.AssemblyId.HasValue && assemblyIds.Contains(x.AssemblyId.Value));
                List<int> bomIds = await uow.Set<Bom>().AsNoTracking()
                    .Where(b => !b.Deleted && assemblyIds.Contains(b.AssemblyId))
                    .Select(b => b.Id)
                    .ToListAsync(cancellationToken);
                await BomProbesAsync(uow, p, bomIds, cancellationToken);
            }
        }

        // ------------------------------------------------------------------------------------------ provider

        /// <summary>A provider (supplier) with purchase orders, invoices, payments or stock receipts cannot be deleted.</summary>
        public static async Task<string> ProviderAsync(IUnitOfWork uow, int providerId, CancellationToken cancellationToken)
        {
            var p = new Probe(uow, cancellationToken);
            await p.Live<PurchaseOrder>("سفارش‌های خرید", x => x.ProviderId == providerId);
            await p.Live<SupplierInvoice>("فاکتورهای تأمین‌کننده", x => x.ProviderId == providerId);
            await p.Live<PaymentRequest>("درخواست‌های پرداخت", x => x.ProviderId == providerId);
            await p.Any<SupplierQuote>("پیشنهادهای قیمت", x => x.ProviderId == providerId);
            await p.Live<Inquiry>("استعلام‌ها", x => x.ProviderId == providerId);
            await p.Any<FnPayment>("پرداخت‌ها", x => x.ProviderId == providerId);
            await p.Any<FnIssuedCheque>("پرداخت‌ها", x => x.ProviderId == providerId);
            await p.Any<FnPaymentProposalLine>("پرداخت‌ها", x => x.ProviderId == providerId);
            await p.Any<FnSupplierNote>("یادداشت‌ها و صورت‌حساب‌های تأمین‌کننده", x => x.ProviderId == providerId);
            await p.Any<FnSupplierStatement>("یادداشت‌ها و صورت‌حساب‌های تأمین‌کننده", x => x.ProviderId == providerId);
            await p.Any<FnLetterOfCredit>("اعتبار اسنادی", x => x.ProviderId == providerId);
            await p.Any<FnBankGuarantee>("ضمانت‌نامه و سپرده‌ها", x => x.ProviderId == providerId);
            await p.Any<FnRetentionRelease>("ضمانت‌نامه و سپرده‌ها", x => x.ProviderId == providerId);
            await p.Any<FnInsuranceClearance>("ضمانت‌نامه و سپرده‌ها", x => x.ProviderId == providerId);
            await p.Any<AccountingEventLine>("رویدادهای حسابداری", x => x.ProviderId == providerId);
            await p.Live<InventoryDocument>("اسناد انبار", x => x.ProviderId == providerId);
            await p.Live<InventoryLot>("محموله‌های انبار", x => x.ProviderId == providerId);
            await p.Live<NonConformance>("گزارش‌های عدم انطباق", x => x.ProviderId == providerId);

            // R1a2 (M3): his evaluations, his entry in the approved vendor list, the qualified people he supplies and the letters' party record
            await p.Live<SupplierEvaluation>("ارزیابی‌های تأمین‌کننده", x => x.ProviderId == providerId);
            await p.Any<SupplierProfile>("پرونده‌ی تأمین‌کننده", x => x.ProviderId == providerId);
            await p.Live<QualifiedPerson>("افراد واجد صلاحیت", x => x.ProviderId == providerId);
            await p.Live<OfExternalParty>("طرف‌های مکاتبات", x => x.ProviderId == providerId);
            return p.Refusal("حذف تأمین‌کننده ممکن نیست؛ برای او سوابق ثبت شده است", "تأمین‌کننده‌ای که سفارش خرید، فاکتور، پرداخت یا رسید انبار دارد حذف نمی‌شود.");
        }

        // ------------------------------------------------------------------------------------------ equipment definitions

        /// <summary>An equipment definition used by a contract, a work order or a sales estimate (or whose BOM lines are used) cannot be deleted.</summary>
        public static async Task<string> EquipmentAsync(IUnitOfWork uow, int equipmentId, CancellationToken cancellationToken)
        {
            var p = new Probe(uow, cancellationToken);
            await p.Any<ContractEquipment>("فهرست تجهیزات قرارداد", x => x.EquipmentId == equipmentId && !x.Contract.Deleted);
            await p.Live<WorkOrder>("دستورهای ساخت", x => x.EquipmentId == equipmentId);
            await p.Any<SlEstimateEquipment>("برآوردهای فروش", x => x.EquipmentId == equipmentId);
            await p.Any<SlEstimateLine>("برآوردهای فروش", x => x.EquipmentId == equipmentId);

            List<int> equipmentBomIds = await uow.Set<EquipmentBom>().AsNoTracking()
                .Where(b => !b.Deleted && b.EquipmentAssembly.EquipmentId == equipmentId)
                .Select(b => b.Id)
                .ToListAsync(cancellationToken);
            await EquipmentBomProbesAsync(p, equipmentBomIds);
            return p.Refusal("حذف تجهیز ممکن نیست؛ از آن استفاده شده است", "تجهیزی که در قرارداد، دستور ساخت یا برآورد فروش به کار رفته حذف نمی‌شود.");
        }

        /// <summary>An equipment assembly whose BOM lines are used by work orders or sales estimates cannot be deleted.</summary>
        public static async Task<string> EquipmentAssemblyAsync(IUnitOfWork uow, int equipmentAssemblyId, CancellationToken cancellationToken)
        {
            var p = new Probe(uow, cancellationToken);
            List<int> equipmentBomIds = await uow.Set<EquipmentBom>().AsNoTracking()
                .Where(b => !b.Deleted && b.EquipmentAssemblyId == equipmentAssemblyId)
                .Select(b => b.Id)
                .ToListAsync(cancellationToken);
            await EquipmentBomProbesAsync(p, equipmentBomIds);
            return p.Refusal("حذف مجموعه تجهیز ممکن نیست؛ از اجزای آن استفاده شده است", "اجزایی که در دستور ساخت یا برآورد فروش به کار رفته‌اند حذف نمی‌شوند.");
        }

        /// <summary>An equipment BOM line used by a work order BOM or a sales estimate cannot be deleted.</summary>
        public static async Task<string> EquipmentBomAsync(IUnitOfWork uow, int equipmentBomId, CancellationToken cancellationToken)
        {
            var p = new Probe(uow, cancellationToken);
            await EquipmentBomProbesAsync(p, new List<int> { equipmentBomId });
            return p.Refusal("حذف قطعه تجهیز ممکن نیست؛ از آن استفاده شده است", "قطعه‌ای که در BOM دستور ساخت یا برآورد فروش به کار رفته حذف نمی‌شود.");
        }

        private static async Task EquipmentBomProbesAsync(Probe p, List<int> equipmentBomIds)
        {
            if (equipmentBomIds.Count == 0) return;
            await p.Live<Bom>("BOM دستورهای ساخت", x => x.EquipmentBomId.HasValue && equipmentBomIds.Contains(x.EquipmentBomId.Value));
            await p.Any<SlEstimateLine>("برآوردهای فروش", x => x.EquipmentBomId.HasValue && equipmentBomIds.Contains(x.EquipmentBomId.Value));
        }

        // ------------------------------------------------------------------------------------------ work order BOMs

        /// <summary>A work order BOM that a production order or a project control WBS node refers to cannot be deleted.</summary>
        public static async Task<string> BomAsync(IUnitOfWork uow, int bomId, CancellationToken cancellationToken)
        {
            var p = new Probe(uow, cancellationToken);
            await BomProbesAsync(uow, p, new List<int> { bomId }, cancellationToken);
            return p.Refusal("حذف BOM ممکن نیست؛ از آن استفاده شده است", "BOMی که دستور تولید یا برنامه کنترل پروژه به آن وصل است حذف نمی‌شود.");
        }

        /// <summary>An assembly of a work order is deleted with its BOMs and processes: whatever blocks one of them blocks the assembly.</summary>
        public static async Task<string> AssemblyAsync(IUnitOfWork uow, int assemblyId, CancellationToken cancellationToken)
        {
            var p = new Probe(uow, cancellationToken);
            List<int> bomIds = await uow.Set<Bom>().AsNoTracking()
                .Where(b => !b.Deleted && b.AssemblyId == assemblyId)
                .Select(b => b.Id)
                .ToListAsync(cancellationToken);
            await BomProbesAsync(uow, p, bomIds, cancellationToken);
            await p.Live<PcWbsNode>("ساختار شکست کار (WBS)", x => x.AssemblyId == assemblyId);
            return p.Refusal("حذف مجموعه ممکن نیست؛ از آن یا از اجزای آن استفاده شده است", "مجموعه‌ای که دستور تولید یا برنامه کنترل پروژه به آن وصل است حذف نمی‌شود.");
        }

        private static async Task BomProbesAsync(IUnitOfWork uow, Probe p, List<int> bomIds, CancellationToken cancellationToken)
        {
            if (bomIds.Count == 0) return;
            await p.Live<MfProductionOrder>("دستورهای تولید", x => x.BomId.HasValue && bomIds.Contains(x.BomId.Value));
            await p.Live<PcWbsNode>("ساختار شکست کار (WBS)", x => x.BomId.HasValue && bomIds.Contains(x.BomId.Value));
            List<int> processIds = await uow.Set<ProcessEntity>().AsNoTracking()
                .Where(x => !x.Deleted && bomIds.Contains(x.BomId))
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            if (processIds.Count > 0)
            {
                await p.Live<MfProductionOrder>("دستورهای تولید", x => x.ProcessId.HasValue && processIds.Contains(x.ProcessId.Value));
                await p.Live<PcWbsNode>("ساختار شکست کار (WBS)", x => x.ProcessId.HasValue && processIds.Contains(x.ProcessId.Value));   // R1a2 (M3)
            }
        }

        // ------------------------------------------------------------------------------------------ process, provisional statement, guaranties (R1a2, M3)

        /// <summary>A process (a step of a BOM) that a production order or a project control WBS node refers to cannot be deleted.</summary>
        public static async Task<string> ProcessAsync(IUnitOfWork uow, int processId, CancellationToken cancellationToken)
        {
            var p = new Probe(uow, cancellationToken);
            await p.Live<MfProductionOrder>("دستورهای تولید", x => x.ProcessId == processId);
            await p.Live<PcWbsNode>("ساختار شکست کار (WBS)", x => x.ProcessId == processId);
            return p.Refusal("حذف فرایند ممکن نیست؛ از آن استفاده شده است", "فرایندی که دستور تولید یا برنامه کنترل پروژه به آن وصل است حذف نمی‌شود.");
        }

        /// <summary>
        /// A provisional statement (صورت‌وضعیت) that a payment point of project control or a sales invoice refers to is neither edited nor deleted: the amounts of the
        /// invoice and of the payment would no longer agree with it. <paramref name="forDelete"/> only changes the wording of the refusal.
        /// </summary>
        public static async Task<string> ProvisionalStatementAsync(IUnitOfWork uow, int statementId, bool forDelete, CancellationToken cancellationToken)
        {
            var p = new Probe(uow, cancellationToken);
            await p.Live<PcPaymentMilestone>("نقاط پرداخت", x => x.ProvisionalStatementId == statementId);
            await p.Any<FnSalesInvoice>("فاکتورهای فروش", x => x.ProvisionalStatementId == statementId);
            return p.Refusal(forDelete ? "حذف صورت‌وضعیت ممکن نیست؛ از آن استفاده شده است" : "ویرایش صورت‌وضعیت ممکن نیست؛ از آن استفاده شده است",
                "صورت‌وضعیتی که نقطه پرداخت یا فاکتور فروش به آن وصل است ویرایش یا حذف نمی‌شود.");
        }

        /// <summary>A legacy guaranty of a contract (ContractGuaranty) or of a tender (TenderGuaranty) that project control keeps a record for (expiry, alerts, release) cannot be deleted.</summary>
        public static async Task<string> LegacyGuarantyAsync(IUnitOfWork uow, PcGuaranteeSource kind, int guarantyId, CancellationToken cancellationToken)
        {
            var p = new Probe(uow, cancellationToken);
            await p.Live<PcGuaranteeInfo>("ضمانت‌نامه‌های کنترل پروژه", x => x.SourceKind == kind && x.SourceId == guarantyId);
            return p.Refusal("حذف ضمانت‌نامه ممکن نیست؛ برای آن سوابق ثبت شده است", "ضمانت‌نامه‌ای که کنترل پروژه برای آن تاریخ انقضا و آزادسازی نگه می‌دارد حذف نمی‌شود.");
        }

        // ------------------------------------------------------------------------------------------ plumbing

        private sealed class Probe
        {
            private readonly IUnitOfWork _uow;
            private readonly CancellationToken _cancellationToken;
            private readonly List<string> _found = new List<string>();

            public Probe(IUnitOfWork uow, CancellationToken cancellationToken)
            {
                _uow = uow ?? throw new ArgumentNullException(nameof(uow));
                _cancellationToken = cancellationToken;
            }

            /// <summary>A record of a type without the soft-delete flag.</summary>
            public async Task Any<T>(string label, Expression<Func<T, bool>> where) where T : class
            {
                if (_found.Contains(label)) return;
                if (await _uow.Set<T>().AsNoTracking().AnyAsync(where, _cancellationToken)) _found.Add(label);
            }

            /// <summary>A record that is not soft-deleted (a deleted record no longer blocks anything).</summary>
            public async Task Live<T>(string label, Expression<Func<T, bool>> where) where T : BaseEntity
            {
                if (_found.Contains(label)) return;
                if (await _uow.Set<T>().AsNoTracking().Where(x => !x.Deleted).AnyAsync(where, _cancellationToken)) _found.Add(label);
            }

            public string Refusal(string head, string tail) =>
                _found.Count == 0 ? null : head + ": " + string.Join("، ", _found) + ". " + tail;
        }
    }
}
