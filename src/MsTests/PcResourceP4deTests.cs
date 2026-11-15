using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Providers;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Ai;
using Services.NasbNiro.ProductBank;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Production;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProductionDtos;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace MsTests
{
    /// <summary>Priority 4, phases 4d and 4e: the MS Project import, one resource truth with production, actual cost and performance, the cost proposal, the charter list and the weekly report section.</summary>
    [TestClass]
    public class PcResourceP4deTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1001, "seed", "PMO");
        private static readonly ClaimsPrincipal Planner = AppHost.Principal(1051, "planner", "PLANNING");
        private static readonly ClaimsPrincipal Pmo = AppHost.Principal(1011, "pmo.one", "PMO");
        private static readonly ClaimsPrincipal Engineer = AppHost.Principal(1301, "engineer", "ENGINEER");
        private static readonly ClaimsPrincipal Payroll = AppHost.Principal(1401, "payroll", "PAYROLL");
        private static readonly ClaimsPrincipal ProductionManager = AppHost.Principal(1601, "prod.manager", "PRODUCTIONMANAGER");

        private sealed class Env
        {
            public AppHost Host;
            public Dictionary<string, AiActingUser> Seats;
            public int Contract;
            public Dictionary<string, int> Activities = new Dictionary<string, int>();
        }

        private static async Task<Env> NewEnv(string name, bool seats = false)
        {
            var host = new AppHost("P4de_" + name + "_" + Guid.NewGuid().ToString("N"));
            var e = new Env { Host = host };
            if (seats) e.Seats = await host.AsSystem(p => p.GetRequiredService<IAiSeatProvisioner>().EnsureAsync(Ct));
            return e;
        }

        private static DateTime Work(int index)
        {
            DateTime d = DateTime.Today.AddDays(1);
            while (d.DayOfWeek != DayOfWeek.Saturday) d = d.AddDays(1);
            int n = 0;
            while (true) { if (d.DayOfWeek != DayOfWeek.Friday) { if (n == index) return d; n++; } d = d.AddDays(1); }
        }

        /// <summary>The first working days counted from today (Friday is the day off of the company calendar).</summary>
        private static List<DateTime> FirstWorkingDays(int count)
        {
            var days = new List<DateTime>();
            for (DateTime d = DateTime.Today; days.Count < count; d = d.AddDays(1)) if (d.DayOfWeek != DayOfWeek.Friday) days.Add(d);
            return days;
        }

        private static int NewContract(Env e, string code, params (string Code, DateTime Start, DateTime Finish)[] activities)
        {
            int id = 0;
            e.Host.Write(u =>
            {
                ContractEntity c = AppHost.Add(u, new ContractEntity { Code = code, Title = "پروژه " + code, Employer = "کارفرما", Status = ContractStatus.Current, ContractDate = DateTimeOffset.Now.AddDays(-30), StartDate = DateTimeOffset.Now.Date });
                u.SaveChanges();
                AppHost.Add(u, new PcContractTerms { ContractId = c.Id, CurrencyCode = "IRR", ValueExVat = 1_000_000_000m, ScheduleStartDate = DateTime.Today, ContractDeliveryDate = DateTime.Today.AddDays(200), ProjectManagerUserId = 1081 });
                int sort = 1;
                foreach (var a in activities)
                {
                    PcActivity act = AppHost.Add(u, new PcActivity { ContractId = c.Id, Code = a.Code, Title = "فعالیت " + a.Code, DurationDays = 5, Weight = 1m, ResponsibleUnit = "ZZZ", EarlyStart = a.Start, EarlyFinish = a.Finish, Sort = sort++ });
                    u.SaveChanges();
                    e.Activities[code + ":" + a.Code] = act.Id;
                }
                u.SaveChanges();
                id = c.Id;
            }, Seeder);
            return id;
        }

        private static int NewResource(Env e, string code, PcResourceKind kind, PcResourceUnit unit, decimal capacity, int? workCenter = null, int? provider = null, string unitRole = null, bool inactive = false)
        {
            int id = 0;
            e.Host.Write(u =>
            {
                PcResource r = AppHost.Add(u, new PcResource { Code = code, Title = "منبع " + code, Kind = kind, Unit = unit, Capacity = capacity, WorkCenterId = workCenter, ProviderId = provider, ResponsibleUnit = unitRole, IsActive = !inactive });
                u.SaveChanges();
                id = r.Id;
            }, Seeder);
            return id;
        }

        private static void ApproveRate(Env e, int resourceId, decimal rate)
        {
            e.Host.Write(u => { AppHost.Add(u, new PcResourceRate { ResourceId = resourceId, Rate = rate, EffectiveFrom = DateTime.Today.AddDays(-400), Status = PcRateStatus.Approved, MakerUserId = 1201, ApprovedByUserId = 1202, ApprovedAt = DateTimeOffset.Now }); u.SaveChanges(); }, Seeder);
        }

        private static int ApprovedPlan(Env e, int contract, params (string Activity, int Resource, decimal Quantity)[] rows) => AddPlan(e, contract, 1, PcResourcePlanStatus.Approved, rows);

        private static int AddPlan(Env e, int contract, int version, PcResourcePlanStatus status, params (string Activity, int Resource, decimal Quantity)[] rows)
        {
            int id = 0;
            e.Host.Write(u =>
            {
                var plan = new PcResourcePlan { ContractId = contract, Version = version, Status = status, MakerUserId = 1051, EditorUserIds = ",1051,", ApprovedAt = status == PcResourcePlanStatus.Approved ? DateTimeOffset.Now : (DateTimeOffset?)null, ApprovedByUserId = status == PcResourcePlanStatus.Approved ? 1081 : (int?)null, Assignments = new List<PcActivityResource>() };
                foreach (var r in rows) plan.Assignments.Add(new PcActivityResource { ActivityId = e.Activities[r.Activity], ResourceId = r.Resource, Quantity = r.Quantity, Distribution = PcResourceDistribution.Uniform, Source = PcAssignmentSource.Human, EditedByUserId = 1051, EditedAt = DateTimeOffset.Now });
                AppHost.Add(u, plan);
                u.SaveChanges();
                id = plan.Id;
            }, Seeder);
            return id;
        }

        private static T Svc<T, S>(Env e, ClaimsPrincipal who, Func<S, Task<T>> body) => e.Host.As(who, s => body(s.GetRequiredService<S>())).GetAwaiter().GetResult();

        // ------------------------------------------------------------------------------------------------ the pure rules

        [TestMethod]
        public void The_Title_Is_Normalised_And_The_Work_Is_Converted_To_The_Unit_Of_The_Resource()
        {
            Assert.AreEqual("کتاب جوشکار ها", PcMspRules.Normalize("  كتاب   جوشكار‌ها "));
            Assert.AreEqual("علی", PcMspRules.Normalize("عليّ".Replace("ّ", "")));
            Assert.AreEqual("abc d", PcMspRules.Normalize(" ABC\t D"));
            Assert.AreEqual(string.Empty, PcMspRules.Normalize("   "));
            Assert.AreEqual(480m, PbMspdiResources.WorkMinutes("PT8H0M0S"));
            Assert.AreEqual(990m, PbMspdiResources.WorkMinutes("PT16H30M0S"));
            Assert.AreEqual(0m, PbMspdiResources.WorkMinutes("PT0H0M0S"));
            Assert.IsNull(PbMspdiResources.WorkMinutes(""));
            Assert.IsNull(PbMspdiResources.WorkMinutes("not a duration"));
            Assert.AreEqual(16m, PcMspRules.Quantity(PcResourceUnit.Hour, 16m, 7.5m, false, 1m));
            Assert.AreEqual(2m, PcMspRules.Quantity(PcResourceUnit.Day, 16m, 8m, false, 1m), "a day of eight hours");
            Assert.AreEqual(2.1333m, PcMspRules.Quantity(PcResourceUnit.Day, 16m, 7.5m, false, 1m), "the day length without a calendar is 7.5 hours");
            Assert.AreEqual(5m, PcMspRules.Quantity(PcResourceUnit.Each, 0m, 7.5m, true, 5m), "a material resource counts its units");
            Assert.IsNull(PcMspRules.Quantity(PcResourceUnit.Each, 8m, 7.5m, false, 1m), "work cannot be counted in pieces");
            Assert.IsNull(PcMspRules.Quantity(PcResourceUnit.Hour, 8m, 7.5m, true, 5m), "units of a material cannot be counted in hours");
        }

        [TestMethod]
        public void The_Planned_Value_Phasing_Adds_Up_To_The_Quantity_And_The_Amount_Exactly()
        {
            var calendar = PcWorkCalendar.MondayToFriday();
            DateTime start = new DateTime(2030, 1, 7), finish = new DateTime(2030, 1, 25);
            foreach (PcResourceDistribution d in new[] { PcResourceDistribution.Uniform, PcResourceDistribution.FrontLoaded, PcResourceDistribution.BackLoaded })
                foreach (decimal q in new[] { 10m, 7.5m, 33.3333m, 1234.5678m })
                {
                    decimal amount = PcResourceRules.Round2(q * 1234.5m);
                    var days = PcPvRules.Phase(calendar, start, finish, q, amount, d);
                    Assert.AreEqual(q, days.Sum(x => x.Quantity), d + " " + q);
                    Assert.AreEqual(amount, days.Sum(x => x.Amount), d + " " + q);
                    Assert.IsTrue(days.All(x => calendar.IsWorking(x.Day)));
                }
            // a span without a working day puts everything on the next working day
            var saturday = PcPvRules.Phase(calendar, new DateTime(2030, 1, 5), new DateTime(2030, 1, 6), 4m, 40m, PcResourceDistribution.Uniform);
            Assert.AreEqual(1, saturday.Count); Assert.AreEqual(new DateTime(2030, 1, 7), saturday[0].Day); Assert.AreEqual(40m, saturday[0].Amount);
            Assert.AreEqual("۱۴۰۸/۰۱", PcPvRules.Digits("1408/01"));
        }

        // ------------------------------------------------------------------------------------------------ 4d-1: the MS Project import

        private const string Ns = "http://schemas.microsoft.com/project";

        private static string Mspdi(string tasks, string resources, string assignments) =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Project xmlns=\"" + Ns + "\"><Name>پروژه</Name><MinutesPerDay>480</MinutesPerDay>"
            + "<Tasks><Task><UID>0</UID><ID>0</ID><Name>پروژه</Name><Summary>1</Summary><OutlineLevel>0</OutlineLevel></Task>" + tasks + "</Tasks><Resources>" + resources + "</Resources><Assignments>" + assignments + "</Assignments></Project>";

        private static string Task(int uid, string name, string wbs) => $"<Task><UID>{uid}</UID><ID>{uid}</ID><Name>{name}</Name><WBS>{wbs}</WBS><OutlineLevel>1</OutlineLevel><Duration>PT40H0M0S</Duration></Task>";
        private static string Resource(int uid, string name, string code = null, int type = 1) => $"<Resource><UID>{uid}</UID><Name>{name}</Name>" + (code == null ? "" : $"<Code>{code}</Code>") + $"<Type>{type}</Type><StandardRate>999999</StandardRate><OvertimeRate>1999999</OvertimeRate><Cost>5000000</Cost></Resource>";
        private static string Asg(int task, int resource, string work, string units = "1") => $"<Assignment><UID>{task * 100 + resource}</UID><TaskUID>{task}</TaskUID><ResourceUID>{resource}</ResourceUID><Work>{work}</Work><Units>{units}</Units><Cost>7000000</Cost></Assignment>";

        private static async Task<(Env E, int Contract, int Weld, int Crane)> ImportEnv(string name)
        {
            Env e = await NewEnv(name);
            int contract = NewContract(e, "CM", ("A1", Work(0), Work(4)), ("A2", Work(0), Work(4)));
            int weld = NewResource(e, "R-WELD", PcResourceKind.Labour, PcResourceUnit.Hour, 3m);
            int crane = NewResource(e, "CR-1", PcResourceKind.Equipment, PcResourceUnit.Day, 1m);
            e.Host.Write(u => { u.Set<PcResource>().Single(r => r.Id == crane).Title = "جرثقیل برجی"; u.SaveChanges(); }, Seeder);
            Assert.IsTrue(Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = contract }, Ct)).Success);
            return (e, contract, weld, crane);
        }

        private static string SampleFile() => Mspdi(
            Task(1, "فعالیت A1", "A1") + Task(2, "فعالیت A2", "A2") + Task(3, "کار ناشناخته", "Z9"),
            Resource(1, "جوشكار", "r-weld") + Resource(2, "جرثقيل   برجي") + Resource(3, "ناشناخته"),
            Asg(1, 1, "PT16H0M0S") + Asg(2, 1, "PT8H0M0S") + Asg(1, 2, "PT24H0M0S") + Asg(1, 3, "PT8H0M0S") + Asg(3, 1, "PT8H0M0S") + Asg(2, 2, "PT0H0M0S") + Asg(1, 1, "PT4H0M0S"));

        [TestMethod]
        public async Task The_Preview_Suggests_Matches_In_Order_Converts_The_Work_And_Counts_Exactly_Without_Saving()
        {
            var env = await ImportEnv("preview");
            ResponseDto<PcMspPreviewDto> r = Svc<ResponseDto<PcMspPreviewDto>, IPcResourceService>(env.E, Planner, s => s.PreviewImportAsync(new PcMspImportRequestDto { ContractId = env.Contract, Xml = SampleFile() }, Ct));
            Assert.IsTrue(r.Success, r.Message);
            PcMspPreviewDto p = r.Data;
            Assert.IsTrue(p.RatesNote.Contains("هرگز"));
            // exact code (case-insensitive, the file's code field), then the normalised title (ي and the double space), then none
            PcMspResourceRowDto weld = p.Resources.Single(x => x.MspUid == 1), crane = p.Resources.Single(x => x.MspUid == 2), unknown = p.Resources.Single(x => x.MspUid == 3);
            Assert.AreEqual("code", weld.MatchKind); Assert.AreEqual(env.Weld, weld.SuggestedResourceId);
            Assert.AreEqual("normalized", crane.MatchKind); Assert.AreEqual(env.Crane, crane.SuggestedResourceId);
            Assert.AreEqual("none", unknown.MatchKind); Assert.IsNull(unknown.SuggestedResourceId);
            Assert.AreEqual(7, p.Counts.Assignments);
            Assert.AreEqual(3, p.Counts.Ok); Assert.AreEqual(1, p.Counts.ResourceUnmatched); Assert.AreEqual(1, p.Counts.ActivityUnmatched); Assert.AreEqual(1, p.Counts.ZeroWork); Assert.AreEqual(1, p.Counts.Duplicate);
            Assert.AreEqual(3, p.Counts.ResourcesInFile); Assert.AreEqual(2, p.Counts.ResourcesMatched);
            Assert.AreEqual(16m, p.Rows.Single(x => x.TaskUid == 1 && x.MspResourceUid == 1 && x.Status == "ok").Quantity, "an hour resource: the work in hours");
            PcMspAssignmentRowDto days = p.Rows.Single(x => x.TaskUid == 1 && x.MspResourceUid == 2);
            Assert.AreEqual(24m, days.WorkHours); Assert.AreEqual(3.2m, days.Quantity, "a day resource: 24 hours of 7.5 hours a day"); Assert.AreEqual("wbs", days.ActivityMatch);
            Assert.AreEqual("duplicate", p.Rows.Last().Status);
            Assert.AreEqual("activityUnmatched", p.Rows.Single(x => x.TaskUid == 3).Status);
            Assert.AreEqual("zeroWork", p.Rows.Single(x => x.TaskUid == 2 && x.MspResourceUid == 2).Status);
            // nothing was saved
            Assert.AreEqual(0, env.E.Host.Read(u => u.Set<PcActivityResource>().Count()));
            Assert.AreEqual(0, env.E.Host.Read(u => u.Set<PcResourceRate>().Count()));
        }

        [TestMethod]
        public async Task The_Commit_Imports_Only_The_Matched_Rows_Reports_Exact_Counts_And_Never_Imports_A_Rate()
        {
            var env = await ImportEnv("commit");
            var request = new PcMspImportRequestDto
            {
                ContractId = env.Contract, Xml = SampleFile(),
                Resources = new List<PcMspResourceMapDto> { new PcMspResourceMapDto { MspUid = 1, ResourceId = env.Weld }, new PcMspResourceMapDto { MspUid = 2, ResourceId = env.Crane } },
            };
            ResponseDto<PcMspImportResultDto> r = Svc<ResponseDto<PcMspImportResultDto>, IPcResourceService>(env.E, Planner, s => s.ImportAsync(request, Ct));
            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(3, r.Data.Created); Assert.AreEqual(0, r.Data.Updated); Assert.AreEqual(0, r.Data.ResourcesCreated);
            Assert.AreEqual(7, r.Data.Total); Assert.AreEqual(1, r.Data.SkippedResourceUnmatched); Assert.AreEqual(1, r.Data.SkippedActivityUnmatched); Assert.AreEqual(1, r.Data.SkippedZeroWork); Assert.AreEqual(1, r.Data.SkippedDuplicate);
            List<PcActivityResource> rows = env.E.Host.Read(u => u.Set<PcActivityResource>().AsNoTracking().OrderBy(x => x.ActivityId).ThenBy(x => x.ResourceId).ToList());
            Assert.AreEqual(3, rows.Count);
            Assert.IsTrue(rows.All(x => x.Source == PcAssignmentSource.Import));
            Assert.AreEqual(16m, rows.Single(x => x.ResourceId == env.Weld && x.ActivityId == env.E.Activities["CM:A1"]).Quantity, "the duplicate row did not add to it");
            Assert.AreEqual(8m, rows.Single(x => x.ResourceId == env.Weld && x.ActivityId == env.E.Activities["CM:A2"]).Quantity);
            Assert.AreEqual(3.2m, rows.Single(x => x.ResourceId == env.Crane).Quantity);
            // the rates and costs of the file are never imported: no rate row, nothing priced
            Assert.AreEqual(0, env.E.Host.Read(u => u.Set<PcResourceRate>().Count()));
            Assert.IsTrue(r.Data.RatesNote.Contains("هرگز"));
            // the plan was touched by a person
            Assert.AreEqual(1051, env.E.Host.Read(u => u.Set<PcResourcePlan>().Single().MakerUserId));

            // importing the same file again updates the same three rows
            ResponseDto<PcMspImportResultDto> again = Svc<ResponseDto<PcMspImportResultDto>, IPcResourceService>(env.E, Planner, s => s.ImportAsync(request, Ct));
            Assert.IsTrue(again.Success, again.Message);
            Assert.AreEqual(0, again.Data.Created); Assert.AreEqual(3, again.Data.Updated);
            Assert.AreEqual(3, env.E.Host.Read(u => u.Set<PcActivityResource>().Count()));
        }

        [TestMethod]
        public async Task The_Commit_Can_Create_A_New_Resource_Only_When_A_Row_Uses_It_And_Never_With_A_Rate()
        {
            var env = await ImportEnv("create");
            var request = new PcMspImportRequestDto
            {
                ContractId = env.Contract, Xml = SampleFile().Replace("</Resources>", Resource(4, "بدون ردیف") + "</Resources>"),
                Resources = new List<PcMspResourceMapDto>
                {
                    new PcMspResourceMapDto { MspUid = 1, ResourceId = env.Weld },
                    new PcMspResourceMapDto { MspUid = 3, Create = new PcMspNewResourceDto { Code = "NEW-1", Title = "ناشناخته", Kind = PcResourceKind.Labour, Unit = PcResourceUnit.Hour, Capacity = 4m } },
                    new PcMspResourceMapDto { MspUid = 4, Create = new PcMspNewResourceDto { Code = "NEW-UNUSED", Title = "بدون ردیف", Kind = PcResourceKind.Equipment, Unit = PcResourceUnit.Day, Capacity = 1m } },
                },
            };
            ResponseDto<PcMspImportResultDto> r = Svc<ResponseDto<PcMspImportResultDto>, IPcResourceService>(env.E, Planner, s => s.ImportAsync(request, Ct));
            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(1, r.Data.ResourcesCreated, "only the new resource that an imported row uses is created");
            Assert.AreEqual(1, env.E.Host.Read(u => u.Set<PcResource>().Count(x => x.Code == "NEW-1")));
            Assert.AreEqual(0, env.E.Host.Read(u => u.Set<PcResource>().Count(x => x.Code == "NEW-UNUSED")));
            Assert.AreEqual(0, env.E.Host.Read(u => u.Set<PcResourceRate>().Count()));
            // the mapping is validated: a duplicate code, both choices at once, an unknown MS Project resource
            request.Resources = new List<PcMspResourceMapDto> { new PcMspResourceMapDto { MspUid = 3, Create = new PcMspNewResourceDto { Code = "R-WELD", Title = "x", Kind = PcResourceKind.Labour, Unit = PcResourceUnit.Hour, Capacity = 4m } } };
            Assert.IsTrue(Svc<ResponseDto<PcMspImportResultDto>, IPcResourceService>(env.E, Planner, s => s.ImportAsync(request, Ct)).Message.Contains("تکراری"));
            request.Resources = new List<PcMspResourceMapDto> { new PcMspResourceMapDto { MspUid = 3, ResourceId = env.Weld, Create = new PcMspNewResourceDto { Code = "Q", Title = "x", Capacity = 1m } } };
            Assert.IsFalse(Svc<ResponseDto<PcMspImportResultDto>, IPcResourceService>(env.E, Planner, s => s.ImportAsync(request, Ct)).Success);
            request.Resources = new List<PcMspResourceMapDto> { new PcMspResourceMapDto { MspUid = 99, ResourceId = env.Weld } };
            Assert.IsFalse(Svc<ResponseDto<PcMspImportResultDto>, IPcResourceService>(env.E, Planner, s => s.ImportAsync(request, Ct)).Success);
        }

        [TestMethod]
        public async Task The_Import_Refuses_Other_Roles_A_Doctype_An_Oversized_File_And_A_Plan_That_Is_Not_Open()
        {
            var env = await ImportEnv("refuse");
            var good = new PcMspImportRequestDto { ContractId = env.Contract, Xml = SampleFile() };
            Assert.IsFalse(Svc<ResponseDto<PcMspPreviewDto>, IPcResourceService>(env.E, Engineer, s => s.PreviewImportAsync(good, Ct)).Success, "only planning and the PMO");
            Assert.IsFalse(Svc<ResponseDto<PcMspImportResultDto>, IPcResourceService>(env.E, Engineer, s => s.ImportAsync(good, Ct)).Success);
            // a DTD (the classic external entity) is refused, nothing is read from the system
            string xxe = "<?xml version=\"1.0\"?><!DOCTYPE foo [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><Project xmlns=\"" + Ns + "\"><Name>&xxe;</Name><Tasks/></Project>";
            ResponseDto<PcMspPreviewDto> bad = Svc<ResponseDto<PcMspPreviewDto>, IPcResourceService>(env.E, Planner, s => s.PreviewImportAsync(new PcMspImportRequestDto { ContractId = env.Contract, Xml = xxe }, Ct));
            Assert.IsFalse(bad.Success); Assert.IsTrue(bad.Message.Contains("معتبر نیست"), bad.Message);
            Assert.IsFalse(Svc<ResponseDto<PcMspImportResultDto>, IPcResourceService>(env.E, Planner, s => s.ImportAsync(new PcMspImportRequestDto { ContractId = env.Contract, Xml = xxe }, Ct)).Success);
            ResponseDto<PcMspPreviewDto> big = Svc<ResponseDto<PcMspPreviewDto>, IPcResourceService>(env.E, Planner, s => s.PreviewImportAsync(new PcMspImportRequestDto { ContractId = env.Contract, Xml = new string('a', PbMspdi.MaxXmlLength + 1) }, Ct));
            Assert.IsFalse(big.Success); Assert.IsTrue(big.Message.Contains("۸ مگابایت"), big.Message);
            Assert.IsFalse(Svc<ResponseDto<PcMspPreviewDto>, IPcResourceService>(env.E, Planner, s => s.PreviewImportAsync(new PcMspImportRequestDto { ContractId = env.Contract, Xml = "not xml" }, Ct)).Success);
            // a plan that is submitted or approved is not open: nothing is imported into it
            env.E.Host.Write(u => { u.Set<PcResourcePlan>().Single().Status = PcResourcePlanStatus.Submitted; u.SaveChanges(); }, Seeder);
            ResponseDto<PcMspPreviewDto> closed = Svc<ResponseDto<PcMspPreviewDto>, IPcResourceService>(env.E, Planner, s => s.PreviewImportAsync(good, Ct));
            Assert.IsFalse(closed.Success); Assert.IsTrue(closed.Message.Contains("باز"));
            // the shared reader keeps the old behaviour: tasks still parse and carry their WBS
            var tasks = PbMspdi.ParseWithWarnings(SampleFile());
            Assert.IsNull(tasks.Error); Assert.AreEqual("A1", tasks.Tasks[0].Wbs);
        }

        [TestMethod]
        public async Task The_Match_Order_Is_Code_Then_Title_Then_Normalised_Title_And_An_Ambiguous_Name_Matches_Nothing()
        {
            Env e = await NewEnv("order");
            int contract = NewContract(e, "CO", ("A1", Work(0), Work(4)));
            int byCode = NewResource(e, "WELD-X", PcResourceKind.Labour, PcResourceUnit.Hour, 3m);
            int byTitle = NewResource(e, "T-1", PcResourceKind.Labour, PcResourceUnit.Hour, 3m);
            int a = NewResource(e, "D-1", PcResourceKind.Labour, PcResourceUnit.Hour, 3m), b = NewResource(e, "D-2", PcResourceKind.Labour, PcResourceUnit.Hour, 3m);
            e.Host.Write(u =>
            {
                u.Set<PcResource>().Single(r => r.Id == byTitle).Title = "بازرس جوش";
                u.Set<PcResource>().Single(r => r.Id == a).Title = "راننده  لیفتراک";
                u.Set<PcResource>().Single(r => r.Id == b).Title = "راننده لیفتراک";
                u.SaveChanges();
            }, Seeder);
            Assert.IsTrue(Svc<ResponseDto<PcResourcePlanDto>, IPcResourceService>(e, Planner, s => s.OpenPlanAsync(new PcResourcePlanOpenDto { ContractId = contract }, Ct)).Success);
            string xml = Mspdi(Task(1, "فعالیت A1", "A1"), Resource(1, "x", "WELD-X") + Resource(2, "بازرس جوش") + Resource(3, "راننده لیفتراک") + Resource(4, "WELD-X"), Asg(1, 1, "PT8H0M0S"));
            PcMspPreviewDto p = Svc<ResponseDto<PcMspPreviewDto>, IPcResourceService>(e, Planner, s => s.PreviewImportAsync(new PcMspImportRequestDto { ContractId = contract, Xml = xml }, Ct)).Data;
            Assert.AreEqual("code", p.Resources.Single(x => x.MspUid == 1).MatchKind);
            Assert.AreEqual("code", p.Resources.Single(x => x.MspUid == 4).MatchKind, "the name is compared with the code too");
            Assert.AreEqual("title", p.Resources.Single(x => x.MspUid == 2).MatchKind); Assert.AreEqual(byTitle, p.Resources.Single(x => x.MspUid == 2).SuggestedResourceId);
            // two pool resources have the same normalised title: the name matches the exact one (the second), and a name that fits two only by normalisation matches none
            Assert.AreEqual("title", p.Resources.Single(x => x.MspUid == 3).MatchKind); Assert.AreEqual(b, p.Resources.Single(x => x.MspUid == 3).SuggestedResourceId);
            Assert.AreEqual(byCode, p.Resources.Single(x => x.MspUid == 1).SuggestedResourceId);
        }

        // ------------------------------------------------------------------------------------------------ 4d-2: one truth with production

        private static int NewCentre(Env e, string code, int parallel = 1, decimal efficiency = 100m, decimal utilisation = 100m, decimal hoursPerShift = 8m, int shifts = 1)
        {
            int id = 0;
            e.Host.Write(u =>
            {
                MfWorkCenter w = AppHost.Add(u, new MfWorkCenter { Code = code, Title = "مرکز " + code, Kind = MfWorkCenterKind.Machine, ShiftsPerDay = shifts, HoursPerShift = hoursPerShift, EfficiencyPercent = efficiency, UtilisationPercent = utilisation, ParallelResources = parallel, IsActive = true });
                u.SaveChanges();
                id = w.Id;
            }, Seeder);
            return id;
        }

        private static int NewOrder(Env e, string number, int contract, int centre, decimal hours)
        {
            int id = 0;
            e.Host.Write(u =>
            {
                var order = AppHost.Add(u, new MfProductionOrder { Number = number, WorkOrderId = 1, ContractId = contract, Quantity = 1, Unit = "عدد", Status = MfOrderStatus.Released });
                order.Operations.Add(new MfOperation { Sequence = 10, Title = "تراشکاری", WorkCenterId = centre, RunHoursPlanned = hours, Status = MfOperationStatus.Ready });
                u.SaveChanges();
                id = order.Id;
            }, Seeder);
            return id;
        }

        [TestMethod]
        public async Task A_Machine_Linked_To_A_Work_Centre_Reads_Its_Capacity_And_Calendar_From_It()
        {
            Env e = await NewEnv("readthrough");
            int wc = NewCentre(e, "WC-1", parallel: 2, efficiency: 90m, utilisation: 80m, hoursPerShift: 8m);
            // the typed capacity (5) is not used: parallel resources x efficiency x utilisation = 2 x 0.9 x 0.8
            ResponseDto<PcResourceDto> saved = Svc<ResponseDto<PcResourceDto>, IPcResourceService>(e, Planner, s => s.SaveResourceAsync(new PcResourceDto { Code = "M-1", Title = "تراش", Kind = PcResourceKind.Machine, Unit = PcResourceUnit.Hour, Capacity = 5m, WorkCenterId = wc }, Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.AreEqual(1.44m, saved.Data.Capacity); Assert.IsTrue(saved.Data.CapacityFromWorkCenter); Assert.AreEqual("WC-1", saved.Data.WorkCenterCode); Assert.AreEqual(8m, saved.Data.WorkCenterDayHours);
            // the work centre changes: the resource follows without being saved again
            e.Host.Write(u => { u.Set<MfWorkCenter>().Single().ParallelResources = 3; u.SaveChanges(); }, Seeder);
            PcResourceDto read = Svc<ResponseDto<PcResourceDto>, IPcResourceService>(e, Planner, s => s.GetResourceAsync(saved.Data.Id, Ct)).Data;
            Assert.AreEqual(2.16m, read.Capacity);
            // a linked machine is counted in hours; a machine without a link keeps its own capacity
            ResponseDto<PcResourceDto> days = Svc<ResponseDto<PcResourceDto>, IPcResourceService>(e, Planner, s => s.SaveResourceAsync(new PcResourceDto { Code = "M-2", Title = "x", Kind = PcResourceKind.Machine, Unit = PcResourceUnit.Day, Capacity = 1m, WorkCenterId = wc }, Ct));
            Assert.IsFalse(days.Success); Assert.IsTrue(days.Message.Contains("ساعت"));
            PcResourceDto free = Svc<ResponseDto<PcResourceDto>, IPcResourceService>(e, Planner, s => s.SaveResourceAsync(new PcResourceDto { Code = "M-3", Title = "y", Kind = PcResourceKind.Machine, Unit = PcResourceUnit.Hour, Capacity = 2m }, Ct)).Data;
            Assert.AreEqual(2m, free.Capacity); Assert.IsFalse(free.CapacityFromWorkCenter);
        }

        [TestMethod]
        public async Task Only_A_Machine_Counted_In_Hours_Reads_Through_And_An_Existing_Day_Link_Stays_Editable_With_Its_Own_Numbers()
        {
            Env e = await NewEnv("dayLink");
            int wc = NewCentre(e, "WC-D", parallel: 4);
            int day = NewResource(e, "M-DAY", PcResourceKind.Machine, PcResourceUnit.Day, 2m, workCenter: wc);
            PcResourceDto read = Svc<ResponseDto<PcResourceDto>, IPcResourceService>(e, Planner, s => s.GetResourceAsync(day, Ct)).Data;
            Assert.AreEqual(2m, read.Capacity); Assert.IsFalse(read.CapacityFromWorkCenter);
            PcResourceLoadRowDto row = Svc<ResponseDto<PcResourceLoadDto>, IPcResourceLoadService>(e, Planner, s => s.GetLoadAsync(new PcResourceLoadQuery { ResourceId = day, From = Work(0), To = Work(2), Bucket = "day" }, Ct)).Data.Resources.Single();
            Assert.IsNull(row.WorkCenterId); Assert.AreEqual(2m, row.Capacity); Assert.AreEqual(0m, row.Buckets.Sum(b => b.ProductionLoad));
            // the old row can still be saved as it is, with its own capacity
            PcResourceDto dto = read; dto.Title = "عنوان تازه"; dto.Capacity = 3m;
            ResponseDto<PcResourceDto> saved = Svc<ResponseDto<PcResourceDto>, IPcResourceService>(e, Planner, s => s.SaveResourceAsync(dto, Ct));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.AreEqual(3m, saved.Data.Capacity); Assert.IsFalse(saved.Data.CapacityFromWorkCenter);
            // but a changed link to a Day machine is refused
            int wc2 = NewCentre(e, "WC-D2");
            PcResourceDto moved = saved.Data; moved.WorkCenterId = wc2;
            Assert.IsFalse(Svc<ResponseDto<PcResourceDto>, IPcResourceService>(e, Planner, s => s.SaveResourceAsync(moved, Ct)).Success);
        }

        [TestMethod]
        public async Task A_Work_Centre_Has_One_Active_Machine_And_Existing_Duplicates_Keep_Working()
        {
            Env e = await NewEnv("oneMachine");
            int wc = NewCentre(e, "WC-O");
            ResponseDto<PcResourceDto> first = Svc<ResponseDto<PcResourceDto>, IPcResourceService>(e, Planner, s => s.SaveResourceAsync(new PcResourceDto { Code = "M-A", Title = "الف", Kind = PcResourceKind.Machine, Unit = PcResourceUnit.Hour, Capacity = 1m, WorkCenterId = wc }, Ct));
            Assert.IsTrue(first.Success, first.Message);
            ResponseDto<PcResourceDto> second = Svc<ResponseDto<PcResourceDto>, IPcResourceService>(e, Planner, s => s.SaveResourceAsync(new PcResourceDto { Code = "M-B", Title = "ب", Kind = PcResourceKind.Machine, Unit = PcResourceUnit.Hour, Capacity = 1m, WorkCenterId = wc }, Ct));
            Assert.IsFalse(second.Success); Assert.IsTrue(second.Message.Contains("M-A"));
            // an old duplicate (seeded directly) can still be edited without a new link
            int dup = NewResource(e, "M-OLD", PcResourceKind.Machine, PcResourceUnit.Hour, 1m, workCenter: wc);
            PcResourceDto old = Svc<ResponseDto<PcResourceDto>, IPcResourceService>(e, Planner, s => s.GetResourceAsync(dup, Ct)).Data;
            old.Title = "ویرایش";
            Assert.IsTrue(Svc<ResponseDto<PcResourceDto>, IPcResourceService>(e, Planner, s => s.SaveResourceAsync(old, Ct)).Success);
        }

        [TestMethod]
        public async Task A_Production_Order_Tied_To_An_Activity_Of_The_Linked_Machine_Is_Counted_Once_In_Both_Views()
        {
            Env e = await NewEnv("tied", true);
            List<DateTime> days = FirstWorkingDays(3);
            int wc = NewCentre(e, "WC-T");
            int machine = NewResource(e, "M-TIE", PcResourceKind.Machine, PcResourceUnit.Hour, 1m, workCenter: wc);
            int contract = NewContract(e, "CT", ("A1", days[0], days[1]));
            ApprovedPlan(e, contract, ("CT:A1", machine, 10m));
            int activity = e.Activities["CT:A1"];
            int order = NewOrder(e, "PO-T", contract, wc, 10m);
            e.Host.Write(u => { u.Set<MfProductionOrder>().Single(o => o.Id == order).ActivityId = activity; u.SaveChanges(); }, Seeder);
            PcResourceLoadRowDto row = Svc<ResponseDto<PcResourceLoadDto>, IPcResourceLoadService>(e, Planner, s => s.GetLoadAsync(new PcResourceLoadQuery { ResourceId = machine, From = days[0], To = days[2], Bucket = "day" }, Ct)).Data.Resources.Single();
            Assert.AreEqual(10m, row.Buckets.Sum(b => b.Load), "the project's hours");
            Assert.AreEqual(0m, row.Buckets.Sum(b => b.ProductionLoad), "the same work is not counted again as production");
            LoadReportDto report = Svc<ResponseDto<LoadReportDto>, IMfScheduleService>(e, ProductionManager, s => s.GetLoadAsync(new LoadQuery { From = days[0], Weeks = 2 }, Ct)).Data;
            LoadRowDto r = report.Rows.Single(x => x.Code == "WC-T");
            Assert.AreEqual(10m, r.Cells.Sum(c => c.ProjectLoadHours) + r.Cells.Sum(c => c.LoadHours), "the work is in one layer only");
        }

        [TestMethod]
        public async Task The_Two_Views_Show_Each_Others_Load_As_A_Separate_Layer_And_Stay_Unchanged_Without_A_Link()
        {
            Env e = await NewEnv("layers", true);
            List<DateTime> days = FirstWorkingDays(3);
            int wc = NewCentre(e, "WC-L"), other = NewCentre(e, "WC-U");
            int linked = NewResource(e, "M-LINK", PcResourceKind.Machine, PcResourceUnit.Hour, 9m, workCenter: wc);
            int unlinked = NewResource(e, "M-FREE", PcResourceKind.Machine, PcResourceUnit.Hour, 1m);
            int contract = NewContract(e, "CL", ("A1", days[0], days[1]), ("A2", days[0], days[1]));
            // the project puts 10 hours on the linked machine (5 on each of two days) and 10 on the free one
            ApprovedPlan(e, contract, ("CL:A1", linked, 10m), ("CL:A2", unlinked, 10m));
            // production: 20 hours on the linked centre (8, 8 and 4 on the first three working days), 6 hours on the other centre
            NewOrder(e, "PO-1", 999, wc, 20m);
            NewOrder(e, "PO-2", 999, other, 6m);

            // the histogram of the linked machine: the production layer is apart and counts toward the over-allocation
            PcResourceLoadRowDto row = Svc<ResponseDto<PcResourceLoadDto>, IPcResourceLoadService>(e, Planner, s => s.GetLoadAsync(new PcResourceLoadQuery { ResourceId = linked, From = days[0], To = days[2], Bucket = "day" }, Ct)).Data.Resources.Single();
            Assert.AreEqual(wc, row.WorkCenterId); Assert.AreEqual(1m, row.Capacity);
            Assert.AreEqual(3, row.Buckets.Count);
            Assert.AreEqual(5m, row.Buckets[0].Load); Assert.AreEqual(8m, row.Buckets[0].ProductionLoad); Assert.AreEqual(8m, row.Buckets[0].Capacity); Assert.AreEqual(5m, row.Buckets[0].Over);
            Assert.AreEqual(5m, row.Buckets[1].Load); Assert.AreEqual(8m, row.Buckets[1].ProductionLoad); Assert.AreEqual(5m, row.Buckets[1].Over);
            Assert.AreEqual(0m, row.Buckets[2].Load); Assert.AreEqual(4m, row.Buckets[2].ProductionLoad); Assert.AreEqual(0m, row.Buckets[2].Over, "4 hours of production in a day of 8 is within the capacity");
            // the machine without a link: no production layer at all, the numbers are those of before
            PcResourceLoadRowDto free = Svc<ResponseDto<PcResourceLoadDto>, IPcResourceLoadService>(e, Planner, s => s.GetLoadAsync(new PcResourceLoadQuery { ResourceId = unlinked, From = days[0], To = days[2], Bucket = "day" }, Ct)).Data.Resources.Single();
            Assert.IsNull(free.WorkCenterId); Assert.AreEqual(0m, free.Buckets.Sum(b => b.ProductionLoad)); Assert.AreEqual(5m, free.Buckets[0].Load); Assert.AreEqual(1m, free.Capacity);

            // the production view: the project layer of the linked centre, apart from the work orders' load
            LoadReportDto report = Svc<ResponseDto<LoadReportDto>, IMfScheduleService>(e, ProductionManager, s => s.GetLoadAsync(new LoadQuery { From = days[0], Weeks = 2 }, Ct)).Data;
            LoadRowDto linkedRow = report.Rows.Single(r => r.Code == "WC-L"), otherRow = report.Rows.Single(r => r.Code == "WC-U");
            Assert.AreEqual(10m, linkedRow.Cells.Sum(c => c.ProjectLoadHours)); Assert.AreEqual(20m, linkedRow.Cells.Sum(c => c.LoadHours));
            Assert.IsTrue(linkedRow.Cells.All(c => c.HasProjectLayer));
            foreach (LoadCellDto c in linkedRow.Cells)
            {
                Assert.AreEqual(Math.Max(0m, c.CapacityHours - c.ProjectLoadHours), c.AvailableHours, "the project layer reduces what is left for the work orders");
                if (c.CapacityHours > 0m) Assert.AreEqual(Math.Round((c.LoadHours + c.ProjectLoadHours) / c.CapacityHours * 100m, 1, MidpointRounding.AwayFromZero), c.LoadPercent);
            }
            Assert.AreEqual(0m, otherRow.Cells.Sum(c => c.ProjectLoadHours)); Assert.IsTrue(otherRow.Cells.All(c => !c.HasProjectLayer));
            Assert.AreEqual(6m, otherRow.Cells.Sum(c => c.LoadHours));
            foreach (LoadCellDto c in otherRow.Cells)
            {
                Assert.AreEqual(c.CapacityHours, c.AvailableHours);
                Assert.AreEqual(c.CapacityHours > 0m ? Math.Round(c.LoadHours / c.CapacityHours * 100m, 1, MidpointRounding.AwayFromZero) : (decimal?)null, c.LoadPercent, "without a link the percentage is the production's own");
            }

            // the agent's check counts the production layer toward the over-allocation of the linked machine, and only of that one
            ResponseDto<PcLoadCheckResultDto> check = await AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats["AG-PLAN"], "AG-PLAN",
                async sp => { using (AgentStepContext.Enter("RES.LOAD.CHECK")) return await sp.GetRequiredService<IPcResourceLoadService>().CheckOverloadsAsync(Ct); });
            Assert.IsTrue(check.Success, check.Message);
            List<PcResourceOverload> overloads = e.Host.Read(u => u.Set<PcResourceOverload>().AsNoTracking().ToList());
            Assert.AreEqual(1, overloads.Count);
            Assert.AreEqual(linked, overloads[0].ResourceId); Assert.AreEqual(days[0], overloads[0].FromDate); Assert.AreEqual(days[1], overloads[0].ToDate);
            Assert.AreEqual(13m, overloads[0].PeakLoad); Assert.AreEqual(8m, overloads[0].PeakCapacity);
        }

        // ------------------------------------------------------------------------------------------------ 4d-3: actual cost, planned value and performance

        private sealed class Perf
        {
            public Env E;
            public int Contract, Machine, Labour, Small, Material, Sub, Centre, Provider;
        }

        private static async Task<Perf> PerfEnv(string name, int bookingEmployees)
        {
            Env e = await NewEnv(name);
            var p = new Perf { E = e };
            p.Centre = NewCentre(e, "WC-P");
            e.Host.Write(u => { p.Provider = AppHost.Add(u, new Provider { Name = "پیمانکار" }).Id; u.SaveChanges(); }, Seeder);
            p.Machine = NewResource(e, "P-MACH", PcResourceKind.Machine, PcResourceUnit.Hour, 1m, workCenter: p.Centre);
            p.Labour = NewResource(e, "P-WELD", PcResourceKind.Labour, PcResourceUnit.Hour, 3m);
            p.Small = NewResource(e, "P-ONE", PcResourceKind.Labour, PcResourceUnit.Hour, 1m);
            p.Material = NewResource(e, "P-MAT", PcResourceKind.Material, PcResourceUnit.Each, 1m);
            p.Sub = NewResource(e, "P-SUB", PcResourceKind.Subcontract, PcResourceUnit.Each, 1m, provider: p.Provider);
            ApproveRate(e, p.Machine, 1000m); ApproveRate(e, p.Labour, 500m); ApproveRate(e, p.Small, 400m); ApproveRate(e, p.Material, 10m); ApproveRate(e, p.Sub, 100m);
            p.Contract = NewContract(e, "CP", ("A1", Work(0), Work(4)));
            int other = NewContract(e, "CQ", ("A1", Work(0), Work(4)));
            var rows = new[] { ("CP:A1", p.Machine, 10m), ("CP:A1", p.Labour, 20m), ("CP:A1", p.Small, 5m), ("CP:A1", p.Material, 100m), ("CP:A1", p.Sub, 3m) };
            ApprovedPlan(e, p.Contract, rows);
            e.Host.Write(u =>
            {
                PcBaseline b = AppHost.Add(u, new PcBaseline { ContractId = p.Contract, Number = 0, Kind = PcBaselineKind.Master, Status = PcBaselineStatus.Approved, ProjectStart = Work(0), ProjectFinish = Work(4), ApprovedAt = DateTimeOffset.Now, ApprovedByUserId = 1011 });
                u.SaveChanges();
                AppHost.Add(u, new PcBaselineActivity { BaselineId = b.Id, ActivityId = e.Activities["CP:A1"], Code = "A1", Title = "فعالیت A1", Start = Work(0), Finish = Work(4), DurationDays = 5 });
                foreach (var r in rows) AppHost.Add(u, new PcBaselineResource { BaselineId = b.Id, ActivityId = e.Activities["CP:A1"], ResourceId = r.Item2, Quantity = r.Item3, Distribution = PcResourceDistribution.Uniform, RateAtBaseline = r.Item2 == p.Machine ? 1000m : r.Item2 == p.Labour ? 500m : r.Item2 == p.Small ? 400m : r.Item2 == p.Material ? 10m : 100m });
                u.Set<PcContractTerms>().Single(t => t.ContractId == p.Contract).CurrentBaselineId = b.Id;
                u.SaveChanges();
                // production: an order of this project with an operation at the linked centre, booked by `bookingEmployees` people for 10 hours in all; an order of another project that must not count
                var order = AppHost.Add(u, new MfProductionOrder { Number = "PO-P", WorkOrderId = 1, ContractId = p.Contract, Quantity = 1, Status = MfOrderStatus.InProgress });
                order.Operations.Add(new MfOperation { Sequence = 10, Title = "تراش", WorkCenterId = p.Centre, RunHoursPlanned = 10m, Status = MfOperationStatus.InProgress });
                var foreign = AppHost.Add(u, new MfProductionOrder { Number = "PO-Q", WorkOrderId = 2, ContractId = other, Quantity = 1, Status = MfOrderStatus.InProgress });
                foreign.Operations.Add(new MfOperation { Sequence = 10, Title = "تراش", WorkCenterId = p.Centre, RunHoursPlanned = 10m, Status = MfOperationStatus.InProgress });
                u.SaveChanges();
                int op = order.Operations.Single().Id, foreignOp = foreign.Operations.Single().Id;
                for (int i = 1; i <= bookingEmployees; i++)
                    AppHost.Add(u, new MfTimeBooking { OperationId = op, EmployeeId = 10 + i, Kind = MfBookingKind.Run, StartAt = Work(0).AddHours(8), Hours = bookingEmployees == 2 ? 5m : 10m / bookingEmployees, Minutes = 300, Source = MfBookingSource.Terminal });
                AppHost.Add(u, new MfTimeBooking { OperationId = op, EmployeeId = 11, Kind = MfBookingKind.Waiting, StartAt = Work(0).AddHours(8), Hours = 3m, Minutes = 180, Source = MfBookingSource.Terminal });
                AppHost.Add(u, new MfTimeBooking { OperationId = foreignOp, EmployeeId = 11, Kind = MfBookingKind.Run, StartAt = Work(0).AddHours(8), Hours = 50m, Minutes = 3000, Source = MfBookingSource.Terminal });
                // purchase orders: one sent, one still a draft, one of another project
                AppHost.Add(u, new PurchaseOrder { Number = "PO-S1", Status = PurchaseOrderStatus.Released, Kind = PurchaseKind.Subcontract, OrderDate = DateTime.Today, ProviderId = p.Provider, ContractId = p.Contract, TotalAmount = 7000m });
                AppHost.Add(u, new PurchaseOrder { Number = "PO-S2", Status = PurchaseOrderStatus.Draft, Kind = PurchaseKind.Subcontract, OrderDate = DateTime.Today, ProviderId = p.Provider, ContractId = p.Contract, TotalAmount = 9999m });
                AppHost.Add(u, new PurchaseOrder { Number = "PO-S3", Status = PurchaseOrderStatus.Released, Kind = PurchaseKind.Subcontract, OrderDate = DateTime.Today, ProviderId = p.Provider, ContractId = other, TotalAmount = 5555m });
                // warehouse: an issue of this project (2500) and one of another
                InventoryDocument d1 = AppHost.Add(u, new InventoryDocument { Type = InventoryDocumentType.Issue, Number = "IS-1", Status = InventoryDocumentStatus.Confirmed, WarehouseId = 1, DocumentDate = DateTime.Today, ContractId = p.Contract });
                InventoryDocument d2 = AppHost.Add(u, new InventoryDocument { Type = InventoryDocumentType.Issue, Number = "IS-2", Status = InventoryDocumentStatus.Confirmed, WarehouseId = 1, DocumentDate = DateTime.Today, ContractId = other });
                u.SaveChanges();
                AppHost.Add(u, new StockTransaction { ItemId = 1, WarehouseId = 1, Quantity = -5m, CostAmount = -2500m, TransactionDate = DateTime.Today, DocumentId = d1.Id });
                AppHost.Add(u, new StockTransaction { ItemId = 1, WarehouseId = 1, Quantity = -5m, CostAmount = -999m, TransactionDate = DateTime.Today, DocumentId = d2.Id });
                u.SaveChanges();
            }, Seeder);
            return p;
        }

        private static PcResourcePerformanceDto Performance(Perf p, ClaimsPrincipal who, DateTime? asOf = null) =>
            Svc<ResponseDto<PcResourcePerformanceDto>, IPcResourcePerformanceService>(p.E, who, s => s.GetPerformanceAsync(p.Contract, asOf ?? DateTime.Today.AddDays(60), Ct)).Data;

        [TestMethod]
        public async Task The_Performance_View_Prices_Machine_Hours_Orders_And_Issues_Of_The_Project_Only_And_Reports_What_Is_Not_Tied()
        {
            Perf p = await PerfEnv("perf", 2);
            PcResourcePerformanceDto d = Performance(p, Payroll);
            Assert.IsTrue(d.HasBaseline);
            PcPerfRowDto machine = d.Rows.Single(r => r.ResourceId == p.Machine);
            Assert.AreEqual(10m, machine.BaselineQuantity); Assert.AreEqual(10000m, machine.BaselineAmount);
            // the bookings of the project at the linked centre: 5 + 5 hours (waiting and the other project's hours do not count) at the approved rate 1000
            Assert.AreEqual(10m, machine.ActualQuantity); Assert.AreEqual(10000m, machine.ActualAmount);
            Assert.AreEqual(0m, machine.VarianceQuantity); Assert.AreEqual(0m, machine.VarianceAmount);
            Assert.AreEqual(7000m, d.Rows.Single(r => r.ResourceId == p.Sub).ActualAmount, "the sent order of the project and the supplier: not the draft, not the other project's");
            PcPerfRowDto material = d.ByClass.Single(c => c.ResourceClass == PcResourceClass.Material);
            Assert.AreEqual(2500m, material.ActualAmount, "the issue of the project only");
            // labour: aggregated to the class, never a resource row, no amount
            PcPerfRowDto labour = d.ByClass.Single(c => c.ResourceClass == PcResourceClass.Labour);
            Assert.IsNull(labour.ResourceId); Assert.IsNull(labour.ActualAmount);
            Assert.AreEqual(10m, labour.ActualQuantity, "payroll sees the hours of a group of two");
            Assert.IsTrue(d.Rows.Where(r => r.ResourceClass == PcResourceClass.Labour).All(r => r.ActualQuantity == null && r.ActualAmount == null));
            Assert.IsTrue(d.Ties.Any(t => !t.Tied));
            Assert.AreEqual(PcResourceClass.Equipment, d.ByClass.Single(c => c.ResourceClass == PcResourceClass.Equipment).ResourceClass);
        }

        [TestMethod]
        public async Task A_Reader_Outside_Payroll_Never_Sees_A_Small_Groups_Cost_Or_Hours_And_A_Group_Of_Three_Is_Shown()
        {
            Perf p = await PerfEnv("mask", 2);
            PcResourcePerformanceDto planner = Performance(p, Planner), payroll = Performance(p, Payroll);
            PcPerfRowDto small = planner.Rows.Single(r => r.ResourceId == p.Small);
            Assert.IsTrue(small.RateHidden); Assert.IsNull(small.BaselineAmount); Assert.IsNull(small.PlanAmount); Assert.IsNull(small.PlannedToDateAmount);
            Assert.IsTrue(planner.RateHidden); Assert.AreEqual(1, planner.HiddenLineCount);
            PcPerfRowDto smallPayroll = payroll.Rows.Single(r => r.ResourceId == p.Small);
            Assert.AreEqual(2000m, smallPayroll.BaselineAmount); Assert.IsFalse(smallPayroll.RateHidden);
            // the totals of the planner leave the hidden line out; the payroll roles' include it
            Assert.AreEqual(payroll.BaselineTotal - 2000m, planner.BaselineTotal);
            Assert.AreEqual(payroll.PlanTotal - 2000m, planner.PlanTotal);
            // two people booked: the hours of that small group are not shown to the planner, but the class row still says why
            PcPerfRowDto labour = planner.ByClass.Single(c => c.ResourceClass == PcResourceClass.Labour);
            Assert.IsNull(labour.ActualQuantity); Assert.IsTrue(labour.ActualNote.Contains("محرمانگی"));
            // three people booked: the group is large enough and the planner sees the total (still no amount, still no person)
            Perf q = await PerfEnv("mask3", 3);
            PcPerfRowDto three = Performance(q, Planner).ByClass.Single(c => c.ResourceClass == PcResourceClass.Labour);
            Assert.IsNotNull(three.ActualQuantity); Assert.IsNull(three.ActualAmount);
            Assert.IsTrue(Math.Abs(three.ActualQuantity.Value - 10m) < 0.01m);
            // a role outside the readers is refused
            Assert.IsFalse(Svc<ResponseDto<PcResourcePerformanceDto>, IPcResourcePerformanceService>(p.E, AppHost.Principal(1701, "operator", "OPERATOR"), s => s.GetPerformanceAsync(p.Contract, null, Ct)).Success);
        }

        [TestMethod]
        public async Task The_Tie_Notes_Never_Show_The_Hours_Of_A_Small_Group_To_A_Reader_Outside_Payroll()
        {
            Perf p = await PerfEnv("tienote", 2);
            string planner = string.Join(" ", Performance(p, Planner).Ties.Select(t => t.Note));
            string payroll = string.Join(" ", Performance(p, Payroll).Ties.Select(t => t.Note));
            Assert.IsFalse(planner.Contains("10 ساعت"), planner);
            Assert.IsTrue(planner.Contains("گروه کوچک"));
            Assert.IsTrue(payroll.Contains("10 ساعت"), payroll);
            Perf big = await PerfEnv("tienote3", 3);
            Assert.IsTrue(string.Join(" ", Performance(big, Planner).Ties.Select(t => t.Note)).Contains("10 ساعت"), "a group of three is shown");
        }

        [TestMethod]
        public async Task The_Planned_Value_Is_Time_Phased_Over_The_Baseline_Dates_And_Adds_Up_To_The_Baseline_Amount()
        {
            Perf p = await PerfEnv("pv", 2);
            PcResourcePerformanceDto all = Performance(p, Payroll);
            // the periods (Persian months) add up to the baseline amount exactly, in total and per class
            Assert.AreEqual(all.BaselineTotal, all.Periods.Sum(x => x.Amount ?? 0m));
            Assert.AreEqual(10_000m + 10_000m + 2_000m + 1_000m + 300m, all.BaselineTotal);
            foreach (PcResourceClass cls in new[] { PcResourceClass.Labour, PcResourceClass.Equipment, PcResourceClass.Material, PcResourceClass.Subcontract })
                Assert.AreEqual(all.ByClass.Single(c => c.ResourceClass == cls).BaselineAmount ?? 0m, all.Periods.Where(x => x.ResourceClass == cls).Sum(x => x.Amount ?? 0m), cls.ToString());
            Assert.AreEqual(all.BaselineTotal, all.PlannedToDateTotal, "after the baseline finish the whole value is planned");
            // the first two of the five working days: two fifths of the machine's value
            PcResourcePerformanceDto early = Performance(p, Payroll, Work(1));
            PcPerfRowDto machine = early.Rows.Single(r => r.ResourceId == p.Machine);
            Assert.AreEqual(4m, machine.PlannedToDateQuantity); Assert.AreEqual(4000m, machine.PlannedToDateAmount);
            Assert.AreEqual(machine.ActualQuantity - 4m, machine.VarianceQuantity);
            // a viewer outside payroll: the periods still add up to what the viewer's total says
            PcResourcePerformanceDto planner = Performance(p, Planner);
            Assert.AreEqual(planner.BaselineTotal, planner.Periods.Sum(x => x.Amount ?? 0m));
        }

        [TestMethod]
        public async Task A_Resource_Without_An_Approved_Rate_Is_Counted_And_Not_Priced()
        {
            Perf p = await PerfEnv("norate", 2);
            e_RemoveRate(p, p.Material);
            PcResourcePerformanceDto d = Performance(p, Payroll);
            PcPerfRowDto material = d.Rows.Single(r => r.ResourceId == p.Material);
            Assert.IsTrue(material.NoApprovedRate); Assert.IsNull(material.PlanAmount); Assert.AreEqual("بدون نرخ مصوب", material.Note);
            Assert.AreEqual(1, d.NoRateCount);
        }

        private static void e_RemoveRate(Perf p, int resourceId) => p.E.Host.Write(u =>
        {
            foreach (PcResourceRate r in u.Set<PcResourceRate>().Where(x => x.ResourceId == resourceId).ToList()) u.Set<PcResourceRate>().Remove(r);
            foreach (PcBaselineResource b in u.Set<PcBaselineResource>().Where(x => x.ResourceId == resourceId).ToList()) b.RateAtBaseline = null;
            u.SaveChanges();
        }, Seeder);

        // ------------------------------------------------------------------------------------------------ 4d-3: the cost-baseline proposal

        private static async Task Settle(Env e)
        {
            for (int i = 0; i < 3; i++)
                foreach (string seat in new[] { "AG-MASTER", "AG-PLAN", "AG-MASTER" })
                {
                    AiSeatJobResult r = await AiActing.RunAsync(e.Host.Provider.GetRequiredService<IServiceScopeFactory>(), e.Seats[seat], seat, sp => sp.GetServices<IAiSeatJob>().Single(j => j.Code == seat).RunAsync(new AiSeatRunContext { AsOf = DateTime.Today }, Ct));
                    Assert.AreEqual(AiSeatRunStatus.Succeeded, r.Status, r.Error);
                }
        }

        private static List<AgentTask> Open(Env e, string step) => e.Host.Read(u => u.Set<AgentTask>().AsNoTracking().Where(t => t.StepCode == step && t.State < AgentTaskState.Done).ToList());

        private static async Task<(Env E, int Contract, int Labour, int Small)> CostEnv(string name)
        {
            Env e = await NewEnv(name, true);
            int labour = NewResource(e, "C-WELD", PcResourceKind.Labour, PcResourceUnit.Hour, 3m), small = NewResource(e, "C-ONE", PcResourceKind.Labour, PcResourceUnit.Hour, 1m), equip = NewResource(e, "C-EQ", PcResourceKind.Equipment, PcResourceUnit.Day, 1m);
            ApproveRate(e, labour, 100m); ApproveRate(e, small, 200m);
            int contract = NewContract(e, "CC", ("A1", Work(0), Work(4)));
            ApprovedPlan(e, contract, ("CC:A1", labour, 10m), ("CC:A1", small, 5m), ("CC:A1", equip, 2m));
            return (e, contract, labour, small);
        }

        [TestMethod]
        public async Task The_Agent_Proposes_Cost_Lines_By_Class_Never_Applies_Them_And_A_Human_Accepts_Through_The_Cost_Baseline_Path()
        {
            var env = await CostEnv("cost");
            await Settle(env.E);
            List<PcResourceCostProposal> proposals = env.E.Host.Read(u => u.Set<PcResourceCostProposal>().AsNoTracking().ToList());
            // one proposal: the labour class, 10 hours x 100; the small group is excluded (counted), the equipment has no rate (no amount, no proposal)
            Assert.AreEqual(1, proposals.Count, string.Join(" | ", proposals.Select(x => x.ResourceClass + ":" + x.Amount)));
            Assert.AreEqual(PcResourceClass.Labour, proposals[0].ResourceClass); Assert.AreEqual(1000m, proposals[0].Amount);
            Assert.AreEqual(1, proposals[0].ExcludedSensitive); Assert.AreEqual(2, proposals[0].LineCount); Assert.AreEqual(PcCostProposalStatus.Proposed, proposals[0].Status); Assert.IsTrue(proposals[0].PreparedByAgent);
            // never applied by itself: the cost baseline has no line, the gate waits for a person
            Assert.AreEqual(0, env.E.Host.Read(u => u.Set<PcCostBaselineLine>().Count()));
            Assert.AreEqual(1, Open(env.E, "RES.COST.REVIEW").Count);
            Assert.AreEqual(0, Open(env.E, "RES.COST.PROPOSE").Count);
            // the agent does not repeat itself for the same plan
            await Settle(env.E);
            Assert.AreEqual(1, env.E.Host.Read(u => u.Set<PcResourceCostProposal>().Count()));

            // the owner of the cost baseline accepts: the line is written by the existing path (a person with the planning role)
            Assert.IsFalse(Svc<ResponseDto<PcResourceCostProposalDto>, IPcResourcePerformanceService>(env.E, Engineer, s => s.DecideProposalAsync(new PcCostProposalDecideDto { Id = proposals[0].Id, Action = "accept" }, Ct)).Success);
            ResponseDto<PcResourceCostProposalDto> accepted = Svc<ResponseDto<PcResourceCostProposalDto>, IPcResourcePerformanceService>(env.E, Planner, s => s.DecideProposalAsync(new PcCostProposalDecideDto { Id = proposals[0].Id, Action = "accept" }, Ct));
            Assert.IsTrue(accepted.Success, accepted.Message);
            Assert.AreEqual(PcCostProposalStatus.Accepted, accepted.Data.Status); Assert.IsNotNull(accepted.Data.CostLineId);
            PcCostBaselineLine line = env.E.Host.Read(u => u.Set<PcCostBaselineLine>().AsNoTracking().Single());
            Assert.AreEqual(PcResourceClass.Labour, line.ResourceClass); Assert.AreEqual(1000m, line.Amount); Assert.AreEqual(env.Contract, line.ContractId); Assert.IsNull(line.BaselineId);
            Assert.IsFalse(Svc<ResponseDto<PcResourceCostProposalDto>, IPcResourcePerformanceService>(env.E, Planner, s => s.DecideProposalAsync(new PcCostProposalDecideDto { Id = proposals[0].Id, Action = "accept" }, Ct)).Success, "decided once");
            await Settle(env.E);
            Assert.AreEqual(0, Open(env.E, "RES.COST.REVIEW").Count, "the gate closes when nothing is left to decide");
        }

        [TestMethod]
        public async Task A_Rejection_Needs_A_Reason_Writes_Nothing_And_Is_Not_Proposed_Again_And_A_Locked_Cost_Baseline_Gets_No_Proposal()
        {
            var env = await CostEnv("reject");
            await Settle(env.E);
            int id = env.E.Host.Read(u => u.Set<PcResourceCostProposal>().Single().Id);
            Assert.IsTrue(Svc<ResponseDto<PcResourceCostProposalDto>, IPcResourcePerformanceService>(env.E, Planner, s => s.DecideProposalAsync(new PcCostProposalDecideDto { Id = id, Action = "reject", Reason = " " }, Ct)).Message.Contains("علت"));
            ResponseDto<PcResourceCostProposalDto> rejected = Svc<ResponseDto<PcResourceCostProposalDto>, IPcResourcePerformanceService>(env.E, Pmo, s => s.DecideProposalAsync(new PcCostProposalDecideDto { Id = id, Action = "reject", Reason = "برآورد دیگری داریم" }, Ct));
            Assert.IsTrue(rejected.Success, rejected.Message);
            Assert.AreEqual(PcCostProposalStatus.Rejected, rejected.Data.Status);
            Assert.AreEqual(0, env.E.Host.Read(u => u.Set<PcCostBaselineLine>().Count()));
            await Settle(env.E);
            Assert.AreEqual(1, env.E.Host.Read(u => u.Set<PcResourceCostProposal>().Count()), "a rejected proposal is not made again for the same plan");

            // a contract whose baseline is in force: the cost baseline changes only through a change request, so the agent proposes nothing
            Env locked = await NewEnv("locked", true);
            int r = NewResource(locked, "L-WELD", PcResourceKind.Labour, PcResourceUnit.Hour, 3m); ApproveRate(locked, r, 100m);
            int contract = NewContract(locked, "CK", ("A1", Work(0), Work(4)));
            ApprovedPlan(locked, contract, ("CK:A1", r, 10m));
            locked.Host.Write(u => { u.Set<PcContractTerms>().Single(t => t.ContractId == contract).CurrentBaselineId = 1; u.SaveChanges(); }, Seeder);
            ResponseDto<int> direct = await AiActing.RunAsync(locked.Host.Provider.GetRequiredService<IServiceScopeFactory>(), locked.Seats["AG-PLAN"], "AG-PLAN",
                async sp => { using (AgentStepContext.Enter("RES.COST.PROPOSE")) return await sp.GetRequiredService<IPcResourcePerformanceService>().ProposeCostAsync(contract, Ct); });
            Assert.IsFalse(direct.Success);
            await Settle(locked);
            Assert.AreEqual(0, locked.Host.Read(u => u.Set<PcResourceCostProposal>().Count()));
            // and nobody but the agent in its step makes a proposal
            Assert.IsFalse(Svc<ResponseDto<int>, IPcResourcePerformanceService>(locked, Planner, s => s.ProposeCostAsync(contract, Ct)).Success);
        }

        private static ResponseDto<PcResourceCostProposalDto> Decide(Env e, ClaimsPrincipal who, int id, string action, string rowVersion = null, string reason = null) =>
            Svc<ResponseDto<PcResourceCostProposalDto>, IPcResourcePerformanceService>(e, who, s => s.DecideProposalAsync(new PcCostProposalDecideDto { Id = id, Action = action, RowVersion = rowVersion, Reason = reason }, Ct));

        [TestMethod]
        public async Task A_Stale_Or_Missing_Row_Version_Is_Refused_And_A_Second_Accept_Leaves_Exactly_One_Cost_Line()
        {
            var env = await CostEnv("rowversion");
            await Settle(env.E);
            int id = env.E.Host.Read(u => u.Set<PcResourceCostProposal>().Single().Id);
            byte[] current = { 1, 2, 3, 4, 5, 6, 7, 8 };
            env.E.Host.Write(u => { u.Set<PcResourceCostProposal>().Single().RowVersion = current; u.SaveChanges(); }, Seeder);
            // the DTO carries the row version, and the decision needs it
            string shown = Svc<ResponseDto<PagedList<PcResourceCostProposalDto>>, IPcResourcePerformanceService>(env.E, Planner, s => s.GetProposalsAsync(env.Contract, null, 1, 20, Ct)).Data.Single().RowVersion;
            Assert.AreEqual(Convert.ToBase64String(current), shown);
            Assert.IsFalse(Decide(env.E, Planner, id, "accept").Success, "no row version, a row that has one");
            Assert.IsFalse(Decide(env.E, Planner, id, "accept", Convert.ToBase64String(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 })).Success, "a stale version");
            Assert.AreEqual(0, env.E.Host.Read(u => u.Set<PcCostBaselineLine>().Count()));
            Assert.AreEqual(PcCostProposalStatus.Proposed, env.E.Host.Read(u => u.Set<PcResourceCostProposal>().Single().Status));
            ResponseDto<PcResourceCostProposalDto> ok = Decide(env.E, Planner, id, "accept", shown);
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.IsFalse(Decide(env.E, Pmo, id, "accept", shown).Success, "the second accept");
            Assert.AreEqual(1, env.E.Host.Read(u => u.Set<PcCostBaselineLine>().Count()));
            PcResourceCostProposal row = env.E.Host.Read(u => u.Set<PcResourceCostProposal>().AsNoTracking().Single());
            Assert.AreEqual(PcCostProposalStatus.Accepted, row.Status); Assert.IsNotNull(row.CostLineId);
            Assert.AreEqual(row.CostLineId, env.E.Host.Read(u => u.Set<PcCostBaselineLine>().Single().Id));
        }

        [TestMethod]
        public async Task The_Proposal_Is_The_Delta_Over_The_Cost_Lines_Of_The_Same_Class_And_Nothing_When_It_Is_Covered()
        {
            var env = await CostEnv("delta");
            env.E.Host.Write(u => { AppHost.Add(u, new PcCostBaselineLine { ContractId = env.Contract, CostClass = PcCostClass.Direct, ResourceClass = PcResourceClass.Labour, Amount = 400m, Description = "قبلی" }); u.SaveChanges(); }, Seeder);
            await Settle(env.E);
            PcResourceCostProposal p = env.E.Host.Read(u => u.Set<PcResourceCostProposal>().AsNoTracking().Single());
            Assert.AreEqual(600m, p.Amount); Assert.AreEqual(400m, p.ExistingAmount);
            ResponseDto<PcResourceCostProposalDto> ok = Decide(env.E, Planner, p.Id, "accept");
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.AreEqual(1000m, env.E.Host.Read(u => u.Set<PcCostBaselineLine>().Where(l => l.ResourceClass == PcResourceClass.Labour).Sum(l => l.Amount)), "the lines add up to the estimate, not to the estimate plus the old lines");

            var covered = await CostEnv("covered");
            covered.E.Host.Write(u => { AppHost.Add(u, new PcCostBaselineLine { ContractId = covered.Contract, CostClass = PcCostClass.Direct, ResourceClass = PcResourceClass.Labour, Amount = 1000m, Description = "پوشش" }); u.SaveChanges(); }, Seeder);
            await Settle(covered.E);
            Assert.AreEqual(0, covered.E.Host.Read(u => u.Set<PcResourceCostProposal>().Count(x => x.Status == PcCostProposalStatus.Proposed)), "no proposal when the lines already cover the estimate");
        }

        // ------------------------------------------------------------------------------------------------ 4d-4: the charter list

        [TestMethod]
        public async Task The_Charter_Keeps_Its_Free_Text_As_History_Once_A_Structured_List_Exists()
        {
            Env e = await NewEnv("charter");
            int contract = NewContract(e, "CH");
            int weld = NewResource(e, "H-WELD", PcResourceKind.Labour, PcResourceUnit.Hour, 3m), off = NewResource(e, "H-OFF", PcResourceKind.Labour, PcResourceUnit.Hour, 3m, inactive: true);
            PcCharterDto Save(PcCharterDto d, ClaimsPrincipal who = null) => Svc<ResponseDto<PcCharterDto>, IPcProjectService>(e, who ?? Pmo, s => s.SaveCharterAsync(d, Ct)).Data;
            ResponseDto<PcCharterDto> Try(PcCharterDto d) => Svc<ResponseDto<PcCharterDto>, IPcProjectService>(e, Pmo, s => s.SaveCharterAsync(d, Ct));

            Assert.AreEqual("متن قدیمی", Save(new PcCharterDto { ContractId = contract, ScopeDescription = "دامنه", PreAssignedResources = "متن قدیمی" }).PreAssignedResources);
            // with a list the free text is kept as it was and flagged read only
            PcCharterDto withList = Save(new PcCharterDto { ContractId = contract, ScopeDescription = "دامنه", PreAssignedResources = "متن تازه", PreAssignedList = new List<PcCharterResourceDto> { new PcCharterResourceDto { ResourceId = weld, Quantity = 2m, Note = "از ماه اول" } } });
            Assert.AreEqual("متن قدیمی", withList.PreAssignedResources); Assert.IsTrue(withList.PreAssignedTextLocked);
            Assert.AreEqual(1, withList.PreAssignedList.Count); Assert.AreEqual("H-WELD", withList.PreAssignedList[0].ResourceCode); Assert.AreEqual(2m, withList.PreAssignedList[0].Quantity);
            // a save that sends no list leaves the list (and the text) alone
            PcCharterDto untouched = Save(new PcCharterDto { ContractId = contract, ScopeDescription = "دامنه ۲", PreAssignedResources = "باز هم تازه" });
            Assert.AreEqual(1, untouched.PreAssignedList.Count); Assert.AreEqual("متن قدیمی", untouched.PreAssignedResources);
            Assert.AreEqual(1, Svc<ResponseDto<PcCharterDto>, IPcProjectService>(e, Pmo, s => s.GetCharterAsync(contract, Ct)).Data.PreAssignedList.Count);
            // validation
            Assert.IsFalse(Try(new PcCharterDto { ContractId = contract, PreAssignedList = new List<PcCharterResourceDto> { new PcCharterResourceDto { ResourceId = weld, Quantity = 0m } } }).Success);
            Assert.IsFalse(Try(new PcCharterDto { ContractId = contract, PreAssignedList = new List<PcCharterResourceDto> { new PcCharterResourceDto { ResourceId = weld, Quantity = 1m }, new PcCharterResourceDto { ResourceId = weld, Quantity = 1m } } }).Success);
            Assert.IsFalse(Try(new PcCharterDto { ContractId = contract, PreAssignedList = new List<PcCharterResourceDto> { new PcCharterResourceDto { ResourceId = 9999, Quantity = 1m } } }).Success);
            Assert.IsFalse(Try(new PcCharterDto { ContractId = contract, PreAssignedList = new List<PcCharterResourceDto> { new PcCharterResourceDto { ResourceId = off, Quantity = 1m } } }).Success, "an inactive resource is not picked");
            Assert.IsFalse(Svc<ResponseDto<PcCharterDto>, IPcProjectService>(e, Engineer, s => s.SaveCharterAsync(new PcCharterDto { ContractId = contract }, Ct)).Success);
            // an empty list removes the rows and the text is editable again
            PcCharterDto cleared = Save(new PcCharterDto { ContractId = contract, ScopeDescription = "دامنه", PreAssignedResources = "متن نهایی", PreAssignedList = new List<PcCharterResourceDto>() });
            Assert.AreEqual(0, cleared.PreAssignedList.Count); Assert.IsFalse(cleared.PreAssignedTextLocked); Assert.AreEqual("متن نهایی", cleared.PreAssignedResources);
            // an approved charter changes only through change control
            e.Host.Write(u => { u.Set<PcProjectCharter>().Single().ApprovedAt = DateTimeOffset.Now; u.SaveChanges(); }, Seeder);
            Assert.IsFalse(Try(new PcCharterDto { ContractId = contract, PreAssignedList = new List<PcCharterResourceDto> { new PcCharterResourceDto { ResourceId = weld, Quantity = 1m } } }).Success);
        }

        // ------------------------------------------------------------------------------------------------ 4e: the report section

        [TestMethod]
        public async Task The_Weekly_Report_Section_Counts_The_Projects_Resources_Exactly()
        {
            Env e = await NewEnv("report");
            int r1 = NewResource(e, "W-1", PcResourceKind.Labour, PcResourceUnit.Hour, 3m), r2 = NewResource(e, "W-2", PcResourceKind.Labour, PcResourceUnit.Hour, 3m), r3 = NewResource(e, "W-3", PcResourceKind.Labour, PcResourceUnit.Hour, 3m);
            int outside = NewResource(e, "W-X", PcResourceKind.Labour, PcResourceUnit.Hour, 3m);
            ApproveRate(e, r1, 100m);
            int contract = NewContract(e, "CR", ("A1", Work(0), Work(4)), ("A2", Work(0), Work(4)));
            int other = NewContract(e, "CS", ("A1", Work(0), Work(4)));
            ApprovedPlan(e, contract, ("CR:A1", r1, 40m), ("CR:A2", r2, 10m));
            // the next version waits for approval and also names a third resource that has no rate
            AddPlan(e, contract, 2, PcResourcePlanStatus.Submitted, ("CR:A1", r1, 40m), ("CR:A2", r2, 10m), ("CR:A2", r3, 5m));
            AddPlan(e, other, 1, PcResourcePlanStatus.Submitted, ("CS:A1", outside, 5m));
            e.Host.Write(u =>
            {
                DateTimeOffset now = DateTimeOffset.Now;
                PcResourceOverload O(int resource, PcOverloadStatus s) => new PcResourceOverload { ResourceId = resource, FromDate = DateTime.Today.AddDays((int)s * 10), ToDate = DateTime.Today.AddDays((int)s * 10 + 2), PeakLoad = 9m, PeakCapacity = 3m, Status = s, DetectedAt = now };
                AppHost.Add(u, O(r1, PcOverloadStatus.Open)); AppHost.Add(u, O(r2, PcOverloadStatus.Open)); AppHost.Add(u, O(r1, PcOverloadStatus.Accepted)); AppHost.Add(u, O(r2, PcOverloadStatus.Lapsed));
                AppHost.Add(u, O(outside, PcOverloadStatus.Open)); // another project's resource: not in this report
                u.SaveChanges();
            }, Seeder);
            PcWeeklyResourcesDto section = e.Host.Read(u => PcResourceReportSection.BuildAsync(u, contract, DateTime.Today, Ct).GetAwaiter().GetResult());
            Assert.AreEqual(2, section.OpenOverloads); Assert.AreEqual(1, section.AcceptedOverloads); Assert.AreEqual(1, section.LapsedOverloads);
            Assert.AreEqual(1, section.PlansWaitingApproval, "the other project's submitted plan is not counted");
            Assert.AreEqual(2, section.ResourcesWithoutRate, "W-2 and W-3 have no approved rate");
            Assert.AreEqual(2, section.Top.Count);
            Assert.AreEqual("W-1", section.Top[0].Code); Assert.AreEqual(40m, section.Top[0].Load);
            Assert.AreEqual("W-2", section.Top[1].Code); Assert.AreEqual(10m, section.Top[1].Load);
            // the weekly report of the register service carries the section and a highlight
            ResponseDto<PcWeeklyReportDto> weekly = Svc<ResponseDto<PcWeeklyReportDto>, IPcRegistersService>(e, Seeder, s => s.GenerateWeeklyAsync(new PcWeeklyGenerateDto { ContractId = contract }, Ct));
            Assert.IsTrue(weekly.Success, weekly.Message);
            Assert.AreEqual(2, weekly.Data.Auto.Resources.OpenOverloads); Assert.AreEqual(2, weekly.Data.Auto.Resources.Top.Count);
        }
    }
}
