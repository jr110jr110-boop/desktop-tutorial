using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.ContractsSystem;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.ContractsSystem;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.ProcurementDtos;

namespace MsTests
{
    /// <summary>PB6: form types, return of a request, legal rates (no invented rates, a second person verifies), the deal request chain, the tender winner, the Excel writer.</summary>
    [TestClass]
    public class Pb6Tests
    {
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1, "seed", "PMO");
        private static readonly ClaimsPrincipal Planner = AppHost.Principal(51, "planner", "PLANNING");
        private static readonly ClaimsPrincipal Buyer = AppHost.Principal(26, "buyer", "COMMERCIAL");
        private static readonly ClaimsPrincipal Buyer2 = AppHost.Principal(27, "buyer.two", "COMMERCIAL");
        private static readonly ClaimsPrincipal Finance = AppHost.Principal(61, "finance.one", "FINANCIAL");
        private static readonly ClaimsPrincipal FinanceManager = AppHost.Principal(62, "finance.manager", "FINANCEMANAGER");
        private static readonly ClaimsPrincipal Ceo = AppHost.Principal(31, "ceo", "CEO");

        private static AppHost H;
        private static AppHost NewHost(string name) => H = new AppHost("Pb6_" + name + "_" + Guid.NewGuid().ToString("N"));

        [TestMethod]
        public void Xlsx_writer_makes_a_zip_with_the_sheet()
        {
            byte[] bytes = XlsxWriter.Write("t", new List<string> { "a", "b" }, new List<List<object>> { new List<object> { "x", 5m } });
            using var zip = new ZipArchive(new MemoryStream(bytes));
            Assert.IsNotNull(zip.GetEntry("xl/worksheets/sheet1.xml"));
            using var r = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml").Open());
            string xml = r.ReadToEnd();
            Assert.IsTrue(xml.Contains("<v>5</v>") && xml.Contains("rightToLeft"));
        }

        [TestMethod]
        public async Task Legal_rates_start_empty_and_are_verified_by_a_second_person()
        {
            NewHost("rates");
            var list = await H.As(Finance, p => p.GetRequiredService<IProgressStatementService>().GetRatesAsync(CancellationToken.None));
            Assert.IsTrue(list.Success);
            Assert.AreEqual(0, list.Data.Count, "no rate is invented");
            var saved = await H.As(Finance, p => p.GetRequiredService<IProgressStatementService>().SaveRateAsync(new SaveLegalRateDto { Code = "T1", Title = "test rate", Percent = 5m, SourceReference = "test source" }, CancellationToken.None));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.AreEqual(PsRateStatus.Draft, saved.Data.Status);
            var self = await H.As(Finance, p => p.GetRequiredService<IProgressStatementService>().RateActionAsync(new FlowActionDto { Id = saved.Data.Id, Action = "verify" }, CancellationToken.None));
            Assert.IsFalse(self.Success, "the maker does not verify");
            var buyer = await H.As(Buyer, p => p.GetRequiredService<IProgressStatementService>().RateActionAsync(new FlowActionDto { Id = saved.Data.Id, Action = "verify" }, CancellationToken.None));
            Assert.IsFalse(buyer.Success, "a buyer does not verify");
            var ok = await H.As(FinanceManager, p => p.GetRequiredService<IProgressStatementService>().RateActionAsync(new FlowActionDto { Id = saved.Data.Id, Action = "verify" }, CancellationToken.None));
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.AreEqual(PsRateStatus.Verified, ok.Data.Status);
            var noSource = await H.As(Finance, p => p.GetRequiredService<IProgressStatementService>().SaveRateAsync(new SaveLegalRateDto { Code = "T2", Title = "x", Percent = 1m }, CancellationToken.None));
            Assert.IsFalse(noSource.Success, "a rate needs its source");
        }

        [TestMethod]
        public async Task Deal_request_is_approved_by_another_person_in_the_chain()
        {
            NewHost("deal");
            var dto = new SaveDealRequestDto { Title = "t", Subject = "s", EstimatedAmount = 1000000m, DealLevel = CtDealLevel.Small, Submit = true };
            var saved = await H.As(Planner, p => p.GetRequiredService<IContractsSystemService>().SaveDealAsync(dto, CancellationToken.None));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.AreEqual(CtDealStatus.Submitted, saved.Data.Status);
            Assert.AreEqual(1, saved.Data.Steps.Count);
            var self = await H.As(AppHost.Principal(51, "planner", "PLANNING", "COMMERCIAL"), p => p.GetRequiredService<IContractsSystemService>().DealActionAsync(new FlowActionDto { Id = saved.Data.Id, Action = "approve" }, CancellationToken.None));
            Assert.IsFalse(self.Success, "the maker does not approve");
            var ret = await H.As(Buyer, p => p.GetRequiredService<IContractsSystemService>().DealActionAsync(new FlowActionDto { Id = saved.Data.Id, Action = "return" }, CancellationToken.None));
            Assert.IsFalse(ret.Success, "a return needs a reason");
            var ok = await H.As(Buyer, p => p.GetRequiredService<IContractsSystemService>().DealActionAsync(new FlowActionDto { Id = saved.Data.Id, Action = "approve" }, CancellationToken.None));
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.AreEqual(CtDealStatus.Approved, ok.Data.Status);
        }

