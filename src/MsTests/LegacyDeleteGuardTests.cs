using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
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
using Entities.Model.NasbNiro.Tenders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro;
using Services.NasbNiro.Contracts;
using Services.NasbNiro.WorkOrders;
using ViewModels.NasbNiro.ContractDtos;
using ViewModels.NasbNiro.ProcessDtos;
using ViewModels.NasbNiro.ProviderDtos;
using ViewModels.NasbNiro.ProvisionalStatementDtos;
using ViewModels.NasbNiro.WorkOrderDtos;

namespace MsTests
{
    /// <summary>
    /// R1a: the legacy deletes are soft deletes, but the newer modules keep plain integer references to contracts, work orders, providers,
    /// equipment and BOMs. A record that those modules still use is not deleted: the refusal is Persian, names what is in the way and leaves
    /// the row as it was. An empty record is deleted as before, and a deleted (flagged) record in the way no longer blocks.
    /// </summary>
    [TestClass]
    public class LegacyDeleteGuardTests
    {
        /// <summary>The purchase orders that only stand for another module's record point to a provider that never exists in this host.</summary>
        private const int UnrealProvider = 900001;

        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(7, "pmo.user", "PMO");
        private static readonly Lazy<AppHost> SharedHost = new Lazy<AppHost>(() => new AppHost("DeleteGuard_" + Guid.NewGuid().ToString("N")));
        private static AppHost H => SharedHost.Value;

        // ------------------------------------------------------------------------------------------------ helpers

        private static void Put(Action<IUnitOfWork> add) => H.Write(uow => { add(uow); uow.SaveChanges(); }, Pmo);

        private static int NewContract()
        {
            int id = 0;
            H.Write(uow => { var c = AppHost.Add(uow, new ContractEntity { Code = "C-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "contract" }); uow.SaveChanges(); id = c.Id; }, Pmo);
            return id;
        }

        private static int NewWorkOrder(int contractId, int equipmentId = 0)
        {
            int id = 0;
            H.Write(uow => { var w = AppHost.Add(uow, new WorkOrder { Code = "W", Title = "work order", ContractId = contractId, EquipmentId = equipmentId }); uow.SaveChanges(); id = w.Id; }, Pmo);
            return id;
        }

        private static int NewProvider()
        {
            int id = 0;
            H.Write(uow => { var p = AppHost.Add(uow, new Provider { Name = "supplier " + Guid.NewGuid().ToString("N").Substring(0, 6) }); uow.SaveChanges(); id = p.Id; }, Pmo);
            return id;
        }

        private static int NewEquipment()
        {
            int id = 0;
            H.Write(uow => { var e = AppHost.Add(uow, new Equipment { Code = "E", Title = "equipment" }); uow.SaveChanges(); id = e.Id; }, Pmo);
            return id;
        }

        private static int NewEquipmentAssembly(int equipmentId)
        {
            int id = 0;
            H.Write(uow => { var e = AppHost.Add(uow, new EquipmentAssembly { Code = "EA", Title = "assembly", EquipmentId = equipmentId }); uow.SaveChanges(); id = e.Id; }, Pmo);
            return id;
        }

        private static int NewEquipmentBom(int equipmentAssemblyId)
        {
            int id = 0;
            H.Write(uow => { var e = AppHost.Add(uow, new EquipmentBom { Title = "part", EquipmentAssemblyId = equipmentAssemblyId }); uow.SaveChanges(); id = e.Id; }, Pmo);
            return id;
        }

        private static int NewAssembly(int workOrderId)
        {
            int id = 0;
            H.Write(uow => { var a = AppHost.Add(uow, new AssemblyEntity { Code = "A", Title = "assembly", WorkOrderId = workOrderId }); uow.SaveChanges(); id = a.Id; }, Pmo);
            return id;
        }

        private static int NewBom(int assemblyId, int? equipmentBomId = null)
        {
            int id = 0;
            H.Write(uow => { var b = AppHost.Add(uow, new Bom { Title = "bom", AssemblyId = assemblyId, EquipmentBomId = equipmentBomId }); uow.SaveChanges(); id = b.Id; }, Pmo);
            return id;
        }

        private static Task<string> Guard(Func<IUnitOfWork, CancellationToken, Task<string>> call) =>
            H.AsSystem(provider => call(provider.GetRequiredService<IUnitOfWork>(), CancellationToken.None));

        private static void SoftDelete<T>(Func<T, bool> which) where T : Entities.Model.BaseEntity =>
            H.Write(uow =>
            {
                foreach (T row in uow.Set<T>().Where(x => !x.Deleted).ToList().Where(which)) row.Delete();
                uow.SaveChanges();
            }, Pmo);

        private static void AssertRefused(string refusal, string head, string what)
        {
            Assert.IsNotNull(refusal, "the delete was not refused (" + what + ")");
            Assert.IsTrue(refusal.StartsWith(head), refusal);
            Assert.IsTrue(refusal.Contains(what), "the refusal does not name \"" + what + "\": " + refusal);
        }

        // ------------------------------------------------------------------------------------------------ contract

        private sealed class ContractCase
        {
            public string Name;
            public string Label;
            public Action<IUnitOfWork, int> Seed;
        }

        private static readonly ContractCase[] ContractCases =
        {
            new ContractCase { Name = "project control terms", Label = "شرایط قرارداد در کنترل پروژه", Seed = (u, id) => AppHost.Add(u, new PcContractTerms { ContractId = id }) },
            new ContractCase { Name = "baseline", Label = "خط‌مبنای برنامه", Seed = (u, id) => AppHost.Add(u, new PcBaseline { ContractId = id }) },
            new ContractCase { Name = "activity", Label = "فعالیت‌های برنامه", Seed = (u, id) => AppHost.Add(u, new PcActivity { ContractId = id }) },
            new ContractCase { Name = "WBS node", Label = "ساختار شکست کار (WBS)", Seed = (u, id) => AppHost.Add(u, new PcWbsNode { ContractId = id }) },
            new ContractCase { Name = "payment milestone", Label = "نقاط پرداخت", Seed = (u, id) => AppHost.Add(u, new PcPaymentMilestone { ContractId = id }) },
            new ContractCase { Name = "claim", Label = "ادعاها", Seed = (u, id) => AppHost.Add(u, new PcClaim { ContractId = id }) },
            new ContractCase { Name = "guarantee of project control", Label = "ضمانت‌نامه‌های کنترل پروژه", Seed = (u, id) => AppHost.Add(u, new PcGuaranteeInfo { ContractId = id }) },
            new ContractCase { Name = "shipment", Label = "محموله‌ها", Seed = (u, id) => AppHost.Add(u, new PcShipment { ContractId = id, WorkOrderId = 0 }) },
            new ContractCase { Name = "purchase order", Label = "سفارش‌های خرید", Seed = (u, id) => AppHost.Add(u, new PurchaseOrder { ContractId = id, ProviderId = UnrealProvider }) },
            new ContractCase { Name = "purchase request", Label = "درخواست‌های خرید", Seed = (u, id) => AppHost.Add(u, new PurchaseRequest { ContractId = id }) },
            new ContractCase { Name = "sales invoice", Label = "فاکتورهای فروش", Seed = (u, id) => AppHost.Add(u, new FnSalesInvoice { ContractId = id, CustomerId = 1 }) },
            new ContractCase { Name = "accounting event line", Label = "رویدادهای حسابداری", Seed = (u, id) => AppHost.Add(u, new AccountingEventLine { ContractId = id, EventId = 1 }) },
            new ContractCase { Name = "production order", Label = "دستورهای تولید", Seed = (u, id) => AppHost.Add(u, new MfProductionOrder { ContractId = id, WorkOrderId = 0 }) },
            new ContractCase { Name = "installed unit", Label = "تجهیزات نصب‌شده نزد مشتری", Seed = (u, id) => AppHost.Add(u, new SvInstalledUnit { ContractId = id }) },

            // R1a2 (M3): the records the security review found missing
            new ContractCase { Name = "project initiation", Label = "راه‌اندازی پروژه", Seed = (u, id) => AppHost.Add(u, new PcProjectInitiation { ContractId = id }) },
            new ContractCase { Name = "project charter", Label = "منشور پروژه", Seed = (u, id) => AppHost.Add(u, new PcProjectCharter { ContractId = id }) },
            new ContractCase { Name = "project plan", Label = "برنامه اجرایی پروژه", Seed = (u, id) => AppHost.Add(u, new PcProjectPlan { ContractId = id }) },
            new ContractCase { Name = "contract review", Label = "بازنگری قرارداد", Seed = (u, id) => AppHost.Add(u, new PcContractReview { ContractId = id }) },
            new ContractCase { Name = "engineering deliverable of the contract", Label = "مدارک مهندسی", Seed = (u, id) => AppHost.Add(u, new PcEngineeringDeliverable { ContractId = id }) },
            new ContractCase { Name = "transmittal", Label = "ترنسمیتال‌ها", Seed = (u, id) => AppHost.Add(u, new PcTransmittal { ContractId = id }) },
            new ContractCase { Name = "issued report", Label = "گزارش‌های صادرشده", Seed = (u, id) => AppHost.Add(u, new PcReportIssue { ContractId = id }) },
            new ContractCase { Name = "daily report", Label = "گزارش‌های روزانه", Seed = (u, id) => AppHost.Add(u, new PcDailyReport { ContractId = id }) },
            new ContractCase { Name = "purchase order line", Label = "سفارش‌های خرید", Seed = (u, id) => AppHost.Add(u, new PurchaseOrderLine { PurchaseOrderId = 1, ContractId = id }) },
            new ContractCase { Name = "budget line", Label = "بودجه", Seed = (u, id) => AppHost.Add(u, new FnBudgetLine { BudgetId = 1, ContractId = id }) },
            new ContractCase { Name = "deferral", Label = "پیش‌پرداخت‌ها و درآمدهای انتقالی", Seed = (u, id) => AppHost.Add(u, new FnDeferral { ContractId = id }) },
            new ContractCase { Name = "customer complaint", Label = "شکایت‌های مشتری", Seed = (u, id) => AppHost.Add(u, new CustomerComplaint { ContractId = id }) },
            new ContractCase { Name = "controlled document", Label = "مدارک کنترل‌شده", Seed = (u, id) => AppHost.Add(u, new ControlledDocument { ContractId = id }) },
            new ContractCase { Name = "letter", Label = "نامه‌ها", Seed = (u, id) => AppHost.Add(u, new OfLetter { ContractId = id }) },
            new ContractCase { Name = "provisional statement", Label = "صورت‌وضعیت‌ها", Seed = (u, id) => AppHost.Add(u, new ProvisionalStatement { ContractId = id }) },
        };

        private const string ContractHead = "حذف قرارداد ممکن نیست؛ برای این قرارداد سوابق ثبت شده است";

        [TestMethod]
        public async Task AnEmptyContractMayBeDeleted()
        {
            int id = NewContract();
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.ContractAsync(u, id, ct)));
        }

        [TestMethod]
        public async Task AContractWithRecordsOfAnotherModuleIsRefusedAndTheMessageNamesThem()
        {
            var failures = new List<string>();
            foreach (ContractCase c in ContractCases)
            {
                int id = NewContract();
                Put(u => c.Seed(u, id));
                string refusal = await Guard((u, ct) => LegacyDeleteGuard.ContractAsync(u, id, ct));
                if (refusal == null) failures.Add(c.Name + ": not refused");
                else if (!refusal.StartsWith(ContractHead)) failures.Add(c.Name + ": wrong head: " + refusal);
                else if (!refusal.Contains(c.Label)) failures.Add(c.Name + ": does not name \"" + c.Label + "\": " + refusal);
            }
            Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
        }

        [TestMethod]
        public async Task AContractIsRefusedWhenOneOfItsWorkOrdersHasAProductionOrder()
        {
            int contract = NewContract();
            int workOrder = NewWorkOrder(contract);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.ContractAsync(u, contract, ct)), "a contract with an empty work order may be deleted");

            Put(u => AppHost.Add(u, new MfProductionOrder { WorkOrderId = workOrder, ContractId = 0 }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.ContractAsync(u, contract, ct)), ContractHead, "دستورهای تولید");
        }

