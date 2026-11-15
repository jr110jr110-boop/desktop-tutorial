using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Providers;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace MsTests
{
    /// <summary>Priority 4 (phases 4a and 4b): the resource pool, the standard rates, the resource plan and its assignments, the budget view and the part of the baseline.</summary>
    [TestClass]
    public class PcResourceServiceTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");
        private static readonly ClaimsPrincipal Planner = AppHost.Principal(1051, "planner", "PLANNING");
        private static readonly ClaimsPrincipal Planner2 = AppHost.Principal(1052, "planner.two", "PLANNING");
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(1011, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Manager = AppHost.Principal(1081, "project.manager", "PROJECTMANAGER");
        private static readonly ClaimsPrincipal FinMaker = AppHost.Principal(1201, "fin.one", "FINANCEMANAGER");
        private static readonly ClaimsPrincipal FinSecond = AppHost.Principal(1202, "fin.two", "FINANCEMANAGER");
        private static readonly ClaimsPrincipal Engineer = AppHost.Principal(1301, "engineer", "ENGINEER");
        private static readonly ClaimsPrincipal Payroll = AppHost.Principal(1401, "payroll", "PAYROLL");
        private static readonly ClaimsPrincipal Outsider = AppHost.Principal(1501, "outsider", "OPERATOR");

        private sealed class Env
        {
            public AppHost Host;
            public int Contract;
            public List<int> Activities = new List<int>();
        }

        private static Env NewEnv(string name, params (string Code, int Days, string Unit)[] activities)
        {
            var e = new Env { Host = new AppHost("PcRes_" + name + "_" + Guid.NewGuid().ToString("N")) };
            e.Host.Write(u =>
            {
                ContractEntity c = AppHost.Add(u, new ContractEntity { Code = "C-700", Title = "پروژه منابع", Employer = "کارفرما", Status = ContractStatus.Current, ContractDate = DateTimeOffset.Now.AddDays(-30), StartDate = DateTimeOffset.Now.Date, Price = 1_000_000_000m });
                u.SaveChanges();
                AppHost.Add(u, new PcContractTerms { ContractId = c.Id, CurrencyCode = "IRR", ValueExVat = 1_000_000_000m, ScheduleStartDate = DateTime.Today, ContractDeliveryDate = DateTime.Today.AddDays(100), ProjectManagerUserId = 1081 });
                foreach (var a in activities)
                {
                    PcActivity act = AppHost.Add(u, new PcActivity { ContractId = c.Id, Code = a.Code, Title = "فعالیت " + a.Code, DurationDays = a.Days, ResponsibleUnit = a.Unit });
                    u.SaveChanges();
                    e.Activities.Add(act.Id);
                }
                u.SaveChanges();
                e.Contract = c.Id;
            }, Seeder);
            return e;
        }

        private static T Svc<T>(Env e, ClaimsPrincipal who, Func<IPcResourceService, Task<T>> body) => e.Host.As(who, s => body(s.GetRequiredService<IPcResourceService>())).GetAwaiter().GetResult();

        private static PcResourceDto Res(string code, PcResourceKind kind = PcResourceKind.Labour, PcResourceUnit unit = PcResourceUnit.Hour, decimal capacity = 3m, string unitRole = "QC") =>
            new PcResourceDto { Code = code, Title = "منبع " + code, Kind = kind, Unit = unit, Capacity = capacity, ResponsibleUnit = unitRole };

        private static PcResourceDto Save(Env e, PcResourceDto dto, ClaimsPrincipal who = null)
        {
            ResponseDto<PcResourceDto> r = Svc(e, who ?? Planner, s => s.SaveResourceAsync(dto, Ct));
            Assert.IsTrue(r.Success, dto.Code + ": " + r.Message);
            return r.Data;
        }

        /// <summary>Proposes, submits and approves a rate (two different finance people).</summary>
        private static void ApprovedRate(Env e, int resourceId, decimal rate, DateTime from)
        {
            ResponseDto<PcResourceRateDto> p = Svc(e, FinMaker, s => s.SaveRateAsync(new PcResourceRateSaveDto { ResourceId = resourceId, Rate = rate, EffectiveFrom = from }, Ct));
            Assert.IsTrue(p.Success, p.Message);
            Assert.IsTrue(Svc(e, FinMaker, s => s.RateActionAsync(new PcResourceRateActionDto { Id = p.Data.Id, Action = "submit" }, Ct)).Success);
            ResponseDto<PcResourceRateDto> a = Svc(e, FinSecond, s => s.RateActionAsync(new PcResourceRateActionDto { Id = p.Data.Id, Action = "approve" }, Ct));
            Assert.IsTrue(a.Success, a.Message);
        }

        private static string Fail<T>(ResponseDto<T> r) { Assert.IsFalse(r.Success, "should have been refused"); return r.Message; }

        // ------------------------------------------------------------------------------------------------ pure rules

        [TestMethod]
        public void The_Draft_Rule_Matches_The_Unit_Of_The_Activity_And_Counts_Hours_By_The_Day_Length()
        {
            var acts = new List<PcResourceRules.DraftActivity>
            {
                new PcResourceRules.DraftActivity { Id = 1, DurationDays = 4, ResponsibleUnit = "qc", DayHours = 7.5m },
                new PcResourceRules.DraftActivity { Id = 2, DurationDays = 3, ResponsibleUnit = "QC", DayHours = 8m },
                new PcResourceRules.DraftActivity { Id = 3, DurationDays = 2, ResponsibleUnit = "WAREHOUSE", DayHours = 7.5m },
                new PcResourceRules.DraftActivity { Id = 4, DurationDays = 0, ResponsibleUnit = "QC", DayHours = 7.5m },
                new PcResourceRules.DraftActivity { Id = 5, DurationDays = 2, ResponsibleUnit = null, DayHours = 7.5m },
                new PcResourceRules.DraftActivity { Id = 6, DurationDays = 5, ResponsibleUnit = "QC", DayHours = 0m, Classes = new HashSet<PcResourceClass> { PcResourceClass.Labour, PcResourceClass.Equipment } },
            };
            var pool = new List<PcResourceRules.DraftResource>
            {
                new PcResourceRules.DraftResource { Id = 20, Code = "B-LAB", Kind = PcResourceKind.Labour, Unit = PcResourceUnit.Hour, ResponsibleUnit = "QC" },
                new PcResourceRules.DraftResource { Id = 10, Code = "A-LAB", Kind = PcResourceKind.Labour, Unit = PcResourceUnit.Day, ResponsibleUnit = "QC" },
                new PcResourceRules.DraftResource { Id = 30, Code = "M-1", Kind = PcResourceKind.Machine, Unit = PcResourceUnit.Hour, ResponsibleUnit = "QC" },
            };
            List<PcResourceRules.DraftRow> rows = PcResourceRules.Draft(acts, pool);
            // the first resource by code (A-LAB, counted in days) serves the labour of QC; activity 3 has no WAREHOUSE resource and activities 4 and 5 have no work or no unit
            CollectionAssert.AreEqual(new[] { 1, 2, 6, 6 }, rows.Select(r => r.ActivityId).ToArray());
            Assert.AreEqual(4m, rows[0].Quantity);
            Assert.AreEqual(3m, rows[1].Quantity);
            Assert.AreEqual(5m, rows.First(r => r.ActivityId == 6 && r.ResourceId == 10).Quantity);
            Assert.AreEqual(5m * 7.5m, rows.First(r => r.ActivityId == 6 && r.ResourceId == 30).Quantity, "a machine counted in hours: days times 7.5 when no calendar says otherwise");
            Assert.AreEqual(7.5m, PcResourceRules.DayHours(null, null));
            Assert.AreEqual(8m, PcResourceRules.DayHours(null, 8m));
            Assert.AreEqual(6m, PcResourceRules.DayHours(6m, 8m));
            Assert.AreEqual(PcResourceClass.Equipment, PcResourceRules.ClassOf(PcResourceKind.Machine));
        }

        // ------------------------------------------------------------------------------------------------ 4a: the pool

        [TestMethod]
        public void A_Resource_Is_Validated_And_The_Link_Must_Match_Its_Kind()
        {
            Env e = NewEnv("pool");
            Assert.IsTrue(Fail(Svc(e, Engineer, s => s.SaveResourceAsync(Res("R1"), Ct))).Contains("مجوز"), "only planning and the PMO write");
            Assert.IsTrue(Svc(e, Pmo, s => s.SaveResourceAsync(Res("R-PMO"), Ct)).Success);
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.SaveResourceAsync(Res(""), Ct))).Contains("کد"));
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.SaveResourceAsync(Res("R2", capacity: 0m), Ct))).Contains("ظرفیت"));
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.SaveResourceAsync(Res("R2", capacity: -1m), Ct))).Contains("ظرفیت"));
            PcResourceDto bad = Res("R3", kind: PcResourceKind.Machine); bad.EmployeeId = 1;
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.SaveResourceAsync(bad, Ct))).Contains("کارمند"), "an employee link is for labour only");
            PcResourceDto two = Res("R4", kind: PcResourceKind.Subcontract); two.ProviderId = 1; two.WorkCenterId = 1;
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.SaveResourceAsync(two, Ct))).Contains("حداکثر به یک"));
            PcResourceDto wc = Res("R5", kind: PcResourceKind.Labour); wc.WorkCenterId = 1;
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.SaveResourceAsync(wc, Ct))).Contains("مرکز کار"), "a work centre link is for a machine");
            PcResourceDto missing = Res("R6", kind: PcResourceKind.Machine); missing.WorkCenterId = 9999;
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.SaveResourceAsync(missing, Ct))).Contains("مرکز کار پیدا نشد"));

            int wcId = 0, providerId = 0;
            e.Host.Write(u => { wcId = AppHost.Add(u, new MfWorkCenter { Code = "WC-9", Title = "تراش", IsActive = true }).Id; providerId = AppHost.Add(u, new Provider { Name = "پیمانکار جوش" }).Id; u.SaveChanges(); wcId = u.Set<MfWorkCenter>().Single(w => w.Code == "WC-9").Id; providerId = u.Set<Provider>().Single(p => p.Name == "پیمانکار جوش").Id; }, Seeder);
            PcResourceDto machine = Res("LATHE", kind: PcResourceKind.Machine, capacity: 1m); machine.WorkCenterId = wcId;
            Assert.AreEqual(wcId, Save(e, machine).WorkCenterId);
            PcResourceDto sub = Res("SUB", kind: PcResourceKind.Subcontract, unit: PcResourceUnit.Each, capacity: 1m); sub.ProviderId = providerId;
            Assert.AreEqual(providerId, Save(e, sub).ProviderId);

            PcResourceDto dup = Res("LATHE");
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.SaveResourceAsync(dup, Ct))).Contains("تکراری"), "the code is unique");

            // deactivate and activate; the list is ordered by code and filtered
            PcResourceDto role = Save(e, Res("WELDER", capacity: 3m));
            Assert.IsFalse(Svc(e, Planner, s => s.SetResourceActiveAsync(role.Id, false, Ct)).Data.IsActive);
            Assert.IsTrue(Svc(e, Planner, s => s.SetResourceActiveAsync(role.Id, true, Ct)).Data.IsActive);
            Svc(e, Planner, s => s.SetResourceActiveAsync(role.Id, false, Ct));
            List<string> active = Svc(e, Engineer, s => s.GetResourcesAsync(new PcResourceQuery { IsActive = true, PageSize = 50 }, Ct)).Data.Select(r => r.Code).ToList();
            CollectionAssert.AreEqual(new[] { "LATHE", "R-PMO", "SUB" }, active);
            Assert.IsTrue(Fail(Svc(e, Outsider, s => s.GetResourcesAsync(new PcResourceQuery(), Ct))).Contains("مجوز"));
        }

        [TestMethod]
        public void A_Resource_With_An_Assignment_Cannot_Be_Deleted_But_Can_Be_Deactivated()
        {
            Env e = NewEnv("delete", ("A1", 2, "QC"));
            PcResourceDto welder = Save(e, Res("WELDER"));
            PcResourceDto spare = Save(e, Res("SPARE"));
            Assert.IsTrue(Svc(e, Planner, s => s.DeleteResourceAsync(spare.Id, Ct)).Data, "no assignment: deleted");
            PcResourcePlanDto plan = Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct)).Data;
            Assert.IsTrue(Svc(e, Planner, s => s.SaveAssignmentsAsync(new PcActivityAssignmentsDto { PlanId = plan.Id, ActivityId = e.Activities[0], Lines = new List<PcAssignmentLineDto> { new PcAssignmentLineDto { ResourceId = welder.Id, Quantity = 15m } } }, Ct)).Success);
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.DeleteResourceAsync(welder.Id, Ct))).Contains("غیرفعال"));
            Assert.IsFalse(Svc(e, Planner, s => s.SetResourceActiveAsync(welder.Id, false, Ct)).Data.IsActive);
            // the kind and the unit of a resource that a plan uses do not change
            welder.Kind = PcResourceKind.Equipment;
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.SaveResourceAsync(welder, Ct))).Contains("تغییر نمی‌کند"));
        }

        // ------------------------------------------------------------------------------------------------ 4a: the rates

        [TestMethod]
        public void A_Rate_Is_Never_Set_On_A_Resource_Linked_To_An_Employee_And_Such_A_Resource_Uses_The_Rate_Of_Its_Role()
        {
            Env e = NewEnv("privacy");
            int employee = 0;
            e.Host.Write(u => { employee = AppHost.Add(u, new FnEmployee { PersonnelCode = "E77", FirstName = "علی", LastName = "جوشکار", NationalCode = "0499370899", HireDate = new DateTime(2020, 1, 1), IsActive = true, CreatedAt = DateTimeOffset.Now }).Id; u.SaveChanges(); employee = u.Set<FnEmployee>().Single(x => x.PersonnelCode == "E77").Id; }, Seeder);
            PcResourceDto role = Save(e, Res("WELDER", capacity: 3m));
            PcResourceDto noRole = Res("ALI"); noRole.EmployeeId = employee;
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.SaveResourceAsync(noRole, Ct))).Contains("منبع نقش"), "an employee-linked resource names the role whose rate it uses");
            PcResourceDto person = Res("ALI", capacity: 1m); person.EmployeeId = employee; person.RateResourceId = role.Id;
            PcResourceDto saved = Save(e, person);
            Assert.IsFalse(saved.CanCarryRate);
            Assert.AreEqual("WELDER", saved.RateResourceCode);

            string refused = Fail(Svc(e, FinMaker, s => s.SaveRateAsync(new PcResourceRateSaveDto { ResourceId = saved.Id, Rate = 1_000m, EffectiveFrom = DateTime.Today }, Ct)));
            Assert.IsTrue(refused.Contains("محرمانگی"), refused);
            Assert.AreEqual(0, e.Host.Read(u => u.Set<PcResourceRate>().Count(r => r.ResourceId == saved.Id)));

            ApprovedRate(e, role.Id, 120_000m, DateTime.Today.AddDays(-1));
            Assert.AreEqual(120_000m, Svc(e, FinMaker, s => s.GetResourceAsync(saved.Id, Ct)).Data.EffectiveRate, "the person-linked resource takes the rate of the role, shown to a finance reader");
        }

        [TestMethod]
        public void A_Rate_Needs_A_Second_Person_And_Only_The_Approved_One_Is_Effective_From_Its_Date()
        {
            Env e = NewEnv("rates");
            PcResourceDto crane = Save(e, Res("CRANE", kind: PcResourceKind.Equipment, unit: PcResourceUnit.Day, capacity: 2m));
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.SaveRateAsync(new PcResourceRateSaveDto { ResourceId = crane.Id, Rate = 5m, EffectiveFrom = DateTime.Today }, Ct))).Contains("مجوز"), "planning does not propose rates");
            Assert.IsTrue(Fail(Svc(e, FinMaker, s => s.SaveRateAsync(new PcResourceRateSaveDto { ResourceId = crane.Id, Rate = -5m, EffectiveFrom = DateTime.Today }, Ct))).Contains("نرخ"));

            PcResourceRateDto draft = Svc(e, FinMaker, s => s.SaveRateAsync(new PcResourceRateSaveDto { ResourceId = crane.Id, Rate = 1_000_000m, EffectiveFrom = DateTime.Today.AddDays(-10) }, Ct)).Data;
            Assert.AreEqual(PcRateStatus.Draft, draft.Status);
            Assert.IsNull(Svc(e, FinMaker, s => s.GetResourceAsync(crane.Id, Ct)).Data.EffectiveRate, "a draft is not a rate");
            Assert.IsTrue(Fail(Svc(e, FinMaker, s => s.RateActionAsync(new PcResourceRateActionDto { Id = draft.Id, Action = "approve" }, Ct))).Contains("ارسال‌شده"), "not before it is submitted");
            Assert.IsTrue(Fail(Svc(e, FinSecond, s => s.RateActionAsync(new PcResourceRateActionDto { Id = draft.Id, Action = "submit" }, Ct))).Contains("پیشنهاددهنده"), "only the maker submits");
            Assert.IsTrue(Svc(e, FinMaker, s => s.RateActionAsync(new PcResourceRateActionDto { Id = draft.Id, Action = "submit" }, Ct)).Success);
            Assert.IsTrue(Fail(Svc(e, FinMaker, s => s.RateActionAsync(new PcResourceRateActionDto { Id = draft.Id, Action = "approve" }, Ct))).Contains("شخص دیگری"), "the maker cannot approve his own rate");
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.RateActionAsync(new PcResourceRateActionDto { Id = draft.Id, Action = "approve" }, Ct))).Contains("مجوز"));
            Assert.IsNull(Svc(e, FinMaker, s => s.GetResourceAsync(crane.Id, Ct)).Data.EffectiveRate);
            Assert.IsTrue(Fail(Svc(e, FinSecond, s => s.RateActionAsync(new PcResourceRateActionDto { Id = draft.Id, Action = "reject" }, Ct))).Contains("علت"), "a rejection needs a reason");

            // an AI seat never approves, even with the role
            Dictionary<string, AiActingUser> seats = e.Host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct)).GetAwaiter().GetResult();
            string seat = AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), seats["AG-FIN"], "AG-FIN",
                async sp => (await sp.GetRequiredService<IPcResourceService>().RateActionAsync(new PcResourceRateActionDto { Id = draft.Id, Action = "approve" }, Ct)).Message).GetAwaiter().GetResult();
            Assert.IsTrue(seat.Contains("هوش مصنوعی") || seat.Contains("مجوز"), seat);
            Assert.AreEqual(PcRateStatus.Submitted, e.Host.Read(u => u.Set<PcResourceRate>().Single(r => r.Id == draft.Id).Status));

            PcResourceRateDto approved = Svc(e, FinSecond, s => s.RateActionAsync(new PcResourceRateActionDto { Id = draft.Id, Action = "approve" }, Ct)).Data;
            Assert.AreEqual(PcRateStatus.Approved, approved.Status);
            Assert.AreEqual(1202, approved.ApprovedByUserId);
            Assert.AreEqual(1_000_000m, Svc(e, FinMaker, s => s.GetResourceAsync(crane.Id, Ct)).Data.EffectiveRate);

            // a later rate applies from its date only; a rejected one never applies
            ApprovedRate(e, crane.Id, 1_500_000m, DateTime.Today.AddDays(10));
            Assert.AreEqual(1_000_000m, Svc(e, FinMaker, s => s.GetResourceAsync(crane.Id, Ct)).Data.EffectiveRate, "today still the first rate");
            Assert.AreEqual(1_500_000m, e.Host.As(FinMaker, async sp => (await PcResourceData.EffectiveRatesAsync(sp.GetRequiredService<IUnitOfWork>(), new[] { crane.Id }, DateTime.Today.AddDays(11), Ct))[crane.Id]).GetAwaiter().GetResult());
            PcResourceRateDto third = Svc(e, FinMaker, s => s.SaveRateAsync(new PcResourceRateSaveDto { ResourceId = crane.Id, Rate = 9_000_000m, EffectiveFrom = DateTime.Today.AddDays(-1) }, Ct)).Data;
            Svc(e, FinMaker, s => s.RateActionAsync(new PcResourceRateActionDto { Id = third.Id, Action = "submit" }, Ct));
            PcResourceRateDto rejected = Svc(e, FinSecond, s => s.RateActionAsync(new PcResourceRateActionDto { Id = third.Id, Action = "reject", Note = "نرخ بالا است" }, Ct)).Data;
            Assert.AreEqual(PcRateStatus.Rejected, rejected.Status);
            Assert.AreEqual(1_000_000m, Svc(e, FinMaker, s => s.GetResourceAsync(crane.Id, Ct)).Data.EffectiveRate);
            Assert.IsTrue(Fail(Svc(e, FinMaker, s => s.SaveRateAsync(new PcResourceRateSaveDto { ResourceId = crane.Id, Rate = 1m, EffectiveFrom = DateTime.Today.AddDays(10) }, Ct))).Contains("تاریخ"), "one rate per resource and date");
        }

        [TestMethod]
        public void The_Rate_Of_An_Individual_Is_Masked_For_Readers_Who_May_Not_See_It()
        {
            Env e = NewEnv("mask");
            PcResourceDto lone = Save(e, Res("LONE-WELDER", capacity: 1m));
            PcResourceDto pair = Save(e, Res("PAIR-WELDER", capacity: 2m));
            PcResourceDto group = Save(e, Res("WELDER-GROUP", capacity: 3m));
            PcResourceDto machine = Save(e, Res("PRESS", kind: PcResourceKind.Machine, capacity: 1m));
            foreach (PcResourceDto r in new[] { lone, pair, group, machine }) ApprovedRate(e, r.Id, 100m, DateTime.Today.AddDays(-5));

            // the readers of payroll (MfPrivacy.SeesPayroll: PAYROLL, FINANCEMANAGER, CEO) see every rate; the PMO does not
            foreach (ClaimsPrincipal privileged in new[] { FinMaker, Payroll, AppHost.Principal(1601, "the.ceo", "CEO") })
            {
                List<PcResourceDto> seen = Svc(e, privileged, s => s.GetResourcesAsync(new PcResourceQuery { PageSize = 50 }, Ct)).Data.ToList();
                Assert.IsTrue(seen.All(r => r.EffectiveRate == 100m && !r.RateHidden), privileged.Identity.Name);
            }
            foreach (ClaimsPrincipal reader in new[] { Engineer, Pmo })
            {
                List<PcResourceDto> masked = Svc(e, reader, s => s.GetResourcesAsync(new PcResourceQuery { PageSize = 50 }, Ct)).Data.OrderBy(r => r.Code).ToList();
                foreach (string code in new[] { "LONE-WELDER", "PAIR-WELDER" })
                {
                    PcResourceDto l = masked.Single(r => r.Code == code);
                    Assert.IsNull(l.EffectiveRate, reader.Identity.Name + " " + code); Assert.IsTrue(l.RateHidden);
                }
                PcResourceDto g = masked.Single(r => r.Code == "WELDER-GROUP");
                Assert.AreEqual(100m, g.EffectiveRate, "a group of three (MinEmployees) does not show one person's cost"); Assert.IsFalse(g.RateHidden);
                Assert.AreEqual(100m, masked.Single(r => r.Code == "PRESS").EffectiveRate, "a machine is not a person");
            }
            PcResourceRateDto rate = Svc(e, Pmo, s => s.GetRatesAsync(new PcResourceRateQuery { ResourceId = lone.Id, PageSize = 10 }, Ct)).Data.Single();
            Assert.IsNull(rate.Rate); Assert.IsTrue(rate.RateHidden);
            Assert.AreEqual(100m, Svc(e, FinMaker, s => s.GetRatesAsync(new PcResourceRateQuery { ResourceId = lone.Id, PageSize = 10 }, Ct)).Data.Single().Rate);
        }

        // ------------------------------------------------------------------------------------------------ 4b: the plan

        private static PcResourcePlanDto Assign(Env e, ClaimsPrincipal who, int plan, int activity, int resource, decimal quantity)
        {
            ResponseDto<PcResourcePlanDto> r = Svc(e, who, s => s.SaveAssignmentsAsync(new PcActivityAssignmentsDto { PlanId = plan, ActivityId = activity, Lines = new List<PcAssignmentLineDto> { new PcAssignmentLineDto { ResourceId = resource, Quantity = quantity } } }, Ct));
            Assert.IsTrue(r.Success, r.Message);
            return r.Data;
        }

        /// <summary>The stored row version of a plan as the front end sends it back (null in the in-memory provider, where it is never generated).</summary>
        private static string RowVersionOf(Env e, int plan)
        {
            byte[] v = e.Host.Read(u => u.Set<PcResourcePlan>().AsNoTracking().Single(p => p.Id == plan).RowVersion);
            return v == null ? null : Convert.ToBase64String(v);
        }

        private static PcResourcePlanDto Act(Env e, ClaimsPrincipal who, int plan, string action, string note = null)
        {
            string version = RowVersionOf(e, plan); // read first: a read inside the call would replace the acting user of the host
            ResponseDto<PcResourcePlanDto> r = Svc(e, who, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = plan, Action = action, Note = note, RowVersion = version }, Ct));
            Assert.IsTrue(r.Success, action + ": " + r.Message);
            return r.Data;
        }

        [TestMethod]
        public void The_Plan_Goes_Through_Submit_And_The_Approval_Of_The_Project_Manager_Of_The_Contract_Only()
        {
            Env e = NewEnv("plan", ("A1", 2, "QC"), ("A2", 3, "QC"), ("A3", 1, "WAREHOUSE"));
            PcResourceDto welder = Save(e, Res("WELDER"));
            PcResourcePlanDto plan = Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct)).Data;
            Assert.AreEqual(1, plan.Version);
            Assert.AreEqual(PcResourcePlanStatus.Draft, plan.Status);
            Assert.AreEqual(3, plan.UnassignedCount);
            Assert.AreEqual(1081, plan.ApproverUserId);
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct))).Contains("طرح منابع باز"), "one open plan per contract");

            // validation of the lines
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.SaveAssignmentsAsync(new PcActivityAssignmentsDto { PlanId = plan.Id, ActivityId = e.Activities[0], Lines = new List<PcAssignmentLineDto> { new PcAssignmentLineDto { ResourceId = welder.Id, Quantity = 0m } } }, Ct))).Contains("بیشتر از صفر"));
            Assert.IsTrue(Fail(Svc(e, Engineer, s => s.SaveAssignmentsAsync(new PcActivityAssignmentsDto { PlanId = plan.Id, ActivityId = e.Activities[0], Lines = new List<PcAssignmentLineDto>() }, Ct))).Contains("مجوز"));
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.SaveAssignmentsAsync(new PcActivityAssignmentsDto { PlanId = plan.Id, ActivityId = 987654, Lines = new List<PcAssignmentLineDto>() }, Ct))).Contains("فعالیت"));

            // the submit is refused while an activity has neither a resource nor the mark
            Assign(e, Planner, plan.Id, e.Activities[0], welder.Id, 15m);
            Assign(e, Planner, plan.Id, e.Activities[1], welder.Id, 22.5m);
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = plan.Id, Action = "submit" }, Ct))).Contains("1 فعالیت"));
            PcResourcePlanDto marked = Svc(e, Planner, s => s.MarkUnassignedAsync(new PcMarkUnassignedDto { PlanId = plan.Id, AllRemaining = true }, Ct)).Data;
            CollectionAssert.AreEqual(new[] { e.Activities[2] }, marked.MarkedUnassignedActivityIds.ToArray());
            Assert.AreEqual(0, marked.UnassignedCount);
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.MarkUnassignedAsync(new PcMarkUnassignedDto { PlanId = plan.Id, ActivityIds = new List<int> { e.Activities[0] } }, Ct))).Contains("منبع دارد"));

            // submitting is for people; the planner who made the plan cannot approve it, nor can anybody who is not its manager
            Assert.IsTrue(Fail(Svc(e, Manager, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = plan.Id, Action = "approve" }, Ct))).Contains("ارسال‌شده"));
            Assert.AreEqual(PcResourcePlanStatus.Submitted, Act(e, Planner, plan.Id, "submit").Status);
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.SaveAssignmentsAsync(new PcActivityAssignmentsDto { PlanId = plan.Id, ActivityId = e.Activities[0], Lines = new List<PcAssignmentLineDto>() }, Ct))).Contains("پیش‌نویس یا برگشت"), "a submitted plan is not edited");
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = plan.Id, Action = "approve" }, Ct))).Contains("مدیر پروژه"));
            Assert.IsTrue(Fail(Svc(e, Pmo, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = plan.Id, Action = "approve" }, Ct))).Contains("مدیر پروژه"), "the contract has a manager: the PMO does not decide");
            Assert.IsTrue(Fail(Svc(e, AppHost.Principal(1082, "other.manager", "PROJECTMANAGER"), s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = plan.Id, Action = "approve" }, Ct))).Contains("مدیر پروژه"), "another project manager is not the manager of this contract");

            // a return needs a reason and sends the plan back to the planner
            Assert.IsTrue(Fail(Svc(e, Manager, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = plan.Id, Action = "return" }, Ct))).Contains("علت"));
            PcResourcePlanDto returned = Act(e, Manager, plan.Id, "return", "ظرفیت جوشکار کافی نیست");
            Assert.AreEqual(PcResourcePlanStatus.Returned, returned.Status);
            Assert.AreEqual("ظرفیت جوشکار کافی نیست", returned.ReturnReason);
            Assert.IsTrue(returned.Editable);
            Assign(e, Planner, plan.Id, e.Activities[0], welder.Id, 30m);
            Assert.AreEqual(PcResourcePlanStatus.Submitted, Act(e, Planner, plan.Id, "submit").Status);

            PcResourcePlanDto approved = Act(e, Manager, plan.Id, "approve");
            Assert.AreEqual(PcResourcePlanStatus.Approved, approved.Status);
            Assert.AreEqual(1081, approved.ApprovedByUserId);
            Assert.AreEqual(1051, approved.MakerUserId);
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.DeleteAssignmentAsync(approved.Assignments[0].Id, Ct))).Contains("پیش‌نویس یا برگشت"), "an approved plan is not edited");
            // an approved plan is not open any more: a new version may be opened (before the baseline)
            Assert.AreEqual(2, Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct)).Data.Version);
        }

        [TestMethod]
        public void A_Person_Who_Edits_The_Draft_Of_The_Agent_Becomes_Its_Maker_And_Cannot_Decide_It()
        {
            Env e = NewEnv("maker", ("A1", 2, "QC"));
            PcResourceDto welder = Save(e, Res("WELDER"));
            Dictionary<string, AiActingUser> seats = e.Host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct)).GetAwaiter().GetResult();
            int seatUser = seats["AG-PLAN"].UserId;

            // outside a step the agent cannot make a plan; inside RES.PLAN.DRAFT it does
            string outside = AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), seats["AG-PLAN"], "AG-PLAN",
                async sp => (await sp.GetRequiredService<IPcResourceService>().DraftPlanAsync(e.Contract, Ct)).Message).GetAwaiter().GetResult();
            Assert.AreEqual(PcResourceText.NotAllowed, outside);
            string agentOpen = AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), seats["AG-PLAN"], "AG-PLAN",
                async sp => (await sp.GetRequiredService<IPcResourceService>().OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct)).Message).GetAwaiter().GetResult();
            Assert.AreEqual(PcResourceText.NotAllowed, agentOpen, "OpenPlanAsync is the human way");

            ResponseDto<PcResourcePlanDto> drafted = AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), seats["AG-PLAN"], "AG-PLAN", async sp =>
            {
                using (global::Services.NasbNiro.Agents.AgentStepContext.Enter("RES.PLAN.DRAFT"))
                    return await sp.GetRequiredService<IPcResourceService>().DraftPlanAsync(e.Contract, Ct);
            }).GetAwaiter().GetResult();
            Assert.IsTrue(drafted.Success, drafted.Message);
            Assert.IsTrue(drafted.Data.PreparedByAgent);
            Assert.AreEqual(seatUser, drafted.Data.MakerUserId);
            Assert.AreEqual(1, drafted.Data.Assignments.Count);
            Assert.AreEqual(PcAssignmentSource.Agent, drafted.Data.Assignments[0].Source);
            Assert.AreEqual(2 * 7.5m, drafted.Data.Assignments[0].Quantity, "two days of 7.5 hours");
            Assert.AreEqual(0, drafted.Data.UnassignedCount);

            // the agent does not submit or approve
            string submit = AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), seats["AG-PLAN"], "AG-PLAN",
                async sp => (await sp.GetRequiredService<IPcResourceService>().PlanActionAsync(new PcResourcePlanActionDto { Id = drafted.Data.Id, Action = "submit" }, Ct)).Message).GetAwaiter().GetResult();
            Assert.AreEqual(PcResourceText.NotAllowed + " (ارسال طرح با برنامه‌ریز انسانی است)", submit);

            // a person changes a quantity: the person is the maker now, the plan still says it was prepared by the agent
            PcResourcePlanDto edited = Assign(e, Planner, drafted.Data.Id, e.Activities[0], welder.Id, 20m);
            Assert.AreEqual(1051, edited.MakerUserId);
            Assert.IsTrue(edited.PreparedByAgent);
            Assert.AreEqual(PcAssignmentSource.Human, edited.Assignments.Single().Source);
            Act(e, Planner, drafted.Data.Id, "submit");
            Assert.IsTrue(Fail(Svc(e, AppHost.Principal(1051, "planner", "PLANNING", "PROJECTMANAGER"), s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = drafted.Data.Id, Action = "approve" }, Ct))).Contains("مدیر پروژه"));
            // the manager is the one who edited: nobody but the PMO may decide, never the editor
            e.Host.Write(u => { u.Set<PcContractTerms>().Single(t => t.ContractId == e.Contract).ProjectManagerUserId = 1051; u.SaveChanges(); }, Seeder);
            Assert.AreEqual(PcResourceText.NotAllowed + " (مدیر پروژه همین قرارداد)", Fail(Svc(e, AppHost.Principal(1051, "planner", "PLANNING", "PROJECTMANAGER"), s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = drafted.Data.Id, Action = "approve" }, Ct))), "the maker never approves; the PMO decides");
            Assert.AreEqual(PcResourcePlanStatus.Approved, Act(e, Pmo, drafted.Data.Id, "approve").Status, "the PMO decides when the manager made the plan");
        }

        [TestMethod]
        public void The_Budget_View_Costs_The_Approved_Rates_Per_Resource_And_Class_And_Lists_The_Unrated_Lines()
        {
            Env e = NewEnv("budget", ("A1", 2, "QC"), ("A2", 3, "QC"));
            PcResourceDto crew = Save(e, Res("CREW", capacity: 4m));
            PcResourceDto lone = Save(e, Res("INSPECTOR", capacity: 1m));
            PcResourceDto machine = Save(e, Res("LATHE", kind: PcResourceKind.Machine, capacity: 1m));
            PcResourceDto crane = Save(e, Res("CRANE", kind: PcResourceKind.Equipment, unit: PcResourceUnit.Day, capacity: 1m));
            PcResourceDto steel = Save(e, Res("STEEL", kind: PcResourceKind.Material, unit: PcResourceUnit.Each, capacity: 1000m));
            ApprovedRate(e, crew.Id, 100m, DateTime.Today.AddDays(-3));
            ApprovedRate(e, lone.Id, 500m, DateTime.Today.AddDays(-3));
            ApprovedRate(e, machine.Id, 1_000m, DateTime.Today.AddDays(-3));
            ApprovedRate(e, crane.Id, 10_000m, DateTime.Today.AddDays(30)); // starts later: not effective today

            PcResourcePlanDto plan = Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct)).Data;
            Svc(e, Planner, s => s.SaveAssignmentsAsync(new PcActivityAssignmentsDto
            {
                PlanId = plan.Id, ActivityId = e.Activities[0], Lines = new List<PcAssignmentLineDto>
                {
                    new PcAssignmentLineDto { ResourceId = crew.Id, Quantity = 10m }, new PcAssignmentLineDto { ResourceId = lone.Id, Quantity = 4m }, new PcAssignmentLineDto { ResourceId = machine.Id, Quantity = 3m },
                },
            }, Ct));
            Svc(e, Planner, s => s.SaveAssignmentsAsync(new PcActivityAssignmentsDto
            {
                PlanId = plan.Id, ActivityId = e.Activities[1], Lines = new List<PcAssignmentLineDto>
                {
                    new PcAssignmentLineDto { ResourceId = crew.Id, Quantity = 5m }, new PcAssignmentLineDto { ResourceId = crane.Id, Quantity = 2m }, new PcAssignmentLineDto { ResourceId = steel.Id, Quantity = 7m },
                },
            }, Ct));

            PcResourceBudgetDto full = Svc(e, FinMaker, s => s.GetBudgetAsync(plan.Id, null, Ct)).Data;
            Assert.AreEqual(6, full.Lines.Count);
            Assert.AreEqual(2, full.NoRateCount, "the crane (rate not yet effective) and the steel (no rate)");
            Assert.IsTrue(full.Lines.Where(l => l.NoApprovedRate).All(l => l.Note == "بدون نرخ مصوب" && l.Amount == null));
            Assert.AreEqual(10m * 100m + 4m * 500m + 3m * 1_000m + 5m * 100m, full.Total);
            Assert.AreEqual(0, full.HiddenLineCount);
            Assert.AreEqual(1_500m, full.ByResource.Single(g => g.ResourceCode == "CREW").Amount);
            Assert.AreEqual(2, full.ByResource.Single(g => g.ResourceCode == "CREW").LineCount);
            Assert.AreEqual(1_500m + 2_000m, full.ByClass.Single(g => g.ResourceClass == PcResourceClass.Labour).Amount);
            Assert.AreEqual(3_000m, full.ByClass.Single(g => g.ResourceClass == PcResourceClass.Equipment).Amount, "a machine counts as equipment");
            Assert.IsFalse(full.ByClass.Any(g => g.ResourceClass == PcResourceClass.Material));

            // on a later day the crane has its rate
            PcResourceBudgetDto later = Svc(e, FinMaker, s => s.GetBudgetAsync(plan.Id, DateTime.Today.AddDays(31), Ct)).Data;
            Assert.AreEqual(1, later.NoRateCount);
            Assert.AreEqual(full.Total + 20_000m, later.Total);

            // an engineer does not see the cost of the single inspector: the line is hidden, left out of the totals and counted
            PcResourceBudgetDto masked = Svc(e, Engineer, s => s.GetBudgetAsync(plan.Id, null, Ct)).Data;
            PcResourceBudgetLineDto hidden = masked.Lines.Single(l => l.ResourceCode == "INSPECTOR");
            Assert.IsNull(hidden.Rate); Assert.IsNull(hidden.Amount); Assert.IsTrue(hidden.RateHidden);
            Assert.AreEqual(1, masked.HiddenLineCount);
            Assert.IsTrue(masked.RateHidden);
            Assert.AreEqual(full.Total - 2_000m, masked.Total);
            Assert.AreEqual(100m, masked.Lines.First(l => l.ResourceCode == "CREW").Rate, "a group is not one person");
            Assert.IsTrue(Fail(Svc(e, Outsider, s => s.GetBudgetAsync(plan.Id, null, Ct))).Contains("مجوز"));
        }

        // ------------------------------------------------------------------------------------------------ 4b: the lock after the baseline

        [TestMethod]
        public void After_The_Baseline_A_New_Version_Opens_Only_Through_An_Approved_Change_Request()
        {
            Env e = NewEnv("lock", ("A1", 2, "QC"));
            PcResourceDto welder = Save(e, Res("WELDER"));
            PcResourcePlanDto plan = Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct)).Data;
            Assign(e, Planner, plan.Id, e.Activities[0], welder.Id, 15m);
            Act(e, Planner, plan.Id, "submit");
            Act(e, Manager, plan.Id, "approve");

            int baselineId = 0, changeId = 0;
            e.Host.Write(u =>
            {
                PcBaseline b = AppHost.Add(u, new PcBaseline { ContractId = e.Contract, Number = 0, Kind = PcBaselineKind.Master, Status = PcBaselineStatus.Approved, ProjectStart = DateTime.Today, ProjectFinish = DateTime.Today.AddDays(5), ApprovedAt = DateTimeOffset.Now, ApprovedByUserId = 1011 });
                u.SaveChanges();
                baselineId = b.Id;
                u.Set<PcContractTerms>().Single(t => t.ContractId == e.Contract).CurrentBaselineId = b.Id;
                AppHost.Add(u, new PcBaselineActivity { BaselineId = b.Id, ActivityId = e.Activities[0], Code = "A1", Title = "فعالیت A1", DurationDays = 2 });
                u.Set<PcBaselineResource>().Add(new PcBaselineResource { BaselineId = b.Id, ActivityId = e.Activities[0], ResourceId = welder.Id, Quantity = 15m, Distribution = PcResourceDistribution.Uniform });
                u.SaveChanges();
            }, Seeder);

            string locked = Fail(Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct)));
            Assert.IsTrue(locked.Contains("درخواست تغییر"), locked);
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract, ChangeRequestId = 4242 }, Ct))).Contains("درخواست تغییر"), "an unknown or unapproved request opens nothing");
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.SaveAssignmentsAsync(new PcActivityAssignmentsDto { PlanId = plan.Id, ActivityId = e.Activities[0], Lines = new List<PcAssignmentLineDto>() }, Ct))).Length > 0);

            // an approved change request that is being carried out but built no baseline opens nothing
            e.Host.Write(u =>
            {
                PcChangeRequest c = AppHost.Add(u, new PcChangeRequest { ContractId = e.Contract, Number = "CR-1", Decision = PcChangeDecision.Approve, Status = PcChangeStatus.Implementing });
                u.SaveChanges();
                changeId = c.Id;
            }, Seeder);
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct))).Contains("درخواست تغییر"), "a request without a baseline of its own opens no plan");

            // once the request has built a baseline (now the current one) it opens the version, once
            int changeBaseline = SeedChangeBaseline(e, changeId, welder.Id, 15m, true);
            PcResourcePlanDto v2 = Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct)).Data;
            Assert.AreEqual(2, v2.Version);
            Assert.AreEqual(changeId, v2.ChangeRequestId);
            Assert.AreEqual(1, v2.Assignments.Count, "the new version starts as a copy of the approved one");
            Assert.AreEqual(0, v2.UnassignedCount);
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct))).Contains("طرح منابع باز"));
            Assign(e, Planner, v2.Id, e.Activities[0], welder.Id, 40m);
            Act(e, Planner, v2.Id, "submit");
            Act(e, Manager, v2.Id, "approve");
            // the approval replaced the resource rows frozen with the baseline the request built, and with no other baseline
            Assert.AreEqual(40m, e.Host.Read(u => u.Set<PcBaselineResource>().Single(x => x.BaselineId == changeBaseline)).Quantity);
            Assert.AreEqual(15m, e.Host.Read(u => u.Set<PcBaselineResource>().Single(x => x.BaselineId == baselineId)).Quantity, "the older baseline is untouched");
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct))).Contains("درخواست تغییر"), "the request is used up");
        }

        /// <summary>A baseline that a change request built (approved), with one resource row, optionally the current baseline of the contract.</summary>
        private static int SeedChangeBaseline(Env e, int changeId, int resourceId, decimal quantity, bool current)
        {
            int id = 0;
            e.Host.Write(u =>
            {
                PcBaseline b = AppHost.Add(u, new PcBaseline { ContractId = e.Contract, Number = 1, Kind = PcBaselineKind.Master, Status = PcBaselineStatus.Approved, ChangeRequestId = changeId, ProjectStart = DateTime.Today, ProjectFinish = DateTime.Today.AddDays(5), ApprovedAt = DateTimeOffset.Now, ApprovedByUserId = 1011 });
                u.SaveChanges();
                id = b.Id;
                if (current) u.Set<PcContractTerms>().Single(t => t.ContractId == e.Contract).CurrentBaselineId = b.Id;
                AppHost.Add(u, new PcBaselineActivity { BaselineId = b.Id, ActivityId = e.Activities[0], Code = "A1", Title = "فعالیت A1", DurationDays = 2 });
                u.Set<PcBaselineResource>().Add(new PcBaselineResource { BaselineId = b.Id, ActivityId = e.Activities[0], ResourceId = resourceId, Quantity = quantity, Distribution = PcResourceDistribution.Uniform });
                u.SaveChanges();
            }, Seeder);
            return id;
        }

        /// <summary>An approved plan, a master baseline that is current, and an approved change request in implementation (with or without a baseline of its own).</summary>
        private static (PcResourceDto Welder, int Change) LockedWithRequest(Env e, bool changeBaselineBuilt, out int changeBaseline)
        {
            PcResourceDto welder = Save(e, Res("WELDER"));
            PcResourcePlanDto plan = Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct)).Data;
            Assign(e, Planner, plan.Id, e.Activities[0], welder.Id, 15m);
            Act(e, Planner, plan.Id, "submit");
            Act(e, Manager, plan.Id, "approve");
            int changeId = 0;
            e.Host.Write(u =>
            {
                PcBaseline b = AppHost.Add(u, new PcBaseline { ContractId = e.Contract, Number = 0, Kind = PcBaselineKind.Master, Status = PcBaselineStatus.Approved, ProjectStart = DateTime.Today, ProjectFinish = DateTime.Today.AddDays(5), ApprovedAt = DateTimeOffset.Now, ApprovedByUserId = 1011 });
                u.SaveChanges();
                u.Set<PcContractTerms>().Single(t => t.ContractId == e.Contract).CurrentBaselineId = b.Id;
                PcChangeRequest c = AppHost.Add(u, new PcChangeRequest { ContractId = e.Contract, Number = "CR-2", Decision = PcChangeDecision.Approve, Status = PcChangeStatus.Implementing });
                u.SaveChanges();
                changeId = c.Id;
            }, Seeder);
            changeBaseline = changeBaselineBuilt ? SeedChangeBaseline(e, changeId, welder.Id, 15m, true) : 0;
            return (welder, changeId);
        }

        [TestMethod]
        public void A_Version_Of_A_Change_Request_Is_Worked_On_Only_While_The_Request_Is_Being_Implemented()
        {
            Env e = NewEnv("crstate", ("A1", 2, "QC"));
            var (welder, changeId) = LockedWithRequest(e, true, out int changeBaseline);
            PcResourcePlanDto v2 = Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct)).Data;
            Assign(e, Planner, v2.Id, e.Activities[0], welder.Id, 20m);
            Act(e, Planner, v2.Id, "submit");

            // the request leaves «implementing» (closed behind the back of the plan): the submitted version cannot be approved
            void SetRequest(PcChangeStatus status) => e.Host.Write(u => { u.Set<PcChangeRequest>().Single(c => c.Id == changeId).Status = status; u.SaveChanges(); }, Seeder);
            SetRequest(PcChangeStatus.Closed);
            string rv = RowVersionOf(e, v2.Id);
            Assert.AreEqual(PcResourceChange.NotImplementing, Fail(Svc(e, Manager, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = v2.Id, Action = "approve", RowVersion = rv }, Ct))));
            // a draft is neither edited nor submitted
            e.Host.Write(u => { u.Set<PcResourcePlan>().Single(p => p.Id == v2.Id).Status = PcResourcePlanStatus.Draft; u.SaveChanges(); }, Seeder);
            Assert.AreEqual(PcResourceChange.NotImplementing, Fail(Svc(e, Planner, s => s.SaveAssignmentsAsync(new PcActivityAssignmentsDto { PlanId = v2.Id, ActivityId = e.Activities[0], Lines = new List<PcAssignmentLineDto> { new PcAssignmentLineDto { ResourceId = welder.Id, Quantity = 1m } } }, Ct))));
            Assert.AreEqual(PcResourceChange.NotImplementing, Fail(Svc(e, Planner, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = v2.Id, Action = "submit" }, Ct))));
            Assert.AreEqual(PcResourceChange.NotImplementing, Fail(Svc(e, Planner, s => s.MarkUnassignedAsync(new PcMarkUnassignedDto { PlanId = v2.Id, AllRemaining = true }, Ct))));
            // a request that was rejected is no better
            SetRequest(PcChangeStatus.Implementing);
            e.Host.Write(u => { u.Set<PcChangeRequest>().Single(c => c.Id == changeId).Decision = PcChangeDecision.Reject; u.SaveChanges(); }, Seeder);
            Assert.AreEqual(PcResourceChange.NotImplementing, Fail(Svc(e, Planner, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = v2.Id, Action = "submit" }, Ct))));
        }

        [TestMethod]
        public void A_Version_Is_Not_Approved_When_The_Baseline_Of_Its_Request_Is_No_Longer_The_Current_One()
        {
            Env e = NewEnv("crbase", ("A1", 2, "QC"));
            var (welder, changeId) = LockedWithRequest(e, true, out int changeBaseline);
            PcResourcePlanDto v2 = Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct)).Data;
            Assign(e, Planner, v2.Id, e.Activities[0], welder.Id, 20m);
            Act(e, Planner, v2.Id, "submit");
            // a newer baseline took over
            e.Host.Write(u =>
            {
                PcBaseline newer = AppHost.Add(u, new PcBaseline { ContractId = e.Contract, Number = 2, Kind = PcBaselineKind.Master, Status = PcBaselineStatus.Approved, ProjectStart = DateTime.Today, ProjectFinish = DateTime.Today.AddDays(5), ApprovedAt = DateTimeOffset.Now, ApprovedByUserId = 1011 });
                u.SaveChanges();
                u.Set<PcContractTerms>().Single(t => t.ContractId == e.Contract).CurrentBaselineId = newer.Id;
                u.SaveChanges();
            }, Seeder);
            string rv = RowVersionOf(e, v2.Id);
            Assert.AreEqual(PcResourceChange.BaselineNotCurrent, Fail(Svc(e, Manager, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = v2.Id, Action = "approve", RowVersion = rv }, Ct))));
            Assert.AreEqual(15m, e.Host.Read(u => u.Set<PcBaselineResource>().Single(x => x.BaselineId == changeBaseline)).Quantity, "nothing was written");
            Assert.AreEqual(PcResourcePlanStatus.Submitted, e.Host.Read(u => u.Set<PcResourcePlan>().Single(p => p.Id == v2.Id)).Status);
        }

        [TestMethod]
        public void A_Change_Request_Cannot_Be_Closed_While_The_Resource_Plan_It_Opened_Is_Open()
        {
            Env e = NewEnv("crclose", ("A1", 2, "QC"));
            var (welder, changeId) = LockedWithRequest(e, true, out int changeBaseline);
            PcResourcePlanDto v2 = Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct)).Data;
            ResponseDto<PcChangeDto> refused = e.Host.As(Planner, s => s.GetRequiredService<IPcBaselineService>().ChangeActionAsync(new PcChangeActionDto { Id = changeId, Action = "close" }, Ct)).GetAwaiter().GetResult();
            Assert.IsFalse(refused.Success);
            Assert.IsTrue(refused.Message.Contains("طرح منابع نسخه " + v2.Version), refused.Message);
            Assign(e, Planner, v2.Id, e.Activities[0], welder.Id, 20m);
            Act(e, Planner, v2.Id, "submit");
            Assert.IsFalse(e.Host.As(Planner, s => s.GetRequiredService<IPcBaselineService>().ChangeActionAsync(new PcChangeActionDto { Id = changeId, Action = "close" }, Ct)).GetAwaiter().GetResult().Success, "a submitted version is still open");
            Act(e, Manager, v2.Id, "approve");
            ResponseDto<PcChangeDto> closed = e.Host.As(Planner, s => s.GetRequiredService<IPcBaselineService>().ChangeActionAsync(new PcChangeActionDto { Id = changeId, Action = "close" }, Ct)).GetAwaiter().GetResult();
            Assert.IsTrue(closed.Success, closed.Message);
        }

        [TestMethod]
        public void A_Draft_Plan_Of_A_Contract_Whose_Baseline_Exists_Hits_The_Baseline_Lock_And_Can_Only_Be_Discarded_Before_It()
        {
            Env e = NewEnv("draftlock", ("A1", 2, "QC"));
            PcResourceDto welder = Save(e, Res("WELDER"));
            PcResourcePlanDto draft = Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct)).Data;
            Assert.AreEqual(PcResourcePlanStatus.Draft, draft.Status);

            // discard: planning or PMO only, with an optional reason, soft delete
            Assert.IsTrue(Fail(Svc(e, Engineer, s => s.DiscardPlanAsync(new PcResourcePlanDiscardDto { Id = draft.Id }, Ct))).Contains("مجوز"));
            Assert.IsTrue(Fail(Svc(e, Manager, s => s.DiscardPlanAsync(new PcResourcePlanDiscardDto { Id = draft.Id }, Ct))).Contains("مجوز"));

            // now a baseline exists although the draft plan is still open: editing and submitting meet the baseline lock, and so does the discard
            e.Host.Write(u =>
            {
                PcBaseline b = AppHost.Add(u, new PcBaseline { ContractId = e.Contract, Number = 0, Kind = PcBaselineKind.Master, Status = PcBaselineStatus.Approved, ProjectStart = DateTime.Today, ProjectFinish = DateTime.Today.AddDays(5), ApprovedAt = DateTimeOffset.Now, ApprovedByUserId = 1011 });
                u.SaveChanges();
                u.Set<PcContractTerms>().Single(t => t.ContractId == e.Contract).CurrentBaselineId = b.Id;
                u.SaveChanges();
            }, Seeder);
            Assert.AreEqual(PcResourceText.Locked, Fail(Svc(e, Planner, s => s.SaveAssignmentsAsync(new PcActivityAssignmentsDto { PlanId = draft.Id, ActivityId = e.Activities[0], Lines = new List<PcAssignmentLineDto> { new PcAssignmentLineDto { ResourceId = welder.Id, Quantity = 5m } } }, Ct))));
            Assert.AreEqual(PcResourceText.Locked, Fail(Svc(e, Planner, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = draft.Id, Action = "submit" }, Ct))));
            Assert.AreEqual(PcResourceText.Locked, Fail(Svc(e, Planner, s => s.DiscardPlanAsync(new PcResourcePlanDiscardDto { Id = draft.Id }, Ct))));
        }

        [TestMethod]
        public void An_Open_Plan_Is_Discarded_By_Planning_Or_The_Pmo_Before_Any_Baseline_And_Is_Soft_Deleted()
        {
            Env e = NewEnv("discard", ("A1", 2, "QC"));
            PcResourcePlanDto draft = Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct)).Data;
            ResponseDto<PcResourcePlanDto> gone = Svc(e, Pmo, s => s.DiscardPlanAsync(new PcResourcePlanDiscardDto { Id = draft.Id, Reason = "اشتباهی باز شد" }, Ct));
            Assert.IsTrue(gone.Success, gone.Message);
            PcResourcePlan row = e.Host.Read(u => u.Set<PcResourcePlan>().IgnoreQueryFilters().Single(p => p.Id == draft.Id));
            Assert.IsTrue(row.Deleted);
            Assert.IsTrue(row.ReturnReason.Contains("اشتباهی باز شد"));
            Assert.AreEqual(0, Svc(e, Planner, s => s.GetPlanVersionsAsync(e.Contract, Ct)).Data.Count);
            // a new plan may be opened, and an approved plan is not discarded
            PcResourcePlanDto again = Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct)).Data;
            PcResourceDto welder = Save(e, Res("WELDER"));
            Assign(e, Planner, again.Id, e.Activities[0], welder.Id, 3m);
            Act(e, Planner, again.Id, "submit");
            Assert.IsTrue(Svc(e, Planner, s => s.DiscardPlanAsync(new PcResourcePlanDiscardDto { Id = again.Id }, Ct)).Success, "a submitted plan is open too");
            PcResourcePlanDto third = Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct)).Data;
            Assign(e, Planner, third.Id, e.Activities[0], welder.Id, 3m);
            Act(e, Planner, third.Id, "submit");
            Act(e, Manager, third.Id, "approve");
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.DiscardPlanAsync(new PcResourcePlanDiscardDto { Id = third.Id }, Ct))).Contains("تاییدشده"));
            // a version that a change request opened is never discarded
            e.Host.Write(u => { u.Set<PcResourcePlan>().Single(p => p.Id == third.Id).Status = PcResourcePlanStatus.Draft; u.Set<PcResourcePlan>().Single(p => p.Id == third.Id).ChangeRequestId = 77; u.SaveChanges(); }, Seeder);
            Assert.IsTrue(Fail(Svc(e, Planner, s => s.DiscardPlanAsync(new PcResourcePlanDiscardDto { Id = third.Id }, Ct))).Contains("درخواست تغییر"));
        }

        [TestMethod]
        public void A_Change_Of_Capacity_Or_Employee_Link_That_Crosses_The_Sensitivity_Line_Of_A_Costed_Resource_Is_For_Payroll_Only()
        {
            Env e = NewEnv("sens", ("A1", 2, "QC"));
            PcResourceDto crew = Save(e, Res("CREW", capacity: 5m));
            // before any rate or baseline row the planner may change it freely
            PcResourceDto free = Save(e, Res("FREE", capacity: 5m));
            free.Capacity = 1m;
            Assert.IsTrue(Svc(e, Planner, s => s.SaveResourceAsync(free, Ct)).Success);
            ApprovedRate(e, crew.Id, 100m, DateTime.Today.AddDays(-1));
            ClaimsPrincipal payrollPlanner = AppHost.Principal(1055, "planner.payroll", "PLANNING", "PAYROLL");

            crew.Capacity = 1m; // one person: the rate of the crew would be one person's cost
            Assert.AreEqual(PcResourceText.SensitivityChange, Fail(Svc(e, Planner, s => s.SaveResourceAsync(crew, Ct))));
            crew.Capacity = 2m;
            Assert.AreEqual(PcResourceText.SensitivityChange, Fail(Svc(e, Planner, s => s.SaveResourceAsync(crew, Ct))), "two is below the minimum of three");
            crew.Capacity = 8m; // stays on the same side of the line
            Assert.IsTrue(Svc(e, Planner, s => s.SaveResourceAsync(crew, Ct)).Success);
            crew.Capacity = 1m;
            ResponseDto<PcResourceDto> moved = Svc(e, payrollPlanner, s => s.SaveResourceAsync(crew, Ct));
            Assert.IsTrue(moved.Success, "a payroll role may move it: " + moved.Message);
        }

        [TestMethod]
        public void Editing_A_Resource_Keeps_It_Active_Or_Inactive_And_One_Rate_Per_Date_Holds_At_Submit_And_Approve()
        {
            Env e = NewEnv("misc", ("A1", 2, "QC"));
            PcResourceDto r = Save(e, Res("CREW"));
            Assert.IsTrue(Svc(e, Planner, s => s.SetResourceActiveAsync(r.Id, false, Ct)).Success);
            r.Title = "عنوان تازه"; r.IsActive = true; // the edit does not switch it back on
            Assert.IsTrue(Svc(e, Planner, s => s.SaveResourceAsync(r, Ct)).Success);
            Assert.IsFalse(e.Host.Read(u => u.Set<PcResource>().Single(x => x.Id == r.Id)).IsActive);
            Assert.IsTrue(Svc(e, Planner, s => s.SetResourceActiveAsync(r.Id, true, Ct)).Success);

            // two draft proposals for one date are fine until one of them is submitted
            DateTime day = DateTime.Today.AddDays(-2);
            int a = Svc(e, FinMaker, s => s.SaveRateAsync(new PcResourceRateSaveDto { ResourceId = r.Id, Rate = 10m, EffectiveFrom = day }, Ct)).Data.Id;
            // a second draft for the same date is refused already at proposal; seed the clash the way a race would
            int b = 0;
            e.Host.Write(u =>
            {
                PcResourceRate row = AppHost.Add(u, new PcResourceRate { ResourceId = r.Id, Rate = 20m, EffectiveFrom = day, Status = PcRateStatus.Draft, MakerUserId = 1201 });
                u.SaveChanges(); b = row.Id;
            }, Seeder);
            Assert.IsTrue(Svc(e, FinMaker, s => s.RateActionAsync(new PcResourceRateActionDto { Id = a, Action = "submit" }, Ct)).Success);
            Assert.AreEqual(PcResourceText.RateDuplicate, Fail(Svc(e, FinMaker, s => s.RateActionAsync(new PcResourceRateActionDto { Id = b, Action = "submit" }, Ct))));
            // approve is checked as well: force the second one to Submitted behind the service and approve it
            e.Host.Write(u => { u.Set<PcResourceRate>().Single(x => x.Id == a).Status = PcRateStatus.Approved; u.Set<PcResourceRate>().Single(x => x.Id == b).Status = PcRateStatus.Draft; u.SaveChanges(); }, Seeder);
            Assert.AreEqual(PcResourceText.RateDuplicate, Fail(Svc(e, FinMaker, s => s.RateActionAsync(new PcResourceRateActionDto { Id = b, Action = "submit" }, Ct))));
        }

        [TestMethod]
        public void A_Plan_Decision_Needs_The_Row_Version_When_The_Row_Has_One_And_Shows_Who_Decided()
        {
            Env e = NewEnv("rowver", ("A1", 2, "QC"));
            PcResourceDto welder = Save(e, Res("WELDER"));
            PcResourcePlanDto plan = Svc(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = e.Contract }, Ct)).Data;
            Assign(e, Planner, plan.Id, e.Activities[0], welder.Id, 3m);
            Act(e, Planner, plan.Id, "submit");
            // the in-memory provider has no row version generator: give the row one, as SQL Server does
            e.Host.Write(u => { u.Set<PcResourcePlan>().Single(p => p.Id == plan.Id).RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }; u.SaveChanges(); }, Seeder);
            Assert.AreEqual(PcResourceText.RowVersionMissing, Fail(Svc(e, Manager, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = plan.Id, Action = "approve" }, Ct))));
            Assert.AreEqual(PcResourceText.RowVersionMissing, Fail(Svc(e, Manager, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = plan.Id, Action = "return", Note = "x" }, Ct))));
            string rv = RowVersionOf(e, plan.Id);
            PcResourcePlanDto approved = Svc(e, Manager, s => s.PlanActionAsync(new PcResourcePlanActionDto { Id = plan.Id, Action = "approve", RowVersion = rv }, Ct)).Data;
            Assert.AreEqual(1081, approved.DeciderUserId);
            Assert.AreEqual(1081, approved.ApproverUserId, "the old field stays");
            Assert.AreEqual(approved.ApproverUserId, approved.DeciderUserId);
        }
    }
}
