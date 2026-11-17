using System;
using System.Linq;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1 S9 (the charter view without price, the questions of the units to the PMO) and S10 (a New design becomes a platform only on the manager's proposal and the CEO's approval).</summary>
    [TestClass]
    public class EngineeringPart3Tests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        internal static int Seed(AppHost host, EnClassification classification, EnCaseStatus status)
        {
            int contract = Contract(host), id = 0;
            host.Write(u =>
            {
                EnCase c = AppHost.Add(u, new EnCase { Number = "EC-T" + Guid.NewGuid().ToString("N")[..6], Title = "نمونه", ContractId = contract, Classification = classification, Status = status, CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now });
                u.SaveChanges(); id = c.Id;
            }, Seeder);
            return id;
        }

        /// <summary>The case reached the end of the engineering flow: its product and standard are set and the PMO released its handover (the registration needs that).</summary>
        internal static void ReleaseHandover(AppHost host, int caseId, int product, string code)
        {
            host.Write(u =>
            {
                EnCase c = u.Set<EnCase>().Single(x => x.Id == caseId);
                c.ProductId = product; c.ReferenceCode = code;
                AppHost.Add(u, new EnHandover { Number = "EHO-T" + caseId, CaseId = caseId, ContractId = c.ContractId.Value, Round = 1, Status = EnHandoverStatus.Released, PreparedByUserId = 1001, CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now });
                u.SaveChanges();
            }, Seeder);
        }

        [TestMethod]
        public async Task The_Charter_View_Shows_No_Price_And_The_Confirmation_Hash_Ignores_The_Cost_Section()
        {
            AppHost host = Started("s9a");
            int caseId = Seed(host, EnClassification.Platform, EnCaseStatus.InEngineering);
            int contract = host.Read(u => u.Set<EnCase>().Single(x => x.Id == caseId).ContractId.Value);
            host.Write(u => { AppHost.Add(u, new PcProjectCharter { ContractId = contract, Overview = "نمای کلی", ScopeDescription = "ساخت یک توربین", CostEstimate = "برآورد 987654321 ریال", Risks = "ریسک فنی", CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now }); u.SaveChanges(); }, Seeder);
            EnCharterViewDto view = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnCaseService>().GetCharterViewAsync(caseId, Ct)));
            Assert.IsTrue(view.CharterExists);
            Assert.AreEqual("ساخت یک توربین", view.ScopeDescription);
            Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(view).Contains("987654321"), "the view carries no cost text");
            Refused(await host.As(AppHost.Principal(2199, "finance.one", "FINANCIAL"), p => p.GetRequiredService<IEnCaseService>().GetCharterViewAsync(caseId, Ct)));
            PcProjectCharter before = host.Read(u => u.Set<PcProjectCharter>().Single(x => x.ContractId == contract));
            string hash = EnCaseRules.CharterHash(before);
            host.Write(u => { u.Set<PcProjectCharter>().Single(x => x.ContractId == contract).CostEstimate = "برآورد دیگر 111"; u.SaveChanges(); }, Seeder);
            Assert.AreEqual(hash, EnCaseRules.CharterHash(host.Read(u => u.Set<PcProjectCharter>().Single(x => x.ContractId == contract))), "a cost change does not void the confirmation");
            host.Write(u => { u.Set<PcProjectCharter>().Single(x => x.ContractId == contract).ScopeDescription = "دامنهٔ تغییریافته"; u.SaveChanges(); }, Seeder);
            Assert.AreNotEqual(hash, EnCaseRules.CharterHash(host.Read(u => u.Set<PcProjectCharter>().Single(x => x.ContractId == contract))), "a scope change voids it");
        }

        [TestMethod]
        public async Task A_Unit_Asks_The_Pmo_Who_Answers_And_The_Unit_Closes_And_Nobody_Else_Sees_It()
        {
            AppHost host = Started("s9b");
            int caseId = Seed(host, EnClassification.Platform, EnCaseStatus.Released);
            int contract = host.Read(u => u.Set<EnCase>().Single(x => x.Id == caseId).ContractId.Value);
            host.Write(u => { AppHost.Add(u, new EnHandover { Number = "EHO-INFO", CaseId = caseId, ContractId = contract, Round = 1, Status = EnHandoverStatus.Released, PreparedByUserId = 1001, CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now }); u.SaveChanges(); }, Seeder);   // the units ask only about a released handover
            Func<System.Security.Claims.ClaimsPrincipal, Action<EnInfoAskDto>, Task<Common.Results.ResponseDto<EnInfoRequestDto>>> ask = (who, set) => host.As(who, p => { var d = new EnInfoAskDto { CaseId = caseId, Unit = (int)EnUnit.Qa, Question = "ضخامت ورق در نقشه مشخص نیست، لطفاً مشخص کنید" }; set?.Invoke(d); return p.GetRequiredService<IEnInfoRequestService>().AskAsync(d, Ct); });
            Refused(await ask(Prod1, null), "همان واحد");
            Refused(await ask(Qa1, d => d.Question = "کوتاه"), "15");
            Refused(await ask(await SeatAsync(host, "AG-ENG", "QA"), null));
            EnInfoRequestDto r = Ok(await ask(Qa1, null));
            Assert.AreEqual((int)EnInfoStatus.Open, r.Status);
            Assert.AreEqual(contract, r.ContractId);
            Func<System.Security.Claims.ClaimsPrincipal, string, string, Task<Common.Results.ResponseDto<EnInfoRequestDto>>> act = (who, action, text) => host.As(who, p => p.GetRequiredService<IEnInfoRequestService>().ActionAsync(new EnInfoActionDto { Id = r.Id, Action = action, Answer = text }, Ct));
            Refused(await host.As(Prod1, p => p.GetRequiredService<IEnInfoRequestService>().GetRequestAsync(r.Id, Ct)));
            Refused(await act(Qa1, "answer", "پاسخ کامل بدون قیمت برای پرسش شما"));      // only the PMO answers
            Refused(await act(Pmo1, "answer", "کوتاه"), "15");
            Refused(await act(await SeatAsync(host, "AG-ENG", "PMO"), "answer", "پاسخ کامل بدون قیمت برای پرسش شما"));
            Refused(await act(Qa1, "close", null), "پاسخ‌داده‌شده");
            Assert.IsTrue((await EngineeringStandardsTests.Discover(host, await SeatAsync(host), "ENG.INFO.ANSWER")).Any(i => i.TargetId == r.Id));
            Ok(await act(Pmo1, "answer", "ضخامت ورق ده میلی‌متر است و در نقشه اصلاح می‌شود"));
            Assert.IsFalse((await EngineeringStandardsTests.Discover(host, await SeatAsync(host), "ENG.INFO.ANSWER")).Any(i => i.TargetId == r.Id));
            Refused(await act(Prod1, "close", null));
            Assert.AreEqual((int)EnInfoStatus.Closed, Ok(await act(Qa1, "close", null)).Status);
            EnInfoRequestDto second = Ok(await ask(Qa1, null));
            Refused(await host.As(Qa1, p => p.GetRequiredService<IEnInfoRequestService>().ActionAsync(new EnInfoActionDto { Id = second.Id, Action = "void", Answer = "کوتاه" }, Ct)), "دلیل");
            Assert.AreEqual((int)EnInfoStatus.Void, Ok(await host.As(Qa1, p => p.GetRequiredService<IEnInfoRequestService>().ActionAsync(new EnInfoActionDto { Id = second.Id, Action = "void", Answer = "پرسش اشتباه ثبت شده بود" }, Ct))).Status);
        }

        [TestMethod]
        public async Task A_New_Design_Becomes_A_Platform_Only_On_The_Managers_Proposal_And_The_Ceos_Approval()
        {
            AppHost host = Started("s10");
            await ApprovedList(host, "REG-LIST", ("DWG", true, false));
            int newCase = Seed(host, EnClassification.New, EnCaseStatus.InEngineering), platformCase = Seed(host, EnClassification.Platform, EnCaseStatus.InEngineering), early = Seed(host, EnClassification.New, EnCaseStatus.Classifying);
            int product = Product(host, "99.77.00.000.1");
            ReleaseHandover(host, newCase, product, "REG-LIST");
            Func<System.Security.Claims.ClaimsPrincipal, int, Action<EnRegistrationProposeDto>, Task<Common.Results.ResponseDto<EnRegistrationDto>>> propose = (who, caseId, set) => host.As(who, p => { var d = new EnRegistrationProposeDto { CaseId = caseId, ProductId = product, ReferenceCode = "REG-LIST", StandardBasis = "دیتاشیت ۱۲ بند ۴", Rationale = "این طرح برای چند پروژهٔ دیگر هم قابل استفاده است" }; set?.Invoke(d); return p.GetRequiredService<IEnRegistrationService>().ProposeAsync(d, Ct); });
            Refused(await propose(Eng1, newCase, null));                                                       // only the head of engineering proposes
            Refused(await propose(await SeatAsync(host, "AG-ENG", "ENGINEERING"), newCase, null));
            Refused(await propose(Mgr1, platformCase, null), "طرح جدید");
            Refused(await propose(Mgr1, early, null), "ابلاغ");
            Refused(await propose(Mgr1, newCase, d => d.Rationale = "کوتاه"), "30");
            Refused(await propose(Mgr1, newCase, d => d.ProductId = 999999), "محصول");
            Assert.AreEqual(0, host.Read(u => u.Set<EnPlatformDossier>().Count()), "nothing is a platform before the approval");
            EnRegistrationDto r = Ok(await propose(Mgr1, newCase, null));
            Assert.AreEqual((int)EnRegStatus.Proposed, r.Status);
            Refused(await propose(Mgr2, newCase, null), "پیشنهاد باز");
            Assert.IsTrue((await EngineeringStandardsTests.Discover(host, await SeatAsync(host), "ENG.PLATFORM.REGISTER")).Any(i => i.TargetId == r.Id));
            Assert.IsFalse((await EngineeringStandardsTests.Discover(host, await SeatAsync(host), "ENG.PLATFORM.STANDARD")).Any(i => i.TargetId == product), "the product is not asked for a standard while its registration waits");
            Assert.AreEqual(1, Ok(await host.As(Ceo, p => p.GetRequiredService<IEnOverviewService>().GetOverviewAsync(Ct))).RegistrationsOpen);
            Func<System.Security.Claims.ClaimsPrincipal, string, string, Task<Common.Results.ResponseDto<EnRegistrationDto>>> decide = (who, decision, note) => host.As(who, p => p.GetRequiredService<IEnRegistrationService>().DecideAsync(new EnRegistrationDecisionDto { Id = r.Id, Decision = decision, Note = note }, Ct));
            Refused(await decide(Mgr1, "approve", null));                                                      // the manager never approves their own proposal
            Refused(await decide(Mgr2, "approve", null));
            Refused(await decide(await SeatAsync(host, "AG-ENG", "CEO"), "approve", null));
            Refused(await decide(Ceo, "reject", "کوتاه"), "15");
            Assert.AreEqual(0, host.Read(u => u.Set<EnPlatformDossier>().Count()));
            EnRegistrationDto done = Ok(await decide(Ceo, "approve", null));
            Assert.AreEqual((int)EnRegStatus.Approved, done.Status);
            Assert.IsNotNull(done.DossierId);
            EnPlatformDossier dossier = host.Read(u => u.Set<EnPlatformDossier>().Single());
            Assert.AreEqual(product, dossier.ProductId);
            Assert.AreEqual("REG-LIST", dossier.ReferenceCode);
            Refused(await decide(Ceo, "approve", null));                                                       // a decision is final
            Refused(await propose(Mgr1, newCase, null), "از پیش وجود دارد");
        }

        [TestMethod]
        public async Task A_Rejected_Or_Withdrawn_Proposal_Creates_No_Dossier_And_A_Changed_World_Voids_The_Approval()
        {
            AppHost host = Started("s10b");
            await ApprovedList(host, "REG-LIST2", ("DWG", true, false));
            int newCase = Seed(host, EnClassification.New, EnCaseStatus.HandedOver);
            int product = Product(host, "99.77.00.000.2");
            ReleaseHandover(host, newCase, product, "REG-LIST2");
            Func<Task<Common.Results.ResponseDto<EnRegistrationDto>>> propose = () => host.As(Mgr1, p => p.GetRequiredService<IEnRegistrationService>().ProposeAsync(new EnRegistrationProposeDto { CaseId = newCase, ProductId = product, ReferenceCode = "REG-LIST2", StandardBasis = "دیتاشیت ۱۲ بند ۴", Rationale = "این طرح برای چند پروژهٔ دیگر هم قابل استفاده است" }, Ct));
            Func<System.Security.Claims.ClaimsPrincipal, int, string, string, Task<Common.Results.ResponseDto<EnRegistrationDto>>> decide = (who, id, decision, note) => host.As(who, p => p.GetRequiredService<IEnRegistrationService>().DecideAsync(new EnRegistrationDecisionDto { Id = id, Decision = decision, Note = note }, Ct));
            EnRegistrationDto a = Ok(await propose());
            Refused(await decide(Mgr2, a.Id, "withdraw", "پس‌گرفتن توسط دیگری"));                              // only the proposer withdraws
            Assert.AreEqual((int)EnRegStatus.Rejected, Ok(await decide(Ceo, a.Id, "reject", "هنوز استاندارد طرح روشن نیست")).Status);
            EnRegistrationDto b = Ok(await propose());
            Assert.AreEqual((int)EnRegStatus.Void, Ok(await decide(Mgr1, b.Id, "withdraw", "پیشنهاد را پس می‌گیرم")).Status);
            EnRegistrationDto c = Ok(await propose());
            host.Write(u => { u.Set<EnCase>().Single(x => x.Id == newCase).Classification = EnClassification.Platform; u.SaveChanges(); }, Seeder);
            Refused(await decide(Ceo, c.Id, "approve", null), "طرح جدید");
            Assert.AreEqual((int)EnRegStatus.Void, Ok(await host.As(Ceo, p => p.GetRequiredService<IEnRegistrationService>().GetRegistrationAsync(c.Id, Ct))).Status, "a changed case voids the proposal");
            Assert.AreEqual(0, host.Read(u => u.Set<EnPlatformDossier>().Count()));
        }
    }
}