        [TestMethod]
        public async Task Large_deal_needs_three_steps_and_the_ceo_last()
        {
            NewHost("large");
            var saved = await H.As(Planner, p => p.GetRequiredService<IContractsSystemService>().SaveDealAsync(new SaveDealRequestDto { Title = "t", Subject = "s", EstimatedAmount = 9000000000m, DealLevel = CtDealLevel.Large, Submit = true }, CancellationToken.None));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.AreEqual(3, saved.Data.Steps.Count);
            var early = await H.As(Ceo, p => p.GetRequiredService<IContractsSystemService>().DealActionAsync(new FlowActionDto { Id = saved.Data.Id, Action = "approve" }, CancellationToken.None));
            Assert.IsFalse(early.Success, "the first step belongs to commercial");
        }

        [TestMethod]
        public async Task Tender_winner_is_selected_by_one_and_confirmed_by_another()
        {
            NewHost("tender");
            var deal = await H.As(Planner, p => p.GetRequiredService<IContractsSystemService>().SaveDealAsync(new SaveDealRequestDto { Title = "t", Subject = "s", EstimatedAmount = 1000000m, DealLevel = CtDealLevel.Small, Submit = true }, CancellationToken.None));
            await H.As(Buyer, p => p.GetRequiredService<IContractsSystemService>().DealActionAsync(new FlowActionDto { Id = deal.Data.Id, Action = "approve" }, CancellationToken.None));
            var tender = await H.As(Buyer, p => p.GetRequiredService<IContractsSystemService>().SaveTenderAsync(new SaveTenderRunDto
            {
                DealRequestId = deal.Data.Id, Method = CtTenderMethod.Inquiry,
                Participants = new List<SaveParticipantDto> { new SaveParticipantDto { CompanyName = "A", OfferAmount = 100 }, new SaveParticipantDto { CompanyName = "B", OfferAmount = 90 } },
            }, CancellationToken.None));
            Assert.IsTrue(tender.Success, tender.Message);
            int a = tender.Data.Participants.First(x => x.CompanyName == "A").Id.Value;
            var noReason = await H.As(Buyer, p => p.GetRequiredService<IContractsSystemService>().SelectWinnerAsync(new SelectWinnerDto { TenderRunId = tender.Data.Id, ParticipantId = a }, CancellationToken.None));
            Assert.IsFalse(noReason.Success, "not the lowest offer needs a reason");
            var sel = await H.As(Buyer, p => p.GetRequiredService<IContractsSystemService>().SelectWinnerAsync(new SelectWinnerDto { TenderRunId = tender.Data.Id, ParticipantId = a, Reason = "technical score" }, CancellationToken.None));
            Assert.IsTrue(sel.Success, sel.Message);
            var self = await H.As(AppHost.Principal(26, "buyer", "COMMERCIAL", "FINANCEMANAGER"), p => p.GetRequiredService<IContractsSystemService>().TenderActionAsync(new FlowActionDto { Id = tender.Data.Id, Action = "confirm-winner" }, CancellationToken.None));
            Assert.IsFalse(self.Success, "the selector does not confirm");
            var ok = await H.As(FinanceManager, p => p.GetRequiredService<IContractsSystemService>().TenderActionAsync(new FlowActionDto { Id = tender.Data.Id, Action = "confirm-winner" }, CancellationToken.None));
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.AreEqual(CtTenderStatus.WinnerConfirmed, ok.Data.Status);
        }

        [TestMethod]
        public async Task Statement_needs_a_commercial_maker_and_a_contract()
        {
            NewHost("statement");
            var denied = await H.As(Finance, p => p.GetRequiredService<IProgressStatementService>().SaveStatementAsync(new SaveStatementDto { PeriodFrom = DateTime.Today, PeriodTo = DateTime.Today }, CancellationToken.None));
            Assert.IsFalse(denied.Success);
            var noOrder = await H.As(Buyer, p => p.GetRequiredService<IProgressStatementService>().SaveStatementAsync(new SaveStatementDto { PeriodFrom = DateTime.Today, PeriodTo = DateTime.Today }, CancellationToken.None));
            Assert.IsFalse(noOrder.Success, "a supplier statement names its order");
        }

