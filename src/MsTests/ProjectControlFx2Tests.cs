using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Identity;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.Identity.AccountServices;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace MsTests
{
    /// <summary>
    /// FX2: the sign predicate of the contract review. CanSignPmo / CanSignCommercial and their reasons are computed by the same code as the accept action,
    /// so the action refuses exactly when the predicate says blocked (and with the same text); a role that still waits for the second person gives the waiting text.
    /// </summary>
    [TestClass]
    public class ProjectControlFx2Tests
    {
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1, "seed", "PMO");
        private static readonly ClaimsPrincipal Pmo1 = AppHost.Principal(11, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Pmo2 = AppHost.Principal(12, "pmo.two", "PMO");
        private static readonly ClaimsPrincipal MultiRole = AppHost.Principal(40, "projects", "PMO", "PLANNING", "PROJECTMANAGER");
        private static readonly ClaimsPrincipal Commercial = AppHost.Principal(26, "commercial.manager", "COMMERCIAL");
        private static readonly ClaimsPrincipal Procurement = AppHost.Principal(27, "procurement", "COMMERCIAL");
        private static readonly ClaimsPrincipal PmoAndCommercial = AppHost.Principal(30, "pmo.and.commercial", "PMO", "COMMERCIAL");
        private static readonly ClaimsPrincipal ProjectManager = AppHost.Principal(22, "project.manager", "PROJECTMANAGER");
        private static readonly ClaimsPrincipal NewPmoUser = AppHost.Principal(50, "new.pmo.user", "OPERATOR");   // the role PMO is only requested, not given

        private const string Waiting = "نقش PMO شما منتظر تأیید نفر دوم است؛ پس از تأیید یک بار خارج و دوباره وارد شوید.";

        private static AppHost NewHost(string name) => new AppHost("PcFx2_" + name + "_" + Guid.NewGuid().ToString("N"));

        private static int NewContract(AppHost host, decimal? value = 1_000_000m)
        {
            int id = 0;
            host.Write(u =>
            {
                var c = AppHost.Add(u, new ContractEntity { Code = "C-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "contract", Status = ContractStatus.Current });
                u.SaveChanges();
                id = c.Id;
            }, Seeder);
            if (value.HasValue)
            {
                var terms = new PcContractTermsDto
                {
                    ContractId = id, ValueExVat = value.Value, ContractDeliveryDate = new DateTime(2026, 12, 1), LatePenaltyPercentPerWeek = 0.5m, PenaltyCapPercent = 10m, GraceDays = 3,
                    DataDateWeekday = 3, ClientReviewDays = 14, PenaltyBasis = PcPenaltyBasis.PerContract, WarrantyMonthsAfterCommissioning = 12, WarrantyMonthsAfterDelivery = 18,
                };
                Assert.IsTrue(host.As(Commercial, p => p.GetRequiredService<IPcProjectService>().SaveTermsAsync(terms, CancellationToken.None)).GetAwaiter().GetResult().Success);
            }
            return id;
        }

        private static PcReviewDto Review(int contractId, bool complete = true) => new PcReviewDto
        {
            ContractId = contractId, Kind = PcReviewKind.StandardProduct, RequirementsDefined = complete, DifferencesResolved = true, DeliveryFeasible = true, StandardsItpAgreed = true,
            GuaranteesReviewed = true, PenaltiesReviewed = true, PaymentTermsReviewed = true, CurrencyReviewed = true,
        };

        private static async Task<int> SaveReview(AppHost host, ClaimsPrincipal who, int contractId, bool complete = true)
        {
            ResponseDto<PcReviewDto> r = await host.As(who, p => p.GetRequiredService<IPcProjectService>().SaveReviewAsync(Review(contractId, complete), CancellationToken.None));
            Assert.IsTrue(r.Success, r.Message);
            return r.Data.Id.Value;
        }

        private static async Task<PcReviewDto> View(AppHost host, ClaimsPrincipal who, int contractId, int reviewId)
        {
            var page = await host.As(who, p => p.GetRequiredService<IPcProjectService>().GetReviewsAsync(new PcContractQuery { ContractId = contractId, PageSize = 100 }, CancellationToken.None));
            Assert.IsTrue(page.Success, page.Message);
            return page.Data.Single(x => x.Id == reviewId);
        }

        private static Task<ResponseDto<PcReviewDto>> Accept(AppHost host, ClaimsPrincipal who, int reviewId, string slot) =>
            host.As(who, p => p.GetRequiredService<IPcProjectService>().ReviewActionAsync(new PcReviewActionDto { Id = reviewId, Action = "accept", Slot = slot }, CancellationToken.None));

        /// <summary>The predicate says blocked: the action refuses with the very reason (or, without any role, with the not-allowed text).</summary>
        private static async Task AssertBlockedAs(AppHost host, ClaimsPrincipal who, int contractId, int reviewId, string slot, string expectedPart, bool reasonShown = true)
        {
            PcReviewDto v = await View(host, who, contractId, reviewId);
            bool can = slot == "pmo" ? v.CanSignPmo : v.CanSignCommercial;
            string reason = slot == "pmo" ? v.SignBlockedReasonPmo : v.SignBlockedReasonCommercial;
            Assert.IsFalse(can, "predicate: blocked (" + expectedPart + ")");
            if (reasonShown) { Assert.IsNotNull(reason); Assert.IsTrue(reason.Contains(expectedPart), reason); } else Assert.IsNull(reason, "no role at all: no reason, no button");
            ResponseDto<PcReviewDto> act = await Accept(host, who, reviewId, slot);
            Assert.IsFalse(act.Success, "the action refuses exactly when the predicate says blocked");
            Assert.IsTrue(act.Message.Contains(expectedPart), act.Message);
            if (reasonShown) Assert.AreEqual(reason, act.Message, "the same text in the action and in the DTO");
        }

        [TestMethod]
        public async Task AUserWithPmoPlanningAndProjectManagerSignsAsPmoAndTheReviewIsAcceptedWithTheCommercialSignatureOfAnotherUser()
        {
            AppHost host = NewHost("Multi");
            int contractId = NewContract(host);
            int reviewId = await SaveReview(host, Commercial, contractId);

            PcReviewDto before = await View(host, MultiRole, contractId, reviewId);
            Assert.IsTrue(before.CanSignPmo, "three roles: the PMO signature is possible");
            Assert.IsNull(before.SignBlockedReasonPmo);
            Assert.IsFalse(before.CanSignCommercial);
            Assert.IsNull(before.SignBlockedReasonCommercial, "no commercial role at all: no button, no reason");

            ResponseDto<PcReviewDto> pmo = await Accept(host, MultiRole, reviewId, "pmo");
            Assert.IsTrue(pmo.Success, pmo.Message);
            Assert.AreEqual(PcReviewDecision.Pending, pmo.Data.Decision);
            Assert.IsTrue((await View(host, Procurement, contractId, reviewId)).CanSignCommercial);

            ResponseDto<PcReviewDto> commercial = await Accept(host, Procurement, reviewId, "commercial");
            Assert.IsTrue(commercial.Success, commercial.Message);
            Assert.AreEqual(PcReviewDecision.Accept, commercial.Data.Decision);
            Assert.AreEqual(40, commercial.Data.PmoAcceptedByUserId);
            Assert.AreEqual(27, commercial.Data.CommercialAcceptedByUserId);
            Assert.IsFalse(commercial.Data.CanSignPmo, "a decided review cannot be signed");
        }

        [TestMethod]
        public async Task EveryReasonOfTheSignPredicateBlocksTheActionWithTheSameText()
        {
            AppHost host = NewHost("Reasons");
            int contractId = NewContract(host);
            int reviewId = await SaveReview(host, Commercial, contractId);

            // no role at all: nothing is shown
            await AssertBlockedAs(host, ProjectManager, contractId, reviewId, "pmo", "شما مجوز این کار را ندارید", reasonShown: false);
            await AssertBlockedAs(host, ProjectManager, contractId, reviewId, "commercial", "شما مجوز این کار را ندارید", reasonShown: false);

            // the maker (creator) of the review: the commercial manager saved it
            await AssertBlockedAs(host, Commercial, contractId, reviewId, "commercial", "ثبت‌کننده یا ویرایش‌کننده");

            // the same person cannot sign both slots
            Assert.IsTrue((await Accept(host, PmoAndCommercial, reviewId, "pmo")).Success);
            await AssertBlockedAs(host, PmoAndCommercial, contractId, reviewId, "commercial", "دو نفر متفاوت");

            // duplicate: the PMO slot is signed already
            await AssertBlockedAs(host, Pmo1, contractId, reviewId, "pmo", "قبلاً ثبت شده");

            // the last signature accepts the review; afterwards it is decided
            Assert.IsTrue((await Accept(host, Procurement, reviewId, "commercial")).Success);
            await AssertBlockedAs(host, Pmo2, contractId, reviewId, "pmo", "تصمیم گرفته شده", reasonShown: false);   // a decided review: the DTO offers nothing (no reason), the action says it is decided
        }

        [TestMethod]
        public async Task AnIncompleteReviewAndMissingTermsAndAnotherAcceptedReviewBlockTheSignature()
        {
            AppHost host = NewHost("Gates");

            int c1 = NewContract(host);
            int incomplete = await SaveReview(host, Commercial, c1, complete: false);
            await AssertBlockedAs(host, Pmo1, c1, incomplete, "pmo", "پذیرش پیش از تکمیل بازنگری ممکن نیست");

            int noTerms = NewContract(host, value: null);
            int r2 = await SaveReview(host, Commercial, noTerms);
            await AssertBlockedAs(host, Pmo1, noTerms, r2, "pmo", "شرایط قرارداد با مبلغ آن ثبت شود");

            // another review of the contract is accepted: the second one cannot be signed any more
            int c3 = NewContract(host);
            int first = await SaveReview(host, Commercial, c3);
            int second = await SaveReview(host, Commercial, c3);
            Assert.IsTrue((await Accept(host, Pmo1, first, "pmo")).Success);
            Assert.IsTrue((await Accept(host, Procurement, first, "commercial")).Success);
            await AssertBlockedAs(host, Pmo2, c3, second, "pmo", "قبلاً پذیرفته شده");
        }

        [TestMethod]
        public async Task AUserWhoseRoleWaitsForTheSecondPersonSeesTheButtonBlockedWithTheWaitingReasonAndTheActionAgrees()
        {
            AppHost host = NewHost("Pending");
            int contractId = NewContract(host);
            int reviewId = await SaveReview(host, Commercial, contractId);

            // a request for PMO that nobody has approved (the user has no active PMO role); an expired request does not count
            host.Write(u =>
            {
                AppHost.Add(u, new UserClaim { UserId = 50, ClaimType = IdentityRules.PendingRoleGrantClaimType, ClaimValue = new PendingRoleGrant { Role = "PMO", RequestedBy = 1, RequestedAt = DateTimeOffset.Now.AddHours(-1) }.ToClaimValue() });
                AppHost.Add(u, new UserClaim { UserId = 51, ClaimType = IdentityRules.PendingRoleGrantClaimType, ClaimValue = new PendingRoleGrant { Role = "PMO", RequestedBy = 1, RequestedAt = DateTimeOffset.Now.AddDays(-30) }.ToClaimValue() });
                u.SaveChanges();
            }, Seeder);

            await AssertBlockedAs(host, NewPmoUser, contractId, reviewId, "pmo", Waiting);
            // the commercial slot is not what he waits for: no role, no reason
            await AssertBlockedAs(host, NewPmoUser, contractId, reviewId, "commercial", "شما مجوز این کار را ندارید", reasonShown: false);
            await AssertBlockedAs(host, AppHost.Principal(51, "expired.request", "OPERATOR"), contractId, reviewId, "pmo", "شما مجوز این کار را ندارید", reasonShown: false);

            // after the approval the role is active in the token (the user signs in again): the same review can be signed
            ClaimsPrincipal approved = AppHost.Principal(50, "new.pmo.user", "OPERATOR", "PMO");
            host.Write(u => { foreach (UserClaim c in u.Set<UserClaim>().Where(x => x.UserId == 50).ToList()) u.Set<UserClaim>().Remove(c); u.SaveChanges(); }, Seeder);
            Assert.IsTrue((await View(host, approved, contractId, reviewId)).CanSignPmo);
        }

        [TestMethod]
        public void TheWaitingTextNamesEveryPendingRoleInPersian()
        {
            Assert.AreEqual(Waiting, PendingRoleLookup.WaitingText(new[] { "PMO" }));
            Assert.AreEqual("نقش‌های PMO، مدیرعامل شما منتظر تأیید نفر دوم است؛ پس از تأیید یک بار خارج و دوباره وارد شوید.", PendingRoleLookup.WaitingText(new[] { "PMO", "CEO" }));
            Assert.IsNull(PendingRoleLookup.WaitingText(new string[0]));
        }
    }
}
