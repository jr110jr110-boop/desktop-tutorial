using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.ProcurementDtos;

namespace MsTests
{
    /// <summary>
    /// R1c, procurement authority: the number of quotes and the approval level are judged again on the value of the selected quote and of the order; an order is built only from the quote
    /// that won; only engineering or quality accepts a quote technically; the PMO deputy approves and never buys; a revised order freezes the old revision; the scored supplier evaluation
    /// reads the recorded performance with the weights of the settings; the late penalty and the retention of the order are proposed and confirmed by finance.
    /// </summary>
    [TestClass]
    public class ProcurementR1cTests
    {
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1, "seed", "PMO");
        private static readonly ClaimsPrincipal Pmo1 = AppHost.Principal(11, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Pmo2 = AppHost.Principal(12, "pmo.two", "PMO");
        private static readonly ClaimsPrincipal Engineering = AppHost.Principal(21, "engineering.manager", "ENGINEERING");
        private static readonly ClaimsPrincipal Qc = AppHost.Principal(25, "qc.manager", "QC");
        private static readonly ClaimsPrincipal Buyer = AppHost.Principal(26, "buyer", "COMMERCIAL");
        private static readonly ClaimsPrincipal Accountant = AppHost.Principal(28, "accountant", "FINANCIAL");
        private static readonly ClaimsPrincipal PmoAndBuyer = AppHost.Principal(30, "pmo.and.buyer", "PMO", "COMMERCIAL");
        private static readonly ClaimsPrincipal Ceo = AppHost.Principal(31, "ceo", "CEO");
        private static readonly ClaimsPrincipal BuyerAndQc = AppHost.Principal(40, "buyer.and.qc", "COMMERCIAL", "QC");
        private static readonly ClaimsPrincipal BuyerAndAccountant = AppHost.Principal(45, "buyer.and.accountant", "COMMERCIAL", "FINANCIAL");
        private static readonly ClaimsPrincipal PmoAndAccountant = AppHost.Principal(46, "pmo.and.accountant", "PMO", "FINANCIAL");

        private const string NotAllowed = "شما مجوز این کار را ندارید";
        private const string SameUser = "شخص دیگری";

        private static AppHost H;
        private static AppHost NewHost(string name) => H = new AppHost("ProcR1c_" + name + "_" + Guid.NewGuid().ToString("N"));

        private static Dictionary<string, decimal> Settings() => ProcurementSettingKeys.Defaults().ToDictionary(d => d.Key, d => d.Value);

        // ------------------------------------------------------------------------------------------------ the authority arithmetic

        private static PurchaseInquiry MakeInquiry(InquiryStatus status, int required, int accepted, string fewerReason = null, bool managementApproved = false, int rejected = 0)
        {
            var quotes = new List<SupplierQuote>();
            for (int i = 0; i < accepted; i++) quotes.Add(new SupplierQuote { Id = i + 1, TechnicallyAccepted = true, TotalAmount = 1m });
            for (int i = 0; i < rejected; i++) quotes.Add(new SupplierQuote { Id = 100 + i, TechnicallyAccepted = false, TotalAmount = 1m });
            return new PurchaseInquiry { Status = status, RequiredQuotes = required, FewerQuotesReason = fewerReason, ManagementApprovedAt = managementApproved ? DateTimeOffset.Now : (DateTimeOffset?)null, Quotes = quotes };
        }

        private static PurchaseRequest MakeRequest(bool management, string board = null, decimal estimate = 0m, string number = "PR-1") =>
            new PurchaseRequest { Number = number, EstimatedAmount = estimate, ManagementApprovedAt = management ? DateTimeOffset.Now : (DateTimeOffset?)null, BoardResolution = board };

        [TestMethod]
        public void TheNumberOfQuotesIsJudgedOnTheValueOfTheQuoteAndNotOnlyOnTheEstimateOfTheRequest()
        {
            Dictionary<string, decimal> s = Settings();
            Assert.AreEqual(0, ProcurementAuthority.RequiredQuotes(s, 0, 9_000_000m), "a small value needs no quote");
            Assert.AreEqual(1, ProcurementAuthority.RequiredQuotes(s, 0, 15_000_000m));
            Assert.AreEqual(2, ProcurementAuthority.RequiredQuotes(s, 0, 30_000_000m));
            Assert.AreEqual(3, ProcurementAuthority.RequiredQuotes(s, 0, 50_000_000m));
            Assert.AreEqual(3, ProcurementAuthority.RequiredQuotes(s, 0, 60_000_000m), "the request was approved on a small estimate, the selected quote needs three");
            Assert.AreEqual(2, ProcurementAuthority.RequiredQuotes(s, 2, 16_000_000m), "what the estimate needed is kept when it is more");
        }

        [TestMethod]
        public void AnOrderIsRefusedWhenTheInquiryIsNotApprovedOrTheQuotesFallShortOfTheValueOfTheOrder()
        {
            Dictionary<string, decimal> s = Settings();
            PurchaseRequest approvedRequest = MakeRequest(management: true);

            Assert.AreEqual(ProcurementAuthority.InquiryNotApproved, ProcurementAuthority.OrderProblem(s, 20_000_000m, MakeInquiry(InquiryStatus.Selected, 1, 2), approvedRequest), "a selected inquiry has not been approved by the committee");
            Assert.AreEqual(ProcurementAuthority.InquiryNotApproved, ProcurementAuthority.OrderProblem(s, 20_000_000m, MakeInquiry(InquiryStatus.Open, 1, 2), approvedRequest));
            Assert.IsNull(ProcurementAuthority.OrderProblem(s, 20_000_000m, MakeInquiry(InquiryStatus.Approved, 1, 2), approvedRequest));

            string few = ProcurementAuthority.OrderProblem(s, 60_000_000m, MakeInquiry(InquiryStatus.Approved, 0, 1), approvedRequest);
            Assert.IsNotNull(few);
            Assert.IsTrue(few.Contains("3 پیشنهاد قابل قبول فنی"), few);
            Assert.IsTrue(few.Contains("1 پیشنهاد"), few);
            string rejectedOnes = ProcurementAuthority.OrderProblem(s, 60_000_000m, MakeInquiry(InquiryStatus.Approved, 0, 1, rejected: 5), approvedRequest);
            Assert.IsNotNull(rejectedOnes, "quotes that failed the technical review do not count");
            string needsCeo = ProcurementAuthority.OrderProblem(s, 60_000_000m, MakeInquiry(InquiryStatus.Approved, 0, 1, fewerReason: "one maker only"), approvedRequest);
            Assert.IsNotNull(needsCeo);
            Assert.IsTrue(needsCeo.Contains("تایید مدیرعامل"), "fewer quotes need the managing director: " + needsCeo);
            Assert.IsNull(ProcurementAuthority.OrderProblem(s, 60_000_000m, MakeInquiry(InquiryStatus.Approved, 0, 1, fewerReason: "one maker only", managementApproved: true), approvedRequest));
        }

        [TestMethod]
        public void AnOrderAboveTheLimitOfTheApprovalOfItsRequestNeedsTheManagingDirectorAndAboveTheBoardLimitTheResolution()
        {
            Dictionary<string, decimal> s = Settings();

            // the request was approved on an estimate below the managing-director limit (10 million); the order is 20 million
            PurchaseRequest small = MakeRequest(management: false, estimate: 9_000_000m);
            Assert.IsTrue(ProcurementAuthority.RequestLevelGap(s, small, 20_000_000m));
            Assert.IsFalse(ProcurementAuthority.RequestLevelGap(s, small, 9_000_000m));
            string gap = ProcurementAuthority.OrderProblem(s, 20_000_000m, MakeInquiry(InquiryStatus.Approved, 1, 1), small);
            Assert.IsNotNull(gap);
            Assert.IsTrue(gap.Contains("تایید مدیرعامل"), gap);
            Assert.IsNull(ProcurementAuthority.OrderProblem(s, 20_000_000m, MakeInquiry(InquiryStatus.Approved, 1, 1, managementApproved: true), small), "the managing director approved the inquiry");

            // a major purchase (200 million and above) needs the board resolution on the request
            PurchaseRequest withoutBoard = MakeRequest(management: true, board: null);
            Assert.IsTrue(ProcurementAuthority.RequestLevelGap(s, withoutBoard, 250_000_000m));
            string board = ProcurementAuthority.OrderProblem(s, 250_000_000m, MakeInquiry(InquiryStatus.Approved, 3, 3, managementApproved: true), withoutBoard);
            Assert.IsNotNull(board);
            Assert.IsTrue(board.Contains("هیئت‌مدیره"), board);
            Assert.IsNull(ProcurementAuthority.OrderProblem(s, 250_000_000m, MakeInquiry(InquiryStatus.Approved, 3, 3, managementApproved: true), MakeRequest(management: true, board: "B-14")));

            // the committee needs the managing director for a very large value, for fewer quotes than the value needs, and for a gap in the approval of the request
            Assert.IsTrue(ProcurementAuthority.InquiryNeedsManagement(s, MakeInquiry(InquiryStatus.Selected, 3, 3), MakeRequest(true, "B-1"), 6_000_000_000m));
            Assert.IsTrue(ProcurementAuthority.InquiryNeedsManagement(s, MakeInquiry(InquiryStatus.Selected, 3, 1, fewerReason: "one maker only"), MakeRequest(true), 60_000_000m));
            Assert.IsTrue(ProcurementAuthority.InquiryNeedsManagement(s, MakeInquiry(InquiryStatus.Selected, 1, 1), small, 20_000_000m));
            Assert.IsFalse(ProcurementAuthority.InquiryNeedsManagement(s, MakeInquiry(InquiryStatus.Selected, 1, 1), MakeRequest(true), 20_000_000m));
            Assert.IsFalse(ProcurementAuthority.InquiryNeedsManagement(s, MakeInquiry(InquiryStatus.Selected, 3, 3), MakeRequest(true), 60_000_000m), "three accepted quotes are enough");
        }

        [TestMethod]
        public void AnOrderWithoutAnInquiryIsASmallPurchaseOfARequestApprovedAtTheLevelOfItsShare()
        {
            Dictionary<string, decimal> s = Settings();
            Assert.AreEqual(ProcurementAuthority.InquiryRequired, ProcurementAuthority.OrderProblemWithoutInquiry(s, 15_000_000m, new List<(PurchaseRequest, decimal)>()), "from the first-quote limit an inquiry is required");
            var shares = new List<(PurchaseRequest Request, decimal Share)> { (MakeRequest(false, estimate: 5_000_000m, number: "PR-7"), 12_000_000m) };
            string gap = ProcurementAuthority.OrderProblemWithoutInquiry(s, 12_000_000m, shares);
            Assert.IsNotNull(gap, "12 million of a request that was approved on 5 million without the managing director");
            Assert.IsTrue(gap.Contains("PR-7"), gap);
            Assert.IsNull(ProcurementAuthority.OrderProblemWithoutInquiry(s, 9_000_000m, new List<(PurchaseRequest, decimal)> { (MakeRequest(false, estimate: 5_000_000m), 9_000_000m) }));
            Assert.IsNull(ProcurementAuthority.OrderProblemWithoutInquiry(s, 12_000_000m, new List<(PurchaseRequest, decimal)> { (MakeRequest(true, estimate: 5_000_000m), 12_000_000m) }));
        }

        // ------------------------------------------------------------------------------------------------ the scored supplier evaluation

        [TestMethod]
        public void TheScoredEvaluationIsTheWeightedMeanOfTheCriteriaThatHaveData()
        {
            Dictionary<string, decimal> s = Settings();
            var metrics = new SupplierMetrics { Orders = 5, ReceiptLines = 20, LateReceiptLines = 2, Ncrs = 1, Returns = 1, Warnings = 1, PriceRatios = new List<decimal> { 1m, 0.8m } };
            List<CriterionScore> criteria = SupplierScoring.Criteria(metrics, s);
            CollectionAssert.AreEqual(new[] { SupplierScoring.Quality, SupplierScoring.Delivery, SupplierScoring.Price, SupplierScoring.Service }, criteria.Select(c => c.Key).ToArray());
            CollectionAssert.AreEqual(new[] { 40m, 30m, 20m, 10m }, criteria.Select(c => c.Weight).ToArray(), "the weights come from the settings (★ 40, 30, 20, 10)");
            Assert.AreEqual(90m, criteria[0].Score, "two defects in twenty receipt lines");
            Assert.AreEqual(90m, criteria[1].Score, "eighteen of twenty lines on time");
            Assert.AreEqual(90m, criteria[2].Score, "the mean of 1 and 0.8");
            Assert.AreEqual(66.7m, criteria[3].Score, "one of three warnings");
            Assert.IsTrue(criteria.All(c => !string.IsNullOrWhiteSpace(c.Basis)));
            Assert.AreEqual(87.7m, SupplierScoring.Total(criteria), "(90x40 + 90x30 + 90x20 + 66.7x10) / 100");

            // the weights are the company's: with the price weight at zero the price does not count
            s[ProcurementSettingKeys.EvalWeightPrice] = 0m;
            s[ProcurementSettingKeys.EvalWeightQuality] = 100m;
            s[ProcurementSettingKeys.EvalWeightDelivery] = 0m;
            s[ProcurementSettingKeys.EvalWeightService] = 0m;
            Assert.AreEqual(90m, SupplierScoring.Total(SupplierScoring.Criteria(metrics, s)));
        }

        [TestMethod]
        public void ACriterionWithoutDataIsLeftOutAndASupplierWithoutAnyDataHasNoScore()
        {
            Dictionary<string, decimal> s = Settings();
            Assert.IsNull(SupplierScoring.Total(SupplierScoring.Criteria(new SupplierMetrics(), s)), "nothing recorded yet: the manual questionnaire is the only way");
            Assert.IsTrue(SupplierScoring.Criteria(new SupplierMetrics(), s).All(c => !c.Score.HasValue));

            // only price data: the score is the price score
            var priceOnly = new SupplierMetrics { PriceRatios = new List<decimal> { 0.8m } };
            Assert.AreEqual(80m, SupplierScoring.Total(SupplierScoring.Criteria(priceOnly, s)));

            // one defect and nothing received: the quality is zero (and counts)
            List<CriterionScore> defect = SupplierScoring.Criteria(new SupplierMetrics { Ncrs = 1 }, s);
            Assert.AreEqual(0m, defect[0].Score);
            Assert.IsFalse(defect[1].Score.HasValue, "no receipt line: no delivery score");
            Assert.AreEqual(0m, SupplierScoring.Total(defect));

            // an order and no warning: the service criterion alone is 100
            Assert.AreEqual(100m, SupplierScoring.Total(SupplierScoring.Criteria(new SupplierMetrics { Orders = 1 }, s)));

            // every criterion that has data has weight zero: no score
            s[ProcurementSettingKeys.EvalWeightPrice] = 0m;
            Assert.IsNull(SupplierScoring.Total(SupplierScoring.Criteria(priceOnly, s)));
            Assert.AreEqual(Entities.Model.NasbNiro.Procurement.SupplierGrade.A, SupplierGrades.FromScore(87.7m));
        }

        // ------------------------------------------------------------------------------------------------ the deductions of the order terms

        [TestMethod]
        public void TheLatePenaltyIsProposedPerInvoiceLineFromTheDueDateAndNeverTakenTwice()
        {
            DateTime due = new DateTime(2026, 5, 1);
            var lines = new List<(decimal LineAmount, DateTime? Due, DateTime? Received)>
            {
                (10_000_000m, due, due.AddDays(14)),
                (5_000_000m, due, due),
                (4_000_000m, due, null),
                (3_000_000m, null, due.AddDays(30)),
            };
            PaymentProposals.Proposal p = PaymentProposals.LatePenalty(1m, lines, 0m, 100_000_000m);
            Assert.IsNotNull(p);
            Assert.AreEqual(FnDeductionKind.LatePenalty, p.Kind);
            Assert.AreEqual(200_000m, p.Amount, "10,000,000 x 1 percent a week x two weeks; the lines on time, not received or without a due date add nothing");
            Assert.AreEqual(10_000_000m, p.Base);
            Assert.AreEqual(1m, p.RatePercent);
            Assert.IsTrue(p.Note.Contains(PaymentProposals.Marker), "the line says that the system proposed it");
            Assert.IsTrue(p.Note.Contains("14"), p.Note);

            Assert.AreEqual(150_000m, PaymentProposals.LatePenalty(1m, lines, 50_000m, 100_000_000m).Amount, "what another request of the invoice deducts already is subtracted");
            Assert.IsNull(PaymentProposals.LatePenalty(1m, lines, 200_000m, 100_000_000m), "all of it was taken");
            Assert.AreEqual(120_000m, PaymentProposals.LatePenalty(1m, lines, 0m, 120_000m).Amount, "the room left in the request is the limit");
            Assert.IsNull(PaymentProposals.LatePenalty(1m, lines, 0m, 0m));
            Assert.IsNull(PaymentProposals.LatePenalty(null, lines, 0m, 100_000_000m), "no rate in the order");
            Assert.IsNull(PaymentProposals.LatePenalty(0m, lines, 0m, 100_000_000m));
        }

        [TestMethod]
        public void TheRetentionIsAShareOfThePaymentLimitedByTheRoomLeft()
        {
            PaymentProposals.Proposal r = PaymentProposals.Retention(10m, 2_000_000m, 2_000_000m);
            Assert.IsNotNull(r);
            Assert.AreEqual(FnDeductionKind.PerformanceRetention, r.Kind);
            Assert.AreEqual(200_000m, r.Amount);
            Assert.AreEqual(2_000_000m, r.Base);
            Assert.AreEqual(10m, r.RatePercent);
            Assert.IsTrue(r.Note.Contains(PaymentProposals.Marker));
            Assert.AreEqual(50_000m, PaymentProposals.Retention(10m, 2_000_000m, 50_000m).Amount);
            Assert.IsNull(PaymentProposals.Retention(null, 2_000_000m, 2_000_000m));
            Assert.IsNull(PaymentProposals.Retention(0m, 2_000_000m, 2_000_000m));
            Assert.IsNull(PaymentProposals.Retention(10m, 0m, 2_000_000m));
            Assert.IsNull(PaymentProposals.Retention(10m, 2_000_000m, 0m));
        }

        [TestMethod]
        public void TheFrozenSnapshotOfAnOrderCarriesTheRevisionTheTermsAndTheLinesInOrder()
        {
            var order = new PurchaseOrder
            {
                Number = "PO-9", Revision = 2, Status = PurchaseOrderStatus.Approved, Kind = PurchaseKind.Goods, OrderDate = new DateTime(2026, 5, 1), ProviderId = 7, InquiryId = 3, QuoteId = 4,
                TotalAmount = 1_000m, RevisionReason = "delivery moved", DeliveryDate = new DateTime(2026, 6, 1), RetentionPercent = 10m, LatePenaltyPercentPerWeek = 1m,
                Lines = new List<PurchaseOrderLine>
                {
                    new PurchaseOrderLine { RowNumber = 2, Description = "second", Quantity = 1m, UnitPrice = 600m },
                    new PurchaseOrderLine { RowNumber = 1, Description = "first", Quantity = 2m, UnitPrice = 200m },
                },
            };
            string json = OrderSnapshot.Json(order);
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                Assert.AreEqual("PO-9", root.GetProperty("Number").GetString());
                Assert.AreEqual(2, root.GetProperty("Revision").GetInt32());
                Assert.AreEqual("2026-06-01", root.GetProperty("DeliveryDate").GetString());
                Assert.AreEqual(10m, root.GetProperty("RetentionPercent").GetDecimal());
                JsonElement lines = root.GetProperty("Lines");
                Assert.AreEqual(2, lines.GetArrayLength());
                Assert.AreEqual("first", lines[0].GetProperty("Description").GetString(), "lines are in row order");
                Assert.AreEqual(600m, lines[1].GetProperty("UnitPrice").GetDecimal());
            }
            Assert.AreEqual(json, OrderSnapshot.Json(order), "the same order gives the same text (so the hash is stable)");
            order.Lines.First().UnitPrice = 601m;
            Assert.AreNotEqual(json, OrderSnapshot.Json(order), "a changed price changes the snapshot");
        }

