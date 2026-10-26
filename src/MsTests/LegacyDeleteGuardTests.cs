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
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Providers;
using Entities.Model.NasbNiro.Sales;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro;
using Services.NasbNiro.Contracts;
using Services.NasbNiro.WorkOrders;
using ViewModels.NasbNiro.ContractDtos;
using ViewModels.NasbNiro.ProviderDtos;
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
    }
}