        [TestMethod]
        public async Task ARecordThatIsAlreadyDeletedNoLongerBlocks()
        {
            int contract = NewContract();
            Put(u => { AppHost.Add(u, new PcContractTerms { ContractId = contract }); AppHost.Add(u, new PcActivity { ContractId = contract }); });
            Assert.IsNotNull(await Guard((u, ct) => LegacyDeleteGuard.ContractAsync(u, contract, ct)));

            SoftDelete<PcContractTerms>(x => x.ContractId == contract);
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.ContractAsync(u, contract, ct)), ContractHead, "فعالیت‌های برنامه");
            SoftDelete<PcActivity>(x => x.ContractId == contract);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.ContractAsync(u, contract, ct)), "only deleted rows are left");
        }

        // ------------------------------------------------------------------------------------------------ work order

        [TestMethod]
        public async Task AWorkOrderWithDownstreamRecordsIsRefused()
        {
            const string head = "حذف دستور ساخت ممکن نیست؛ برای آن سوابق ثبت شده است";
            int contract = NewContract();

            int empty = NewWorkOrder(contract);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.WorkOrderAsync(u, empty, ct)));

            int withProduction = NewWorkOrder(contract);
            Put(u => AppHost.Add(u, new MfProductionOrder { WorkOrderId = withProduction, ContractId = contract }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.WorkOrderAsync(u, withProduction, ct)), head, "دستورهای تولید");

            int withShipment = NewWorkOrder(contract);
            Put(u => AppHost.Add(u, new PcShipment { WorkOrderId = withShipment, ContractId = contract }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.WorkOrderAsync(u, withShipment, ct)), head, "محموله‌ها");

            int withOrder = NewWorkOrder(contract);
            Put(u => AppHost.Add(u, new PurchaseOrder { WorkOrderId = withOrder, ProviderId = UnrealProvider }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.WorkOrderAsync(u, withOrder, ct)), head, "سفارش‌های خرید");

            // the other work orders of the same contract are not in the way
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.WorkOrderAsync(u, empty, ct)));

            SoftDelete<MfProductionOrder>(x => x.WorkOrderId == withProduction);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.WorkOrderAsync(u, withProduction, ct)), "a deleted production order no longer blocks");
        }

        // ------------------------------------------------------------------------------------------------ provider

        [TestMethod]
        public async Task AProviderWithPurchasesIsRefused()
        {
            const string head = "حذف تأمین‌کننده ممکن نیست؛ برای او سوابق ثبت شده است";
            int empty = NewProvider();
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.ProviderAsync(u, empty, ct)));

            int buyer = NewProvider();
            Put(u => AppHost.Add(u, new PurchaseOrder { ProviderId = buyer }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.ProviderAsync(u, buyer, ct)), head, "سفارش‌های خرید");

            int payee = NewProvider();
            Put(u => AppHost.Add(u, new PaymentRequest { ProviderId = payee }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.ProviderAsync(u, payee, ct)), head, "درخواست‌های پرداخت");

            SoftDelete<PurchaseOrder>(x => x.ProviderId == buyer);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.ProviderAsync(u, buyer, ct)), "a deleted purchase order no longer blocks");
        }

        // ------------------------------------------------------------------------------------------------ equipment

        [TestMethod]
        public async Task AnEquipmentUsedByAContractOrAWorkOrderIsRefused()
        {
            const string head = "حذف تجهیز ممکن نیست؛ از آن استفاده شده است";
            int equipment = NewEquipment();
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.EquipmentAsync(u, equipment, ct)));

            int contract = NewContract();
            Put(u => AppHost.Add(u, new ContractEquipment { ContractId = contract, EquipmentId = equipment, Count = 1 }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.EquipmentAsync(u, equipment, ct)), head, "فهرست تجهیزات قرارداد");

            // a deleted contract no longer holds its equipment list
            SoftDelete<ContractEntity>(x => x.Id == contract);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.EquipmentAsync(u, equipment, ct)));

            int liveContract = NewContract();
            NewWorkOrder(liveContract, equipment);
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.EquipmentAsync(u, equipment, ct)), head, "دستورهای ساخت");

            int estimated = NewEquipment();
            Put(u => AppHost.Add(u, new SlEstimateEquipment { EstimateId = 1, EquipmentId = estimated, Count = 1 }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.EquipmentAsync(u, estimated, ct)), head, "برآوردهای فروش");
        }

        [TestMethod]
        public async Task EquipmentBomLinesUsedByWorkOrderBomsBlockTheLineItsAssemblyAndTheEquipment()
        {
            int equipment = NewEquipment();
            int equipmentAssembly = NewEquipmentAssembly(equipment);
            int equipmentBom = NewEquipmentBom(equipmentAssembly);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.EquipmentBomAsync(u, equipmentBom, ct)));
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.EquipmentAssemblyAsync(u, equipmentAssembly, ct)));

            int workOrder = NewWorkOrder(NewContract());
            int assembly = NewAssembly(workOrder);
            NewBom(assembly, equipmentBom);

            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.EquipmentBomAsync(u, equipmentBom, ct)), "حذف قطعه تجهیز ممکن نیست؛ از آن استفاده شده است", "BOM دستورهای ساخت");
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.EquipmentAssemblyAsync(u, equipmentAssembly, ct)), "حذف مجموعه تجهیز ممکن نیست؛ از اجزای آن استفاده شده است", "BOM دستورهای ساخت");
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.EquipmentAsync(u, equipment, ct)), "حذف تجهیز ممکن نیست؛ از آن استفاده شده است", "BOM دستورهای ساخت");

            SoftDelete<Bom>(x => x.EquipmentBomId == equipmentBom);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.EquipmentBomAsync(u, equipmentBom, ct)), "a deleted BOM no longer blocks");
        }

        // ------------------------------------------------------------------------------------------------ work order BOMs and assemblies

        [TestMethod]
        public async Task ABomOrAssemblyThatAProductionOrderOrTheScheduleUsesIsRefused()
        {
            const string bomHead = "حذف BOM ممکن نیست؛ از آن استفاده شده است";
            const string assemblyHead = "حذف مجموعه ممکن نیست؛ از آن یا از اجزای آن استفاده شده است";
            int workOrder = NewWorkOrder(NewContract());

            int assemblyA = NewAssembly(workOrder);
            int bomA = NewBom(assemblyA);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.BomAsync(u, bomA, ct)));
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.AssemblyAsync(u, assemblyA, ct)));

            // a production order for the BOM blocks the BOM and the assembly that holds it
            Put(u => AppHost.Add(u, new MfProductionOrder { WorkOrderId = workOrder, ContractId = 0, BomId = bomA }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.BomAsync(u, bomA, ct)), bomHead, "دستورهای تولید");
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.AssemblyAsync(u, assemblyA, ct)), assemblyHead, "دستورهای تولید");

            // a WBS node of the schedule that points to the BOM
            int assemblyB = NewAssembly(workOrder);
            int bomB = NewBom(assemblyB);
            Put(u => AppHost.Add(u, new PcWbsNode { ContractId = 1, BomId = bomB }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.BomAsync(u, bomB, ct)), bomHead, "ساختار شکست کار (WBS)");

            // a WBS node of the schedule that points to the assembly itself blocks the assembly, not its BOM
            int assemblyC = NewAssembly(workOrder);
            int bomC = NewBom(assemblyC);
            Put(u => AppHost.Add(u, new PcWbsNode { ContractId = 1, AssemblyId = assemblyC }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.AssemblyAsync(u, assemblyC, ct)), assemblyHead, "ساختار شکست کار (WBS)");
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.BomAsync(u, bomC, ct)));

            // a production order for a process of the BOM blocks the BOM
            int assemblyD = NewAssembly(workOrder);
            int bomD = NewBom(assemblyD);
            int processId = 0;
            H.Write(u => { var p = AppHost.Add(u, new ProcessEntity { BomId = bomD, Title = "process" }); u.SaveChanges(); processId = p.Id; }, Pmo);
            Put(u => AppHost.Add(u, new MfProductionOrder { WorkOrderId = workOrder, ContractId = 0, ProcessId = processId }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.BomAsync(u, bomD, ct)), bomHead, "دستورهای تولید");

            SoftDelete<MfProductionOrder>(x => x.BomId == bomA);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.BomAsync(u, bomA, ct)), "a deleted production order no longer blocks");
        }

        // ------------------------------------------------------------------------------------------------ R1a2 (M3): work orders, buy orders, providers, processes, statements, guaranties

        private sealed class WorkOrderCase
        {
            public string Name;
            public string Label;
            public Action<IUnitOfWork, int, int> Seed;   // (unit of work, contract id, work order id)
        }

        private static readonly WorkOrderCase[] WorkOrderCasesM3 =
        {
            new WorkOrderCase { Name = "payment milestone", Label = "نقاط پرداخت", Seed = (u, c, w) => AppHost.Add(u, new PcPaymentMilestone { ContractId = c, WorkOrderId = w }) },
            new WorkOrderCase { Name = "penalty assessment", Label = "ارزیابی‌های جریمه", Seed = (u, c, w) => AppHost.Add(u, new PcPenaltyAssessment { ContractId = c, WorkOrderId = w }) },
            new WorkOrderCase { Name = "project code of the work order", Label = "کد پروژه‌ی دستور ساخت", Seed = (u, c, w) => AppHost.Add(u, new PcWorkOrderInfo { ContractId = c, WorkOrderId = w }) },
            new WorkOrderCase { Name = "purchase order line", Label = "سفارش‌های خرید", Seed = (u, c, w) => AppHost.Add(u, new PurchaseOrderLine { PurchaseOrderId = 1, WorkOrderId = w }) },
            new WorkOrderCase { Name = "customer complaint", Label = "شکایت‌های مشتری", Seed = (u, c, w) => AppHost.Add(u, new CustomerComplaint { WorkOrderId = w }) },
            new WorkOrderCase { Name = "controlled document", Label = "مدارک کنترل‌شده", Seed = (u, c, w) => AppHost.Add(u, new ControlledDocument { WorkOrderId = w }) },
        };

        private const string WorkOrderHead = "حذف دستور ساخت ممکن نیست؛ برای آن سوابق ثبت شده است";

        [TestMethod]
        public async Task AWorkOrderWithTheRecordsOfTheReviewIsRefusedAndSoIsItsContract()
        {
            var failures = new List<string>();
            foreach (WorkOrderCase c in WorkOrderCasesM3)
            {
                int contract = NewContract();
                int workOrder = NewWorkOrder(contract);
                Put(u => c.Seed(u, contract, workOrder));
                string refusal = await Guard((u, ct) => LegacyDeleteGuard.WorkOrderAsync(u, workOrder, ct));
                if (refusal == null) failures.Add(c.Name + ": the work order was not refused");
                else if (!refusal.StartsWith(WorkOrderHead)) failures.Add(c.Name + ": wrong head: " + refusal);
                else if (!refusal.Contains(c.Label)) failures.Add(c.Name + ": does not name \"" + c.Label + "\": " + refusal);

                // the work orders of a contract are deleted with it, so the contract is in the same position
                string contractRefusal = await Guard((u, ct) => LegacyDeleteGuard.ContractAsync(u, contract, ct));
                if (contractRefusal == null) failures.Add(c.Name + ": the contract was not refused");
                else if (!contractRefusal.Contains(c.Label)) failures.Add(c.Name + ": the contract refusal does not name \"" + c.Label + "\": " + contractRefusal);
            }
            Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
        }

        private static int NewBuyOrder(int workOrderId, Action<BuyOrder> progress = null)
        {
            int id = 0;
            H.Write(uow =>
            {
                var b = AppHost.Add(uow, new BuyOrder { Title = "buy order", WorkOrderId = workOrderId });
                progress?.Invoke(b);
                uow.SaveChanges();
                id = b.Id;
            }, Pmo);
            return id;
        }

        private static int NewPurchaseRequest()
        {
            int id = 0;
            H.Write(uow => { var r = AppHost.Add(uow, new PurchaseRequest { Number = "PR-" + Guid.NewGuid().ToString("N").Substring(0, 6) }); uow.SaveChanges(); id = r.Id; }, Pmo);
            return id;
        }

        [TestMethod]
        public async Task ABuyOrderThatHasStartedOrHasDocumentsBlocksItsWorkOrderAndItsContract()
        {
            const string started = "سفارش‌های خرید پروژه که فرایندشان شروع شده است";
            int contract = NewContract();

            // a buy order that nobody has touched goes with its work order (that is what the delete did before)
            int fresh = NewWorkOrder(contract);
            NewBuyOrder(fresh);
            int forAssembler = NewWorkOrder(contract);
            NewBuyOrder(forAssembler, b => b.NewForAssembler());
            int forConstructor = NewWorkOrder(contract);
            NewBuyOrder(forConstructor, b => b.NewForConstructor());
            foreach (int id in new[] { fresh, forAssembler, forConstructor })
                Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.WorkOrderAsync(u, id, ct)), "a buy order at its first stage does not block");

            // a buy order past its first stage does, at any later stage
            foreach (Action<BuyOrder> advance in new Action<BuyOrder>[] { b => b.InquiriesAdded("fin"), b => b.TechnicalScoreAdded(), b => b.WinnerSelected(), b => b.FinanceDocumentSetted(null), b => b.CommercialBought(1), b => b.WarehouseFinallyAccepted() })
            {
                int workOrder = NewWorkOrder(contract);
                NewBuyOrder(workOrder, advance);
                AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.WorkOrderAsync(u, workOrder, ct)), WorkOrderHead, started);
            }

            // a stock document that points to the buy order blocks it even at the first stage
            int withDocument = NewWorkOrder(contract);
            int documentBuyOrder = NewBuyOrder(withDocument);
            Put(u => AppHost.Add(u, new InventoryDocument { BuyOrderId = documentBuyOrder }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.WorkOrderAsync(u, withDocument, ct)), WorkOrderHead, "اسناد انبار");

            // so does a line of a purchase request that buys for it; a line of a deleted request does not
            int withLine = NewWorkOrder(contract);
            int lineBuyOrder = NewBuyOrder(withLine);
            int request = NewPurchaseRequest();
            Put(u => AppHost.Add(u, new PurchaseRequestLine { PurchaseRequestId = request, BuyOrderId = lineBuyOrder }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.WorkOrderAsync(u, withLine, ct)), WorkOrderHead, "درخواست‌های خرید");
            SoftDelete<PurchaseRequest>(x => x.Id == request);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.WorkOrderAsync(u, withLine, ct)), "the line of a deleted request no longer blocks");

            // the contract is refused while any of its work orders is
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.ContractAsync(u, contract, ct)), ContractHead, started);

            // a deleted buy order no longer blocks
            int deleted = NewWorkOrder(NewContract());
            int deletedBuyOrder = NewBuyOrder(deleted, b => b.InquiriesAdded("fin"));
            Assert.IsNotNull(await Guard((u, ct) => LegacyDeleteGuard.WorkOrderAsync(u, deleted, ct)));
            SoftDelete<BuyOrder>(x => x.Id == deletedBuyOrder);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.WorkOrderAsync(u, deleted, ct)));
        }

        [TestMethod]
        public async Task TheAssembliesBomsAndProcessesThatAWorkOrderTakesWithItAreCheckedToo()
        {
            int contract = NewContract();
            int workOrder = NewWorkOrder(contract);
            int assembly = NewAssembly(workOrder);
            int bom = NewBom(assembly);
            int process = NewProcess(bom);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.WorkOrderAsync(u, workOrder, ct)), "a work order with an empty assembly, BOM and process may be deleted");

            // the schedule points to the assembly, the BOM or the process with a WBS node that does not name the work order
            foreach (PcWbsNode node in new[] { new PcWbsNode { ContractId = 1, AssemblyId = assembly }, new PcWbsNode { ContractId = 1, BomId = bom }, new PcWbsNode { ContractId = 1, ProcessId = process } })
            {
                PcWbsNode seeded = node;
                Put(u => AppHost.Add(u, seeded));
                AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.WorkOrderAsync(u, workOrder, ct)), WorkOrderHead, "ساختار شکست کار (WBS)");
                AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.ContractAsync(u, contract, ct)), ContractHead, "ساختار شکست کار (WBS)");
                SoftDelete<PcWbsNode>(x => x.AssemblyId == assembly || x.BomId == bom || x.ProcessId == process);
                Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.WorkOrderAsync(u, workOrder, ct)), "the node is deleted: nothing is in the way");
            }

            // a production order for the process of the BOM of the work order (the order does not name the work order itself)
            Put(u => AppHost.Add(u, new MfProductionOrder { WorkOrderId = 0, ContractId = 0, ProcessId = process }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.WorkOrderAsync(u, workOrder, ct)), WorkOrderHead, "دستورهای تولید");
        }

        private sealed class ProviderCase
        {
            public string Name;
            public string Label;
            public Action<IUnitOfWork, int> Seed;
        }

        private static readonly ProviderCase[] ProviderCasesM3 =
        {
            new ProviderCase { Name = "evaluation", Label = "ارزیابی‌های تأمین‌کننده", Seed = (u, id) => AppHost.Add(u, new SupplierEvaluation { ProviderId = id }) },
            new ProviderCase { Name = "entry of the approved vendor list", Label = "پرونده‌ی تأمین‌کننده", Seed = (u, id) => AppHost.Add(u, new SupplierProfile { ProviderId = id }) },
            new ProviderCase { Name = "qualified person", Label = "افراد واجد صلاحیت", Seed = (u, id) => AppHost.Add(u, new QualifiedPerson { ProviderId = id, Name = "welder" }) },
            new ProviderCase { Name = "party of the letters", Label = "طرف‌های مکاتبات", Seed = (u, id) => AppHost.Add(u, new OfExternalParty { ProviderId = id, Name = "supplier" }) },
        };

        [TestMethod]
        public async Task AProviderWithEvaluationsAProfileQualifiedPeopleOrALettersPartyIsRefused()
        {
            const string head = "حذف تأمین‌کننده ممکن نیست؛ برای او سوابق ثبت شده است";
            var failures = new List<string>();
            foreach (ProviderCase c in ProviderCasesM3)
            {
                int id = NewProvider();
                Put(u => c.Seed(u, id));
                string refusal = await Guard((u, ct) => LegacyDeleteGuard.ProviderAsync(u, id, ct));
                if (refusal == null) failures.Add(c.Name + ": not refused");
                else if (!refusal.StartsWith(head)) failures.Add(c.Name + ": wrong head: " + refusal);
                else if (!refusal.Contains(c.Label)) failures.Add(c.Name + ": does not name \"" + c.Label + "\": " + refusal);
            }
            Assert.AreEqual(0, failures.Count, string.Join("\n", failures));

            // a deleted evaluation no longer blocks (the profile has no delete flag and stays in the way)
            int provider = NewProvider();
            Put(u => AppHost.Add(u, new SupplierEvaluation { ProviderId = provider }));
            SoftDelete<SupplierEvaluation>(x => x.ProviderId == provider);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.ProviderAsync(u, provider, ct)));
        }

        private static int NewProcess(int bomId)
        {
            int id = 0;
            H.Write(u => { var p = AppHost.Add(u, new ProcessEntity { BomId = bomId, Title = "process" }); u.SaveChanges(); id = p.Id; }, Pmo);
            return id;
        }

        [TestMethod]
        public async Task AProcessThatAProductionOrderOrTheScheduleUsesIsRefused()
        {
            const string head = "حذف فرایند ممکن نیست؛ از آن استفاده شده است";
            int workOrder = NewWorkOrder(NewContract());
            int bom = NewBom(NewAssembly(workOrder));

            int free = NewProcess(bom);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.ProcessAsync(u, free, ct)));

            int withOrder = NewProcess(bom);
            Put(u => AppHost.Add(u, new MfProductionOrder { WorkOrderId = workOrder, ContractId = 0, ProcessId = withOrder }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.ProcessAsync(u, withOrder, ct)), head, "دستورهای تولید");

            int withNode = NewProcess(bom);
            Put(u => AppHost.Add(u, new PcWbsNode { ContractId = 1, ProcessId = withNode }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.ProcessAsync(u, withNode, ct)), head, "ساختار شکست کار (WBS)");

            // the other processes of the BOM are not in the way, but the BOM that holds the two used ones is
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.ProcessAsync(u, free, ct)));
            string bomRefusal = await Guard((u, ct) => LegacyDeleteGuard.BomAsync(u, bom, ct));
            Assert.IsNotNull(bomRefusal);
            Assert.IsTrue(bomRefusal.Contains("دستورهای تولید") && bomRefusal.Contains("ساختار شکست کار (WBS)"), bomRefusal);

            SoftDelete<PcWbsNode>(x => x.ProcessId == withNode);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.ProcessAsync(u, withNode, ct)), "a deleted node no longer blocks");
            SoftDelete<MfProductionOrder>(x => x.ProcessId == withOrder);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.ProcessAsync(u, withOrder, ct)), "a deleted production order no longer blocks");
        }

        private static int NewStatement(int contractId)
        {
            int id = 0;
            H.Write(u =>
            {
                var s = AppHost.Add(u, new ProvisionalStatement { ContractId = contractId, Number = 1, Date = DateTimeOffset.Now, Price = 1000m, AcceptedPrice = 900m, Type = "progress", Status = "draft" });
                u.SaveChanges();
                id = s.Id;
            }, Pmo);
            return id;
        }

        [TestMethod]
        public async Task AProvisionalStatementThatAPaymentPointOrAnInvoiceUsesIsNeitherEditedNorDeleted()
        {
            const string deleteHead = "حذف صورت‌وضعیت ممکن نیست؛ از آن استفاده شده است";
            const string updateHead = "ویرایش صورت‌وضعیت ممکن نیست؛ از آن استفاده شده است";
            int contract = NewContract();

            int free = NewStatement(contract);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.ProvisionalStatementAsync(u, free, true, ct)));
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.ProvisionalStatementAsync(u, free, false, ct)));

            int withMilestone = NewStatement(contract);
            Put(u => AppHost.Add(u, new PcPaymentMilestone { ContractId = contract, ProvisionalStatementId = withMilestone }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.ProvisionalStatementAsync(u, withMilestone, true, ct)), deleteHead, "نقاط پرداخت");
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.ProvisionalStatementAsync(u, withMilestone, false, ct)), updateHead, "نقاط پرداخت");

            int withInvoice = NewStatement(contract);
            Put(u => AppHost.Add(u, new FnSalesInvoice { ContractId = contract, CustomerId = 1, ProvisionalStatementId = withInvoice }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.ProvisionalStatementAsync(u, withInvoice, true, ct)), deleteHead, "فاکتورهای فروش");
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.ProvisionalStatementAsync(u, withInvoice, false, ct)), updateHead, "فاکتورهای فروش");

            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.ProvisionalStatementAsync(u, free, true, ct)), "the other statements of the contract are not in the way");
            SoftDelete<PcPaymentMilestone>(x => x.ProvisionalStatementId == withMilestone);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.ProvisionalStatementAsync(u, withMilestone, true, ct)), "a deleted payment point no longer blocks");
        }

        private static int NewContractGuaranty(int contractId)
        {
            int id = 0;
            H.Write(u => { var g = AppHost.Add(u, new ContractGuaranty { Title = "guaranty", ContractEntityId = contractId }); u.SaveChanges(); id = g.Id; }, Pmo);
            return id;
        }

        [TestMethod]
        public async Task ALegacyGuarantyThatProjectControlKeepsARecordForIsRefused()
        {
            const string head = "حذف ضمانت‌نامه ممکن نیست؛ برای آن سوابق ثبت شده است";
            const string label = "ضمانت‌نامه‌های کنترل پروژه";
            int contract = NewContract();
            int guaranty = NewContractGuaranty(contract);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.LegacyGuarantyAsync(u, PcGuaranteeSource.ContractGuaranty, guaranty, ct)));
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.LegacyGuarantyAsync(u, PcGuaranteeSource.TenderGuaranty, guaranty, ct)));

            Put(u => AppHost.Add(u, new PcGuaranteeInfo { SourceKind = PcGuaranteeSource.ContractGuaranty, SourceId = guaranty, ContractId = contract }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.LegacyGuarantyAsync(u, PcGuaranteeSource.ContractGuaranty, guaranty, ct)), head, label);
            // the record belongs to the contract guaranty with that number, not to a tender guaranty that happens to have the same number
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.LegacyGuarantyAsync(u, PcGuaranteeSource.TenderGuaranty, guaranty, ct)));
            // and a guaranty of another number is not in the way
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.LegacyGuarantyAsync(u, PcGuaranteeSource.ContractGuaranty, guaranty + 1000, ct)));

            int tenderGuaranty = 0;
            H.Write(u => { var g = AppHost.Add(u, new TenderGuaranty { Title = "tender guaranty", TenderId = 1 }); u.SaveChanges(); tenderGuaranty = g.Id; }, Pmo);
            Put(u => AppHost.Add(u, new PcGuaranteeInfo { SourceKind = PcGuaranteeSource.TenderGuaranty, SourceId = tenderGuaranty }));
            AssertRefused(await Guard((u, ct) => LegacyDeleteGuard.LegacyGuarantyAsync(u, PcGuaranteeSource.TenderGuaranty, tenderGuaranty, ct)), head, label);

            SoftDelete<PcGuaranteeInfo>(x => x.SourceKind == PcGuaranteeSource.ContractGuaranty && x.SourceId == guaranty);
            Assert.IsNull(await Guard((u, ct) => LegacyDeleteGuard.LegacyGuarantyAsync(u, PcGuaranteeSource.ContractGuaranty, guaranty, ct)), "a deleted record no longer blocks");
        }

        // ------------------------------------------------------------------------------------------------ the real services

        private static Task<T> AsPmo<T>(Func<IServiceProvider, Task<T>> call) => H.As(Pmo, call);

        private static bool IsDeleted<T>(int id) where T : Entities.Model.BaseEntity => H.Read(u => u.Set<T>().Single(x => x.Id == id).Deleted);

        /// <summary>
        /// The legacy deletes answer by re-reading the record they just flagged as deleted: the contract delete then answers "not found" and the provider
        /// delete throws a NullReferenceException, both after the soft delete was committed. R1a does not change that (a finding for the front end, which
        /// should refresh the list and not read the answer), so the tests read the row and not the success flag of a delete that went through.
        /// </summary>
        private static async Task DeleteAndIgnoreTheLegacyAnswer(Func<IServiceProvider, Task> delete)
        {
            try { await H.As(Pmo, async p => { await delete(p); return 0; }); }
            catch (NullReferenceException) { }
        }

        [TestMethod]
        public async Task TheContractServiceRefusesInPersianAndLeavesTheRowThenDeletesAnEmptyContract()
        {
            int blocked = NewContract();
            Put(u => AppHost.Add(u, new PcContractTerms { ContractId = blocked }));
            ResponseDto<ViewModels.NasbNiro.ContractDtos.ResponseContractDto> refused =
                await AsPmo(p => p.GetRequiredService<IContractService>().DeleteContractAsync(blocked, CancellationToken.None).AsTask());
            Assert.IsFalse(refused.Success);
            Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, refused.HttpStatusCode);
            Assert.IsTrue(refused.Message.StartsWith(ContractHead), refused.Message);
            Assert.IsFalse(IsDeleted<ContractEntity>(blocked), "the row is untouched");

            int empty = NewContract();
            await DeleteAndIgnoreTheLegacyAnswer(p => p.GetRequiredService<IContractService>().DeleteContractAsync(empty, CancellationToken.None).AsTask());
            Assert.IsTrue(IsDeleted<ContractEntity>(empty), "an empty contract is deleted as before");

            // the soft delete of the contract is in the audit trail with the user
            var trail = H.Read(u => u.Set<FnAuditLog>().Where(l => l.EntityType == nameof(ContractEntity) && l.EntityId == empty && l.Action == "Modified").ToList());
            Assert.AreEqual(1, trail.Count);
            Assert.AreEqual(7, trail[0].UserId);

            // the refused contract left no audit row for a delete
            Assert.AreEqual(0, H.Read(u => u.Set<FnAuditLog>().Count(l => l.EntityType == nameof(ContractEntity) && l.EntityId == blocked)));
        }

        [TestMethod]
        public async Task TheCollectionDeleteUsesTheSameGuard()
        {
            int blocked = NewContract();
            Put(u => AppHost.Add(u, new PurchaseOrder { ContractId = blocked, ProviderId = UnrealProvider }));
            int empty = NewContract();

            ResponseDto<IEnumerable<DeleteContractResultDto>> result =
                await AsPmo(p => p.GetRequiredService<IContractService>().DeleteContractCollectionAsync(new[] { blocked, empty }, CancellationToken.None).AsTask());
            List<DeleteContractResultDto> items = result.Data.ToList();
            Assert.AreEqual(2, items.Count);
            Assert.IsFalse(items.Single(x => x.Id == blocked).Success);
            Assert.IsTrue(items.Single(x => x.Id == blocked).Message.StartsWith(ContractHead), items.Single(x => x.Id == blocked).Message);
            Assert.IsFalse(IsDeleted<ContractEntity>(blocked), "the blocked contract stays");
            Assert.IsTrue(IsDeleted<ContractEntity>(empty), "the empty contract of the same call is deleted");
        }

        [TestMethod]
        public async Task TheProviderAndWorkOrderServicesRefuseInPersianAndDeleteTheEmptyOnes()
        {
            int providerBlocked = NewProvider();
            Put(u => AppHost.Add(u, new PurchaseOrder { ProviderId = providerBlocked }));
            var refusedProvider = await AsPmo(p => p.GetRequiredService<IProviderService>().DeleteProviderAsync(providerBlocked, CancellationToken.None).AsTask());
            Assert.IsFalse(refusedProvider.Success);
            Assert.IsTrue(refusedProvider.Message.StartsWith("حذف تأمین‌کننده ممکن نیست"), refusedProvider.Message);
            Assert.IsFalse(IsDeleted<Provider>(providerBlocked));

            int providerEmpty = NewProvider();
            await DeleteAndIgnoreTheLegacyAnswer(p => p.GetRequiredService<IProviderService>().DeleteProviderAsync(providerEmpty, CancellationToken.None).AsTask());
            Assert.IsTrue(IsDeleted<Provider>(providerEmpty));

            int contract = NewContract();
            int workOrderBlocked = NewWorkOrder(contract);
            Put(u => AppHost.Add(u, new MfProductionOrder { WorkOrderId = workOrderBlocked, ContractId = contract }));
            var refusedWorkOrder = await AsPmo(p => p.GetRequiredService<IWorkOrderService>().DeleteWorkOrderAsync(workOrderBlocked, CancellationToken.None).AsTask());
            Assert.IsFalse(refusedWorkOrder.Success);
            Assert.IsTrue(refusedWorkOrder.Message.StartsWith("حذف دستور ساخت ممکن نیست"), refusedWorkOrder.Message);
            Assert.IsFalse(IsDeleted<WorkOrder>(workOrderBlocked));

            int workOrderEmpty = NewWorkOrder(contract);
            await DeleteAndIgnoreTheLegacyAnswer(p => p.GetRequiredService<IWorkOrderService>().DeleteWorkOrderAsync(workOrderEmpty, CancellationToken.None).AsTask());
            Assert.IsTrue(IsDeleted<WorkOrder>(workOrderEmpty));
        }

        [TestMethod]
        public async Task TheEquipmentBomAndAssemblyServicesRefuseWhenInUse()
        {
            int equipment = NewEquipment();
            int equipmentAssembly = NewEquipmentAssembly(equipment);
            int equipmentBom = NewEquipmentBom(equipmentAssembly);
            int workOrder = NewWorkOrder(NewContract());
            int assembly = NewAssembly(workOrder);
            NewBom(assembly, equipmentBom);

            var refusedEquipment = await AsPmo(p => p.GetRequiredService<IEquipmentService>().DeleteEquipmentAsync(equipment, CancellationToken.None).AsTask());
            Assert.IsFalse(refusedEquipment.Success);
            Assert.IsTrue(refusedEquipment.Message.StartsWith("حذف تجهیز ممکن نیست"), refusedEquipment.Message);
            Assert.IsFalse(IsDeleted<Equipment>(equipment));

            int emptyEquipment = NewEquipment();
            await DeleteAndIgnoreTheLegacyAnswer(p => p.GetRequiredService<IEquipmentService>().DeleteEquipmentAsync(emptyEquipment, CancellationToken.None).AsTask());
            Assert.IsTrue(IsDeleted<Equipment>(emptyEquipment));
        }

        // ------------------------------------------------------------------------------------------------ R1a2 (M3): the services

        private static System.Text.Json.JsonElement Field(string json, string name)
        {
            Assert.IsNotNull(json, "the trail row has no values");
            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json))
                return doc.RootElement.GetProperty(name).Clone();
        }

        [TestMethod]
        public async Task TheProcessServiceRefusesAProcessInUseAndTheCollectionDeleteDoesToo()
        {
            int workOrder = NewWorkOrder(NewContract());
            int bom = NewBom(NewAssembly(workOrder));
            int used = NewProcess(bom);
            Put(u => AppHost.Add(u, new PcWbsNode { ContractId = 1, ProcessId = used }));
            int free = NewProcess(bom);

            var refused = await AsPmo(p => p.GetRequiredService<IProcessService>().DeleteProcessAsync(used, CancellationToken.None).AsTask());
            Assert.IsFalse(refused.Success);
            Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, refused.HttpStatusCode);
            Assert.IsTrue(refused.Message.StartsWith("حذف فرایند ممکن نیست"), refused.Message);
            Assert.IsTrue(refused.Message.Contains("ساختار شکست کار (WBS)"), refused.Message);
            Assert.IsFalse(IsDeleted<ProcessEntity>(used), "the row is untouched");

            var many = await AsPmo(p => p.GetRequiredService<IProcessService>().DeleteProcessCollectionAsync(new[] { used, free }, CancellationToken.None).AsTask());
            List<DeleteProcessResultDto> items = many.Data.ToList();
            Assert.IsFalse(items.Single(x => x.Id == used).Success);
            Assert.IsTrue(items.Single(x => x.Id == used).Message.StartsWith("حذف فرایند ممکن نیست"), items.Single(x => x.Id == used).Message);
            Assert.IsFalse(IsDeleted<ProcessEntity>(used));
            Assert.IsTrue(IsDeleted<ProcessEntity>(free), "the free process of the same call is deleted");
        }

        [TestMethod]
        public async Task TheProvisionalStatementServiceRefusesAStatementInUseAndLeavesATrailRowForTheOnesItChanges()
        {
            int contract = NewContract();
            int used = NewStatement(contract);
            Put(u => AppHost.Add(u, new PcPaymentMilestone { ContractId = contract, ProvisionalStatementId = used }));

            UpdateProStatDto Edit(int id, decimal price) => new UpdateProStatDto { Id = id, ContractId = contract, Number = 1, Date = DateTimeOffset.Now, Price = price, AcceptedPrice = 900m, Type = "progress", Status = "draft" };

            var refusedUpdate = await AsPmo(p => p.GetRequiredService<IProStatService>().UpdateProStatAsync(Edit(used, 5000m), CancellationToken.None).AsTask());
            Assert.IsFalse(refusedUpdate.Success);
            Assert.IsTrue(refusedUpdate.Message.StartsWith("ویرایش صورت‌وضعیت ممکن نیست"), refusedUpdate.Message);
            Assert.AreEqual(1000m, H.Read(u => u.Set<ProvisionalStatement>().Single(x => x.Id == used).Price), "the amount is untouched");

            var refusedDelete = await AsPmo(p => p.GetRequiredService<IProStatService>().DeleteProStatAsync(new DeleteProStatDto { Id = used, ContractId = contract }, CancellationToken.None).AsTask());
            Assert.IsFalse(refusedDelete.Success);
            Assert.IsTrue(refusedDelete.Message.StartsWith("حذف صورت‌وضعیت ممکن نیست"), refusedDelete.Message);
            Assert.IsFalse(IsDeleted<ProvisionalStatement>(used));
            Assert.AreEqual(0, H.Read(u => u.Set<FnAuditLog>().Count(l => l.EntityType == nameof(ProvisionalStatement) && l.EntityId == used && l.Action != "Added")), "a refused call leaves no row");

            // a statement nothing refers to is changed and deleted, and both leave a row with the user
            int free = NewStatement(contract);
            var updated = await AsPmo(p => p.GetRequiredService<IProStatService>().UpdateProStatAsync(Edit(free, 1500m), CancellationToken.None).AsTask());
            Assert.IsTrue(updated.Success, updated.Message);
            Assert.AreEqual(1500m, H.Read(u => u.Set<ProvisionalStatement>().Single(x => x.Id == free).Price));
            var deleted = await AsPmo(p => p.GetRequiredService<IProStatService>().DeleteProStatAsync(new DeleteProStatDto { Id = free, ContractId = contract }, CancellationToken.None).AsTask());
            Assert.IsTrue(deleted.Success, deleted.Message);
            Assert.IsTrue(IsDeleted<ProvisionalStatement>(free));

            List<FnAuditLog> rows = H.Read(u => u.Set<FnAuditLog>().Where(l => l.EntityType == nameof(ProvisionalStatement) && l.EntityId == free && l.Action == "Modified").OrderBy(l => l.Id).ToList());
            Assert.AreEqual(2, rows.Count, "one row for the edit and one for the delete");
            Assert.AreEqual(7, rows[0].UserId);
            Assert.AreEqual(1000m, Field(rows[0].BeforeJson, "Price").GetDecimal());
            Assert.AreEqual(1500m, Field(rows[0].AfterJson, "Price").GetDecimal());
            Assert.IsTrue(rows[0].Summary.StartsWith("صورت‌وضعیت"), rows[0].Summary);
            Assert.AreEqual(7, rows[1].UserId);
            Assert.IsFalse(Field(rows[1].BeforeJson, "Deleted").GetBoolean());
            Assert.IsTrue(Field(rows[1].AfterJson, "Deleted").GetBoolean());
        }

        [TestMethod]
        public async Task AProvisionalStatementForAContractThatDoesNotExistIsNotSaved()
        {
            int before = H.Read(u => u.Set<ProvisionalStatement>().Count(x => x.ContractId == 987654));
            var answer = await AsPmo(p => p.GetRequiredService<IProStatService>().AddProStatAsync(
                new AddProStatDto { ContractId = 987654, Number = 1, Date = DateTimeOffset.Now, Price = 10m, AcceptedPrice = 10m, Type = "progress", Status = "draft" }, CancellationToken.None).AsTask());
            Assert.IsFalse(answer.Success, "the answer was built but not returned before R1a2");
            Assert.AreEqual(before, H.Read(u => u.Set<ProvisionalStatement>().Count(x => x.ContractId == 987654)), "no row for a missing contract");
        }

        [TestMethod]
        public async Task TheGuarantyDeletesOfAContractAndATenderRefuseWhenProjectControlKeepsARecord()
        {
            int contract = NewContract();
            int used = NewContractGuaranty(contract);
            Put(u => AppHost.Add(u, new PcGuaranteeInfo { SourceKind = PcGuaranteeSource.ContractGuaranty, SourceId = used, ContractId = contract }));
            var refused = await AsPmo(p => p.GetRequiredService<IContractService>().DeleteContractGuarantyAsync(used, CancellationToken.None).AsTask());
            Assert.IsFalse(refused.Success);
            Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, refused.HttpStatusCode);
            Assert.IsTrue(refused.Message.StartsWith("حذف ضمانت‌نامه ممکن نیست"), refused.Message);
            Assert.IsFalse(IsDeleted<ContractGuaranty>(used), "the row is untouched");

            int free = NewContractGuaranty(contract);
            await DeleteAndIgnoreTheLegacyAnswer(p => p.GetRequiredService<IContractService>().DeleteContractGuarantyAsync(free, CancellationToken.None).AsTask());
            Assert.IsTrue(IsDeleted<ContractGuaranty>(free), "a guaranty nothing refers to is deleted as before");

            int usedTender = 0, freeTender = 0;
            H.Write(u => { var g = AppHost.Add(u, new TenderGuaranty { Title = "used", TenderId = 1 }); var f = AppHost.Add(u, new TenderGuaranty { Title = "free", TenderId = 1 }); u.SaveChanges(); usedTender = g.Id; freeTender = f.Id; }, Pmo);
            Put(u => AppHost.Add(u, new PcGuaranteeInfo { SourceKind = PcGuaranteeSource.TenderGuaranty, SourceId = usedTender }));
            var refusedTender = await AsPmo(p => p.GetRequiredService<ITenderService>().DeleteTenderGuarantyAsync(usedTender, CancellationToken.None).AsTask());
            Assert.IsFalse(refusedTender.Success);
            Assert.IsTrue(refusedTender.Message.StartsWith("حذف ضمانت‌نامه ممکن نیست"), refusedTender.Message);
            Assert.IsFalse(IsDeleted<TenderGuaranty>(usedTender));
            await DeleteAndIgnoreTheLegacyAnswer(p => p.GetRequiredService<ITenderService>().DeleteTenderGuarantyAsync(freeTender, CancellationToken.None).AsTask());
            Assert.IsTrue(IsDeleted<TenderGuaranty>(freeTender));
        }

        [TestMethod]
        public async Task TheWorkOrderServiceRefusesAWorkOrderWhoseBuyOrderHasStarted()
        {
            int contract = NewContract();
            int workOrder = NewWorkOrder(contract);
            NewBuyOrder(workOrder, b => b.InquiriesAdded("fin"));
            var refused = await AsPmo(p => p.GetRequiredService<IWorkOrderService>().DeleteWorkOrderAsync(workOrder, CancellationToken.None).AsTask());
            Assert.IsFalse(refused.Success);
            Assert.IsTrue(refused.Message.StartsWith("حذف دستور ساخت ممکن نیست"), refused.Message);
            Assert.IsTrue(refused.Message.Contains("سفارش‌های خرید پروژه که فرایندشان شروع شده است"), refused.Message);
            Assert.IsFalse(IsDeleted<WorkOrder>(workOrder));
            Assert.IsFalse(H.Read(u => u.Set<BuyOrder>().Single(x => x.WorkOrderId == workOrder).Deleted), "the buy order is not deleted with it");

            int untouched = NewWorkOrder(contract);
            int buyOrder = NewBuyOrder(untouched);
            await DeleteAndIgnoreTheLegacyAnswer(p => p.GetRequiredService<IWorkOrderService>().DeleteWorkOrderAsync(untouched, CancellationToken.None).AsTask());
            Assert.IsTrue(IsDeleted<WorkOrder>(untouched), "a work order whose buy order has not started is deleted as before");
            Assert.IsTrue(IsDeleted<BuyOrder>(buyOrder), "and its buy order goes with it");
        }
    }
}
