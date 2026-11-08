using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Integrity;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Integrity;
using Services.NasbNiro.OpsData;
using Services.NasbNiro.ProductBank;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.OpsDataDtos;
using ViewModels.NasbNiro.ProductBankDtos;
using ViewModels.NasbNiro.ProjectControlDtos;
using XlsxSheet = Services.NasbNiro.Finance.XlsxSheet;

namespace MsTests
{
    /// <summary>Package A1: design plan, design input register, design review / verification / validation, the release gates (BOM, design-output drawings), readiness item, FCA and the registrations.</summary>
    [TestClass]
    public class PackageA1DesignControlTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1, "seed", "PMO");
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(1, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Pmo2 = AppHost.Principal(2, "pmo.two", "PMO");
        private static readonly ClaimsPrincipal Eng1 = AppHost.Principal(11, "engineer.one", "ENGINEERING");
        private static readonly ClaimsPrincipal Eng2 = AppHost.Principal(12, "engineer.two", "ENGINEERING");
        private static readonly ClaimsPrincipal Qc = AppHost.Principal(13, "qc.one", "QC");
        private static readonly ClaimsPrincipal Shop = AppHost.Principal(14, "shop.one", "CONSTRUCTION");
        private static readonly ClaimsPrincipal Maker = AppHost.Principal(61, "design.maker", "ENGINEER");
        private static readonly ClaimsPrincipal Mgr = AppHost.Principal(62, "design.manager", "ENGINEERING");
        private static readonly ClaimsPrincipal Qa = AppHost.Principal(63, "design.qa", "QA");
        private static readonly ClaimsPrincipal Qa2 = AppHost.Principal(64, "design.qa2", "QA");
        private static readonly ClaimsPrincipal PmoD = AppHost.Principal(65, "design.pmo", "PMO");
        private static readonly ClaimsPrincipal Dcc = AppHost.Principal(71, "dcc.one", "DCC");
        private static readonly ClaimsPrincipal Reviewer = AppHost.Principal(72, "engineer.rev", "ENGINEER");
        private static readonly ClaimsPrincipal Approver = AppHost.Principal(73, "engineering.manager", "ENGINEERING");
        private static readonly ClaimsPrincipal PmoE = AppHost.Principal(74, "pmo.edms", "PMO");
        private static readonly ClaimsPrincipal ListMaker = AppHost.Principal(75, "list.maker", "ENGINEER");
        private static readonly ClaimsPrincipal ListChecker = AppHost.Principal(76, "list.checker", "QA");
        private static readonly ClaimsPrincipal Operator = AppHost.Principal(77, "operator.one", "OPERATOR");

        private static AppHost NewHost(string name, bool control = true)
        {
            var host = new AppHost("PKA1_" + name + "_" + Guid.NewGuid().ToString("N"));
            host.Write(u =>
            {
                foreach (int id in new[] { 2, 11, 12, 13, 14, 61, 62, 63, 64, 65, 71, 72, 73, 74, 75, 76, 77 }) AppHost.Add(u, new User { Id = id, UserName = "pka1.user" + id, Email = "pka1.user" + id + "@example.com", IsActive = true });
                u.SaveChanges();
            }, Seeder);
            return control ? host.WithoutReadinessGate() : host.WithoutReadinessGate().WithoutDesignControl();
        }

        private static Task<T> Plans<T>(AppHost h, ClaimsPrincipal who, Func<IDesignPlanService, Task<T>> call) => h.As(who, p => call(p.GetRequiredService<IDesignPlanService>()));
        private static Task<T> Inputs<T>(AppHost h, ClaimsPrincipal who, Func<IDesignInputService, Task<T>> call) => h.As(who, p => call(p.GetRequiredService<IDesignInputService>()));
        private static Task<T> Reviews<T>(AppHost h, ClaimsPrincipal who, Func<IDesignReviewService, Task<T>> call) => h.As(who, p => call(p.GetRequiredService<IDesignReviewService>()));
        private static Task<T> Boms<T>(AppHost h, ClaimsPrincipal who, Func<IPbBomService, Task<T>> call) => h.As(who, p => call(p.GetRequiredService<IPbBomService>()));

        // ------------------------------------------------------------------------------------------ fixtures

        private static async Task<int> NewProduct(AppHost h, string code = "99.11.00.000.2")
        {
            var r = await h.As(Eng1, p => p.GetRequiredService<IPbProductService>().SaveProductAsync(new SavePbProductDto { Code = code, Title = "Test product" }, Ct));
            Assert.IsTrue(r.Success, r.Message);
            return r.Data.Id;
        }

        /// <summary>A BOM revision of the product that is submitted (so it has its content hash) and signed by four other people; not released.</summary>
        private static async Task<int> ApprovedBom(AppHost h, int productId)
        {
            var dto = new SavePbBomDto
            {
                ProductId = productId, AssemblyPartNumber = "99.11.00.000.2", Title = "BOM", Lines = new List<SavePbBomLineDto>
                {
                    new SavePbBomLineDto { ItemNumber = 1, PartNumber = "99.11.01.001.2", PartName = "Wheel case", Qty = 1, Unit = "عدد", Material = "WCB", MakeOrBuy = PbMakeOrBuy.Make, NetWeight = 10m, GrossWeight = 12m },
                },
            };
            var saved = await Boms(h, Eng1, s => s.SaveAsync(dto, Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.IsTrue((await Boms(h, Eng1, s => s.SubmitAsync(new PbBomActionDto { Id = saved.Data.Id }, Ct))).Success);
            foreach (var (who, step) in new[] { (Eng2, PbBomSignature.Checked), (Qc, PbBomSignature.QcChecked), (Shop, PbBomSignature.ManufacturingChecked), (Pmo, PbBomSignature.Approved) })
            {
                var s = await Boms(h, who, x => x.SignAsync(new PbSignDto { Id = saved.Data.Id, Step = step }, Ct));
                Assert.IsTrue(s.Success, step + ": " + s.Message);
            }
            return saved.Data.Id;
        }

        private static SaveDesignPlanDto PlanDto(int productId, bool validation = true, string waiver = null)
        {
            var dto = new SaveDesignPlanDto { ProductId = productId, ProductRevision = 0, Title = "برنامه طراحی آزمون", ValidationRequired = validation, ValidationWaiverReason = waiver };
            foreach (var d in DesignRules.DefaultStages())
                dto.Stages.Add(new SaveDesignStageDto { Kind = d.Kind, ResponsibleRole = d.Role, ResponsibleUserId = 61, PlannedStart = DateTime.Today, PlannedEnd = DateTime.Today.AddDays(30) });
            return dto;
        }

        private static async Task<int> ApprovedPlan(AppHost h, int productId, bool validation = true, string waiver = null)
        {
            var saved = await Plans(h, Maker, s => s.SaveAsync(PlanDto(productId, validation, waiver), Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.IsTrue((await Plans(h, Maker, s => s.ActionAsync(new DesignPlanActionDto { Id = saved.Data.Id, Action = "submit" }, Ct))).Success);
            var ap = await Plans(h, validation ? Mgr : Qa, s => s.ActionAsync(new DesignPlanActionDto { Id = saved.Data.Id, Action = "approve" }, Ct));
            Assert.IsTrue(ap.Success, ap.Message);
            return saved.Data.Id;
        }

        private static SaveDesignInputDto In(int productId, DesignInputKind kind, string text) => new SaveDesignInputDto
        {
            ProductId = productId, ProductRevision = 0, Kind = kind, Statement = text, Source = "قرارداد بند ۳", AcceptanceCriterion = "آزمون مطابق بند ۳ قبول",
            StandardEdition = kind == DesignInputKind.Standard ? "ASME 2019" : null, PreviousDesignRef = kind == DesignInputKind.PreviousDesign ? "DP-1403-001" : null,
            RefText = kind == DesignInputKind.CustomerSpec ? "مشخصات فنی مشتری" : null, RefRevisionText = kind == DesignInputKind.CustomerSpec ? "B" : null,
        };

        private static async Task<List<int>> AllKindInputs(AppHost h, int productId)
        {
            var ids = new List<int>();
            foreach (DesignInputKind k in DesignRules.AllKinds)
            {
                var r = await Inputs(h, Maker, s => s.SaveAsync(In(productId, k, "الزام " + k), Ct));
                Assert.IsTrue(r.Success, k + ": " + r.Message);
                ids.Add(r.Data.Id);
            }
            return ids;
        }

        private static async Task DeclareComplete(AppHost h, int planId)
        {
            var r = await Plans(h, Qa, s => s.ActionAsync(new DesignPlanActionDto { Id = planId, Action = "inputs-complete", Note = "منابع قرارداد و استانداردها بررسی شد" }, Ct));
            Assert.IsTrue(r.Success, r.Message);
        }

        private static SaveDesignReviewDto ReviewDto(int planId, DesignReviewType type, params SaveDesignOutputDto[] outputs) => new SaveDesignReviewDto
        {
            PlanId = planId, Type = type, Outputs = outputs.ToList(), Participants = new List<SaveDesignParticipantDto> { new SaveDesignParticipantDto { UserId = 63, RoleText = "QA" }, new SaveDesignParticipantDto { UserId = 62, RoleText = "مدیر مهندسی" } },
        };

        private static SaveDesignOutputDto BomOut(int bomId) => new SaveDesignOutputDto { Kind = DesignOutputKind.Bom, BomHeaderId = bomId };

        private static async Task<DesignReviewDto> Held(AppHost h, SaveDesignReviewDto dto)
        {
            var saved = await Reviews(h, Maker, s => s.SaveAsync(dto, Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            var held = await Reviews(h, Maker, s => s.ActionAsync(new DesignReviewActionDto { Id = saved.Data.Id, Action = "hold", Minutes = "جلسه برگزار شد و موارد بررسی شد" }, Ct));
            Assert.IsTrue(held.Success, held.Message);
            return held.Data;
        }

        private static async Task Judge(AppHost h, DesignReviewDto r, DesignTraceResult result = DesignTraceResult.Passed, int? onlyInput = null)
        {
            var rows = new List<SaveDesignTraceRowDto>();
            foreach (DesignReviewInputRowDto i in r.Inputs.Where(i => !onlyInput.HasValue || i.InputId == onlyInput.Value))
                foreach (DesignOutputDto o in r.Outputs)
                    rows.Add(new SaveDesignTraceRowDto { InputId = i.InputId, OutputId = o.Id, Result = result, Evidence = "اندازه‌گیری و آزمون مطابق معیار", EvidenceRef = "QR-1" });
            var t = await Reviews(h, Qa, s => s.SaveTraceAsync(new SaveDesignTraceDto { ReviewId = r.Id, Rows = rows }, Ct));
            Assert.IsTrue(t.Success, t.Message);
        }

        private static Task<ResponseDto<DesignReviewDto>> Close(AppHost h, int id, DesignReviewOutcome outcome = DesignReviewOutcome.Accepted, string note = null) =>
            Reviews(h, Mgr, s => s.ActionAsync(new DesignReviewActionDto { Id = id, Action = "close", Outcome = outcome, Note = note }, Ct));

        /// <summary>Plan, inputs of every kind, completeness declared: the design is ready for verification.</summary>
        private static async Task<(int Product, int Plan, int Bom)> Ready(AppHost h, bool validation = true, string waiver = null)
        {
            int product = await NewProduct(h);
            int plan = await ApprovedPlan(h, product, validation, waiver);
            await AllKindInputs(h, product);
            await DeclareComplete(h, plan);
            int bom = await ApprovedBom(h, product);
            return (product, plan, bom);
        }

        private static async Task<DesignReviewDto> AcceptedVerification(AppHost h, int plan, int bom)
        {
            DesignReviewDto held = await Held(h, ReviewDto(plan, DesignReviewType.Verification, BomOut(bom)));
            await Judge(h, held);
            var closed = await Close(h, held.Id);
            Assert.IsTrue(closed.Success, closed.Message);
            return closed.Data;
        }

        // ------------------------------------------------------------------------------------------ settings and registrations

        [TestMethod]
        public void TheDesignSettingsDefaultToTheStrictValuesAndAreOnTheSafetyProfile()
        {
            var defaults = DesignSettingKeys.Defaults().ToDictionary(d => d.Key, d => d.Value);
            Assert.AreEqual(1m, defaults[DesignSettingKeys.VerificationRequired]);
            Assert.AreEqual(1m, defaults[DesignSettingKeys.ValidationRequired]);
            Assert.AreEqual(1m, defaults[DesignSettingKeys.MinIndependent]);
            Assert.AreEqual(15m, defaults[DesignSettingKeys.ReasonMinChars]);
            Assert.IsTrue(QualitySettingKeys.Defaults().Any(d => d.Key == DesignSettingKeys.VerificationRequired), "the quality store lists the key");
            Assert.IsTrue(QualitySettingRules.TryGetBounds(DesignSettingKeys.MinIndependent, out int min, out int max) && min == 1 && max == 5);
            foreach (string key in defaults.Keys)
            {
                SafetySettingSpec spec = SafetyProfile.Find(SafetyStore.Quality, key);
                Assert.IsNotNull(spec, key + " is not on the safety profile");
            }
            Assert.IsTrue(SafetyProfile.IsWeaker(SafetyProfile.Find(SafetyStore.Quality, DesignSettingKeys.VerificationRequired), "0"), "switching the gate off is a weakening");
            Assert.IsTrue(SafetyProfile.IsWeaker(SafetyProfile.Find(SafetyStore.Quality, DesignSettingKeys.ReasonMinChars), "5"), "a shorter reason is a weakening");
        }

        [TestMethod]
        public void TheDesignRecordTypesAreInTheAuditChainWithPersianLabels()
        {
            foreach (Type type in new[] { typeof(PbDesignPlan), typeof(PbDesignStage), typeof(PbDesignInterface), typeof(PbDesignInput), typeof(PbDesignReview), typeof(PbDesignReviewParticipant), typeof(PbDesignReviewInput), typeof(PbDesignReviewOutput), typeof(PbDesignTrace), typeof(PbDesignFinding) })
            {
                string label = AuditEntityLabels.Of(type.Name);
                Assert.IsFalse(string.IsNullOrWhiteSpace(label), type.Name + " has no label");
                Assert.IsTrue(label.Any(c => c >= '؀' && c <= 'ۿ'), type.Name + " label is not Persian: " + label);
            }
        }

        // ------------------------------------------------------------------------------------------ plan

        [TestMethod]
        public async Task ThePlanIsApprovedByAnotherPersonOfEngineeringOrQa_NeverThePreparerNorAnAiSeat()
        {
            AppHost h = NewHost("plan");
            int product = await NewProduct(h);
            var saved = await Plans(h, Maker, s => s.SaveAsync(PlanDto(product), Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.AreEqual(DesignPlanStatus.Draft, saved.Data.Status);
            Assert.AreEqual(6, saved.Data.Stages.Count);
            Assert.IsFalse((await Plans(h, Maker, s => s.ActionAsync(new DesignPlanActionDto { Id = saved.Data.Id, Action = "approve" }, Ct))).Success, "a draft cannot be approved");
            Assert.IsTrue((await Plans(h, Maker, s => s.ActionAsync(new DesignPlanActionDto { Id = saved.Data.Id, Action = "submit" }, Ct))).Success);
            Assert.IsFalse((await Plans(h, Maker, s => s.ActionAsync(new DesignPlanActionDto { Id = saved.Data.Id, Action = "approve" }, Ct))).Success, "the preparer never approves");
            Assert.IsFalse((await Plans(h, Operator, s => s.ActionAsync(new DesignPlanActionDto { Id = saved.Data.Id, Action = "approve" }, Ct))).Success, "a role outside engineering and QA never approves");
            var seat = new AiActingUser { UserId = 90, UserName = "seat.qa", Roles = new[] { "QA", "ENGINEERING" } };
            var ai = await AiActing.RunAsync(h.Provider.GetRequiredService<IServiceScopeFactory>(), seat, "QA", sp => sp.GetRequiredService<IDesignPlanService>().ActionAsync(new DesignPlanActionDto { Id = saved.Data.Id, Action = "approve" }, Ct));
            Assert.IsFalse(ai.Success, "an AI seat never approves");
            Assert.IsFalse((await Plans(h, Maker, s => s.SaveAsync(new SaveDesignPlanDto { Id = saved.Data.Id, ProductId = product, ProductRevision = 0, Stages = PlanDto(product).Stages, Title = "x" }, Ct))).Success, "a submitted plan is not edited");
            var approved = await Plans(h, Mgr, s => s.ActionAsync(new DesignPlanActionDto { Id = saved.Data.Id, Action = "approve" }, Ct));
            Assert.IsTrue(approved.Success, approved.Message);
            Assert.AreEqual(DesignPlanStatus.Approved, approved.Data.Status);
            Assert.AreEqual(62, approved.Data.ApprovedByUserId);
            Assert.IsFalse((await Plans(h, Maker, s => s.SaveAsync(PlanDto(product), Ct))).Success, "one plan per product revision; changes are a new version");
        }

        [TestMethod]
        public async Task ASubmittedPlanNeedsAPersonAndDatesForEveryStage_AndAReturnedPlanIsEditedAgain()
        {
            AppHost h = NewHost("planfull");
            int product = await NewProduct(h);
            SaveDesignPlanDto dto = PlanDto(product);
            dto.Stages[2].ResponsibleUserId = null;
            dto.Stages[3].PlannedEnd = null;
            var saved = await Plans(h, Maker, s => s.SaveAsync(dto, Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            var submit = await Plans(h, Maker, s => s.ActionAsync(new DesignPlanActionDto { Id = saved.Data.Id, Action = "submit" }, Ct));
            Assert.IsFalse(submit.Success);
            StringAssert.Contains(submit.Message, "برنامه کامل نیست");
            dto.Id = saved.Data.Id; dto.RowVersion = saved.Data.RowVersion;
            dto.Stages[2].ResponsibleUserId = 61; dto.Stages[3].PlannedEnd = DateTime.Today.AddDays(5);
            var fixedPlan = await Plans(h, Maker, s => s.SaveAsync(dto, Ct));
            Assert.IsTrue(fixedPlan.Success, fixedPlan.Message);
            Assert.IsTrue((await Plans(h, Maker, s => s.ActionAsync(new DesignPlanActionDto { Id = saved.Data.Id, Action = "submit" }, Ct))).Success);
            Assert.IsFalse((await Plans(h, Qa, s => s.ActionAsync(new DesignPlanActionDto { Id = saved.Data.Id, Action = "return" }, Ct))).Success, "a return needs a reason");
            var back = await Plans(h, Qa, s => s.ActionAsync(new DesignPlanActionDto { Id = saved.Data.Id, Action = "return", Note = "مسئول بازبینی عوض شود" }, Ct));
            Assert.IsTrue(back.Success, back.Message);
            Assert.AreEqual(DesignPlanStatus.Draft, back.Data.Status);
        }

        [TestMethod]
        public async Task ANewVersionSupersedesTheApprovedPlanOnApproval()
        {
            AppHost h = NewHost("version");
            int product = await NewProduct(h);
            int first = await ApprovedPlan(h, product);
            var v2 = await Plans(h, Maker, s => s.ActionAsync(new DesignPlanActionDto { Id = first, Action = "new-version" }, Ct));
            Assert.IsTrue(v2.Success, v2.Message);
            Assert.AreEqual(2, v2.Data.Version);
            Assert.AreEqual(first, v2.Data.SupersedesPlanId);
            Assert.IsFalse((await Plans(h, Maker, s => s.ActionAsync(new DesignPlanActionDto { Id = first, Action = "new-version" }, Ct))).Success, "one open version at a time");
            Assert.IsTrue((await Plans(h, Maker, s => s.ActionAsync(new DesignPlanActionDto { Id = v2.Data.Id, Action = "submit" }, Ct))).Success);
            Assert.IsTrue((await Plans(h, Qa, s => s.ActionAsync(new DesignPlanActionDto { Id = v2.Data.Id, Action = "approve" }, Ct))).Success);
            Assert.AreEqual(DesignPlanStatus.Superseded, host(h).Single(p => p.Id == first).Status);
            Assert.AreEqual(1, host(h).Count(p => p.Status == DesignPlanStatus.Approved));
        }

        private static List<PbDesignPlan> host(AppHost h) => h.Read(u => u.Set<PbDesignPlan>().ToList());

        [TestMethod]
        public async Task ExemptingValidationNeedsAReasonAndOnlyQaApproves()
        {
            AppHost h = NewHost("waiver");
            int product = await NewProduct(h);
            Assert.IsFalse((await Plans(h, Maker, s => s.SaveAsync(PlanDto(product, false, null), Ct))).Success, "the exemption needs a reason");
            var saved = await Plans(h, Maker, s => s.SaveAsync(PlanDto(product, false, "محصول استاندارد کاتالوگی بدون الزام مشتری خاص"), Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.IsTrue((await Plans(h, Maker, s => s.ActionAsync(new DesignPlanActionDto { Id = saved.Data.Id, Action = "submit" }, Ct))).Success);
            Assert.IsFalse((await Plans(h, Mgr, s => s.ActionAsync(new DesignPlanActionDto { Id = saved.Data.Id, Action = "approve" }, Ct))).Success, "engineering alone cannot waive the validation");
            Assert.IsTrue((await Plans(h, Qa, s => s.ActionAsync(new DesignPlanActionDto { Id = saved.Data.Id, Action = "approve" }, Ct))).Success);
            int waivedBom = await ApprovedBom(h, product);
            var review = await Reviews(h, Maker, s => s.SaveAsync(ReviewDto(saved.Data.Id, DesignReviewType.Validation, BomOut(waivedBom)), Ct));
            Assert.IsFalse(review.Success, "no validation review for a plan that waives it");
        }

        // ------------------------------------------------------------------------------------------ input register

        [TestMethod]
        public async Task AnInputNeedsSourceCriterionAndWhatItsKindRequires()
        {
            AppHost h = NewHost("inputrules");
            int product = await NewProduct(h);
            foreach (Action<SaveDesignInputDto> bad in new Action<SaveDesignInputDto>[]
            {
                x => x.Source = null, x => x.AcceptanceCriterion = " ", x => x.Statement = "",
                x => { x.Kind = DesignInputKind.Standard; x.StandardEdition = null; },
                x => { x.Kind = DesignInputKind.PreviousDesign; x.PreviousDesignRef = null; },
                x => { x.Kind = DesignInputKind.CustomerSpec; x.RefText = null; x.RefRevisionText = null; },
                x => { x.Kind = DesignInputKind.CustomerSpec; x.RefText = "doc"; x.RefRevisionText = null; },
            })
            {
                SaveDesignInputDto dto = In(product, DesignInputKind.Functional, "x");
                bad(dto);
                Assert.IsFalse((await Inputs(h, Maker, s => s.SaveAsync(dto, Ct))).Success);
            }
            Assert.IsFalse((await Inputs(h, Operator, s => s.SaveAsync(In(product, DesignInputKind.Functional, "x"), Ct))).Success, "only engineering, QA and PMO write inputs");
            var ok = await Inputs(h, Maker, s => s.SaveAsync(In(product, DesignInputKind.Functional, "فشار کاری ۱۰ بار"), Ct));
            Assert.IsTrue(ok.Success, ok.Message);
            StringAssert.StartsWith(ok.Data.Code, "DI");
        }

        [TestMethod]
        public async Task AConflictBetweenInputsStopsTheDesignUntilAnotherPersonResolvesIt()
        {
            AppHost h = NewHost("conflict");
            int product = await NewProduct(h);
            int plan = await ApprovedPlan(h, product);
            List<int> ids = await AllKindInputs(h, product);
            Assert.IsFalse((await Inputs(h, Maker, s => s.ActionAsync(new DesignInputActionDto { Id = ids[0], Action = "conflict", Note = "کوتاه" }, Ct))).Success, "a conflict needs a description");
            var mark = await Inputs(h, Maker, s => s.ActionAsync(new DesignInputActionDto { Id = ids[0], Action = "conflict", WithInputId = ids[1], Note = "فشار کاری با ظرفیت استاندارد جور نیست" }, Ct));
            Assert.IsTrue(mark.Success, mark.Message);
            Assert.AreEqual(DesignInputStatus.Conflict, mark.Data.Status);
            Assert.AreEqual(DesignInputStatus.Conflict, (await Inputs(h, Maker, s => s.GetAsync(ids[1], Ct))).Data.Status, "the other input is marked too");
            var declare = await Plans(h, Qa, s => s.ActionAsync(new DesignPlanActionDto { Id = plan, Action = "inputs-complete", Note = "بررسی شد" }, Ct));
            Assert.IsFalse(declare.Success, "completeness cannot be declared while inputs are in conflict");
            var v = await Plans(h, Maker, s => s.ActionAsync(new DesignPlanActionDto { Id = plan, Action = "new-version" }, Ct));
            Assert.IsTrue(v.Success);
            Assert.IsTrue((await Plans(h, Maker, s => s.ActionAsync(new DesignPlanActionDto { Id = v.Data.Id, Action = "submit" }, Ct))).Success);
            Assert.IsFalse((await Plans(h, Qa, s => s.ActionAsync(new DesignPlanActionDto { Id = v.Data.Id, Action = "approve" }, Ct))).Success, "a plan is not approved while inputs are in conflict");
            Assert.IsFalse((await Inputs(h, Maker, s => s.ActionAsync(new DesignInputActionDto { Id = ids[0], Action = "resolve", Note = "رفع شد با اصلاح مقدار فشار" }, Ct))).Success, "the person who marked it does not resolve it");
            var resolved = await Inputs(h, Mgr, s => s.ActionAsync(new DesignInputActionDto { Id = ids[0], Action = "resolve", Note = "ظرفیت استاندارد اصلاح شد و فشار کاری تایید شد" }, Ct));
            Assert.IsTrue(resolved.Success, resolved.Message);
            Assert.AreEqual(DesignInputStatus.Open, (await Inputs(h, Maker, s => s.GetAsync(ids[1], Ct))).Data.Status, "the pair is resolved together");
            Assert.IsTrue((await Plans(h, Qa, s => s.ActionAsync(new DesignPlanActionDto { Id = v.Data.Id, Action = "approve" }, Ct))).Success);
        }

        [TestMethod]
        public async Task AnInputUsedByAReviewIsReplacedNotEdited()
        {
            AppHost h = NewHost("replace");
            var (product, plan, bom) = await Ready(h);
            SaveDesignInputDto edit = In(product, DesignInputKind.Functional, "الزام اصلاح‌شده");
            var inputs = (await Inputs(h, Maker, s => s.GetListAsync(new DesignInputQuery { ProductId = product, Kind = DesignInputKind.Functional, PageSize = 0 }, Ct))).Data;
            edit.Id = inputs[0].Id;
            var unused = await Inputs(h, Maker, s => s.SaveAsync(edit, Ct));
            Assert.IsTrue(unused.Success, unused.Message);
            Assert.AreEqual(inputs[0].Id, unused.Data.Id, "an input no review refers to is edited in place");
            DesignReviewDto held = await Held(h, ReviewDto(plan, DesignReviewType.Verification, BomOut(bom)));
            edit.RowVersion = null;
            edit.Statement = "الزام بعد از بازبینی";
            var replaced = await Inputs(h, Maker, s => s.SaveAsync(edit, Ct));
            Assert.IsTrue(replaced.Success, replaced.Message);
            Assert.AreNotEqual(inputs[0].Id, replaced.Data.Id);
            Assert.AreEqual(inputs[0].Id, replaced.Data.SupersedesInputId);
            Assert.AreEqual(DesignInputStatus.Superseded, (await Inputs(h, Maker, s => s.GetAsync(inputs[0].Id, Ct))).Data.Status);
            Assert.IsFalse((await Inputs(h, Maker, s => s.ActionAsync(new DesignInputActionDto { Id = held.Inputs[1].InputId, Action = "void", Note = "دیگر لازم نیست چون حذف شد" }, Ct))).Success, "an input a review refers to is not voided");
        }

        [TestMethod]
        public async Task TheTemplateImportsAndABadRowStopsTheWholeImport()
        {
            AppHost h = NewHost("excel");
            int product = await NewProduct(h);
            var template = await Inputs(h, Maker, s => Task.FromResult(s.Template()));
            Assert.IsTrue(template.Success && template.Data.Length > 0);
            var preview = await Inputs(h, Maker, s => s.ImportAsync(new DesignInputExcelRequestDto { ProductId = product, ProductRevision = 0, Preview = true }, template.Data, Ct));
            Assert.IsTrue(preview.Success, preview.Message);
            Assert.AreEqual(1, preview.Data.ValidRows);
            Assert.AreEqual(0, h.Read(u => u.Set<PbDesignInput>().Count()), "a preview writes nothing");
            var done = await Inputs(h, Maker, s => s.ImportAsync(new DesignInputExcelRequestDto { ProductId = product, ProductRevision = 0, Preview = false }, template.Data, Ct));
            Assert.IsTrue(done.Success, done.Message);
            Assert.AreEqual(1, done.Data.Created);
            var sheet = new XlsxSheet { Name = "ورودی‌های طراحی", Header = new List<string> { "نوع ورودی", "الزام", "منبع", "معیار پذیرش", "ویرایش استاندارد", "طراحی قبلی", "مرجع مشخصات مشتری", "بازنگری مرجع", "پیامد خرابی" } };
            sheet.Rows.Add(new object[] { DesignRules.KindText(DesignInputKind.Functional), "الزام خوب", "منبع", "معیار", "", "", "", "", "" });
            sheet.Rows.Add(new object[] { DesignRules.KindText(DesignInputKind.Standard), "استاندارد بدون ویرایش", "منبع", "معیار", "", "", "", "", "" });
            byte[] bad = Services.NasbNiro.Finance.SimpleXlsx.Build(new List<XlsxSheet> { sheet }, true);
            var r = await Inputs(h, Maker, s => s.ImportAsync(new DesignInputExcelRequestDto { ProductId = product, ProductRevision = 0, Preview = false }, bad, Ct));
            Assert.IsTrue(r.Success);
            Assert.AreEqual(1, r.Data.ErrorRows);
            Assert.AreEqual(0, r.Data.Created, "one bad row, nothing is created");
            Assert.AreEqual(1, h.Read(u => u.Set<PbDesignInput>().Count()));
            var export = await Inputs(h, Maker, s => s.ExportAsync(product, 0, Ct));
            Assert.IsTrue(export.Success && export.Data.Length > 0);
        }

        // ------------------------------------------------------------------------------------------ plan stages

        [TestMethod]
        public async Task TheDesignStageStartsOnlyAfterANewCompletenessDeclarationByAnotherPerson()
        {
            AppHost h = NewHost("stages");
            int product = await NewProduct(h);
            int plan = await ApprovedPlan(h, product);
            int Stage(DesignStageKind k) => h.Read(u => u.Set<PbDesignStage>().Single(s => s.PlanId == plan && s.Kind == k).Id);
            int sInputs = Stage(DesignStageKind.Inputs), sDesign = Stage(DesignStageKind.Design), sVerification = Stage(DesignStageKind.Verification);
            Assert.IsFalse((await Plans(h, Maker, s => s.StageActionAsync(new DesignStageActionDto { PlanId = plan, StageId = sDesign, Action = "start" }, Ct))).Success, "the previous stage is not done");
            Assert.IsFalse((await Plans(h, Maker, s => s.StageActionAsync(new DesignStageActionDto { PlanId = plan, StageId = sInputs, Action = "complete" }, Ct))).Success, "no inputs yet");
            await AllKindInputs(h, product);
            Assert.IsFalse((await Plans(h, Maker, s => s.ActionAsync(new DesignPlanActionDto { Id = plan, Action = "inputs-complete", Note = "همه بررسی شد" }, Ct))).Success, "the preparer of the plan does not declare completeness");
            await DeclareComplete(h, plan);
            var done = await Plans(h, Maker, s => s.StageActionAsync(new DesignStageActionDto { PlanId = plan, StageId = sInputs, Action = "complete" }, Ct));
            Assert.IsTrue(done.Success, done.Message);
            // a new input after the declaration makes the declaration stale
            var late = await Inputs(h, Maker, s => s.SaveAsync(In(product, DesignInputKind.Functional, "الزام دیرهنگام"), Ct));
            Assert.IsTrue(late.Success);
            var start = await Plans(h, Maker, s => s.StageActionAsync(new DesignStageActionDto { PlanId = plan, StageId = sDesign, Action = "start" }, Ct));
            Assert.IsFalse(start.Success, "a new input needs a new declaration");
            await DeclareComplete(h, plan);
            Assert.IsTrue((await Plans(h, Maker, s => s.StageActionAsync(new DesignStageActionDto { PlanId = plan, StageId = sDesign, Action = "start" }, Ct))).Success);
            Assert.IsFalse((await Plans(h, Maker, s => s.StageActionAsync(new DesignStageActionDto { PlanId = plan, StageId = sVerification, Action = "complete" }, Ct))).Success, "not before the previous stages are done");
        }

        // ------------------------------------------------------------------------------------------ reviews

        [TestMethod]
        public async Task AReviewNeedsAnIndependentParticipantWhoIsNotAnAuthorOfTheOutput()
        {
            AppHost h = NewHost("independent");
            var (product, plan, bom) = await Ready(h);
            SaveDesignReviewDto onlyAuthor = ReviewDto(plan, DesignReviewType.Verification, BomOut(bom));
            onlyAuthor.Participants = new List<SaveDesignParticipantDto> { new SaveDesignParticipantDto { UserId = 11, RoleText = "طراح BOM" } };
            var saved = await Reviews(h, Maker, s => s.SaveAsync(onlyAuthor, Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.AreEqual(0, saved.Data.IndependentCount, "the preparer of the BOM is not independent");
            Assert.IsFalse(saved.Data.Participants[0].Independent);
            var hold = await Reviews(h, Maker, s => s.ActionAsync(new DesignReviewActionDto { Id = saved.Data.Id, Action = "hold", Minutes = "جلسه با طراح به تنهایی" }, Ct));
            Assert.IsFalse(hold.Success, "no independent participant, no meeting");
            Assert.IsFalse((await Reviews(h, Maker, s => s.SaveAsync(new SaveDesignReviewDto { PlanId = plan, Type = DesignReviewType.Verification, Outputs = new List<SaveDesignOutputDto> { BomOut(bom) }, Participants = new List<SaveDesignParticipantDto> { new SaveDesignParticipantDto { UserId = 9999 } } }, Ct))).Success, "an unknown person");
        }

        [TestMethod]
        public async Task AVerificationClosesAcceptedOnlyWhenEveryInputHasPassedEvidenceJudgedByAnIndependentParticipant()
        {
            AppHost h = NewHost("coverage");
            var (product, plan, bom) = await Ready(h);
            DesignReviewDto held = await Held(h, ReviewDto(plan, DesignReviewType.Verification, BomOut(bom)));
            Assert.AreEqual(7, held.Inputs.Count, "a verification covers every live input");
            var early = await Close(h, held.Id);
            Assert.IsFalse(early.Success, "nothing is judged");
            StringAssert.Contains(early.Message, "ورودی");
            var byAuthor = await Reviews(h, Eng1, s => s.SaveTraceAsync(new SaveDesignTraceDto { ReviewId = held.Id, Rows = new List<SaveDesignTraceRowDto> { new SaveDesignTraceRowDto { InputId = held.Inputs[0].InputId, OutputId = held.Outputs[0].Id, Result = DesignTraceResult.Passed, Evidence = "نتیجه آزمون" } } }, Ct));
            Assert.IsFalse(byAuthor.Success, "only participants judge");
            var byOutsider = await Reviews(h, Dcc, s => s.SaveTraceAsync(new SaveDesignTraceDto { ReviewId = held.Id, Rows = new List<SaveDesignTraceRowDto> { new SaveDesignTraceRowDto { InputId = held.Inputs[0].InputId, OutputId = held.Outputs[0].Id, Result = DesignTraceResult.Passed, Evidence = "نتیجه آزمون" } } }, Ct));
            Assert.IsFalse(byOutsider.Success, "a person who is not in the meeting does not judge");
            Assert.IsFalse((await Reviews(h, Qa, s => s.SaveTraceAsync(new SaveDesignTraceDto { ReviewId = held.Id, Rows = new List<SaveDesignTraceRowDto> { new SaveDesignTraceRowDto { InputId = held.Inputs[0].InputId, OutputId = held.Outputs[0].Id, Result = DesignTraceResult.Passed } } }, Ct))).Success, "evidence is required");
            await Judge(h, held, onlyInput: held.Inputs[0].InputId);
            await Judge(h, held, DesignTraceResult.Failed, held.Inputs[1].InputId);
            foreach (DesignReviewInputRowDto i in held.Inputs.Skip(2)) await Judge(h, held, onlyInput: i.InputId);
            var failed = await Close(h, held.Id);
            Assert.IsFalse(failed.Success, "a failed criterion blocks the acceptance");
            await Judge(h, held, DesignTraceResult.Passed, held.Inputs[1].InputId);
            var finally_ = await Close(h, held.Id);
            Assert.IsTrue(finally_.Success, finally_.Message);
        }

        [TestMethod]
        public async Task TheCloserIsNotAnAuthorAndAnInputAddedAfterTheMeetingIsNotCovered()
        {
            AppHost h = NewHost("closer");
            var (product, plan, bom) = await Ready(h);
            DesignReviewDto held = await Held(h, ReviewDto(plan, DesignReviewType.Verification, BomOut(bom)));
            await Judge(h, held);
            var byAuthor = await Reviews(h, Eng1, s => s.ActionAsync(new DesignReviewActionDto { Id = held.Id, Action = "close", Outcome = DesignReviewOutcome.Accepted }, Ct));
            Assert.IsFalse(byAuthor.Success, "the preparer of the BOM does not close its review");
            var seat = new AiActingUser { UserId = 91, UserName = "seat.eng", Roles = new[] { "ENGINEERING", "QA" } };
            var ai = await AiActing.RunAsync(h.Provider.GetRequiredService<IServiceScopeFactory>(), seat, "ENG", sp => sp.GetRequiredService<IDesignReviewService>().ActionAsync(new DesignReviewActionDto { Id = held.Id, Action = "close", Outcome = DesignReviewOutcome.Accepted }, Ct));
            Assert.IsFalse(ai.Success, "an AI seat never closes");
            Assert.IsTrue((await Inputs(h, Maker, s => s.SaveAsync(In(product, DesignInputKind.Performance, "الزام پس از جلسه"), Ct))).Success);
            // fix round 1: the review of the inputs it selected closes; the GATE decides over every live input, so the BOM stays unreleasable until the new input is covered
            var closed = await Close(h, held.Id);
            Assert.IsTrue(closed.Success, closed.Message);
            var refused = await Boms(h, Pmo2, s => s.ReleaseAsync(new PbBomActionDto { Id = bom }, Ct));
            Assert.IsFalse(refused.Success, "the new input is not covered");
            StringAssert.Contains(refused.Message, DesignGate.StaleText);
        }

        [TestMethod]
        public async Task FindingsAreDoneByTheOwnerAndClosedByAnotherPerson_AndBlockTheAcceptance()
        {
            AppHost h = NewHost("findings");
            var (product, plan, bom) = await Ready(h);
            DesignReviewDto held = await Held(h, ReviewDto(plan, DesignReviewType.Verification, BomOut(bom)));
            await Judge(h, held);
            var raised = await Reviews(h, Maker, s => s.SaveFindingAsync(new SaveDesignFindingDto { ReviewId = held.Id, Description = "ضخامت دیواره باید دوباره محاسبه شود", OwnerUserId = 61, DueDate = DateTime.Today.AddDays(7) }, Ct));
            Assert.IsTrue(raised.Success, raised.Message);
            int fid = raised.Data.Findings[0].Id;
            Assert.IsFalse((await Close(h, held.Id)).Success, "an open finding blocks");
            Assert.IsFalse((await Reviews(h, Maker, s => s.FindingActionAsync(new DesignFindingActionDto { Id = fid, Action = "close", Note = "بسته شد" }, Ct))).Success, "not done yet");
            Assert.IsFalse((await Reviews(h, Qa, s => s.FindingActionAsync(new DesignFindingActionDto { Id = fid, Action = "done", Note = "انجام شد توسط دیگری" }, Ct))).Success, "only the owner declares it done");
            Assert.IsTrue((await Reviews(h, Maker, s => s.FindingActionAsync(new DesignFindingActionDto { Id = fid, Action = "done", Note = "محاسبه تکرار و پیوست شد" }, Ct))).Success);
            Assert.IsFalse((await Reviews(h, Maker, s => s.FindingActionAsync(new DesignFindingActionDto { Id = fid, Action = "close", Note = "خودم می‌بندم" }, Ct))).Success, "the owner never closes its own finding");
            Assert.IsFalse((await Close(h, held.Id)).Success, "done is not closed");
            Assert.IsTrue((await Reviews(h, Qa, s => s.FindingActionAsync(new DesignFindingActionDto { Id = fid, Action = "close", Note = "محاسبه بازبینی شد و درست است" }, Ct))).Success);
            var closed = await Close(h, held.Id);
            Assert.IsTrue(closed.Success, closed.Message);
            Assert.AreEqual(DesignReviewOutcome.Accepted, closed.Data.Outcome);
        }

        [TestMethod]
        public async Task AcceptedWithConditionsAndRejectedNeedAReasonAndNeverOpenAGate()
        {
            AppHost h = NewHost("conditions");
            var (product, plan, bom) = await Ready(h, false, "محصول استاندارد کاتالوگی بدون الزام مشتری خاص");
            DesignReviewDto held = await Held(h, ReviewDto(plan, DesignReviewType.Verification, BomOut(bom)));
            await Judge(h, held);
            Assert.IsFalse((await Close(h, held.Id, DesignReviewOutcome.Rejected)).Success, "a rejection needs a reason");
            Assert.IsFalse((await Close(h, held.Id, DesignReviewOutcome.AcceptedWithConditions, "پذیرش مشروط به اصلاح ضخامت")).Success, "the conditions are findings with an owner and a date");
            Assert.IsTrue((await Reviews(h, Maker, s => s.SaveFindingAsync(new SaveDesignFindingDto { ReviewId = held.Id, Description = "ضخامت اصلاح شود", OwnerUserId = 61, DueDate = DateTime.Today.AddDays(3) }, Ct))).Success);
            var cond = await Close(h, held.Id, DesignReviewOutcome.AcceptedWithConditions, "پذیرش مشروط به اصلاح ضخامت");
            Assert.IsTrue(cond.Success, cond.Message);
            var release = await Boms(h, Pmo2, s => s.ReleaseAsync(new PbBomActionDto { Id = bom }, Ct));
            Assert.IsFalse(release.Success, "accepted with conditions does not open the release gate");
            StringAssert.Contains(release.Message, "پذیرفته با شرط");
            var again = await Reviews(h, Maker, s => s.ActionAsync(new DesignReviewActionDto { Id = held.Id, Action = "repeat" }, Ct));
            Assert.IsTrue(again.Success, again.Message);
            Assert.AreEqual(held.Id, again.Data.PreviousReviewId);
            Assert.AreEqual(DesignReviewStatus.Planned, again.Data.Status);
        }

        [TestMethod]
        public async Task OnlyQaVoidsAClosedReviewAndTheGateClosesAgain()
        {
            AppHost h = NewHost("void");
            var (product, plan, bom) = await Ready(h);
            DesignReviewDto accepted = await AcceptedVerification(h, plan, bom);
            Assert.IsFalse((await Reviews(h, Mgr, s => s.ActionAsync(new DesignReviewActionDto { Id = accepted.Id, Action = "void", Note = "باطل می‌کنم چون اشتباه ثبت شد" }, Ct))).Success, "a closed review is voided by QA only");
            Assert.IsFalse((await Reviews(h, Qa, s => s.ActionAsync(new DesignReviewActionDto { Id = accepted.Id, Action = "void", Note = "کوتاه" }, Ct))).Success);
            Assert.IsTrue((await Reviews(h, Qa, s => s.ActionAsync(new DesignReviewActionDto { Id = accepted.Id, Action = "void", Note = "جلسه با شرکت‌کننده اشتباه ثبت شده بود" }, Ct))).Success);
            Assert.IsFalse((await Boms(h, Pmo2, s => s.ReleaseAsync(new PbBomActionDto { Id = bom }, Ct))).Success, "a voided review opens nothing");
            Assert.AreEqual(1, h.Read(u => u.Set<PbDesignReview>().Count()), "void, never delete");
        }

        // ------------------------------------------------------------------------------------------ gates

        [TestMethod]
        public async Task TheBomIsReleasedOnlyAfterAClosedAcceptedVerification()
        {
            AppHost h = NewHost("bomgate");
            var (product, plan, bom) = await Ready(h);
            var refused = await Boms(h, Pmo2, s => s.ReleaseAsync(new PbBomActionDto { Id = bom }, Ct));
            Assert.IsFalse(refused.Success);
            StringAssert.Contains(refused.Message, "تایید طراحی");
            var status = await Plans(h, Maker, s => s.GateStatusAsync("bom", bom, Ct));
            Assert.IsTrue(status.Success);
            Assert.IsTrue(status.Data.Required);
            Assert.IsFalse(status.Data.Passed);
            await AcceptedVerification(h, plan, bom);
            var released = await Boms(h, Pmo2, s => s.ReleaseAsync(new PbBomActionDto { Id = bom }, Ct));
            Assert.IsTrue(released.Success, released.Message);
            Assert.IsTrue((await Plans(h, Maker, s => s.GateStatusAsync("bom", bom, Ct))).Data.Passed);
            var dossier = await Plans(h, Maker, s => s.DossierAsync(product, Ct));
            Assert.IsTrue(dossier.Success);
            Assert.AreEqual(1, dossier.Data.Plans.Count);
            Assert.AreEqual(7, dossier.Data.Inputs.Count);
            Assert.AreEqual(1, dossier.Data.Reviews.Count);
        }

        [TestMethod]
        public async Task TheBomReleaseIsFreeWhenTheSettingIsOffAndANoPlanProductIsRefusedWithAHint()
        {
            AppHost off = NewHost("gateoff", false);
            int p0 = await NewProduct(off);
            int b0 = await ApprovedBom(off, p0);
            Assert.IsTrue((await Boms(off, Pmo2, s => s.ReleaseAsync(new PbBomActionDto { Id = b0 }, Ct))).Success, "the test database has the gate off unless a test turns it on");
            AppHost on = NewHost("gateon");
            int p1 = await NewProduct(on);
            int b1 = await ApprovedBom(on, p1);
            var refused = await Boms(on, Pmo2, s => s.ReleaseAsync(new PbBomActionDto { Id = b1 }, Ct));
            Assert.IsFalse(refused.Success);
            StringAssert.Contains(refused.Message, "برنامه طراحی");
        }

        private static async Task<(int Deliverable, int Revision)> DrawingRevision(AppHost h, string part, bool linked)
        {
            int contract = 0, doc = 0;
            h.Write(u => { var c = AppHost.Add(u, new ContractEntity { Code = "C-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "contract", Status = ContractStatus.Current }); u.SaveChanges(); contract = c.Id; }, Seeder);
            h.Write(u => { var d = AppHost.Add(u, new PcEngineeringDeliverable { ContractId = contract, DocumentNumber = "DWG-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "drawing", Discipline = "Mechanical", DocumentType = "Drawing" }); u.SaveChanges(); doc = d.Id; }, Seeder);
            if (linked) Assert.IsTrue((await h.As(Dcc, p => p.GetRequiredService<IEdmsService>().SaveLinkAsync(new PcDocLinkSaveDto { DeliverableId = doc, Kind = PcDocLinkKind.PartNumber, PartNumber = part }, Ct))).Success);
            var rev = await h.As(Dcc, p => p.GetRequiredService<IEdmsService>().CreateRevisionAsync(new PcRevisionCreateDto { DeliverableId = doc, Reason = "first issue" }, Ct));
            Assert.IsTrue(rev.Success, rev.Message);
            Guid fid = Guid.Empty;
            h.Write(u => { var d = new Entities.Model.Document("files/" + Guid.NewGuid().ToString("N") + ".pdf", "application/pdf"); u.Set<Entities.Model.Document>().Add(d); u.SaveChanges(); fid = d.Id; }, Seeder);
            Assert.IsTrue((await h.As(Dcc, p => p.GetRequiredService<IEdmsService>().AddFileAsync(new PcRevisionFileAddDto { RevisionId = rev.Data.Id, DocumentId = fid, FileName = "a.pdf", Role = PcRevisionFileRole.Pdf }, Ct))).Success);
            return (doc, rev.Data.Id);
        }

        private static async Task<ResponseDto<PcRevisionDto>> ReleaseDrawing(AppHost h, int revisionId, bool onlyRelease = false)
        {
            bool linked = h.Read(u => u.Set<PcDocumentRevision>().Where(r => r.Id == revisionId).Join(u.Set<PcDocumentLink>().Where(l => !l.Deleted && l.UnlinkReason == null), r => r.DeliverableId, l => l.DeliverableId, (r, l) => l.Kind).Any());
            bool hasList = h.Read(u => u.Set<QcCharacteristicList>().Any(l => l.RevisionId == revisionId));
            if (linked && !hasList)
            {
                var saved = await h.As(ListMaker, p => p.GetRequiredService<ICharacteristicService>().SaveAsync(new ViewModels.NasbNiro.QualityDtos.SaveCharListDto { RevisionId = revisionId, Reason = "این نقشه مشخصه قابل اندازه‌گیری ندارد (آزمون)" }, Ct));
                Assert.IsTrue(saved.Success, saved.Message);
                foreach (var step in new[] { (ListMaker, "submit"), (ListChecker, "approve") })
                    Assert.IsTrue((await h.As(step.Item1, p => p.GetRequiredService<ICharacteristicService>().ActionAsync(new ViewModels.NasbNiro.QualityDtos.CharListActionDto { Id = saved.Data.Id, Action = step.Item2 }, Ct))).Success);
            }
            ResponseDto<PcRevisionDto> last = null;
            foreach (var step in new[] { (Dcc, "submit"), (Reviewer, "review"), (Approver, "approve"), (PmoE, "release") }.Skip(onlyRelease ? 3 : 0))
            {
                last = await h.As(step.Item1, p => p.GetRequiredService<IEdmsService>().ActionAsync(new PcRevisionActionDto { Id = revisionId, Action = step.Item2 }, Ct));
                if (!last.Success) return last;
            }
            return last;
        }

        [TestMethod]
        public async Task ADesignOutputDrawingIsReleasedOnlyAfterAVerificationThatCoversItButOtherDocumentsAreNot()
        {
            AppHost h = NewHost("drawing");
            var (product, plan, bom) = await Ready(h);
            var free = await DrawingRevision(h, "x", false);
            var freeRelease = await ReleaseDrawing(h, free.Revision);
            Assert.IsTrue(freeRelease.Success, "a document that is no part's drawing is controlled by the EDMS review only: " + freeRelease.Message);
            var d = await DrawingRevision(h, "99.11.01.001.2", true);
            var refused = await ReleaseDrawing(h, d.Revision);
            Assert.IsFalse(refused.Success);
            StringAssert.Contains(refused.Message, "تایید طراحی");
            DesignReviewDto held = await Held(h, ReviewDto(plan, DesignReviewType.Verification, new SaveDesignOutputDto { Kind = DesignOutputKind.Drawing, RevisionId = d.Revision }));
            await Judge(h, held);
            Assert.IsTrue((await Close(h, held.Id)).Success);
            var released = await ReleaseDrawing(h, d.Revision, true);
            Assert.IsTrue(released.Success, released.Message);
        }

        [TestMethod]
        public void TheReadinessItemIsHardAndOnlyAppliesWhenTheDesignControlIsOn()
        {
            Assert.IsTrue(OpsReadinessCatalog.Find(ReadinessCodes.Design).Hard);
            var settings = new Dictionary<string, decimal>();
            var facts = new ReadinessFacts { ProductId = 1, BomReleased = true, WbsApproved = true, DesignRequired = true, DesignProblems = new List<string> { "بازبینی ندارد" } };
            ReadinessItemDto item = OpsReadiness.Evaluate(facts, settings).Items.Single(i => i.Code == ReadinessCodes.Design);
            Assert.AreEqual(OpsReadiness.MissingStatus, item.Status);
            Assert.IsTrue(item.Hard);
            facts.DesignProblems.Clear();
            Assert.AreEqual(OpsReadiness.Ok, OpsReadiness.Evaluate(facts, settings).Items.Single(i => i.Code == ReadinessCodes.Design).Status);
            facts.DesignRequired = false;
            Assert.AreEqual(OpsReadiness.NotApplicable, OpsReadiness.Evaluate(facts, settings).Items.Single(i => i.Code == ReadinessCodes.Design).Status);
            Assert.IsNotNull(SafetyProfile.Find(SafetyStore.Quality, OpsReadinessCatalog.Key(ReadinessCodes.Design, "Enabled")), "the new item's switches are on the safety profile");
        }

        [TestMethod]
        public async Task TheFunctionalConfigurationAuditNeedsVerificationAndValidationOfTheProjectBom()
        {
            AppHost h = NewHost("fca");
            var (product, plan, bom) = await Ready(h);
            int wo = 700;
            h.Write(u => { AppHost.Add(u, new PbProjectBaseline { WorkOrderId = wo, ProductId = product, BomHeaderId = bom, SourceBomCode = "B", SourceBomRevision = 0, IsCurrent = true, CopiedAt = DateTimeOffset.Now }); u.SaveChanges(); }, Seeder);
            DesignFcaResult none = await h.Read2(u => DesignGate.FcaAsync(u, wo, Ct));
            Assert.IsTrue(none.Applicable);
            Assert.IsFalse(none.Passed, "no review at all");
            await AcceptedVerification(h, plan, bom);
            DesignFcaResult half = await h.Read2(u => DesignGate.FcaAsync(u, wo, Ct));
            Assert.IsFalse(half.Passed, "verification alone is not enough while the validation is required");
            Assert.IsTrue(half.Problems.Any(p => p.Contains("صحه‌گذاری")));
            DesignReviewDto val = await Held(h, ReviewDto(plan, DesignReviewType.Validation, BomOut(bom)));
            Assert.AreEqual(3, val.Inputs.Count, "a validation judges the functional, performance and customer inputs");
            await Judge(h, val);
            Assert.IsFalse((await Close(h, val.Id)).Success, "the method and the customer acceptance are missing");
            var rec = await Reviews(h, Maker, s => s.SaveValidationAsync(new SaveDesignValidationDto { ReviewId = val.Id, Method = DesignValidationMethod.Test, Evidence = "گزارش آزمون عملکرد شماره T-1 روی نمونه", CustomerAcceptanceNotApplicable = true, CustomerAcceptanceNote = "محصول انبار بدون مشتری مشخص" }, Ct));
            Assert.IsTrue(rec.Success, rec.Message);
            var closed = await Close(h, val.Id);
            Assert.IsTrue(closed.Success, closed.Message);
            DesignFcaResult full = await h.Read2(u => DesignGate.FcaAsync(u, wo, Ct));
            Assert.IsTrue(full.Passed, string.Join(" | ", full.Problems));
            Assert.AreEqual(2, full.Reviews.Count);
            Assert.IsFalse((await DesignGateOffFca(h, 999)).Applicable, "a work order without a baseline is not applicable");
        }


        // ------------------------------------------------------------------------------------------ fix round 1

        private static Task<string> Release(AppHost h, int bomId) => h.Read2(async u =>
        {
            PbBomHeader head = await u.Set<PbBomHeader>().AsNoTracking().FirstAsync(x => x.Id == bomId);
            return await DesignGate.CheckReleaseAsync(u, head, null, null, Ct);
        });

        private static Task<string> ReleaseOfDrawing(AppHost h, int revisionId) => h.Read2(async u =>
        {
            PcDocumentRevision rev = await u.Set<PcDocumentRevision>().AsNoTracking().FirstAsync(x => x.Id == revisionId);
            PcEngineeringDeliverable d = await u.Set<PcEngineeringDeliverable>().AsNoTracking().FirstAsync(x => x.Id == rev.DeliverableId);
            return await DesignGate.CheckReleaseAsync(u, null, d, rev, Ct);
        });

        private static SaveDesignReviewDto ScopedReview(int planId, int bomId, params int[] inputs)
        {
            SaveDesignReviewDto dto = ReviewDto(planId, DesignReviewType.Verification, BomOut(bomId));
            dto.InputIds = inputs.ToList();
            return dto;
        }

        [TestMethod]
        public async Task AClosedVerificationIsStaleWhenAnInputIsAddedOrReplacedAndASupplementaryVerificationRestoresIt()
        {
            AppHost h = NewHost("stale");
            var (product, plan, bom) = await Ready(h);
            await AcceptedVerification(h, plan, bom);
            Assert.IsNull(await Release(h, bom), "covered");
            var added = await Inputs(h, Maker, s => s.SaveAsync(In(product, DesignInputKind.Performance, "الزام تازه پس از بستن"), Ct));
            Assert.IsTrue(added.Success, added.Message);
            string stale = await Release(h, bom);
            Assert.IsNotNull(stale);
            StringAssert.Contains(stale, DesignGate.StaleText);
            StringAssert.Contains(stale, added.Data.Code);
            // a supplementary verification that covers only the delta restores it
            DesignReviewDto held = await Held(h, ScopedReview(plan, bom, added.Data.Id));
            await Judge(h, held);
            Assert.IsTrue((await Close(h, held.Id)).Success);
            Assert.IsNull(await Release(h, bom), "the supplementary verification covers the new input");
            // an input that a review used is replaced, never edited: the replacement is a new input and the coverage is stale again
            int used = (await Inputs(h, Maker, s => s.GetListAsync(new DesignInputQuery { ProductId = product, ProductRevision = 0 }, Ct))).Data.First().Id;
            var edit = await Inputs(h, Maker, s => s.SaveAsync(new SaveDesignInputDto { Id = used, ProductId = product, ProductRevision = 0, Kind = DesignInputKind.Functional, Statement = "الزام اصلاح‌شده", Source = "قرارداد بند ۴", AcceptanceCriterion = "آزمون بند ۴" }, Ct));
            Assert.IsTrue(edit.Success, edit.Message);
            StringAssert.Contains(await Release(h, bom) ?? string.Empty, DesignGate.StaleText);
        }

        [TestMethod]
        public async Task AReviewWhoseInputWasVoidedAfterItClosedIsStaleUntilALaterReviewCloses()
        {
            AppHost h = NewHost("voided");
            var (product, plan, bom) = await Ready(h);
            DesignReviewDto accepted = await AcceptedVerification(h, plan, bom);
            int voided = accepted.Inputs.First().InputId;
            h.Write(u => { PbDesignInput i = u.Set<PbDesignInput>().First(x => x.Id == voided); i.Status = DesignInputStatus.Void; i.ModifyDate = DateTimeOffset.Now.AddMinutes(5); u.SaveChanges(); }, Seeder);
            StringAssert.Contains(await Release(h, bom) ?? string.Empty, DesignGate.StaleText);
            DesignReviewDto again = await Held(h, ReviewDto(plan, DesignReviewType.Verification, BomOut(bom)));
            await Judge(h, again);
            var closed = await Close(h, again.Id);
            Assert.IsTrue(closed.Success, closed.Message);
            h.Write(u => { PbDesignReview r = u.Set<PbDesignReview>().First(x => x.Id == again.Id); r.ClosedAt = DateTimeOffset.Now.AddMinutes(10); u.SaveChanges(); }, Seeder);
            Assert.IsNull(await Release(h, bom), "a later accepted verification restores it");
        }

        [TestMethod]
        public async Task ADrawingWhoseFilesChangedAfterTheVerificationOrWasRejectedToDraftIsNoLongerVerified()
        {
            AppHost h = NewHost("fingerprint");
            var (product, plan, bom) = await Ready(h);
            var d = await DrawingRevision(h, "99.11.01.001.2", true);
            DesignReviewDto held = await Held(h, ReviewDto(plan, DesignReviewType.Verification, new SaveDesignOutputDto { Kind = DesignOutputKind.Drawing, RevisionId = d.Revision }));
            Assert.IsFalse(string.IsNullOrEmpty(held.Outputs.Single().ContentHash), "the drawing output carries the fingerprint of its files");
            await Judge(h, held);
            Assert.IsTrue((await Close(h, held.Id)).Success);
            Assert.IsNull(await ReleaseOfDrawing(h, d.Revision), "covered");
            Guid fid = Guid.Empty;
            h.Write(u => { var doc = new Entities.Model.Document("files/" + Guid.NewGuid().ToString("N") + ".dwg", "application/octet-stream"); u.Set<Entities.Model.Document>().Add(doc); u.SaveChanges(); fid = doc.Id; }, Seeder);
            Assert.IsTrue((await h.As(Dcc, p => p.GetRequiredService<IEdmsService>().AddFileAsync(new PcRevisionFileAddDto { RevisionId = d.Revision, DocumentId = fid, FileName = "b.dwg", Role = PcRevisionFileRole.Native }, Ct))).Success);
            string changed = await ReleaseOfDrawing(h, d.Revision);
            Assert.IsNotNull(changed);
            StringAssert.Contains(changed, "تغییر کرده");
            StringAssert.Contains(changed, "DWG-");
            // a verification of the new file set restores it; the revision rejected to draft afterwards is stale again
            DesignReviewDto held2 = await Held(h, ReviewDto(plan, DesignReviewType.Verification, new SaveDesignOutputDto { Kind = DesignOutputKind.Drawing, RevisionId = d.Revision }));
            await Judge(h, held2);
            Assert.IsTrue((await Close(h, held2.Id)).Success);
            Assert.IsNull(await ReleaseOfDrawing(h, d.Revision));
            foreach (var step in new[] { (Dcc, "submit"), (Reviewer, "reject") })
            {
                var r = await h.As(step.Item1, p => p.GetRequiredService<IEdmsService>().ActionAsync(new PcRevisionActionDto { Id = d.Revision, Action = step.Item2, Note = "برگشت برای اصلاح" }, Ct));
                Assert.IsTrue(r.Success, step.Item2 + ": " + r.Message);
            }
            StringAssert.Contains(await ReleaseOfDrawing(h, d.Revision) ?? string.Empty, "پیش‌نویس");
        }

        [TestMethod]
        public async Task ADrawingOrAnOutputWithNoPassedCheckIsNotVerifiedAndOneCheckDoesNotCoverEveryOutput()
        {
            AppHost h = NewHost("zerochecks");
            var (product, plan, bom) = await Ready(h);
            var d = await DrawingRevision(h, "99.11.01.001.2", true);
            DesignReviewDto held = await Held(h, ReviewDto(plan, DesignReviewType.Verification, BomOut(bom), new SaveDesignOutputDto { Kind = DesignOutputKind.Drawing, RevisionId = d.Revision }));
            Assert.AreEqual(2, held.Outputs.Count);
            // every input is judged against the BOM only: the drawing has no check at all
            var rows = held.Inputs.Select(i => new SaveDesignTraceRowDto { InputId = i.InputId, OutputId = held.Outputs.First(o => o.Kind == DesignOutputKind.Bom).Id, Result = DesignTraceResult.Passed, Evidence = "اندازه‌گیری مطابق معیار", EvidenceRef = "QR-1" }).ToList();
            Assert.IsTrue((await Reviews(h, Qa, s => s.SaveTraceAsync(new SaveDesignTraceDto { ReviewId = held.Id, Rows = rows }, Ct))).Success);
            var refused = await Close(h, held.Id);
            Assert.IsFalse(refused.Success, "the drawing has no passed check");
            StringAssert.Contains(refused.Message, "هیچ معیار قبول‌شده‌ای");
            // the drawing gets one passed check against one input: the review closes and the drawing is verified (an input needs a check against the BOM, a drawing needs at least one)
            var one = new List<SaveDesignTraceRowDto> { new SaveDesignTraceRowDto { InputId = held.Inputs.First().InputId, OutputId = held.Outputs.First(o => o.Kind == DesignOutputKind.Drawing).Id, Result = DesignTraceResult.Passed, Evidence = "بررسی نقشه", EvidenceRef = "QR-2" } };
            Assert.IsTrue((await Reviews(h, Qa, s => s.SaveTraceAsync(new SaveDesignTraceDto { ReviewId = held.Id, Rows = one }, Ct))).Success);
            Assert.IsTrue((await Close(h, held.Id)).Success);
            Assert.IsNull(await ReleaseOfDrawing(h, d.Revision));
        }

        [TestMethod]
        public async Task ADrawingThatIsNoPartOfTheStructureOfTheProductIsRefusedInTheReview()
        {
            AppHost h = NewHost("structure");
            var (product, plan, bom) = await Ready(h);
            var outside = await DrawingRevision(h, "55.55.55.555.2", true);
            var refused = await Reviews(h, Maker, s => s.SaveAsync(ReviewDto(plan, DesignReviewType.Verification, new SaveDesignOutputDto { Kind = DesignOutputKind.Drawing, RevisionId = outside.Revision }), Ct));
            Assert.IsFalse(refused.Success);
            StringAssert.Contains(refused.Message, "ساختار BOM");
            var unlinked = await DrawingRevision(h, "x", false);
            Assert.IsFalse((await Reviews(h, Maker, s => s.SaveAsync(ReviewDto(plan, DesignReviewType.Verification, new SaveDesignOutputDto { Kind = DesignOutputKind.Drawing, RevisionId = unlinked.Revision }), Ct))).Success, "a document linked to no part is no output of the design");
        }

        [TestMethod]
        public async Task ADrawingStaysADesignOutputAfterItsLinkIsRemovedAndRemovingTheLinkNeedsASecondPerson()
        {
            AppHost h = NewHost("sticky");
            var (product, plan, bom) = await Ready(h);
            var d = await DrawingRevision(h, "99.11.01.001.2", true);
            int link = h.Read(u => u.Set<PcDocumentLink>().Where(l => l.DeliverableId == d.Deliverable).Select(l => l.Id).Single());
            Assert.IsFalse((await h.As(Dcc, p => p.GetRequiredService<IEdmsService>().VoidLinkAsync(new PcDocLinkVoidDto { Id = link, Reason = "پیوند اشتباه ثبت شده بود" }, Ct))).Success, "the link of a part drawing is removed by engineering or QA, not by the person who made it");
            Assert.IsFalse((await h.As(Mgr, p => p.GetRequiredService<IEdmsService>().VoidLinkAsync(new PcDocLinkVoidDto { Id = link, Reason = "کوتاه" }, Ct))).Success, "a real reason of at least the minimum is needed");
            var removed = await h.As(Mgr, p => p.GetRequiredService<IEdmsService>().VoidLinkAsync(new PcDocLinkVoidDto { Id = link, Reason = "پیوند به قطعه اشتباه ثبت شده بود" }, Ct));
            Assert.IsTrue(removed.Success, removed.Message);
            var release = await ReleaseDrawing(h, d.Revision);
            Assert.IsFalse(release.Success, "unlink - release without a verification is no way round the gate");
            StringAssert.Contains(release.Message, "تایید طراحی");
        }

        [TestMethod]
        public async Task ASubAssemblyBomWithoutAProductIsVerifiedUnderTheProductThatContainsItAndTheStartDayIsRecorded()
        {
            AppHost h = NewHost("subbom");
            var (product, plan, bom) = await Ready(h);
            var sub = await Boms(h, Eng1, s => s.SaveAsync(new SavePbBomDto
            {
                AssemblyPartNumber = "99.11.01.001.2", Title = "sub", Lines = new List<SavePbBomLineDto> { new SavePbBomLineDto { ItemNumber = 1, PartNumber = "99.11.09.001.2", PartName = "Screw", Qty = 4, Unit = "عدد", Material = "ST37", MakeOrBuy = PbMakeOrBuy.Make, NetWeight = 1m, GrossWeight = 1.1m } },
            }, Ct));
            Assert.IsTrue(sub.Success, sub.Message);
            Assert.IsTrue((await Boms(h, Eng1, s => s.SubmitAsync(new PbBomActionDto { Id = sub.Data.Id }, Ct))).Success);
            foreach (var (who, step) in new[] { (Eng2, PbBomSignature.Checked), (Qc, PbBomSignature.QcChecked), (Shop, PbBomSignature.ManufacturingChecked), (Pmo, PbBomSignature.Approved) })
                Assert.IsTrue((await Boms(h, who, x => x.SignAsync(new PbSignDto { Id = sub.Data.Id, Step = step }, Ct))).Success);
            StringAssert.Contains(await Release(h, sub.Data.Id) ?? string.Empty, "هیچ بازبینی");
            DesignReviewDto held = await Held(h, ReviewDto(plan, DesignReviewType.Verification, BomOut(sub.Data.Id)));
            await Judge(h, held);
            Assert.IsTrue((await Close(h, held.Id)).Success);
            Assert.IsNull(await Release(h, sub.Data.Id), "a BOM without a product is covered by a verification that lists it");
            var released = await Boms(h, Pmo2, s => s.ReleaseAsync(new PbBomActionDto { Id = sub.Data.Id }, Ct));
            Assert.IsTrue(released.Success, released.Message);
            Assert.AreEqual(1, h.Read(u => u.Set<PbDesignActivation>().Count(a => !a.Deleted)), "the start day of the design control is recorded once, by the first gate decision");
            var other = await Boms(h, Eng1, s => s.SaveAsync(new SavePbBomDto { AssemblyPartNumber = "11.22.33.444.2", Title = "unrelated", Lines = new List<SavePbBomLineDto> { new SavePbBomLineDto { ItemNumber = 1, PartNumber = "11.22.33.445.2", PartName = "P", Qty = 1, Unit = "عدد", Material = "ST37", MakeOrBuy = PbMakeOrBuy.Make, NetWeight = 1m, GrossWeight = 1m } } }, Ct));
            Assert.IsTrue(other.Success, other.Message);
            Assert.IsTrue((await Boms(h, Eng1, s => s.SubmitAsync(new PbBomActionDto { Id = other.Data.Id }, Ct))).Success);
            Assert.IsFalse((await Reviews(h, Maker, s => s.SaveAsync(ReviewDto(plan, DesignReviewType.Verification, BomOut(other.Data.Id)), Ct))).Success, "a BOM outside the structure of the product is refused");
        }

        [TestMethod]
        public async Task AReviewUnderASupersededPlanNoLongerOpensTheGateAndANewVersionStartsFromScratch()
        {
            AppHost h = NewHost("supersede");
            var (product, plan, bom) = await Ready(h);
            await AcceptedVerification(h, plan, bom);
            Assert.IsNull(await Release(h, bom));
            var v2 = await Plans(h, Maker, s => s.ActionAsync(new DesignPlanActionDto { Id = plan, Action = "new-version" }, Ct));
            Assert.IsTrue(v2.Success, v2.Message);
            Assert.IsTrue(v2.Data.Stages.All(x => x.Status == DesignStageStatus.NotStarted), "the stages of a new version start again");
            Assert.IsTrue((await Plans(h, Maker, s => s.ActionAsync(new DesignPlanActionDto { Id = v2.Data.Id, Action = "submit" }, Ct))).Success);
            Assert.IsTrue((await Plans(h, Qa, s => s.ActionAsync(new DesignPlanActionDto { Id = v2.Data.Id, Action = "approve" }, Ct))).Success);
            string problem = await Release(h, bom);
            Assert.IsNotNull(problem, "the review belongs to the superseded plan");
            StringAssert.Contains(problem, "برنامه طراحی");
        }

        [TestMethod]
        public async Task AConflictingInputIsNotVoidedByItsCreatorAndEveryTextIsBoundedByItsColumn()
        {
            AppHost h = NewHost("limits");
            var (product, plan, bom) = await Ready(h);
            var input = (await Inputs(h, Maker, s => s.SaveAsync(In(product, DesignInputKind.Functional, "الزام آزمایشی تعارض"), Ct))).Data;
            Assert.IsTrue((await Inputs(h, Maker, s => s.ActionAsync(new DesignInputActionDto { Id = input.Id, Action = "conflict", Note = "با الزام دیگر ناسازگار است و باید بررسی شود" }, Ct))).Success);
            var voided = await Inputs(h, Maker, s => s.ActionAsync(new DesignInputActionDto { Id = input.Id, Action = "void", Note = "می‌خواهم ورودی را کنار بگذارم بدون رفع تعارض" }, Ct));
            Assert.IsFalse(voided.Success, "a conflict is resolved by a second person first");
            StringAssert.Contains(voided.Message, "تعارض");
            string tooLong = new string('ا', 1001);
            Assert.IsFalse((await Inputs(h, Mgr, s => s.ActionAsync(new DesignInputActionDto { Id = input.Id, Action = "resolve", Note = tooLong }, Ct))).Success);
            DesignReviewDto held = await Held(h, ReviewDto(plan, DesignReviewType.Verification, BomOut(bom)));
            var evidenceRef = await Reviews(h, Qa, s => s.SaveTraceAsync(new SaveDesignTraceDto { ReviewId = held.Id, Rows = new List<SaveDesignTraceRowDto> { new SaveDesignTraceRowDto { InputId = held.Inputs.First().InputId, OutputId = held.Outputs.First().Id, Result = DesignTraceResult.Passed, Evidence = "اندازه‌گیری مطابق معیار", EvidenceRef = new string('Q', 301) } } }, Ct));
            Assert.IsFalse(evidenceRef.Success, "the reference of the evidence is bounded by its column (300)");
            var voidReview = await Reviews(h, Mgr, s => s.ActionAsync(new DesignReviewActionDto { Id = held.Id, Action = "void", Note = tooLong }, Ct));
            Assert.IsFalse(voidReview.Success);
        }

        [TestMethod]
        public async Task ABomReleasedBeforeTheCutOverIsLegacyAndShownUntilTwoPeopleAdoptItsDesign()
        {
            AppHost h = NewHost("legacy", false);
            int product = await NewProduct(h);
            int bom = await ApprovedBom(h, product);
            Assert.IsTrue((await Boms(h, Pmo2, s => s.ReleaseAsync(new PbBomActionDto { Id = bom }, Ct))).Success, "released before the design control existed");
            h.WithDesignControl();
            h.Write(u => { foreach (PbDesignActivation old in u.Set<PbDesignActivation>().ToList()) u.Set<PbDesignActivation>().Remove(old); u.SaveChanges(); AppHost.Add(u, new PbDesignActivation { StartDate = DateTime.Today.AddDays(1), Note = "cut-over after the release" }); AppHost.Add(u, new PbProjectBaseline { WorkOrderId = 801, ProductId = product, BomHeaderId = bom, SourceBomCode = "B", SourceBomRevision = 0, IsCurrent = true, CopiedAt = DateTimeOffset.Now }); u.SaveChanges(); }, Seeder);
            DesignReadinessResult ready = await h.Read2(async u => await DesignGate.ReadinessAsync(u, await u.Set<PbBomHeader>().AsNoTracking().FirstAsync(x => x.Id == bom), Ct));
            Assert.IsTrue(ready.Legacy);
            Assert.AreEqual(1, ready.Problems.Count);
            StringAssert.Contains(ready.Problems[0], "قدیمی");
            DesignFcaResult fca = await h.Read2(u => DesignGate.FcaAsync(u, 801, Ct));
            Assert.IsFalse(fca.Applicable, "legacy: not applicable, with the note");
            Assert.IsTrue(fca.Legacy);
            StringAssert.Contains(fca.NotApplicableReason, "قدیمی");
            // adoption: engineering asks, QA (a second person) accepts
            Assert.IsFalse((await h.As(Maker, p => p.GetRequiredService<IDesignAdoptionService>().RequestAsync(new SaveDesignAdoptionDto { BomHeaderId = bom, Reason = "طراحی سال‌ها در بهره‌برداری است", EvidenceRef = "گزارش بهره‌برداری ۱۴۰۳" }, Ct))).Success, "only ENGINEERING asks");
            var asked = await h.As(Mgr, p => p.GetRequiredService<IDesignAdoptionService>().RequestAsync(new SaveDesignAdoptionDto { BomHeaderId = bom, Reason = "طراحی سال‌ها در بهره‌برداری است", EvidenceRef = "گزارش بهره‌برداری ۱۴۰۳" }, Ct));
            Assert.IsTrue(asked.Success, asked.Message);
            Assert.AreEqual(DesignAdoptionStatus.Requested, asked.Data.Status);
            Assert.IsFalse((await h.As(Mgr, p => p.GetRequiredService<IDesignAdoptionService>().ActionAsync(new DesignAdoptionActionDto { Id = asked.Data.AdoptionId.Value, Action = "accept" }, Ct))).Success, "not by the person who asked, nor by a non-QA");
            Assert.IsFalse((await h.As(Maker, p => p.GetRequiredService<IDesignAdoptionService>().ActionAsync(new DesignAdoptionActionDto { Id = asked.Data.AdoptionId.Value, Action = "accept" }, Ct))).Success);
            var accepted = await h.As(Qa, p => p.GetRequiredService<IDesignAdoptionService>().ActionAsync(new DesignAdoptionActionDto { Id = asked.Data.AdoptionId.Value, Action = "accept", Note = "مدارک بررسی شد" }, Ct));
            Assert.IsTrue(accepted.Success, accepted.Message);
            Assert.AreEqual(DesignAdoptionStatus.Accepted, accepted.Data.Status);
            ready = await h.Read2(async u => await DesignGate.ReadinessAsync(u, await u.Set<PbBomHeader>().AsNoTracking().FirstAsync(x => x.Id == bom), Ct));
            Assert.AreEqual(0, ready.Problems.Count, "the adopted design is complete");
            fca = await h.Read2(u => DesignGate.FcaAsync(u, 801, Ct));
            Assert.IsTrue(fca.Applicable);
            Assert.IsTrue(fca.Passed);
            // a BOM released after the cut-over cannot be adopted: it needs the full verification
            var voidAdoption = await h.As(Qa, p => p.GetRequiredService<IDesignAdoptionService>().ActionAsync(new DesignAdoptionActionDto { Id = asked.Data.AdoptionId.Value, Action = "void", Note = "پذیرش با مدرک بهتر جایگزین می‌شود" }, Ct));
            Assert.IsTrue(voidAdoption.Success, voidAdoption.Message);
            h.Write(u => { PbBomHeader x = u.Set<PbBomHeader>().First(y => y.Id == bom); x.ReleasedAt = DateTimeOffset.Now.AddDays(3); u.SaveChanges(); }, Seeder);
            var late = await h.As(Mgr, p => p.GetRequiredService<IDesignAdoptionService>().RequestAsync(new SaveDesignAdoptionDto { BomHeaderId = bom, Reason = "طراحی سال‌ها در بهره‌برداری است", EvidenceRef = "گزارش بهره‌برداری ۱۴۰۳" }, Ct));
            Assert.IsFalse(late.Success);
            StringAssert.Contains(late.Message, "پس از آغاز");
        }

        [TestMethod]
        public void TheCutOverDayIsASettingOnTheSafetyProfileAndConvertsFromTheShamsiNumber()
        {
            Assert.IsNotNull(SafetyProfile.Find(SafetyStore.Quality, DesignSettingKeys.ControlStartDate));
            Assert.IsTrue(SafetyProfile.IsWeaker(SafetyProfile.Find(SafetyStore.Quality, DesignSettingKeys.ControlStartDate), "14050101"), "moving the cut-over is a weakening");
            Assert.IsFalse(SafetyProfile.IsWeaker(SafetyProfile.Find(SafetyStore.Quality, DesignSettingKeys.ControlStartDate), "0"));
            Assert.AreEqual("1405/01/01", DesignGate.ShamsiText(DesignGate.ShamsiDay(14050101m).Value), "the Shamsi number converts to a day and back");
            Assert.IsNull(DesignGate.ShamsiDay(0m));
            Assert.IsNull(DesignGate.ShamsiDay(14051399m), "an invalid day is read as not set");
        }

        private static Task<DesignFcaResult> DesignGateOffFca(AppHost h, int wo) => h.Read2(u => DesignGate.FcaAsync(u, wo, Ct));
    }

    internal static class AppHostA1Extensions
    {
        /// <summary>Reads asynchronously with a new scope and no user.</summary>
        public static async Task<T> Read2<T>(this AppHost host, Func<DataLayer.Context.IUnitOfWork, Task<T>> read)
        {
            using (IServiceScope scope = host.Provider.CreateScope())
            {
                host.Http.HttpContext = null;
                return await read(scope.ServiceProvider.GetRequiredService<DataLayer.Context.IUnitOfWork>());
            }
        }
    }
}
