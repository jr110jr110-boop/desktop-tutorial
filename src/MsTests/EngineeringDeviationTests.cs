using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.Office;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EnKit;
using static MsTests.EngineeringStandardsTests;

namespace MsTests
{
    /// <summary>ENG-E1 S3: an instruction against a requirement of a standard needs the head of engineering AND the CEO (two distinct persons, neither the requester, never a seat).</summary>
    [TestClass]
    public class EngineeringDeviationTests
    {
        private const string Reason = "کارفرما این مدرک را برای این پروژه لازم ندارد و در دیتاشیت حذف کرده است";

        private static Task<ResponseDto<EnDeviationDto>> Decide(AppHost host, ClaimsPrincipal who, int id, string decision, string note = "تصمیم آزمون برای انحراف از استاندارد") =>
            host.As(who, p => p.GetRequiredService<IEnDeviationService>().DecideAsync(new EnDeviationDecisionDto { Id = id, Decision = decision, Note = note }, Ct));

        private static async Task<(AppHost Host, int Product, EnDossierLineDto Line, EnDeviationDto Dev)> RequestedAsync(string name, ClaimsPrincipal requester = null)
        {
            AppHost host = NewHost(name);
            StartControl(host);
            await ApprovedList(host);
            int product = Product(host);
            EnDossierDto d = Ok(await Dossier(host, Eng1, product, "assess"));
            EnDossierLineDto line = d.Lines.First(l => l.DocumentType == "DWG");
            var r = await host.As(requester ?? Eng1, p => p.GetRequiredService<IEnPlatformService>().SupplyLineAsync(new EnSupplyLineDto { LineId = line.Id, NotApplicable = true, NotApplicableReason = Reason }, Ct));
            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(0, r.Data.Lines.Single(l => l.Id == line.Id).State, "not applied: the line stays Missing");
            EnDeviationDto dev = Ok(await host.As(Mgr1, p => p.GetRequiredService<IEnDeviationService>().GetDeviationsAsync(new EnDeviationQuery(), Ct))).Single();
            return (host, product, line, dev);
        }