        [TestMethod]
        public void Form_types_are_validated()
        {
            Assert.AreEqual(PurchaseFormType.GoodsProject, ProcFormTypes.Derive(PurchaseKind.Goods, 5));
            Assert.AreEqual(PurchaseFormType.GoodsMiscellaneous, ProcFormTypes.Derive(PurchaseKind.Goods, null));
            Assert.AreEqual(PurchaseFormType.ServiceProject, ProcFormTypes.Derive(PurchaseKind.Service, 5));
            Assert.AreEqual(PurchaseFormType.ServiceGeneral, ProcFormTypes.Derive(PurchaseKind.Service, null));
            Assert.AreEqual(PurchaseFormType.ContractProject, ProcFormTypes.Derive(PurchaseKind.Subcontract, 5));
            Assert.AreEqual(PurchaseKind.Goods, ProcFormTypes.KindOf(PurchaseFormType.GoodsMiscellaneous));
            Assert.AreEqual(PurchaseKind.Service, ProcFormTypes.KindOf(PurchaseFormType.ServiceGeneral));
            foreach (PurchaseFormType f in Enum.GetValues(typeof(PurchaseFormType)))
                Assert.IsTrue(Enum.IsDefined(typeof(PurchaseKind), ProcFormTypes.KindOf(f)), f.ToString());
        }

        [TestMethod]
        public async Task Cbs_can_be_saved_again_and_a_removed_item_is_soft_deleted()
        {
            NewHost("cbs");
            int orderId = 0;
            H.Write(uow =>
            {
                var o = AppHost.Add(uow, new PurchaseOrder { Number = "PO-CBS-1", ProviderId = 1, Status = PurchaseOrderStatus.Released, OrderDate = DateTime.Today, TotalAmount = 1000m });
                uow.SaveChanges();
                orderId = o.Id;
            }, Seeder);
            var first = await H.As(Buyer, p => p.GetRequiredService<IProgressStatementService>().SaveCbsAsync(new SaveCbsDto
            {
                PurchaseOrderId = orderId,
                Items = new List<SaveCbsItemDto> { new SaveCbsItemDto { Code = "A", Title = "first", Amount = 600m }, new SaveCbsItemDto { Code = "B", Title = "second", Amount = 400m } },
            }, CancellationToken.None));
            Assert.IsTrue(first.Success, first.Message);
            Assert.AreEqual(2, first.Data.Count);
            var second = await H.As(Buyer, p => p.GetRequiredService<IProgressStatementService>().SaveCbsAsync(new SaveCbsDto
            {
                PurchaseOrderId = orderId,
                Items = new List<SaveCbsItemDto> { new SaveCbsItemDto { Code = "A", Title = "first changed", Amount = 700m } },
            }, CancellationToken.None));
            Assert.IsTrue(second.Success, second.Message);
            Assert.AreEqual(1, second.Data.Count);
            int rows = 0, deleted = 0;
            H.Write(uow => { rows = uow.Set<CtCbsItem>().Count(); deleted = uow.Set<CtCbsItem>().Count(c => c.Deleted); }, Seeder);
            Assert.AreEqual(2, rows, "the removed item stays as a soft-deleted row");
            Assert.AreEqual(1, deleted);
        }

        [TestMethod]
        public async Task A_large_deal_cannot_use_the_small_chain()
        {
            NewHost("level");
            var small = await H.As(Planner, p => p.GetRequiredService<IContractsSystemService>().SaveDealAsync(new SaveDealRequestDto { Title = "t", Subject = "s", EstimatedAmount = 9000000000m, DealLevel = CtDealLevel.Small, Submit = true }, CancellationToken.None));
            Assert.IsFalse(small.Success, "the level follows the amount");
        }

        [TestMethod]
        public async Task A_legal_rate_has_a_deduction_kind_and_late_penalty_is_not_one_of_them()
        {
            NewHost("ratekind");
            var bad = await H.As(Finance, p => p.GetRequiredService<IProgressStatementService>().SaveRateAsync(new SaveLegalRateDto { Code = "K1", Title = "x", Percent = 5m, SourceReference = "s", DeductionKind = Entities.Model.NasbNiro.Finance.FnDeductionKind.LatePenalty }, CancellationToken.None));
            Assert.IsFalse(bad.Success);
            var ok = await H.As(Finance, p => p.GetRequiredService<IProgressStatementService>().SaveRateAsync(new SaveLegalRateDto { Code = "K2", Title = "x", Percent = 5m, SourceReference = "s", DeductionKind = Entities.Model.NasbNiro.Finance.FnDeductionKind.InsuranceRetention }, CancellationToken.None));
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.AreEqual(Entities.Model.NasbNiro.Finance.FnDeductionKind.InsuranceRetention, ok.Data.DeductionKind);
        }

        [TestMethod]
        public void Warehouse_does_not_read_the_money_reports()
        {
            Assert.IsFalse(ProcurementRoles.ReportReaders.Contains("WAREHOUSE"));
            Assert.IsFalse(ProcurementRoles.ReportReaders.Contains("WHSUPERVISOR"));
        }

        [TestMethod]
        public void Retention_is_its_own_payable_kind()
        {
            Assert.AreEqual(5, (int)PaymentRequestType.Retention);
        }
    }
}
