using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.Contracts;
using Services.NasbNiro;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Engineering;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.ContractDtos;
using ViewModels.NasbNiro.ProjectControlDtos;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1 fix round A (independent review, Critical C1 and High H1 to H5, and the coordinator decisions): direct contracts are controlled, platform completeness is the manager's alone, a New design is a platform only through the CEO-approved registration,
    /// the guard covers every human decision column, the master agent owns the standard-gap report, and the project charter hides its cost estimate from readers who are not charter writers.</summary>
    [TestClass]
    public class EngineeringFixATests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        /// <summary>A stub for the document service: the contract add never calls it.</summary>
        public class NoDocuments : DispatchProxy
        {
            protected override object Invoke(MethodInfo targetMethod, object[] args) => targetMethod.ReturnType.IsValueType ? Activator.CreateInstance(targetMethod.ReturnType) : null;
        }

        // ---------------------------------------------------------------- C1

        [TestMethod]
        public async Task A_Direct_Contract_Added_Through_The_Contract_Service_Is_Controlled_And_An_Older_One_Stays_Legacy()
        {
            AppHost host = Started("fixc1");
            IDocumentService docs = DispatchProxy.Create<IDocumentService, NoDocuments>();
            DateTimeOffset before = DateTimeOffset.Now.AddSeconds(-5);
            ResponseDto<ResponseContractDto> added = await host.As(Eng1, p => new ContractService(NullLogger<ContractService>.Instance, p.GetRequiredService<IUnitOfWork>(), docs, p.GetRequiredService<IMapper>())
                .AddContractAsync(new AddContractDto { Code = "C-DIRECT-1", Title = "قرارداد مستقیم", Employer = "کارفرما", Status = ContractStatus.Current, ContractDate = DateTimeOffset.Now }, Ct).AsTask());
            int contract = Ok(added).Id;
            // the server stamps the date; it never comes from the client
            DateTimeOffset stamped = host.Read(u => u.Set<ContractEntity>().Single(x => x.Id == contract).CreateDate);
            Assert.IsTrue(stamped >= before && stamped <= DateTimeOffset.Now.AddSeconds(5), "the creation date is set by the server");
            ResponseDto<ResponseContractDto> forProvider = await host.As(Eng1, p => new ContractService(NullLogger<ContractService>.Instance, p.GetRequiredService<IUnitOfWork>(), docs, p.GetRequiredService<IMapper>())
                .AddContractForProviderAsync(new AddContractDto { Code = "C-DIRECT-2", Title = "قرارداد تامین‌کننده", Employer = "کارفرما", Status = ContractStatus.Current, ContractDate = DateTimeOffset.Now }, Ct).AsTask());
            Assert.IsTrue(host.Read(u => u.Set<ContractEntity>().Single(x => x.Id == Ok(forProvider).Id).CreateDate) >= before);
            host.Write(u => { AppHost.Add(u, new WorkOrder { Code = "WO-DIRECT", Title = "توربین", Count = 1, ContractId = contract, EquipmentId = 7, CreateDate = DateTimeOffset.Now }); u.SaveChanges(); }, Seeder);

            // the three chokepoints now apply to it, and the engineering case opens
            IUnitOfWork Uow(IServiceProvider p) => p.GetRequiredService<IUnitOfWork>();
            EnGateResult gate = await host.As(Eng1, p => EnGates.ForContractAsync(Uow(p), contract, Ct));
            Assert.IsTrue(gate.Controlled); Assert.IsFalse(gate.Legacy);
            Assert.IsNotNull(await host.As(Eng1, p => EnGates.CharterApproveRefusalAsync(Uow(p), contract, Ct)), "the CEO charter approval waits for the engineering case");
            Assert.IsNotNull(await host.As(Eng1, p => EnGates.InstantiateRefusalAsync(Uow(p), contract, Ct)), "the baseline waits for the accepted handover");
            (bool controlled, bool passed, _) = await host.As(Eng1, p => EnGates.ProductionReleaseAsync(Uow(p), contract, Ct));
            Assert.IsTrue(controlled); Assert.IsFalse(passed);
            Ok(await CaseAct(host, Eng1, null, "open", d => d.ContractId = contract));

            // a contract that predates the control stays legacy (no case, all chokepoints pass), also one with the empty date of an older row
            foreach (DateTimeOffset created in new[] { DateTimeOffset.Now.AddDays(-30), default(DateTimeOffset) })
            {
                int old = Contract(host, created);
                Assert.IsTrue((await host.As(Eng1, p => EnGates.ForContractAsync(Uow(p), old, Ct))).Legacy);
                Assert.IsNull(await host.As(Eng1, p => EnGates.InstantiateRefusalAsync(Uow(p), old, Ct)));
                Refused(await CaseAct(host, Eng1, null, "open", d => d.ContractId = old), "قدیمی");
            }
        }

        // ---------------------------------------------------------------- decision 3 (H2)

        [TestMethod]
        public async Task Platform_Completeness_Is_Confirmed_By_The_Engineering_Manager_Alone()
        {
            AppHost host = Started("fixh2");
            await ApprovedList(host);
            int product = Product(host);
            EnDossierDto d = Ok(await Dossier(host, Eng1, product, "assess"));
            foreach (EnDossierLineDto l in d.Lines.Where(x => x.Required && x.State == 0).ToList()) Ok(await Supply(host, Eng1, l.Id, File(host, 2111)));
            Ok(await Dossier(host, Eng1, product, "complete"));
            ClaimsPrincipal seat = await SeatAsync(host);
            List<AgentWorkItem> gate = await EngineeringStandardsTests.Discover(host, seat, "ENG.PLATFORM.CONFIRM");
            Assert.AreEqual(1, gate.Count);
            Assert.AreEqual(EnRoles.Manager, gate[0].GateRoles, "only the engineering manager is asked");
            Assert.IsFalse(Ok(await host.As(Ceo, p => p.GetRequiredService<IEnPlatformService>().GetDossierAsync(product, Std, Ct))).CanConfirm, "the button says the same as the service");
            Refused(await Dossier(host, Ceo, product, "confirm"), "مدیر مهندسی");
            Refused(await Dossier(host, Ceo, product, "return", "دلیل برگشت کافی برای آزمون"));
            Refused(await Dossier(host, Eng2, product, "confirm"));
            Refused(await Dossier(host, await SeatAsync(host, "AG-ENG", "ENGINEERING"), product, "confirm"), "انسان");
            Assert.AreEqual((int)EnDossierStatus.Complete, Ok(await Dossier(host, Mgr1, product, "confirm")).Status);
        }

        // ---------------------------------------------------------------- decision 4 (H1, H4)

        [TestMethod]
        public async Task A_New_Design_Product_Is_Not_A_Platform_Before_The_Ceo_Approved_Registration()
        {
            AppHost host = Started("fixh1");
            await ApprovedList(host, "REG-A", ("DWG", true, false));
            int product = Product(host, "99.88.00.000.1");
            // a human proposal of Platform for a product without a dossier is refused
            int inquiry = Inquiry(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = inquiry));
            Refused(await CaseAct(host, Eng1, c.Id, "propose", d => { d.Classification = 1; d.ProductId = product; d.Basis = "مبنای آزمون برای پلتفرم"; }), "پرونده مدارک پلتفرم");
            // the product whose only origin is a New design case: no native-standard task and no native-standard action
            int newCase = EngineeringPart3Tests.Seed(host, EnClassification.New, EnCaseStatus.InEngineering);
            EngineeringPart3Tests.ReleaseHandover(host, newCase, product, "REG-A");
            Assert.AreEqual(0, (await EngineeringStandardsTests.Discover(host, await SeatAsync(host), "ENG.PLATFORM.STANDARD")).Count(i => i.TargetId == product), "no task for an unregistered New design");
            Refused(await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().DossierActionAsync(new EnDossierActionDto { ProductId = product, Action = "native-standard", ReferenceCode = "REG-A", StandardBasis = "دیتاشیت پروژه، بند ۴" }, Ct)), "پلتفرم نشده");
            Assert.AreEqual(0, host.Read(u => u.Set<EnPlatformDossier>().Count()));
            // an ordinary product still gets the task
            int ordinary = Product(host, "99.88.00.000.2");
            Assert.AreEqual(1, (await EngineeringStandardsTests.Discover(host, await SeatAsync(host), "ENG.PLATFORM.STANDARD")).Count(i => i.TargetId == ordinary));
        }

        [TestMethod]
        public async Task A_Registration_Needs_The_Released_Handover_The_Own_Product_And_Standard_And_Copies_The_Handover_Lines_Into_The_Dossier()
        {
            AppHost host = Started("fixh4");
            await ApprovedList(host, "REG-B", ("DWG", true, false));
            int newCase = EngineeringPart3Tests.Seed(host, EnClassification.New, EnCaseStatus.InEngineering);
            int product = Product(host, "99.88.00.000.3"), other = Product(host, "99.88.00.000.4");
            Func<int, string, Task<ResponseDto<EnRegistrationDto>>> propose = (productId, code) => host.As(Mgr1, p => p.GetRequiredService<IEnRegistrationService>().ProposeAsync(new EnRegistrationProposeDto { CaseId = newCase, ProductId = productId, ReferenceCode = code, StandardBasis = "دیتاشیت ۱۲ بند ۴", Rationale = "این طرح برای چند پروژهٔ دیگر هم قابل استفاده است" }, Ct));
            host.Write(u => { u.Set<EnCase>().Single(x => x.Id == newCase).ReferenceCode = "REG-B"; u.Set<EnCase>().Single(x => x.Id == newCase).ProductId = product; u.SaveChanges(); }, Seeder);
            Refused(await propose(product, "REG-B"), "ابلاغ");   // the handover is not released yet
            host.Write(u => { AppHost.Add(u, new EnHandover { Number = "EHO-FIX", CaseId = newCase, ContractId = u.Set<EnCase>().Single(x => x.Id == newCase).ContractId.Value, Round = 1, Status = EnHandoverStatus.Released, PreparedByUserId = 1001, CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now }); u.SaveChanges(); }, Seeder);
            Guid file = File(host, 2111);
            host.Write(u =>
            {
                int hid = u.Set<EnHandover>().Single(x => x.CaseId == newCase).Id;
                AppHost.Add(u, new EnHandoverLine { HandoverId = hid, Seq = 1, DocumentType = "DWG", Discipline = "مکانیک", Title = "مدرک DWG", Required = true, State = EnLineState.Available, DocumentId = file, Revision = "C", Units = "Qc" });
                u.SaveChanges();
            }, Seeder);
            Refused(await propose(other, "REG-B"), "تعلق ندارد");   // another product
            Refused(await propose(product, "REG-OTHER"), "استاندارد پروژه");   // another standard than the case's
            EnRegistrationDto r = Ok(await propose(product, "REG-B"));
            Ok(await host.As(Ceo, p => p.GetRequiredService<IEnRegistrationService>().DecideAsync(new EnRegistrationDecisionDto { Id = r.Id, Decision = "approve" }, Ct)));
            EnPlatformDocument line = host.Read(u => u.Set<EnPlatformDocument>().Single(x => x.DocumentType == "DWG"));
            Assert.AreEqual(EnLineState.Available, line.State);
            Assert.AreEqual(file, line.DocumentId);
            Assert.AreEqual("C", line.Revision);
            Assert.AreEqual("Qc", line.Units);
        }

        // ---------------------------------------------------------------- decision 5 (H3)

        [TestMethod]
        public async Task A_Spare_Parts_Case_Gets_Its_Handover_From_The_Agent_And_Appears_In_The_Early_Warning()
        {
            AppHost host = Started("fixh3");
            await ApprovedList(host, "SPARE-LIST", ("DWG", true, false));
            int inquiry = Inquiry(host, SlInquiryStatus.Bidding, SlProductType.SpareParts);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = inquiry));
            Ok(await CaseAct(host, Mgr2, c.Id, "save-opinion", d => d.OpinionText = "رای فنی کامل برای تامین قطعات یدکی مطابق نقشه‌های موجود"));
            Ok(await CaseAct(host, Eng1, c.Id, "propose", d => d.Basis = "پلتفرم مرتبطی مشخص نشد، مبنای آزمون"));
            Ok(await SetStandard(host, Eng1, c.Id, "SPARE-LIST"));
            Ok(await CaseAct(host, Mgr1, c.Id, "confirm", d => d.HandoverListCode = "SPARE-LIST"));
            int contract = Contract(host);
            host.Write(u => { EnCase row = u.Set<EnCase>().Single(x => x.Id == c.Id); row.ContractId = contract; row.Status = EnCaseStatus.Awarded; u.SaveChanges(); }, Seeder);   // the inquiry was won
            ClaimsPrincipal seat = await SeatAsync(host);
            AgentWorkItem item = (await EngineeringStandardsTests.Discover(host, seat, "ENG.HANDOVER.DRAFT")).Single(i => i.TargetId == c.Id);
            Assert.AreEqual(AgentStepOutcome.Done, (await EngineeringStandardsTests.Run(host, seat, "ENG.HANDOVER.DRAFT", item)).Outcome);
            Assert.AreEqual(1, host.Read(u => u.Set<EnHandover>().Count(h => h.CaseId == c.Id && h.Status == EnHandoverStatus.Draft)));
            Assert.IsTrue((await EngineeringStandardsTests.Discover(host, seat, "ENG.RISK.WATCH")).Any(i => i.TargetId == c.Id), "the draft misses a required document: the early warning names the case");
        }

        // ---------------------------------------------------------------- decision 6 (H5) and 9

        /// <summary>The columns of an En entity that record a person and that an agent legitimately writes, or that a service refuses a seat for before this layer (data entry, not a decision). Anything else must match the guard.</summary>
        private static readonly Dictionary<string, string> NotADecision = new Dictionary<string, string>
        {
            ["EnReferenceList.PreparedByUserId"] = "the engineering agent drafts the list",
            ["EnHandover.PreparedByUserId"] = "the engineering agent drafts the handover",
            ["EnPlatformDossier.LastEditedByUserId"] = "who last edited the dossier (the agent assesses it)",
            ["EnPlatformDocument.SuppliedByUserId"] = "the agent supplies system-proven and reused lines",
            ["EnCase.ProposedByUserId"] = "the agent proposes the classification; the decisions are the guarded confirm and approve columns",
            ["EnPlatformRegistration.ProposedByUserId"] = "a proposal, not the decision",
            ["EnCase.OpinionByUserId"] = "an engineer's technical opinion text; the service refuses a seat, the confirmation is the guarded EngConfirmedByUserId",
            ["EnClarification.AskedByUserId"] = "the agent asks the question",
            ["EnInfoRequest.AskedByUserId"] = "the question of a unit",
            ["EnClarification.SentToClientByUserId"] = "data entry of the commercial side; no seat holds that role",
            ["EnClarification.AnswerRecordedByUserId"] = "data entry of the commercial side; no seat holds that role",
            ["EnInfoRequest.AnsweredByUserId"] = "data entry of the PMO; the service refuses a seat",
            ["EnWaiver.RequestedByUserId"] = "a request, not the decision (the CEO decision column is guarded)",
            ["EnDeviation.RequestedByUserId"] = "a request, not the decision (the manager and CEO decisions are guarded)",
            ["EnDocumentDebt.SettledByUserId"] = "the engineering agent settles a debt from the proof of the document",
            ["EnCaseEvent.UserId"] = "the actor of a history line",
        };

        [TestMethod]
        public void Every_User_Column_Of_An_En_Entity_Is_Guarded_Against_A_Seat_Unless_It_Is_Listed_As_Not_A_Decision()
        {
            AppHost host = NewHost("fixh5");
            var guarded = SeatApprovalGuard.Sweep(host.Model);
            List<string> unguarded = new List<string>(), seen = new List<string>();
            foreach (Type type in host.Model.GetEntityTypes().Select(t => t.ClrType).Where(t => t != null && t.Namespace == "Entities.Model.NasbNiro.Engineering" && t.Name.StartsWith("En", StringComparison.Ordinal)))
                foreach (var p in host.Model.FindEntityType(type).GetProperties().Where(p => (p.ClrType == typeof(int) || p.ClrType == typeof(int?)) && p.Name.EndsWith("UserId", StringComparison.Ordinal) && p.Name != "CreatorUserId" && p.Name != "ModifierUserId"))
                {
                    string key = type.Name + "." + p.Name;
                    seen.Add(key);
                    if (guarded.Contains((type.Name, p.Name))) Assert.IsFalse(NotADecision.ContainsKey(key), key + " is guarded: take it off the list");
                    else if (!NotADecision.ContainsKey(key)) unguarded.Add(key);
                }
            Assert.AreEqual(0, unguarded.Count, "these columns record a person, are not guarded against an AI seat and are not listed as 'not a decision' (guard them: rename so that the name matches, or list them with a reason): " + string.Join(", ", unguarded));
            foreach (string key in NotADecision.Keys) CollectionAssert.Contains(seen, key, key + " is listed but is not a column any more");
            foreach ((string entity, string column) in new[] { ("EnHandover", "SubmitConfirmedByUserId"), ("EnDistribution", "ReceiptConfirmedByUserId") })
                Assert.IsTrue(guarded.Contains((entity, column)), entity + "." + column + " is a human decision and is guarded");
        }

        [TestMethod]
        public void A_Seat_Cannot_Write_The_Handover_Submit_Or_The_Receipt_Confirmation_Columns()
        {
            AppHost host = NewHost("fixh5b");
            ClaimsPrincipal seat = AppHost.Principal(97, "seat.ag-eng", "ENGINEER");
            var ex = Assert.ThrowsException<InvalidOperationException>(() => host.Write(u => { AppHost.Add(u, new EnHandover { Number = "EHO-G", CaseId = 1, ContractId = 1, SubmitConfirmedByUserId = 97 }); u.SaveChanges(); }, seat));
            StringAssert.Contains(ex.Message, SeatApprovalGuard.Message);
            ex = Assert.ThrowsException<InvalidOperationException>(() => host.Write(u => { AppHost.Add(u, new EnDistribution { HandoverId = 1, CaseId = 1, ContractId = 1, Roles = "QA", ReceiptConfirmedByUserId = 97 }); u.SaveChanges(); }, seat));
            StringAssert.Contains(ex.Message, SeatApprovalGuard.Message);
        }

        [TestMethod]
        public void The_Master_Seat_Writes_Only_The_Three_Tables_Of_The_Standard_Gap_Report_Among_The_En_Tables_And_Never_A_Decision_Column()
        {
            AppHost host = NewHost("fixmaster");
            ClaimsPrincipal master = AppHost.Principal(98, "seat.ag-master");
            host.Write(u =>
            {
                EnStandardGap g = AppHost.Add(u, new EnStandardGap { Number = "ESG-M", CaseId = 1, TargetCode = "X", Status = EnGapStatus.Draft });
                g.Lines.Add(new EnStandardGapLine { Seq = 1, Kind = EnGapKind.Added, DocumentType = "DWG", Title = "t" });
                AppHost.Add(u, new EnCaseEvent { CaseId = 1, Kind = "standard-gap-built", At = DateTimeOffset.Now });
                u.SaveChanges();
            }, master);
            Assert.AreEqual(1, host.Read(u => u.Set<EnStandardGap>().Count()));
            Assert.AreEqual(1, host.Read(u => u.Set<EnStandardGapLine>().Count()));
            Assert.AreEqual(1, host.Read(u => u.Set<EnCaseEvent>().Count()));
            string[] allowed = { nameof(EnStandardGap), nameof(EnStandardGapLine), nameof(EnCaseEvent) };
            foreach (Type type in host.Model.GetEntityTypes().Select(t => t.ClrType).Where(t => t != null && t.Namespace == "Entities.Model.NasbNiro.Engineering" && t.Name.StartsWith("En", StringComparison.Ordinal) && !allowed.Contains(t.Name)))
            {
                var ex = Assert.ThrowsException<InvalidOperationException>(() => host.Write(u => { u.Entry(Activator.CreateInstance(type)).State = EntityState.Added; u.SaveChanges(); }, master), type.Name);
                StringAssert.Contains(ex.Message, SeatApprovalGuard.MasterMessage, type.Name);
            }
            // the confirmation of the gap stays a human decision even for the allowed table
            var decision = Assert.ThrowsException<InvalidOperationException>(() => host.Write(u => { AppHost.Add(u, new EnStandardGap { Number = "ESG-M2", CaseId = 2, TargetCode = "X", Status = EnGapStatus.Confirmed, ConfirmedByUserId = 98 }); u.SaveChanges(); }, master));
            StringAssert.Contains(decision.Message, SeatApprovalGuard.Message);
        }

        [TestMethod]
        public async Task The_Standard_Gap_Step_Belongs_To_The_Main_Agent_And_Runs_As_Its_Seat_And_The_Missing_List_Is_Drafted_By_The_Engineering_Agent()
        {
            AppHost host = await EngineeringStandardsTests.TwoStandardsAsync("fixgap");
            int product = Product(host);
            Ok(await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().DossierActionAsync(new EnDossierActionDto { ProductId = product, Action = "native-standard", ReferenceCode = "API-611", StandardBasis = "دیتاشیت پروژهٔ اول، بند ۳" }, Ct)));
            EnDossierDto d = Ok(await Dossier(host, Eng1, product, "assess", code: "API-611"));
            Ok(await Supply(host, Eng1, d.Lines.First(l => l.Required).Id, File(host, 2111)));
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", x => x.InquiryId = Inquiry(host)));
            Ok(await CaseAct(host, Eng1, c.Id, "propose", x => { x.Classification = 1; x.ProductId = product; x.Basis = "مبنای آزمون برای تعیین نوع پرونده"; }));
            Ok(await SetStandard(host, Eng1, c.Id, "API-614"));   // a standard that has no list at all
            Dictionary<string, Services.NasbNiro.Ai.AiActingUser> seats = await host.AsSystem(p => p.GetRequiredService<Services.NasbNiro.Ai.IAiSeatProvisioner>().EnsureAsync(Ct));
            ClaimsPrincipal master = AppHost.Principal(seats["AG-MASTER"].UserId, Services.NasbNiro.Ai.AiSeatNames.UserName("AG-MASTER"));
            Assert.AreEqual("AG-MASTER", (await host.As(master, p => Task.FromResult(p.GetServices<IAgentStep>().Single(s => s.Code == "ENG.STANDARD.GAP").AgentCode))));
            AgentWorkItem item = (await EngineeringStandardsTests.Discover(host, master, "ENG.STANDARD.GAP")).Single(i => i.TargetId == c.Id);
            Assert.AreEqual(AgentStepOutcome.Done, (await EngineeringStandardsTests.Run(host, master, "ENG.STANDARD.GAP", item)).Outcome, "the master seat writes the report under the guard");
            Assert.AreEqual(1, host.Read(u => u.Set<EnStandardGap>().Count(g => g.CaseId == c.Id)));
            Assert.AreEqual(0, host.Read(u => u.Set<EnReferenceList>().Count(l => l.Code == "API-614")), "the master seat writes no list");
            ClaimsPrincipal eng = await SeatAsync(host);
            AgentWorkItem draft = (await EngineeringStandardsTests.Discover(host, eng, "ENG.REF.DRAFT")).Single(i => i.TargetType == "EnCase" && i.TargetId == c.Id);
            Assert.AreEqual(AgentStepOutcome.Done, (await EngineeringStandardsTests.Run(host, eng, "ENG.REF.DRAFT", draft)).Outcome);
            Assert.AreEqual(EnRefStatus.Draft, host.Read(u => u.Set<EnReferenceList>().Single(l => l.Code == "API-614").Status));
        }

        // ---------------------------------------------------------------- decision 10 (charter cost leak)

        [TestMethod]
        public async Task The_Project_Charter_Hides_The_Cost_Estimate_From_Readers_Who_Are_Not_Charter_Writers_Or_Managers()
        {
            AppHost host = Started("fixcost");
            int contract = Contract(host);
            host.Write(u => { AppHost.Add(u, new PcProjectCharter { ContractId = contract, Overview = "نمای کلی", CostEstimate = "برآورد 987654321 ریال", CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now }); u.SaveChanges(); }, Seeder);
            foreach (ClaimsPrincipal reader in new[] { Eng1, Mgr1, Qa1, Prod1 })
            {
                PcCharterDto dto = Ok(await host.As(reader, p => p.GetRequiredService<IPcProjectService>().GetCharterAsync(contract, Ct)));
                Assert.IsNull(dto.CostEstimate, "the cost section is not sent to " + reader.Identity.Name);
                Assert.IsTrue(dto.CostEstimateMasked);
                Assert.AreEqual("نمای کلی", dto.Overview, "the rest of the charter is still readable");
            }
            foreach (string role in new[] { "PMO", "CEO", "PROJECTMANAGER", "PLANNING", "COMMERCIAL", "FINANCIAL" })
            {
                PcCharterDto dto = Ok(await host.As(AppHost.Principal(2300 + role.Length, "u." + role, role), p => p.GetRequiredService<IPcProjectService>().GetCharterAsync(contract, Ct)));
                Assert.AreEqual("برآورد 987654321 ریال", dto.CostEstimate, role);
                Assert.IsFalse(dto.CostEstimateMasked);
            }
        }

        // ---------------------------------------------------------------- decision 12

        [TestMethod]
        public void Every_Standard_Code_Column_Of_The_Engineering_Model_Is_Sixty_Characters()
        {
            AppHost host = NewHost("fixlen");
            var tooShort = new List<string>();
            foreach (var entity in host.Model.GetEntityTypes().Where(t => t.ClrType != null && t.ClrType.Namespace == "Entities.Model.NasbNiro.Engineering"))
                foreach (var p in entity.GetProperties().Where(p => p.ClrType == typeof(string) && (p.Name == "ReferenceCode" || p.Name == "StandardCode" || p.Name == "BaseCode" || p.Name == "TargetCode" || p.Name == "HandoverListCode")))
                    if (p.GetMaxLength() != 60) tooShort.Add(entity.ClrType.Name + "." + p.Name + "=" + p.GetMaxLength());
            Assert.AreEqual(0, tooShort.Count, string.Join(", ", tooShort));
        }
    }
}