        [TestMethod]
        public async Task Marking_A_Required_Line_Not_Applicable_Is_Not_Applied_It_Becomes_A_Deviation_And_A_Non_Required_Line_Keeps_The_Old_Rule()
        {
            AppHost host = NewHost("dev0");
            StartControl(host);
            await ApprovedList(host);
            int product = Product(host);
            EnDossierDto d = Ok(await Dossier(host, Eng1, product, "assess"));
            EnDossierLineDto req = d.Lines.First(l => l.Required), opt = d.Lines.Single(l => !l.Required);
            Refused(await Supply(host, Eng1, req.Id, null, true, "دلیل کوتاه برای آزمون"), "دلیل انحراف از استاندارد");
            d = Ok(await Supply(host, Eng1, opt.Id, null, true, "این مدرک اختیاری در این پروژه اجرا نمی‌شود"));
            Assert.AreEqual(2, d.Lines.Single(l => l.Id == opt.Id).State, "a non-required line: the old rule, applied at once");
            var r = await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().SupplyLineAsync(new EnSupplyLineDto { LineId = req.Id, NotApplicable = true, NotApplicableReason = Reason }, Ct));
            Assert.IsTrue(r.Success);
            StringAssert.Contains(r.Message, "EDV");
            Refused(await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().SupplyLineAsync(new EnSupplyLineDto { LineId = req.Id, NotApplicable = true, NotApplicableReason = Reason }, Ct)), "درخواست انحراف باز");
        }

        [TestMethod]
        public async Task The_Deviation_Needs_The_Manager_And_Then_A_Different_Ceo_And_Only_Then_The_Line_Becomes_Not_Applicable()
        {
            var t = await RequestedAsync("dev1");
            AppHost host = t.Host;
            var seat = await SeatAsync(host, "AG-ENG", "ENGINEERING");
            Refused(await Decide(host, Ceo, t.Dev.Id, "approve"), "پس از مدیر مهندسی");            // the CEO before the manager
            Refused(await Decide(host, Eng1, t.Dev.Id, "approve"));                             // an engineer
            Refused(await Decide(host, seat, t.Dev.Id, "approve"), "هوش مصنوعی");                // never a seat
            Refused(await Decide(host, Mgr1, t.Dev.Id, "reject", "کوتاه"), "دلیل رد");
            EnDeviationDto dev = Ok(await Decide(host, Mgr2, t.Dev.Id, "approve"));
            Assert.AreEqual((int)EnDeviationStatus.ManagerApproved, dev.Status);
            Refused(await Decide(host, Mgr2, t.Dev.Id, "approve"));                             // the same person twice
            Refused(await Decide(host, Mgr1, t.Dev.Id, "approve"));                             // the final decision is the CEO's
            EnDossierDto d = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().GetDossierAsync(t.Product, Std, Ct)));
            Assert.AreEqual(0, d.Lines.Single(l => l.Id == t.Line.Id).State, "nothing is applied before the CEO");
            dev = Ok(await Decide(host, Ceo, t.Dev.Id, "approve"));
            Assert.AreEqual((int)EnDeviationStatus.Approved, dev.Status);
            d = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().GetDossierAsync(t.Product, Std, Ct)));
            Assert.AreEqual(2, d.Lines.Single(l => l.Id == t.Line.Id).State, "applied in the CEO approval");
            Assert.AreEqual(t.Dev.Id, host.Read(u => u.Set<EnPlatformDocument>().Single(x => x.Id == t.Line.Id).DeviationId));
        }

        [TestMethod]
        public async Task The_Requester_Never_Approves_His_Own_Request_And_A_Rejection_By_Either_Leaves_The_Line_Missing()
        {
            var t = await RequestedAsync("dev2", Mgr1);
            Refused(await Decide(host: t.Host, who: Mgr1, id: t.Dev.Id, decision: "approve"), "شخص دیگری");
            Ok(await Decide(t.Host, Mgr2, t.Dev.Id, "reject", "مدرک برای این استاندارد الزامی است و انحراف پذیرفته نمی‌شود"));
            Assert.AreEqual(0, Ok(await t.Host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().GetDossierAsync(t.Product, Std, Ct))).Lines.Single(l => l.Id == t.Line.Id).State);
            var u = await RequestedAsync("dev2b");
            Ok(await Decide(u.Host, Mgr2, u.Dev.Id, "approve"));
            Ok(await Decide(u.Host, Ceo, u.Dev.Id, "reject", "مدیرعامل انحراف را نمی‌پذیرد و مدرک باید تهیه شود"));
            Assert.AreEqual(0, Ok(await u.Host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().GetDossierAsync(u.Product, Std, Ct))).Lines.Single(l => l.Id == u.Line.Id).State);
        }

        [TestMethod]
        public async Task A_Change_Of_The_Line_After_The_Request_Voids_It_And_The_Alarm_Is_In_The_Counts()
        {
            var t = await RequestedAsync("dev3");
            EnOverviewDto o = Ok(await t.Host.As(Ceo, p => p.GetRequiredService<IEnOverviewService>().GetOverviewAsync(Ct)));
            Assert.AreEqual(1, o.DeviationsAwaiting);
            Assert.AreEqual(1, o.StandardsDeviationsOpen);
            Assert.IsTrue(t.Host.Read(u => u.Set<OfInboxItem>().Any(i => i.SourceKey == "EN-DEV-" + t.Dev.Id && i.Status < OfInboxStatus.Done)));
            t.Host.Write(u => { u.Set<EnPlatformDocument>().Single(x => x.Id == t.Line.Id).Title = "عنوان تغییرکرده"; u.SaveChanges(); });
            Refused(await Decide(t.Host, Mgr2, t.Dev.Id, "approve"), "باطل");
            Assert.AreEqual((int)EnDeviationStatus.Void, Ok(await t.Host.As(Mgr1, p => p.GetRequiredService<IEnDeviationService>().GetDeviationAsync(t.Dev.Id, Ct))).Status);
            Assert.IsFalse(t.Host.Read(u => u.Set<OfInboxItem>().Any(i => i.SourceKey == "EN-DEV-" + t.Dev.Id && i.Status < OfInboxStatus.Done)), "the alarm closes with the request");
            var v = await RequestedAsync("dev3b");
            Ok(await Supply(v.Host, Eng1, v.Line.Id, File(v.Host, 2111)));
            Assert.AreEqual((int)EnDeviationStatus.Void, Ok(await v.Host.As(Mgr1, p => p.GetRequiredService<IEnDeviationService>().GetDeviationAsync(v.Dev.Id, Ct))).Status, "supplying the document voids the request");
        }

        [TestMethod]
        public async Task A_Revision_That_Removes_A_Required_Line_Needs_Manager_And_Ceo_While_A_Tightening_Needs_One_Approver()
        {
            AppHost host = NewHost("dev4");
            StartControl(host);
            EnReferenceListDto v1 = await ApprovedList(host, "API-X", ("PID", true, true), ("DATASHEET", true, false), ("NOISE", false, false));
            // tightening: NOISE becomes required and a line is added -> the normal rule
            EnReferenceListDto d2 = Ok(await RefAction(host, Eng1, v1.Id, "revise"));
            EnSaveReferenceDto save = Ref("API-X", ("PID", true, true), ("DATASHEET", true, false), ("NOISE", true, false), ("ROTOR", true, false));
            save.Id = d2.Id; save.RowVersion = d2.RowVersion;
            d2 = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(save, Ct)));
            Ok(await RefAction(host, Eng1, d2.Id, "submit"));
            Assert.AreEqual(0, host.Read(u => u.Set<EnDeviation>().Count()), "only tightened: no deviation");
            EnReferenceListDto v2 = Ok(await RefAction(host, Mgr1, d2.Id, "approve"));
            Assert.AreEqual((int)EnRefStatus.Approved, v2.Status);
            // loosening: DATASHEET removed
            EnReferenceListDto d3 = Ok(await RefAction(host, Eng1, v2.Id, "revise"));
            save = Ref("API-X", ("PID", true, true), ("NOISE", true, false), ("ROTOR", true, false));
            save.Id = d3.Id; save.RowVersion = d3.RowVersion;
            d3 = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(save, Ct)));
            Ok(await RefAction(host, Eng1, d3.Id, "submit"));
            EnDeviation dev = host.Read(u => u.Set<EnDeviation>().Single());
            Assert.AreEqual(EnDeviationKind.ReferenceLoosening, dev.Kind);
            Refused(await RefAction(host, Mgr1, d3.Id, "approve"), "مدیرعامل");                     // the plain approve is closed
            Refused(await Decide(host, Eng1, dev.Id, "approve"));                                // the preparer / an engineer
            Ok(await Decide(host, Mgr1, dev.Id, "approve"));
            Assert.AreEqual(EnRefStatus.UnderReview, host.Read(u => u.Set<EnReferenceList>().Single(l => l.Id == d3.Id).Status), "the manager alone does not approve it");
            Ok(await Decide(host, Ceo, dev.Id, "approve"));
            Assert.AreEqual(EnRefStatus.Approved, host.Read(u => u.Set<EnReferenceList>().Single(l => l.Id == d3.Id).Status));
            Assert.AreEqual(EnRefStatus.Superseded, host.Read(u => u.Set<EnReferenceList>().Single(l => l.Id == v2.Id).Status));
        }
    }
}
