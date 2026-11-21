using System.Linq;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.Sales;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1 S5: spare-parts and after-sales service cases: no classification and no CEO gate, a technical opinion confirmed by a manager who did not write it, the tender release hash includes the opinion, and the proposal-approval chokepoint applies.</summary>
    [TestClass]
    public class EngineeringKindTests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        [TestMethod]
        public async Task A_Spare_Parts_Case_Needs_An_Opinion_Confirmed_By_Another_Manager_And_Has_No_Ceo_Gate()
        {
            AppHost host = Started("kind1");
            await ApprovedList(host, "SPARE-LIST", ("DWG", true, false));
            int inquiry = Inquiry(host, SlInquiryStatus.Bidding, SlProductType.SpareParts);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = inquiry));
            Assert.AreEqual((int)EnCaseKind.SpareParts, c.Kind);
            // the chokepoint applies: the proposal of this inquiry cannot be approved before the technical package is released
            Assert.IsNotNull(await host.As(Eng1, p => EnGates.ProposalApproveRefusalAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), inquiry, Ct)));
            Refused(await CaseAct(host, Eng1, c.Id, "save-opinion", d => d.OpinionText = "کوتاه"), "۳۰");
            Refused(await CaseAct(host, Qa1, c.Id, "save-opinion", d => d.OpinionText = "رای فنی کامل برای تامین قطعات یدکی مطابق نقشه‌های موجود"));
            Refused(await CaseAct(host, await SeatAsync(host), c.Id, "save-opinion", d => d.OpinionText = "رای فنی کامل برای تامین قطعات یدکی مطابق نقشه‌های موجود"));
            Refused(await CaseAct(host, Mgr1, c.Id, "change", d => d.Classification = 2), "نوع‌بندی");
            c = Ok(await CaseAct(host, Mgr2, c.Id, "save-opinion", d => d.OpinionText = "رای فنی کامل برای تامین قطعات یدکی مطابق نقشه‌های موجود"));
            Assert.AreEqual((int)EnCaseStatus.Classifying, c.Status, "the evidence of the proposal is still missing");
            Refused(await CaseAct(host, Mgr1, c.Id, "confirm"), "منتظر تایید");
            c = Ok(await CaseAct(host, Eng1, c.Id, "propose", d => d.Basis = "پلتفرم مرتبطی مشخص نشد، مبنای آزمون"));
            Assert.AreEqual((int)EnCaseStatus.AwaitingEngConfirm, c.Status);
            Refused(await CaseAct(host, Mgr1, c.Id, "confirm"), "استاندارد");
            Ok(await SetStandard(host, Eng1, c.Id, "SPARE-LIST"));
            Refused(await CaseAct(host, Mgr2, c.Id, "confirm"), "شخص دیگری");                    // the opinion writer never confirms it
            Refused(await CaseAct(host, Mgr1, c.Id, "confirm", d => d.HandoverListCode = "NO-SUCH-LIST"), "فهرست");
            Refused(await CaseAct(host, Ceo, c.Id, "approve"));                                   // no CEO classification gate: nothing waits for the CEO
            c = Ok(await CaseAct(host, Mgr1, c.Id, "confirm", d => d.HandoverListCode = "SPARE-LIST"));
            Assert.AreEqual((int)EnCaseStatus.TenderPreparation, c.Status);
            Assert.AreEqual((int)EnClassification.None, c.Classification);
            Assert.AreEqual("SPARE-LIST", c.HandoverListCode);
            Refused(await CaseAct(host, Mgr1, c.Id, "reclassify", d => d.Note = "دلیل تغییر نوع پرونده"), "نوع‌بندی");
            var seat = await SeatAsync(host);
            c = Ok(await CaseAct(host, seat, c.Id, "draft-tender"));
            Refused(await CaseAct(host, Mgr1, c.Id, "release-tender"), "فهرست مدارک پروژه (MDL)");   // ENG-E1g: every kind of tender needs an approved project MDL (or the CEO's permit)
            await ApprovedMdl(host, c.Id);                                                         // its standard has an approved list: the agent drafts from it
            c = Ok(await CaseAct(host, Mgr1, c.Id, "release-tender"));                            // documents are optional for spare parts
            Assert.AreEqual((int)EnCaseStatus.TenderIssued, c.Status);
            Assert.IsNull(await host.As(Eng1, p => EnGates.ProposalApproveRefusalAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), inquiry, Ct)));
            EnCase row = host.Read(u => u.Set<EnCase>().Single(x => x.Id == c.Id));
            Assert.AreEqual(await host.As(Eng1, p => EnCaseRules.TenderHashAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), row, Ct)), row.TenderHash);
            Assert.AreNotEqual(await host.As(Eng1, p => EnCaseRules.TenderHashAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), row.TenderPackageId.Value, EnCaseKind.Turbine, null, Ct)), row.TenderHash, "the release hash includes the opinion");
        }

        [TestMethod]
        public async Task A_Service_Case_Gets_Its_Opinion_Step_And_The_Opinion_Step_Is_Not_Raised_For_A_Turbine_Or_A_Written_Opinion()
        {
            AppHost host = Started("kind2");
            EnCaseDto turbine = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = Inquiry(host)));
            EnCaseDto service = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = Inquiry(host, SlInquiryStatus.Bidding, SlProductType.Service)));
            Assert.AreEqual((int)EnCaseKind.Service, service.Kind);
            Refused(await CaseAct(host, Eng1, turbine.Id, "save-opinion", d => d.OpinionText = "رای فنی کامل برای یک توربین که نباید پذیرفته شود"), "قطعات یدکی");
            var seat = await SeatAsync(host);
            var items = await EngineeringStandardsTests.Discover(host, seat, "ENG.OPINION.INPUT");
            Assert.IsTrue(items.Any(i => i.TargetId == service.Id));
            Assert.IsFalse(items.Any(i => i.TargetId == turbine.Id));
            Ok(await CaseAct(host, Eng1, service.Id, "save-opinion", d => d.OpinionText = "رای فنی کامل برای خدمات پس از فروش مطابق دستورالعمل"));
            Assert.IsFalse((await EngineeringStandardsTests.Discover(host, seat, "ENG.OPINION.INPUT")).Any(i => i.TargetId == service.Id));
        }
    }
}