        // ------------------------------------------------------------------------------------------------ the services

        private sealed class Seed
        {
            public int ProviderA, ProviderB, Item, RequestId, Line1, Line2;
        }

        /// <summary>An approved purchase request with two lines (10 and 5 units) and two suppliers.</summary>
        private static Seed SeedRequest(AppHost host, decimal estimate, bool managementApproved, string board = null)
        {
            var seed = new Seed();
            host.Write(u =>
            {
                var a = AppHost.Add(u, new Provider { Name = "supplier A" });
                var b = AppHost.Add(u, new Provider { Name = "supplier B" });
                var item = AppHost.Add(u, new InventoryItem { Code = "I-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "plate", Unit = "عدد" });
                u.SaveChanges();
                var request = AppHost.Add(u, new PurchaseRequest
                {
                    Number = "PR-" + Guid.NewGuid().ToString("N").Substring(0, 6), Source = PurchaseRequestSource.AdHoc, Kind = PurchaseKind.Goods, Status = PurchaseRequestStatus.Approved,
                    RequestDate = DateTime.Today, EstimatedAmount = estimate, PlanningApprovedAt = DateTimeOffset.Now, PlanningApprovedByUserId = 12,
                    ManagementApprovedAt = managementApproved ? DateTimeOffset.Now : (DateTimeOffset?)null, ManagementApprovedByUserId = managementApproved ? 31 : (int?)null, BoardResolution = board,
                });
                u.SaveChanges();
                var l1 = AppHost.Add(u, new PurchaseRequestLine { PurchaseRequestId = request.Id, RowNumber = 1, ItemId = item.Id, Description = "plate", Unit = "عدد", Quantity = 10m, TechnicalRequirements = "EN 10204 3.1" });
                var l2 = AppHost.Add(u, new PurchaseRequestLine { PurchaseRequestId = request.Id, RowNumber = 2, ItemId = item.Id, Description = "plate 2", Unit = "عدد", Quantity = 5m, TechnicalRequirements = "EN 10204 3.1" });
                u.SaveChanges();
                seed.ProviderA = a.Id; seed.ProviderB = b.Id; seed.Item = item.Id; seed.RequestId = request.Id; seed.Line1 = l1.Id; seed.Line2 = l2.Id;
            }, Seeder);
            return seed;
        }

        private static SaveInquiryDto NewInquiry(Seed seed, int? id, params (int Provider, decimal Quantity, decimal UnitPrice)[] offers) => new SaveInquiryDto
        {
            Id = id, PurchaseRequestId = seed.RequestId, InquiryDate = DateTime.Today,
            Quotes = offers.Select(o => new SaveQuoteDto
            {
                ProviderId = o.Provider, QuoteNumber = "Q-" + o.Provider, QuoteDate = DateTime.Today, DeliveryDays = 30, PaymentTerms = "net 30", VatPercent = 10m,
                Lines = new List<SaveQuoteLineDto> { new SaveQuoteLineDto { RequestLineId = seed.Line1, Quantity = o.Quantity, UnitPrice = o.UnitPrice } },
            }).ToList(),
        };

        private static Task<ResponseDto<InquiryDto>> SaveInquiry(ClaimsPrincipal who, SaveInquiryDto dto) => H.As(who, p => p.GetRequiredService<IProcurementService>().SaveInquiryAsync(dto, CancellationToken.None));
        private static Task<ResponseDto<InquiryDto>> TechReview(ClaimsPrincipal who, int inquiryId, int quoteId, bool accepted, decimal? score = null, string note = null) =>
            H.As(who, p => p.GetRequiredService<IProcurementService>().TechnicalReviewAsync(new TechnicalReviewDto { InquiryId = inquiryId, QuoteId = quoteId, Accepted = accepted, Score = score, Note = note }, CancellationToken.None));
        private static Task<ResponseDto<InquiryDto>> SelectWinner(ClaimsPrincipal who, int inquiryId, int quoteId, string reason = null) =>
            H.As(who, p => p.GetRequiredService<IProcurementService>().SelectQuoteAsync(new SelectQuoteDto { InquiryId = inquiryId, QuoteId = quoteId, Reason = reason }, CancellationToken.None));
        private static Task<ResponseDto<InquiryDto>> InquiryAction(ClaimsPrincipal who, int id, string action, string reference = null) =>
            H.As(who, p => p.GetRequiredService<IProcurementService>().InquiryActionAsync(new ProcurementActionDto { Id = id, Action = action, Reference = reference }, CancellationToken.None));
        private static Task<ResponseDto<PurchaseOrderDto>> FromInquiry(ClaimsPrincipal who, int inquiryId) => H.As(who, p => p.GetRequiredService<IProcurementService>().CreateOrderFromInquiryAsync(inquiryId, CancellationToken.None));
        private static Task<ResponseDto<PurchaseOrderDto>> SaveOrder(ClaimsPrincipal who, SavePurchaseOrderDto dto) => H.As(who, p => p.GetRequiredService<IProcurementService>().SaveOrderAsync(dto, CancellationToken.None));
        private static Task<ResponseDto<PurchaseOrderDto>> OrderAction(ClaimsPrincipal who, int id, string action, string note = null) =>
            H.As(who, p => p.GetRequiredService<IProcurementService>().OrderActionAsync(new ProcurementActionDto { Id = id, Action = action, Note = note }, CancellationToken.None));

        private static int QuoteOf(InquiryDto inquiry, int provider) => inquiry.Quotes.Single(q => q.ProviderId == provider).Id;

        private static SavePurchaseOrderDto ToSave(PurchaseOrderDto o) => new SavePurchaseOrderDto
        {
            Id = o.Id, Kind = o.Kind, OrderDate = o.OrderDate, ProviderId = o.ProviderId, InquiryId = o.InquiryId, QuoteId = o.QuoteId, WorkOrderId = o.WorkOrderId, ContractId = o.ContractId,
            Currency = o.Currency, ExchangeRate = o.ExchangeRate, Incoterms = o.Incoterms, PaymentTerms = o.PaymentTerms, VatPercent = o.VatPercent, AdvancePercent = o.AdvancePercent,
            AdvanceGuarantee = o.AdvanceGuarantee, AdvanceGuaranteeExpiry = o.AdvanceGuaranteeExpiry, PerformanceGuarantee = o.PerformanceGuarantee, RetentionPercent = o.RetentionPercent,
            ContractNumber = o.ContractNumber, ContractSignedAt = o.ContractSignedAt, Requirements = o.Requirements, DeliveryDate = o.DeliveryDate, DeliveryPlace = o.DeliveryPlace,
            LatePenaltyPercentPerWeek = o.LatePenaltyPercentPerWeek, Description = o.Description,
            Lines = o.Lines.Select(l => new SavePurchaseOrderLineDto
            {
                RequestLineId = l.RequestLineId, ItemId = l.ItemId, Description = l.Description, Unit = l.Unit, Quantity = l.Quantity, UnitPrice = l.UnitPrice, DeliveryDate = l.DeliveryDate,
                WorkOrderId = l.WorkOrderId, ContractId = l.ContractId, IsCapital = l.IsCapital,
            }).ToList(),
        };

        [TestMethod]
        public async Task TheBuyerCannotAcceptAQuoteTechnicallyAndEngineeringOrQualityCanButNotTheirOwnInquiry()
        {
            AppHost host = NewHost("Technical");
            Seed seed = SeedRequest(host, 20_000_000m, managementApproved: true);

            // the buyer types the acceptance, the score or the note himself: refused
            SaveInquiryDto accepted = NewInquiry(seed, null, (seed.ProviderA, 6m, 3_500_000m));
            accepted.Quotes[0].TechnicallyAccepted = true;
            ResponseDto<InquiryDto> byBuyer = await SaveInquiry(Buyer, accepted);
            Assert.IsFalse(byBuyer.Success);
            Assert.IsTrue(byBuyer.Message.Contains("فقط مهندسی یا کنترل کیفیت"), byBuyer.Message);
            SaveInquiryDto scored = NewInquiry(seed, null, (seed.ProviderA, 6m, 3_500_000m));
            scored.Quotes[0].TechnicalScore = 95m;
            Assert.IsFalse((await SaveInquiry(Buyer, scored)).Success, "the buyer does not type the technical score");
            SaveInquiryDto noted = NewInquiry(seed, null, (seed.ProviderA, 6m, 3_500_000m));
            noted.Quotes[0].TechnicalNote = "fine";
            Assert.IsFalse((await SaveInquiry(Buyer, noted)).Success, "... or the note");

            // the PMO deputy approves purchases and never buys
            Assert.IsFalse((await SaveInquiry(Pmo1, NewInquiry(seed, null, (seed.ProviderA, 6m, 3_500_000m)))).Success);
            ResponseDto<InquiryDto> byPmo = await SaveInquiry(Pmo1, NewInquiry(seed, null, (seed.ProviderA, 6m, 3_500_000m)));
            Assert.IsTrue(byPmo.Message.Contains(NotAllowed), byPmo.Message);

            ResponseDto<InquiryDto> saved = await SaveInquiry(Buyer, NewInquiry(seed, null, (seed.ProviderA, 6m, 3_500_000m), (seed.ProviderB, 6m, 4_000_000m)));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.IsTrue(saved.Data.Quotes.All(q => !q.TechnicallyAccepted), "a new quote is not accepted until engineering or quality judges it");
            int inquiryId = saved.Data.Id, quoteA = QuoteOf(saved.Data, seed.ProviderA), quoteB = QuoteOf(saved.Data, seed.ProviderB);

            // nobody but engineering and quality judges
            ResponseDto<InquiryDto> buyerReview = await TechReview(Buyer, inquiryId, quoteA, true);
            Assert.IsFalse(buyerReview.Success);
            Assert.IsTrue(buyerReview.Message.Contains(NotAllowed), buyerReview.Message);
            Assert.IsFalse((await TechReview(Pmo1, inquiryId, quoteA, true)).Success, "the PMO deputy does not judge technically either");
            Assert.IsFalse((await TechReview(Ceo, inquiryId, quoteA, true)).Success);

            // a rejection needs its reason, a score is between 0 and 100
            Assert.IsFalse((await TechReview(Engineering, inquiryId, quoteA, false)).Success);
            Assert.IsFalse((await TechReview(Engineering, inquiryId, quoteA, true, 101m)).Success);

            ResponseDto<InquiryDto> engineered = await TechReview(Engineering, inquiryId, quoteA, true, 90m, "meets the drawing");
            Assert.IsTrue(engineered.Success, engineered.Message);
            QuoteDto judged = engineered.Data.Quotes.Single(q => q.Id == quoteA);
            Assert.IsTrue(judged.TechnicallyAccepted);
            Assert.AreEqual(90m, judged.TechnicalScore);
            Assert.AreEqual("meets the drawing", judged.TechnicalNote);
            Assert.IsNotNull(judged.TechnicalReviewedAt);
            Assert.IsFalse(engineered.Data.Quotes.Single(q => q.Id == quoteB).TechnicallyAccepted);
            ResponseDto<InquiryDto> byQc = await TechReview(Qc, inquiryId, quoteB, false, null, "no certificate");
            Assert.IsTrue(byQc.Success, byQc.Message);
            Assert.IsFalse(byQc.Data.Quotes.Single(q => q.Id == quoteB).TechnicallyAccepted);
            Assert.AreEqual("no certificate", byQc.Data.Quotes.Single(q => q.Id == quoteB).TechnicalNote);

            // the audit trail knows who judged
            List<FnAuditLog> trail = host.Read(u => u.Set<FnAuditLog>().Where(l => l.EntityType == nameof(SupplierQuote) && l.Action == "Modified").OrderBy(l => l.Id).ToList());
            Assert.IsTrue(trail.Any(l => l.UserId == 21 && l.AfterJson.Contains("TechnicallyAccepted")), "engineering's acceptance");
            Assert.IsTrue(trail.Any(l => l.UserId == 25), "quality's rejection");

            // the buyer saves the inquiry again: an unchanged offer keeps its judgement, a changed offer is judged again
            SaveInquiryDto again = NewInquiry(seed, inquiryId, (seed.ProviderA, 6m, 3_500_000m), (seed.ProviderB, 6m, 4_000_000m));
            again.Quotes[0].TechnicallyAccepted = true;   // the screen sends back what it read
            ResponseDto<InquiryDto> kept = await SaveInquiry(Buyer, again);
            Assert.IsTrue(kept.Success, kept.Message);
            Assert.IsTrue(kept.Data.Quotes.Single(q => q.ProviderId == seed.ProviderA).TechnicallyAccepted, "the offer did not change");
            SaveInquiryDto changed = NewInquiry(seed, inquiryId, (seed.ProviderA, 5m, 3_500_000m), (seed.ProviderB, 6m, 4_000_000m));
            changed.Quotes[0].TechnicallyAccepted = true;
            ResponseDto<InquiryDto> reset = await SaveInquiry(Buyer, changed);
            Assert.IsTrue(reset.Success, reset.Message);
            Assert.IsFalse(reset.Data.Quotes.Single(q => q.ProviderId == seed.ProviderA).TechnicallyAccepted, "a changed offer must be judged again");

            // the person who registered the inquiry does not judge its quotes, even with the quality role
            ResponseDto<InquiryDto> own = await SaveInquiry(BuyerAndQc, NewInquiry(seed, null, (seed.ProviderB, 6m, 4_100_000m)));
            Assert.IsTrue(own.Success, own.Message);
            ResponseDto<InquiryDto> ownReview = await TechReview(BuyerAndQc, own.Data.Id, own.Data.Quotes[0].Id, true);
            Assert.IsFalse(ownReview.Success);
            Assert.IsTrue(ownReview.Message.Contains("تفکیک وظایف"), ownReview.Message);

            // after the winner is chosen the judgement is frozen
            ResponseDto<InquiryDto> second = await TechReview(Engineering, inquiryId, QuoteOf(reset.Data, seed.ProviderA), true, 88m);
            Assert.IsTrue(second.Success, second.Message);
            Assert.IsTrue((await TechReview(Qc, inquiryId, QuoteOf(second.Data, seed.ProviderB), true, 70m)).Success);
            ResponseDto<InquiryDto> chosen = await SelectWinner(Buyer, inquiryId, QuoteOf(second.Data, seed.ProviderA));
            Assert.IsTrue(chosen.Success, chosen.Message);
            Assert.IsFalse((await TechReview(Engineering, inquiryId, QuoteOf(second.Data, seed.ProviderB), false, null, "changed my mind")).Success, "no change of the judgement after the selection");
        }

        [TestMethod]
        public async Task AnOrderIsBuiltOnlyFromTheQuoteThatWonAndItsLinesAreTheLinesOfThatQuote()
        {
            AppHost host = NewHost("Winner");
            Seed seed = SeedRequest(host, 20_000_000m, managementApproved: true);
            ResponseDto<InquiryDto> saved = await SaveInquiry(Buyer, NewInquiry(seed, null, (seed.ProviderA, 6m, 3_500_000m), (seed.ProviderB, 6m, 4_000_000m)));
            Assert.IsTrue(saved.Success, saved.Message);
            int inquiryId = saved.Data.Id, quoteA = QuoteOf(saved.Data, seed.ProviderA), quoteB = QuoteOf(saved.Data, seed.ProviderB);

            // the quote that nobody judged cannot win
            ResponseDto<InquiryDto> notJudged = await SelectWinner(Buyer, inquiryId, quoteA);
            Assert.IsFalse(notJudged.Success, "no technical acceptance, no selection");
            Assert.IsTrue((await TechReview(Engineering, inquiryId, quoteA, true, 90m)).Success);
            Assert.IsTrue((await TechReview(Qc, inquiryId, quoteB, true, 80m)).Success);
            Assert.IsFalse((await SelectWinner(Pmo1, inquiryId, quoteA)).Success, "the PMO deputy does not select");
            ResponseDto<InquiryDto> chosen = await SelectWinner(Buyer, inquiryId, quoteA);
            Assert.IsTrue(chosen.Success, chosen.Message);
            Assert.AreEqual(InquiryStatus.Approved, chosen.Data.Status, "21 million, the request was approved by the managing director: no committee is needed");

            // an order for the other supplier, or from the other quote, is refused
            SavePurchaseOrderDto Dto(int provider, int? quote, decimal quantity, decimal price, int requestLine) => new SavePurchaseOrderDto
            {
                Kind = PurchaseKind.Goods, OrderDate = DateTime.Today, ProviderId = provider, InquiryId = inquiryId, QuoteId = quote, DeliveryDate = DateTime.Today.AddDays(30), Requirements = "EN 10204 3.1",
                Lines = new List<SavePurchaseOrderLineDto> { new SavePurchaseOrderLineDto { RequestLineId = requestLine, ItemId = seed.Item, Description = "plate", Unit = "عدد", Quantity = quantity, UnitPrice = price } },
            };
            ResponseDto<PurchaseOrderDto> otherSupplier = await SaveOrder(Buyer, Dto(seed.ProviderB, null, 6m, 3_500_000m, seed.Line1));
            Assert.IsFalse(otherSupplier.Success);
            Assert.IsTrue(otherSupplier.Message.Contains("با برنده استعلام یکی نیست"), otherSupplier.Message);
            ResponseDto<PurchaseOrderDto> otherQuote = await SaveOrder(Buyer, Dto(seed.ProviderA, quoteB, 6m, 3_500_000m, seed.Line1));
            Assert.IsFalse(otherQuote.Success);
            Assert.IsTrue(otherQuote.Message.Contains("پیشنهاد برنده"), otherQuote.Message);
            ResponseDto<PurchaseOrderDto> noInquiry = await SaveOrder(Buyer, new SavePurchaseOrderDto { Kind = PurchaseKind.Goods, ProviderId = seed.ProviderA, QuoteId = quoteA, Lines = Dto(seed.ProviderA, null, 6m, 3_500_000m, seed.Line1).Lines });
            Assert.IsFalse(noInquiry.Success, "a quote without its inquiry means nothing");

            // the lines differ from the quote
            ResponseDto<PurchaseOrderDto> price = await SaveOrder(Buyer, Dto(seed.ProviderA, null, 6m, 3_000_000m, seed.Line1));
            Assert.IsFalse(price.Success);
            Assert.IsTrue(price.Message.Contains("قیمت پیشنهاد برنده"), price.Message);
            ResponseDto<PurchaseOrderDto> quantity = await SaveOrder(Buyer, Dto(seed.ProviderA, null, 8m, 3_500_000m, seed.Line1));
            Assert.IsFalse(quantity.Success);
            Assert.IsTrue(quantity.Message.Contains("مقدار بیشتر از مقدار پیشنهاد برنده"), quantity.Message);
            ResponseDto<PurchaseOrderDto> notQuoted = await SaveOrder(Buyer, Dto(seed.ProviderA, null, 2m, 3_500_000m, seed.Line2));
            Assert.IsFalse(notQuoted.Success);
            Assert.IsTrue(notQuoted.Message.Contains("در پیشنهاد برنده استعلام نیست"), notQuoted.Message);

            // the PMO deputy does not make orders
            Assert.IsFalse((await FromInquiry(Pmo1, inquiryId)).Success);

            // the right order
            ResponseDto<PurchaseOrderDto> order = await SaveOrder(Buyer, Dto(seed.ProviderA, quoteA, 6m, 3_500_000m, seed.Line1));
            Assert.IsTrue(order.Success, order.Message);
            Assert.AreEqual(quoteA, order.Data.QuoteId);
            Assert.AreEqual(21_000_000m, order.Data.TotalAmount);
            Assert.AreEqual(0, order.Data.Revision);

            // the same through the inquiry: the quantity that is left of the quote is 0, so a second order for the same quote is refused
            ResponseDto<PurchaseOrderDto> again = await FromInquiry(Buyer, inquiryId);
            Assert.IsFalse(again.Success, "the quote is ordered already");

            // the buyer cannot approve; the PMO deputy does, and not the one who made the order
            Assert.IsFalse((await OrderAction(Buyer, order.Data.Id, "approve")).Success);
            ResponseDto<PurchaseOrderDto> approved = await OrderAction(Pmo1, order.Data.Id, "approve");
            Assert.IsTrue(approved.Success, approved.Message);
            Assert.AreEqual(PurchaseOrderStatus.Approved, approved.Data.Status);
        }

        [TestMethod]
        public async Task TheSelectedQuoteNeedsAsManyAcceptedQuotesAsItsOwnValueAndTheManagingDirectorCoversFewerOnes()
        {
            AppHost host = NewHost("Value");
            // the request was approved on 9 million (below the limit of the managing director), the only offer is 60 million
            Seed seed = SeedRequest(host, 9_000_000m, managementApproved: false);
            host.Write(u => { u.Set<SupplierProfile>().Add(new SupplierProfile { ProviderId = seed.ProviderA, Grade = SupplierGrade.B, Score = 70m }); u.SaveChanges(); }, Seeder);
            ResponseDto<InquiryDto> saved = await SaveInquiry(Buyer, NewInquiry(seed, null, (seed.ProviderA, 10m, 6_000_000m)));
            Assert.IsTrue(saved.Success, saved.Message);
            int inquiryId = saved.Data.Id, quoteA = QuoteOf(saved.Data, seed.ProviderA);
            Assert.AreEqual(3, saved.Data.RequiredQuotes, "the offer itself decides how many quotes are needed");
            Assert.IsTrue((await TechReview(Engineering, inquiryId, quoteA, true)).Success);

            ResponseDto<InquiryDto> tooFew = await SelectWinner(Buyer, inquiryId, quoteA);
            Assert.IsFalse(tooFew.Success);
            Assert.IsTrue(tooFew.Message.Contains("3 پیشنهاد قابل قبول فنی لازم است"), tooFew.Message);

            // with the reason, the managing director has to agree
            SaveInquiryDto withReason = NewInquiry(seed, inquiryId, (seed.ProviderA, 10m, 6_000_000m));
            withReason.FewerQuotesReason = "only one maker of this plate";
            withReason.Quotes[0].TechnicallyAccepted = true;
            ResponseDto<InquiryDto> resaved = await SaveInquiry(Buyer, withReason);
            Assert.IsTrue(resaved.Success, resaved.Message);
            quoteA = QuoteOf(resaved.Data, seed.ProviderA);
            ResponseDto<InquiryDto> selected = await SelectWinner(Buyer, inquiryId, quoteA);
            Assert.IsTrue(selected.Success, selected.Message);
            Assert.AreEqual(InquiryStatus.Selected, selected.Data.Status, "waiting for the managing director");
            Assert.IsTrue(selected.Data.NeedsManagement);
            Assert.AreEqual(60_000_000m, selected.Data.AuthorityAmount);

            ResponseDto<PurchaseOrderDto> early = await FromInquiry(Buyer, inquiryId);
            Assert.IsFalse(early.Success);
            Assert.AreEqual(ProcurementAuthority.InquiryNotApproved, early.Message);

            // the buyer who selected cannot approve in the committee; the PMO deputy is not the managing director
            Assert.IsFalse((await InquiryAction(Buyer, inquiryId, "approve-management")).Success);
            Assert.IsFalse((await InquiryAction(Engineering, inquiryId, "approve-management")).Success);
            ResponseDto<InquiryDto> ceo = await InquiryAction(Ceo, inquiryId, "approve-management", "minutes 14");
            Assert.IsTrue(ceo.Success, ceo.Message);
            Assert.AreEqual(InquiryStatus.Approved, ceo.Data.Status);

            ResponseDto<PurchaseOrderDto> order = await FromInquiry(Buyer, inquiryId);
            Assert.IsTrue(order.Success, order.Message);
            Assert.AreEqual(60_000_000m, order.Data.TotalAmount);
            Assert.AreEqual(quoteA, order.Data.QuoteId);
        }

        [TestMethod]
        public async Task AnOrderIsJudgedAgainAtItsApprovalWithTheSettingsOfThatDay()
        {
            AppHost host = NewHost("Approval");
            Seed seed = SeedRequest(host, 20_000_000m, managementApproved: true);
            ResponseDto<InquiryDto> saved = await SaveInquiry(Buyer, NewInquiry(seed, null, (seed.ProviderA, 6m, 3_500_000m)));
            int inquiryId = saved.Data.Id, quoteA = QuoteOf(saved.Data, seed.ProviderA);
            Assert.IsTrue((await TechReview(Engineering, inquiryId, quoteA, true)).Success);
            Assert.IsTrue((await SelectWinner(Buyer, inquiryId, quoteA)).Success);
            ResponseDto<PurchaseOrderDto> order = await FromInquiry(Buyer, inquiryId);
            Assert.IsTrue(order.Success, order.Message);
            Assert.AreEqual(21_000_000m, order.Data.TotalAmount);

            // the company tightens the rule: two quotes from 18 million (setting of the managing director or the PMO deputy)
            Assert.IsFalse((await host.As(Buyer, p => p.GetRequiredService<IProcurementService>().SaveSettingAsync(new ProcurementSettingDto { Key = ProcurementSettingKeys.Quote2From, Value = 18_000_000m }, CancellationToken.None))).Success, "the buyer does not change the limits");
            ResponseDto<ProcurementSettingDto> setting = await host.As(Ceo, p => p.GetRequiredService<IProcurementService>().SaveSettingAsync(new ProcurementSettingDto { Key = ProcurementSettingKeys.Quote2From, Value = 18_000_000m }, CancellationToken.None));
            Assert.IsTrue(setting.Success, setting.Message);

            ResponseDto<PurchaseOrderDto> refused = await OrderAction(Pmo1, order.Data.Id, "approve");
            Assert.IsFalse(refused.Success);
            Assert.IsTrue(refused.Message.Contains("2 پیشنهاد قابل قبول فنی لازم است"), refused.Message);
            Assert.AreEqual(PurchaseOrderStatus.Draft, host.Read(u => u.Set<PurchaseOrder>().Single(o => o.Id == order.Data.Id).Status));

            // the setting is back: the order passes
            Assert.IsTrue((await host.As(Pmo2, p => p.GetRequiredService<IProcurementService>().SaveSettingAsync(new ProcurementSettingDto { Key = ProcurementSettingKeys.Quote2From, Value = 30_000_000m }, CancellationToken.None))).Success);
            ResponseDto<PurchaseOrderDto> approved = await OrderAction(Pmo1, order.Data.Id, "approve");
            Assert.IsTrue(approved.Success, approved.Message);
        }

        [TestMethod]
        public async Task ARevisedOrderFreezesTheOldRevisionGoesBackToDraftAndNeedsANewApprovalByAnotherPerson()
        {
            AppHost host = NewHost("Revision");
            Seed seed = SeedRequest(host, 20_000_000m, managementApproved: true);
            ResponseDto<InquiryDto> saved = await SaveInquiry(Buyer, NewInquiry(seed, null, (seed.ProviderA, 6m, 3_500_000m)));
            int inquiryId = saved.Data.Id, quoteA = QuoteOf(saved.Data, seed.ProviderA);
            Assert.IsTrue((await TechReview(Engineering, inquiryId, quoteA, true)).Success);
            Assert.IsTrue((await SelectWinner(Buyer, inquiryId, quoteA)).Success);
            ResponseDto<PurchaseOrderDto> created = await FromInquiry(Buyer, inquiryId);
            Assert.IsTrue(created.Success, created.Message);
            int orderId = created.Data.Id;

            // a draft is edited directly, not revised
            ResponseDto<PurchaseOrderDto> draftRevise = await OrderAction(PmoAndBuyer, orderId, "revise", "too early");
            Assert.IsFalse(draftRevise.Success);
            Assert.IsTrue((await OrderAction(Pmo1, orderId, "approve")).Success);

            // only the buyer revises, with a reason
            Assert.IsFalse((await OrderAction(Pmo1, orderId, "revise", "x")).Success, "the PMO deputy does not revise");
            Assert.IsFalse((await OrderAction(Buyer, orderId, "revise")).Success, "the reason is required");
            ResponseDto<PurchaseOrderDto> revised = await OrderAction(PmoAndBuyer, orderId, "revise", "delivery moved by two weeks");
            Assert.IsTrue(revised.Success, revised.Message);
            Assert.AreEqual(1, revised.Data.Revision);
            Assert.AreEqual(PurchaseOrderStatus.Draft, revised.Data.Status);
            Assert.IsNull(revised.Data.ApprovedAt, "the approval belonged to the old revision");
            Assert.AreEqual("delivery moved by two weeks", revised.Data.RevisionReason);
            Assert.IsNotNull(revised.Data.RevisedAt);

            // the old revision is frozen with a hash
            List<PurchaseOrderRevisionDto> frozen = (await host.As(Pmo1, p => p.GetRequiredService<IProcurementService>().GetOrderRevisionsAsync(orderId, CancellationToken.None))).Data;
            Assert.AreEqual(1, frozen.Count);
            Assert.AreEqual(0, frozen[0].Revision);
            Assert.AreEqual(PurchaseOrderStatus.Approved, frozen[0].StatusWhenFrozen);
            Assert.AreEqual(21_000_000m, frozen[0].TotalAmount);
            Assert.AreEqual(Services.NasbNiro.ProjectControl.PcText.Sha256(frozen[0].SnapshotJson), frozen[0].Hash);
            Assert.AreEqual(0, JsonDocument.Parse(frozen[0].SnapshotJson).RootElement.GetProperty("Revision").GetInt32());
            string firstSnapshot = frozen[0].SnapshotJson;

            // the reviser does not approve the revision, another PMO deputy does
            ResponseDto<PurchaseOrderDto> self = await OrderAction(PmoAndBuyer, orderId, "approve");
            Assert.IsFalse(self.Success);
            Assert.IsTrue(self.Message.Contains(SameUser), self.Message);

            // the draft is edited (a new delivery date) and approved again; the frozen text of revision 0 does not change
            PurchaseOrderDto current = (await host.As(Pmo1, p => p.GetRequiredService<IProcurementService>().GetOrderAsync(orderId, CancellationToken.None))).Data;
            SavePurchaseOrderDto edit = ToSave(current);
            edit.DeliveryDate = DateTime.Today.AddDays(44);
            Assert.IsTrue((await SaveOrder(PmoAndBuyer, edit)).Success);
            Assert.IsTrue((await OrderAction(Pmo2, orderId, "approve")).Success);
            Assert.IsTrue((await OrderAction(Buyer, orderId, "release")).Success);
            ResponseDto<PurchaseOrderDto> second = await OrderAction(Buyer, orderId, "revise", "quantity of the second delivery");
            Assert.IsTrue(second.Success, second.Message);
            Assert.AreEqual(2, second.Data.Revision);
            frozen = (await host.As(Pmo1, p => p.GetRequiredService<IProcurementService>().GetOrderRevisionsAsync(orderId, CancellationToken.None))).Data;
            Assert.AreEqual(2, frozen.Count);
            Assert.AreEqual(firstSnapshot, frozen[0].SnapshotJson, "the first revision stays as it was");
            Assert.AreEqual(1, frozen[1].Revision);
            Assert.AreEqual(PurchaseOrderStatus.Released, frozen[1].StatusWhenFrozen);
            Assert.AreNotEqual(frozen[0].SnapshotJson, frozen[1].SnapshotJson);
            Assert.AreNotEqual(frozen[0].Hash, frozen[1].Hash);

            // the audit trail keeps the revision with its user
            List<FnAuditLog> trail = host.Read(u => u.Set<FnAuditLog>().Where(l => l.EntityType == nameof(PurchaseOrder) && l.Action == "Modified").OrderBy(l => l.Id).ToList());
            Assert.IsTrue(trail.Any(l => l.UserId == 30 && l.AfterJson.Contains("RevisionReason")), "the revision by the buyer");
            Assert.IsTrue(trail.Any(l => l.UserId == 11 && l.AfterJson.Contains("ApprovedByUserId")), "the first approval");
        }

        // ------------------------------------------------------------------------------------------------ the scored evaluation, the vendor list and the deductions

        [TestMethod]
        public async Task TheScoredEvaluationReadsTheRecordedPerformanceWithTheWeightsOfTheSettingsAndNeedsAComment()
        {
            AppHost host = NewHost("Evaluation");
            Seed seed = SeedRequest(host, 20_000_000m, managementApproved: true);
            host.Write(u =>
            {
                AppHost.Add(u, new PurchaseInquiry
                {
                    Number = "INQ-9", Status = InquiryStatus.Approved, InquiryDate = DateTime.Today, PurchaseRequestId = seed.RequestId,
                    Quotes = new List<SupplierQuote>
                    {
                        new SupplierQuote { ProviderId = seed.ProviderA, TotalAmount = 800m, TechnicallyAccepted = true, QuoteDate = DateTime.Today },
                        new SupplierQuote { ProviderId = seed.ProviderB, TotalAmount = 1000m, TechnicallyAccepted = true, QuoteDate = DateTime.Today },
                    },
                });
                u.SaveChanges();
            }, Seeder);

            Task<ResponseDto<SupplierEvaluationPreviewDto>> Preview(int provider) => host.As(Buyer, p => p.GetRequiredService<IPayablesService>().GetEvaluationPreviewAsync(provider, CancellationToken.None));
            Task<ResponseDto<SupplierEvaluationDto>> Evaluate(ClaimsPrincipal who, SaveSupplierEvaluationDto dto) => host.As(who, p => p.GetRequiredService<IPayablesService>().EvaluateAsync(dto, CancellationToken.None));

            // the lowest accepted quote of the inquiry is 800: supplier A scores 100 on price, supplier B 80; nothing else is recorded
            SupplierEvaluationPreviewDto a = (await Preview(seed.ProviderA)).Data, b = (await Preview(seed.ProviderB)).Data;
            Assert.IsTrue(a.HasData);
            Assert.AreEqual(100m, a.Score);
            Assert.AreEqual(SupplierGrade.A, a.Grade);
            Assert.AreEqual(80m, b.Score);
            Assert.AreEqual(SupplierGrade.B, b.Grade);
            Assert.AreEqual(4, b.Criteria.Count);
            Assert.AreEqual(80m, b.Criteria.Single(c => c.Question == "قیمت").Score);
            Assert.AreEqual(20m, b.Criteria.Single(c => c.Question == "قیمت").Weight);
            Assert.IsNull(b.Criteria.Single(c => c.Question == "کیفیت").Score, "no receipt yet");
            Assert.IsFalse(string.IsNullOrWhiteSpace(b.Criteria.Single(c => c.Question == "قیمت").Basis));

            // the evaluator writes a comment; the scores cannot be typed
            ResponseDto<SupplierEvaluationDto> noComment = await Evaluate(Buyer, new SaveSupplierEvaluationDto { ProviderId = seed.ProviderB, Scored = true });
            Assert.IsFalse(noComment.Success);
            Assert.IsTrue(noComment.Message.Contains("نظر ارزیاب"), noComment.Message);
            Assert.IsFalse((await Evaluate(Accountant, new SaveSupplierEvaluationDto { ProviderId = seed.ProviderB, Scored = true, Notes = "x" })).Success, "finance does not evaluate suppliers");
            var typed = new SaveSupplierEvaluationDto
            {
                ProviderId = seed.ProviderB, Scored = true, Notes = "answers quickly; price is 25 percent above the lowest",
                Lines = new List<EvaluationLineDto> { new EvaluationLineDto { Question = "قیمت", Weight = 20m, Score = 5m } },
            };
            ResponseDto<SupplierEvaluationDto> done = await Evaluate(Buyer, typed);
            Assert.IsTrue(done.Success, done.Message);
            Assert.IsTrue(done.Data.Scored);
            Assert.AreEqual(80m, done.Data.Score, "the typed line was ignored");
            Assert.AreEqual(SupplierGrade.B, done.Data.Grade);
            Assert.AreEqual(4, done.Data.Lines.Count);
            Assert.AreEqual("answers quickly; price is 25 percent above the lowest", done.Data.Notes);
            Assert.IsTrue(done.Data.Lines.Any(l => !string.IsNullOrWhiteSpace(l.Basis)));
            SupplierProfile profile = host.Read(u => u.Set<SupplierProfile>().Single(p => p.ProviderId == seed.ProviderB));
            Assert.AreEqual(SupplierGrade.B, profile.Grade);
            Assert.AreEqual(80m, profile.Score);

            // the weights are the company's: with the price weight at zero B has nothing left to be scored on
            Assert.IsTrue((await host.As(Ceo, p => p.GetRequiredService<IProcurementService>().SaveSettingAsync(new ProcurementSettingDto { Key = ProcurementSettingKeys.EvalWeightPrice, Value = 0m }, CancellationToken.None))).Success);
            Assert.IsFalse((await Preview(seed.ProviderB)).Data.HasData);
            ResponseDto<SupplierEvaluationDto> none = await Evaluate(Buyer, new SaveSupplierEvaluationDto { ProviderId = seed.ProviderB, Scored = true, Notes = "again" });
            Assert.IsFalse(none.Success);

            // the questionnaire of the procedure (FO-PO-003) stays available for a supplier without recorded performance
            var manual = new SaveSupplierEvaluationDto
            {
                ProviderId = seed.ProviderB, Scored = false, Notes = "initial evaluation",
                Lines = new List<EvaluationLineDto> { new EvaluationLineDto { Question = "documents", Weight = 1m, Score = 70m }, new EvaluationLineDto { Question = "references", Weight = 1m, Score = 90m } },
            };
            ResponseDto<SupplierEvaluationDto> byQuestions = await Evaluate(Qc, manual);
            Assert.IsTrue(byQuestions.Success, byQuestions.Message);
            Assert.AreEqual(80m, byQuestions.Data.Score);
            Assert.IsFalse(byQuestions.Data.Scored);
        }

        [TestMethod]
        public async Task ThePmoDeputyBlocksASupplierButDoesNotEditTheVendorListAndOnlyManagementUnblocks()
        {
            AppHost host = NewHost("Vendors");
            Seed seed = SeedRequest(host, 1_000_000m, managementApproved: true);
            Task<ResponseDto<SupplierProfileDto>> Save(ClaimsPrincipal who, SaveSupplierProfileDto dto) => host.As(who, p => p.GetRequiredService<IPayablesService>().SaveSupplierAsync(dto, CancellationToken.None));

            ResponseDto<SupplierProfileDto> byBuyer = await Save(Buyer, new SaveSupplierProfileDto { ProviderId = seed.ProviderA, Categories = "steel", SepidarCode = "S-1" });
            Assert.IsTrue(byBuyer.Success, byBuyer.Message);
            Assert.AreEqual("steel", byBuyer.Data.Categories);
            Assert.IsFalse((await Save(Accountant, new SaveSupplierProfileDto { ProviderId = seed.ProviderA, Categories = "x" })).Success, "finance does not keep the vendor list");
            Assert.IsFalse((await Save(Engineering, new SaveSupplierProfileDto { ProviderId = seed.ProviderA, Categories = "x" })).Success);

            // the PMO deputy blocks (with a reason) and changes nothing else
            Assert.IsFalse((await Save(Pmo2, new SaveSupplierProfileDto { ProviderId = seed.ProviderA, Categories = "steel", Blocked = true })).Success, "the reason is required");
            ResponseDto<SupplierProfileDto> blocked = await Save(Pmo2, new SaveSupplierProfileDto { ProviderId = seed.ProviderA, Categories = "changed by the PMO", SepidarCode = "HACK", Blocked = true, BlockReason = "complaints from the site" });
            Assert.IsTrue(blocked.Success, blocked.Message);
            Assert.IsTrue(blocked.Data.Blocked);
            Assert.AreEqual("complaints from the site", blocked.Data.BlockReason);
            Assert.AreEqual("steel", blocked.Data.Categories, "the vendor list is the buyer's");
            Assert.AreEqual("S-1", blocked.Data.SepidarCode);

            // the buyer cannot lift the block; management can
            ResponseDto<SupplierProfileDto> lift = await Save(Buyer, new SaveSupplierProfileDto { ProviderId = seed.ProviderA, Categories = "steel", Blocked = false });
            Assert.IsFalse(lift.Success);
            Assert.IsTrue(lift.Message.Contains("رفع مسدودی"), lift.Message);
            Assert.IsTrue((await Save(Ceo, new SaveSupplierProfileDto { ProviderId = seed.ProviderA, Blocked = false })).Success);
            Assert.IsFalse(host.Read(u => u.Set<SupplierProfile>().Single(p => p.ProviderId == seed.ProviderA).Blocked));

            // the audit trail names the PMO deputy who blocked and the managing director who lifted
            List<FnAuditLog> trail = host.Read(u => u.Set<FnAuditLog>().Where(l => l.EntityType == nameof(SupplierProfile) && l.Action == "Modified").OrderBy(l => l.Id).ToList());
            Assert.IsTrue(trail.Any(l => l.UserId == 12 && l.AfterJson.Contains("Blocked")), "the block");
            Assert.IsTrue(trail.Any(l => l.UserId == 31 && l.AfterJson.Contains("Blocked")), "the lifting");

            // a blocked supplier gets no order
            Assert.IsTrue((await Save(Pmo2, new SaveSupplierProfileDto { ProviderId = seed.ProviderB, Blocked = true, BlockReason = "no certificate" })).Success);
            ResponseDto<InquiryDto> inquiry = await SaveInquiry(Buyer, NewInquiry(seed, null, (seed.ProviderA, 6m, 1_000m)));
            Assert.IsTrue(inquiry.Success, inquiry.Message);
            ResponseDto<PurchaseOrderDto> order = await SaveOrder(Buyer, new SavePurchaseOrderDto
            {
                Kind = PurchaseKind.Goods, ProviderId = seed.ProviderB, Requirements = "x",
                Lines = new List<SavePurchaseOrderLineDto> { new SavePurchaseOrderLineDto { RequestLineId = seed.Line1, ItemId = seed.Item, Quantity = 1m, UnitPrice = 1_000m } },
            });
            Assert.IsFalse(order.Success);
        }

        [TestMethod]
        public async Task TheLatePenaltyAndTheRetentionOfTheOrderAreProposedAndFinanceConfirmsThemBeforeThePaymentIsApproved()
        {
            AppHost host = NewHost("Payment");
            int orderId = 0, invoiceId = 0;
            host.Write(u =>
            {
                var provider = AppHost.Add(u, new Provider { Name = "supplier" });
                var item = AppHost.Add(u, new InventoryItem { Code = "I-1", Title = "plate", Unit = "عدد" });
                u.SaveChanges();
                var order = AppHost.Add(u, new PurchaseOrder
                {
                    Number = "PO-1", Status = PurchaseOrderStatus.Released, Kind = PurchaseKind.Goods, OrderDate = new DateTime(2026, 4, 1), ProviderId = provider.Id, DeliveryDate = new DateTime(2026, 5, 1),
                    RetentionPercent = 10m, LatePenaltyPercentPerWeek = 1m, TotalAmount = 10_000_000m, ReleasedAt = DateTimeOffset.Now,
                });
                u.SaveChanges();
                var line = AppHost.Add(u, new PurchaseOrderLine
                {
                    PurchaseOrderId = order.Id, RowNumber = 1, ItemId = item.Id, Description = "plate", Unit = "عدد", Quantity = 10m, UnitPrice = 1_000_000m, DeliveryDate = new DateTime(2026, 5, 1),
                    LastReceiptDate = new DateTime(2026, 5, 15), ReceivedQuantity = 10m, InvoicedQuantity = 10m,
                });
                var invoice = AppHost.Add(u, new SupplierInvoice
                {
                    Number = "SI-1", Status = SupplierInvoiceStatus.Approved, ProviderId = provider.Id, PurchaseOrderId = order.Id, InvoiceNumber = "INV-1", InvoiceDate = new DateTime(2026, 5, 20),
                    NetAmount = 10_000_000m, TotalAmount = 10_000_000m,
                });
                u.SaveChanges();
                u.Set<SupplierInvoiceLine>().Add(new SupplierInvoiceLine { InvoiceId = invoice.Id, OrderLineId = line.Id, Quantity = 10m, UnitPrice = 1_000_000m });
                u.SaveChanges();
                orderId = order.Id; invoiceId = invoice.Id;
            }, Seeder);

            Task<ResponseDto<PaymentRequestDto>> Save(ClaimsPrincipal who, decimal amount) =>
                host.As(who, p => p.GetRequiredService<IPayablesService>().SavePaymentAsync(new SavePaymentRequestDto { Type = PaymentRequestType.Invoice, InvoiceId = invoiceId, PurchaseOrderId = orderId, Amount = amount }, CancellationToken.None));
            Task<ResponseDto<PaymentRequestDto>> Act(ClaimsPrincipal who, int id, string action, List<PaymentDeductionDto> lines = null, string note = null) =>
                host.As(who, p => p.GetRequiredService<IPayablesService>().PaymentActionAsync(new ProcurementActionDto { Id = id, Action = action, DeductionLines = lines, Note = note }, CancellationToken.None));

            // the first request: the retention and the penalty are proposed
            ResponseDto<PaymentRequestDto> first = await Save(BuyerAndAccountant, 6_000_000m);
            Assert.IsTrue(first.Success, first.Message);
            Assert.IsTrue(first.Data.DeductionsProposed);
            Assert.IsFalse(first.Data.DeductionsConfirmed);
            Assert.AreEqual(800_000m, first.Data.Deductions, "retention 10 percent of 6,000,000 and the penalty of two weeks");
            Assert.AreEqual(2, first.Data.DeductionLines.Count);
            Assert.AreEqual(600_000m, first.Data.DeductionLines.Single(d => d.Kind == FnDeductionKind.PerformanceRetention).Amount);
            PaymentDeductionDto penalty = first.Data.DeductionLines.Single(d => d.Kind == FnDeductionKind.LatePenalty);
            Assert.AreEqual(200_000m, penalty.Amount);
            Assert.IsTrue(penalty.Note.Contains(PaymentProposals.Marker));

            // the second request of the same invoice does not take the penalty again
            ResponseDto<PaymentRequestDto> second = await Save(BuyerAndAccountant, 4_000_000m);
            Assert.IsTrue(second.Success, second.Message);
            Assert.AreEqual(1, second.Data.DeductionLines.Count);
            Assert.AreEqual(FnDeductionKind.PerformanceRetention, second.Data.DeductionLines[0].Kind);
            Assert.AreEqual(400_000m, second.Data.Deductions);

            // nobody approves a request whose penalty and retention finance has not confirmed
            ResponseDto<PaymentRequestDto> unconfirmed = await Act(Pmo1, first.Data.Id, "approve");
            Assert.IsFalse(unconfirmed.Success);
            Assert.IsTrue(unconfirmed.Message.Contains("باید توسط مالی تایید شود"), unconfirmed.Message);

            // the confirmation is finance's and not the maker's
            ResponseDto<PaymentRequestDto> byBuyer = await Act(PmoAndBuyer, first.Data.Id, "confirm-deductions");
            Assert.IsFalse(byBuyer.Success);
            Assert.IsTrue(byBuyer.Message.Contains(NotAllowed), byBuyer.Message);
            ResponseDto<PaymentRequestDto> byMaker = await Act(BuyerAndAccountant, first.Data.Id, "confirm-deductions");
            Assert.IsFalse(byMaker.Success);
            Assert.IsTrue(byMaker.Message.Contains(SameUser), byMaker.Message);
            ResponseDto<PaymentRequestDto> confirmed = await Act(Accountant, first.Data.Id, "confirm-deductions");
            Assert.IsTrue(confirmed.Success, confirmed.Message);
            Assert.IsTrue(confirmed.Data.DeductionsConfirmed);
            Assert.IsFalse((await Act(Accountant, first.Data.Id, "confirm-deductions")).Success, "confirmed once");

            // whoever confirmed does not approve
            ResponseDto<PaymentRequestDto> confirmer = await Act(PmoAndAccountant, second.Data.Id, "confirm-deductions", new List<PaymentDeductionDto>
            {
                new PaymentDeductionDto { Kind = FnDeductionKind.PerformanceRetention, Base = 4_000_000m, RatePercent = 7.5m, Amount = 300_000m, Note = "agreed with the supplier" },
            }, null);
            Assert.IsFalse(confirmer.Success, "a correction needs its reason");
            confirmer = await Act(PmoAndAccountant, second.Data.Id, "confirm-deductions", new List<PaymentDeductionDto>
            {
                new PaymentDeductionDto { Kind = FnDeductionKind.PerformanceRetention, Base = 4_000_000m, RatePercent = 7.5m, Amount = 300_000m, Note = "agreed with the supplier" },
            }, "the order terms were relaxed in writing");
            Assert.IsTrue(confirmer.Success, confirmer.Message);
            Assert.AreEqual(300_000m, confirmer.Data.Deductions);
            Assert.IsTrue(confirmer.Data.DeductionNote.Contains("تایید مالی"), confirmer.Data.DeductionNote);
            ResponseDto<PaymentRequestDto> selfApprove = await Act(PmoAndAccountant, second.Data.Id, "approve");
            Assert.IsFalse(selfApprove.Success);
            Assert.IsTrue(selfApprove.Message.Contains(SameUser), selfApprove.Message);

            ResponseDto<PaymentRequestDto> approved = await Act(Pmo1, first.Data.Id, "approve");
            Assert.IsTrue(approved.Success, approved.Message);
            Assert.AreEqual(PaymentRequestStatus.Approved, approved.Data.Status);
            Assert.IsTrue(approved.Data.DeductionsConfirmed);
            Assert.IsTrue((await Act(Pmo2, second.Data.Id, "approve")).Success);

            // the trail keeps the proposal and the confirmation with their users
            List<FnAuditLog> trail = host.Read(u => u.Set<FnAuditLog>().Where(l => l.EntityType == nameof(PaymentRequest) && l.Action == "Modified" && l.EntityId == first.Data.Id).OrderBy(l => l.Id).ToList());
            Assert.IsTrue(trail.Any(l => l.UserId == 28 && l.AfterJson.Contains("DeductionsConfirmedByUserId")), "finance's confirmation");
        }
    }
}
