using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProjectBalance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProjectBalance;
using ViewModels.NasbNiro.InventoryDtos;

namespace MsTests
{
    /// <summary>PB5 fix round: the freight reversal mirrors its approval (GL and item cost agree), transfers into a project warehouse use the reservation, the setting rules and the stored default upgrade.</summary>
    [TestClass]
    public class Pb5FixTests
    {
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(1, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Fm = AppHost.Principal(62, "finance.manager", "FINANCEMANAGER");
        private static readonly CancellationToken Ct = CancellationToken.None;

        private static T Seed<T>(AppHost host, T entity) where T : class
        {
            host.Write(uow => { AppHost.Add(uow, entity); uow.SaveChanges(); });
            return entity;
        }

        private static async Task<int> ReceiptAsync(AppHost host, int warehouse, int item, decimal qty, decimal price)
        {
            return await host.As(Pmo, async p =>
            {
                var svc = p.GetRequiredService<IInventoryDocumentService>();
                var saved = await svc.SaveDraftAsync(new SaveDocumentDto
                {
                    Type = InventoryDocumentType.PurchaseReceipt, DocumentDate = DateTime.Today, WarehouseId = warehouse,
                    Lines = new List<SaveDocumentLineDto> { new SaveDocumentLineDto { ItemId = item, Quantity = qty, UnitPrice = price } },
                }, Ct);
                Assert.IsTrue(saved.Success, "receipt draft: " + saved.Message);
                var confirmed = await svc.ConfirmAsync(saved.Data.Id, Ct);
                Assert.IsTrue(confirmed.Success, "receipt confirm: " + confirmed.Message);
                return saved.Data.Id;
            });
        }

        /// <summary>Inventory account balance of the item (events that left the system count, events never sent do not) against the value the item cost holds.</summary>
        private static (decimal Ledger, decimal Cost, decimal Quantity) Books(AppHost host, string itemCode, int itemId)
        {
            string role = AccountRoles.InventoryOf(itemCode);
            return host.Read(u =>
            {
                var lines = u.Set<AccountingEventLine>().Where(l => l.AccountRole == role && l.Event.Status != AccountingEventStatus.NotRequired).Select(l => new { l.Debit, l.Credit }).ToList();
                ItemCost cost = u.Set<ItemCost>().Single(c => c.ItemId == itemId);
                return (lines.Sum(l => l.Debit) - lines.Sum(l => l.Credit), cost.Value, cost.Quantity);
            });
        }

        [TestMethod]
        public async Task Freight_ApproveThenIssueThenCancelTheReceipt_LedgerAndItemCostAgree()
        {
            AppHost host = new AppHost("Pb5Fix_freight_" + Guid.NewGuid().ToString("N"));
            int wh = Seed(host, new Warehouse { Code = "W-P5", Title = "انبار", Kind = WarehouseKind.Parts }).Id;
            var item = Seed(host, new InventoryItem { Code = "TST-FR-1", Title = "قطعه", Unit = "عدد", TrackingMode = ItemTrackingMode.None, RequiresIncomingInspection = false });
            var provider = Seed(host, new Entities.Model.NasbNiro.Providers.Provider { Name = "تامین‌کننده نمونه" });
            int a = await ReceiptAsync(host, wh, item.Id, 10m, 1000m);
            await ReceiptAsync(host, wh, item.Id, 10m, 1000m);   // a second receipt keeps stock after the first one is cancelled

            int freightId = await host.As(Pmo, async p =>
            {
                var svc = p.GetRequiredService<IInventoryFreightService>();
                var saved = await svc.SaveAsync(new SaveFreightDto { ReceiptDocumentId = a, FreightDate = DateTime.Today, Amount = 1000m }, Ct);
                Assert.IsTrue(saved.Success, saved.Message);
                Assert.AreEqual(1000m, saved.Data.Allocations.Single().Amount);
                return saved.Data.Id;
            });
            var denied = await host.As(Pmo, p => p.GetRequiredService<IInventoryFreightService>().ApproveAsync(freightId, Ct));
            Assert.IsFalse(denied.Success, "the preparer (and a non-accountant) cannot approve");
            var approved = await host.As(Fm, p => p.GetRequiredService<IInventoryFreightService>().ApproveAsync(freightId, Ct));
            Assert.IsTrue(approved.Success, approved.Message);

            var alloc = host.Read(u => u.Set<InventoryFreightAllocation>().Single(x => x.FreightId == freightId));
            Assert.AreEqual(1000m, alloc.StockAmount, "the part that went into stock is stored");
            Assert.AreEqual(0m, alloc.RevaluationAmount);
            var afterApprove = Books(host, "TST-FR-1", item.Id);
            Assert.AreEqual(21000m, afterApprove.Cost);
            Assert.AreEqual(afterApprove.Cost, afterApprove.Ledger, "ledger = item cost after the approval");

            // 10 pieces leave (a return to the supplier: they are taken at the average cost that includes the freight)
            await host.As(Pmo, async p =>
            {
                var svc = p.GetRequiredService<IInventoryDocumentService>();
                var saved = await svc.SaveDraftAsync(new SaveDocumentDto
                {
                    Type = InventoryDocumentType.SupplierReturn, DocumentDate = DateTime.Today, WarehouseId = wh, ProviderId = provider.Id, DriverName = "راننده", VehiclePlate = "12الف345", ReceiverName = "تحویل‌گیرنده",
                    Lines = new List<SaveDocumentLineDto> { new SaveDocumentLineDto { ItemId = item.Id, Quantity = 10m } },
                }, Ct);
                Assert.IsTrue(saved.Success, "return draft: " + saved.Message);
                var done = await svc.ConfirmAsync(saved.Data.Id, Ct);
                Assert.IsTrue(done.Success, "return confirm: " + done.Message);
                return true;
            });
            var afterReturn = Books(host, "TST-FR-1", item.Id);
            Assert.AreEqual(afterReturn.Cost, afterReturn.Ledger, "ledger = item cost after the return");

            // cancel the first receipt: the freight is reversed first, then the receipt
            var cancelled = await host.As(Pmo, p => p.GetRequiredService<IInventoryDocumentService>().CancelAsync(a, new CancelDocumentDto { Reason = "آزمون" }, Ct));
            Assert.IsTrue(cancelled.Success, cancelled.Message);
            var freight = host.Read(u => u.Set<InventoryFreight>().Single(x => x.Id == freightId));
            Assert.AreEqual(FreightStatus.Voided, freight.Status);
            var end = Books(host, "TST-FR-1", item.Id);
            Assert.AreEqual(end.Cost, end.Ledger, "ledger = item cost after the cancel (no double credit, no debit left on revaluation)");
            Assert.AreEqual(0m, end.Quantity, "20 received - 10 returned - 10 of the cancelled receipt");
            Assert.AreEqual(0m, end.Cost);
        }

        [TestMethod]
        public async Task Freight_OnAnItemWithNoStock_GoesToRevaluation_AndTheVoidReversesExactlyThat()
        {
            AppHost host = new AppHost("Pb5Fix_freight2_" + Guid.NewGuid().ToString("N"));
            int wh = Seed(host, new Warehouse { Code = "W-P6", Title = "انبار", Kind = WarehouseKind.Parts }).Id;
            var item = Seed(host, new InventoryItem { Code = "TST-FR-2", Title = "قطعه", Unit = "عدد", TrackingMode = ItemTrackingMode.None, RequiresIncomingInspection = false });
            int a = await ReceiptAsync(host, wh, item.Id, 5m, 2000m);
            int freightId = await host.As(Pmo, async p => (await p.GetRequiredService<IInventoryFreightService>().SaveAsync(new SaveFreightDto { ReceiptDocumentId = a, FreightDate = DateTime.Today, Amount = 500m }, Ct)).Data.Id);
            // all the stock is used up before the accountant approves
            host.Write(u => { var c = u.Set<ItemCost>().Single(x => x.ItemId == item.Id); c.Quantity = 0; c.Value = 0; u.SaveChanges(); });
            var approved = await host.As(Fm, p => p.GetRequiredService<IInventoryFreightService>().ApproveAsync(freightId, Ct));
            Assert.IsTrue(approved.Success, approved.Message);
            var alloc = host.Read(u => u.Set<InventoryFreightAllocation>().Single(x => x.FreightId == freightId));
            Assert.AreEqual(0m, alloc.StockAmount);
            Assert.AreEqual(500m, alloc.RevaluationAmount);
            // stock comes back before the void: the void must not touch the item cost (the amount never went into it)
            host.Write(u => { var c = u.Set<ItemCost>().Single(x => x.ItemId == item.Id); c.Quantity = 3; c.Value = 6000; c.AverageCost = 2000; u.SaveChanges(); });
            var voided = await host.As(Fm, p => p.GetRequiredService<IInventoryFreightService>().VoidAsync(freightId, new VoidFreightDto { Reason = "آزمون" }, Ct));
            Assert.IsTrue(voided.Success, voided.Message);
            Assert.AreEqual(6000m, host.Read(u => u.Set<ItemCost>().Single(x => x.ItemId == item.Id).Value), "the item cost is untouched");
            decimal reval = host.Read(u => u.Set<AccountingEventLine>().Where(l => l.AccountRole == AccountRoles.Revaluation && l.Event.SourceType == "InventoryFreight" && l.Event.Status != AccountingEventStatus.NotRequired).Sum(l => l.Debit - l.Credit));
            Assert.AreEqual(0m, reval, "the revaluation account nets to zero for the freight");
        }

        // ---------------------------------------------------------------------------------------------- H3

        [TestMethod]
        public async Task TransferIntoAProjectWarehouse_UsesTheReservationOfThatWorkOrder()
        {
            AppHost host = new AppHost("Pb5Fix_h3_" + Guid.NewGuid().ToString("N"));
            int wo = 77;
            int general = Seed(host, new Warehouse { Code = "W-G", Title = "عمومی", Kind = WarehouseKind.General }).Id;
            int pou = Seed(host, new Warehouse { Code = "W-POU", Title = "پای کار", Kind = WarehouseKind.PointOfUse, WorkOrderId = wo }).Id;
            int item = Seed(host, new InventoryItem { Code = "TST-H3", Title = "قطعه", Unit = "عدد", TrackingMode = ItemTrackingMode.None }).Id;
            host.Write(u => { AppHost.Add(u, new ProjectReservation { ContractId = 1, WorkOrderId = wo, BaselineId = 5, ItemId = item, Quantity = 10m, Status = ReservationStatus.Active, IsHard = true, Reason = "x" }); u.SaveChanges(); }, Pmo);
            var doc = new InventoryDocument
            {
                Type = InventoryDocumentType.Transfer, WarehouseId = general, DestinationWarehouseId = pou,
                Lines = new List<InventoryDocumentLine> { new InventoryDocumentLine { ItemId = item, Quantity = 4m, Direction = DocumentLineDirection.Out } },
            };
            await host.As(Pmo, async p =>
            {
                var uow = p.GetRequiredService<IUnitOfWork>();
                await ProjectReservationRules.ApplyIssueAsync(uow, doc, 1, Ct);
                await uow.SaveChangesAsync(Ct);
                return true;
            });
            var r = host.Read(u => u.Set<ProjectReservation>().Single(x => x.WorkOrderId == wo && x.ItemId == item));
            Assert.AreEqual(4m, r.IssuedQuantity, "the transfer used 4 of the reservation (they are counted as issued to the project, not as reserved again)");
            Assert.AreEqual(ReservationStatus.Active, r.Status);
        }

        // ---------------------------------------------------------------------------------------------- rules

        [TestMethod]
        public void SettingRelations_AreChecked()
        {
            var ok = new Dictionary<string, decimal>
            {
                [Services.NasbNiro.Quality.QualitySettingKeys.ConsignmentAlertDays1] = 7, [Services.NasbNiro.Quality.QualitySettingKeys.ConsignmentAlertDays2] = 3,
                [Services.NasbNiro.Quality.QualitySettingKeys.QcPendingWorkDays] = 3, [Services.NasbNiro.Quality.QualitySettingKeys.QcEscalateWorkDays] = 5,
                [Services.NasbNiro.Quality.QualitySettingKeys.StagnantSlowDays] = 60, [Services.NasbNiro.Quality.QualitySettingKeys.StagnantDays] = 90, [Services.NasbNiro.Quality.QualitySettingKeys.StagnantDeadDays] = 180,
            };
            Assert.IsNull(Services.NasbNiro.Quality.QualitySettingRules.CheckRelations(ok));
            var a = new Dictionary<string, decimal>(ok) { [Services.NasbNiro.Quality.QualitySettingKeys.ConsignmentAlertDays2] = 7 };
            Assert.IsNotNull(Services.NasbNiro.Quality.QualitySettingRules.CheckRelations(a));
            var b = new Dictionary<string, decimal>(ok) { [Services.NasbNiro.Quality.QualitySettingKeys.QcEscalateWorkDays] = 3 };
            Assert.IsNotNull(Services.NasbNiro.Quality.QualitySettingRules.CheckRelations(b));
            var c = new Dictionary<string, decimal>(ok) { [Services.NasbNiro.Quality.QualitySettingKeys.StagnantDays] = 200 };
            Assert.IsNotNull(Services.NasbNiro.Quality.QualitySettingRules.CheckRelations(c));
        }

        [TestMethod]
        public void WorkCalendar_FridayIsAlwaysAWeekend_EvenWhenTheTableHoldsOnlyAnotherDay()
        {
            var cal = new WorkCalendar(new[] { DayOfWeek.Thursday }, null);
            Assert.IsFalse(cal.IsWorkingDay(new DateTime(2026, 3, 13)));   // Friday
            Assert.IsFalse(cal.IsWorkingDay(new DateTime(2026, 3, 12)));   // Thursday from the table
            Assert.IsTrue(cal.IsWorkingDay(new DateTime(2026, 3, 14)));
        }

        [TestMethod]
        public async Task BalanceWarehouseKinds_DefaultIncludesTheNewKinds_AndAStoredOldDefaultIsMoved()
        {
            Assert.IsTrue(BalanceSettingKeys.DefaultWarehouseKinds.Split(',').Select(int.Parse).ToHashSet().SetEquals(new[] { 0, 1, 2, 3, 4, 12, 13, 14, 16, 17 }));
            AppHost host = new AppHost("Pb5Fix_kinds_" + Guid.NewGuid().ToString("N"));
            host.Write(u => { u.Set<Entities.Model.NasbNiro.ProductBank.PbSetting>().Add(new Entities.Model.NasbNiro.ProductBank.PbSetting { Key = BalanceSettingKeys.WarehouseKinds, Value = "0,1,2,3,4", Title = "x", Source = "x" }); u.SaveChanges(); }, Pmo);
            var s = await host.As(Pmo, p => BalanceSettingKeys.LoadAsync(p.GetRequiredService<IUnitOfWork>(), Ct));
            Assert.IsTrue(s.WarehouseKinds.Contains(12) && s.WarehouseKinds.Contains(17), "the old stored default reads as the new default");
            bool moved = await host.As(Pmo, p => BalanceSettingKeys.UpgradeWarehouseKindsAsync(p.GetRequiredService<IUnitOfWork>(), Ct));
            Assert.IsTrue(moved);
            Assert.AreEqual(BalanceSettingKeys.DefaultWarehouseKinds, host.Read(u => u.Set<Entities.Model.NasbNiro.ProductBank.PbSetting>().Single(x => x.Key == BalanceSettingKeys.WarehouseKinds).Value));
            Assert.IsFalse(await host.As(Pmo, p => BalanceSettingKeys.UpgradeWarehouseKindsAsync(p.GetRequiredService<IUnitOfWork>(), Ct)), "idempotent");
        }
    }
}
