using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Engineering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1c fix round (review findings H1, H2, M1, M3, M6).</summary>
    [TestClass]
    public class EngineeringE1cFixTests
    {
        private const string Head = "docType,title,discipline,required,tender,units\n";
        private static byte[] Csv(string text) => new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        private static Task<Common.Results.ResponseDto<EnReferenceListDto>> Upload(AppHost host, System.Security.Claims.ClaimsPrincipal who, EnReferenceListDto d, byte[] file) =>
            host.As(who, p => p.GetRequiredService<IEnReferenceService>().UploadLinesAsync(d.Id, d.RowVersion, file, Ct));
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        /// <summary>An approved list whose PID line goes to the client with a clause and a note.</summary>
        private static async Task<EnReferenceListDto> ApprovedWithClientLine(AppHost host, string code)
        {
            EnSaveReferenceDto save = Ref(code, ("PID", true, true), ("DATASHEET", true, false));
            EnReferenceLineDto pid = save.Lines.Single(l => l.DocumentType == "PID");
            pid.ClientSubmittal = true; pid.StandardClause = "بند ۵٫۲"; pid.Note = "یادداشت نقشه";
            EnReferenceListDto d = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(save, Ct)));
            Ok(await RefAction(host, Eng1, d.Id, "submit"));
            return Ok(await RefAction(host, Mgr1, d.Id, "approve"));
        }

        private const string RevisionLines = Head + "PID,مدرک PID,مکانیک,1,1,\nDATASHEET,مدرک DATASHEET,مکانیک,1,0,\n";

        [TestMethod]
        public async Task H1_Revision_Upload_Without_The_Optional_Columns_Keeps_The_Client_Submittal_Clause_And_Note_And_Submits_Without_A_Deviation()
        {
            AppHost host = Started("e1cfix_h1a");
            EnReferenceListDto v1 = await ApprovedWithClientLine(host, "API-CS");
            EnReferenceListDto d2 = Ok(await RefAction(host, Eng1, v1.Id, "revise"));
            EnReferenceListDto r = Ok(await Upload(host, Eng1, d2, Csv(RevisionLines)));
            EnReferenceLineDto pid = r.Lines.Single(l => l.DocumentType == "PID");
            Assert.IsTrue(pid.ClientSubmittal, "the stored value is kept");
            Assert.AreEqual("بند ۵٫۲", pid.StandardClause);
            Assert.AreEqual("یادداشت نقشه", pid.Note);
            Ok(await RefAction(host, Eng1, d2.Id, "submit"));
            Assert.AreEqual(0, host.Read(u => u.Set<EnDeviation>().Count()), "nothing was loosened");
            Assert.AreEqual((int)EnRefStatus.Approved, Ok(await RefAction(host, Mgr1, d2.Id, "approve")).Status);
        }

        [TestMethod]
        public async Task H1_Revision_Upload_With_The_Column_That_Lowers_Client_Submittal_Needs_The_Deviation()
        {
            AppHost host = Started("e1cfix_h1b");
            EnReferenceListDto v1 = await ApprovedWithClientLine(host, "API-CS2");
            EnReferenceListDto d2 = Ok(await RefAction(host, Eng1, v1.Id, "revise"));
            string csv = "docType,title,discipline,required,tender,units,clientSubmittal,clause,note\nPID,مدرک PID,مکانیک,1,1,,خیر,,\nDATASHEET,مدرک DATASHEET,مکانیک,1,0,,0,بند ۷,توضیح\n";
            EnReferenceListDto r = Ok(await Upload(host, Eng1, d2, Csv(csv)));
            EnReferenceLineDto pid = r.Lines.Single(l => l.DocumentType == "PID");
            Assert.IsFalse(pid.ClientSubmittal);
            Assert.IsTrue(string.IsNullOrEmpty(pid.StandardClause), "a column that is present wins, even empty");
            Assert.AreEqual("بند ۷", r.Lines.Single(l => l.DocumentType == "DATASHEET").StandardClause);
            Ok(await RefAction(host, Eng1, d2.Id, "submit"));
            EnDeviation dev = host.Read(u => u.Set<EnDeviation>().Single());
            Assert.AreEqual(EnDeviationKind.ReferenceLoosening, dev.Kind);
            StringAssert.Contains(dev.Requirement, "ارائه به کارفرما");
            Refused(await RefAction(host, Mgr1, d2.Id, "approve"), "مدیرعامل");
        }

        [TestMethod]
        public void H1_The_Loosening_Rule_Counts_Client_Submittal_For_The_Json_Path_Too()
        {
            EnReferenceList prev = new EnReferenceList { Lines = { new EnReferenceLine { Seq = 1, DocumentType = "PID", Title = "t", Required = false, ClientSubmittal = true } } };
            EnReferenceList next = new EnReferenceList { Lines = { new EnReferenceLine { Seq = 1, DocumentType = "PID", Title = "t", Required = false, ClientSubmittal = false } } };
            Assert.AreEqual(1, EnDeviations.Loosened(prev, next).Count);
            next.Lines.Single().ClientSubmittal = true;
            Assert.AreEqual(0, EnDeviations.Loosened(prev, next).Count);
            Assert.AreEqual(0, EnDeviations.Loosened(null, next).Count);
        }

        [TestMethod]
        public async Task H1_Semicolon_Separated_File_Is_Read_And_Mixed_Rules_Are_Refused_With_The_Row()
        {
            AppHost host = Started("e1cfix_sep");
            EnReferenceListDto d = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(Ref("VDDR-SEP", ("BOM", true, false)), Ct)));
            string csv = "docType;title;discipline;required;tender;units\nDWG;\"نقشه; مونتاژ\";مکانیک;1;1;\nNOTE;یادداشت;;0;0;\"QA,QC\"\nBOM;فهرست;;بله;خیر;تولید|Planning\n";
            EnReferenceListDto r = Ok(await Upload(host, Eng1, d, Csv(csv)));
            Assert.AreEqual(3, r.Lines.Count);
            Assert.AreEqual("نقشه; مونتاژ", r.Lines.Single(l => l.DocumentType == "DWG").Title);
            Assert.AreEqual("Qa,Qc", r.Lines.Single(l => l.DocumentType == "NOTE").Units);
            Assert.AreEqual("Production,Planning", r.Lines.Single(l => l.DocumentType == "BOM").Units);
            Refused(await Upload(host, Eng1, r, Csv("docType;title;discipline;required;tender;units\nDWG;نقشه;;1;1\n")), "ردیف 2");
            Refused(await Upload(host, Eng1, r, Csv("docType;title;discipline;required;tender;units;other\nDWG;نقشه;;1;1;;x\n")), "سرستون");
        }

        // ------------------------------------------------------------------ H2

        [TestMethod]
        public async Task H2_Set_Standard_Without_The_Datasheet_File_Is_Refused()
        {
            AppHost host = Started("e1cfix_h2a");
            int inquiry = Inquiry(host);
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = inquiry));
            Refused(await SetStandard(host, Eng1, c.Id, "API-611", withoutFile: true), "فایل دیتاشیت");
            Assert.IsTrue(string.IsNullOrEmpty(Ok(await GetCase(host, Eng1, c.Id)).ReferenceCode), "nothing was recorded");
            Refused(await SetStandard(host, Eng1, c.Id, "API-611", file: File(host, 2112)), "خود شما");   // somebody else's file
            c = Ok(await SetStandard(host, Eng1, c.Id, "API-611"));
            Assert.IsNotNull(c.StandardAttachmentId);
        }

        [TestMethod]
        public async Task H2_A_Different_Datasheet_File_Is_A_Change_With_A_Reason_And_A_New_Decision_Round_The_Same_File_Is_Not()
        {
            AppHost host = Started("e1cfix_h2b");
            EnCaseDto c = await ApprovedCase(host, Inquiry(host), false);
            Assert.AreEqual((int)EnCaseStatus.TenderPreparation, c.Status);
            Guid stored = c.StandardAttachmentId.Value;
            // the same code, evidence and file again: nothing changes, no reason needed
            c = Ok(await SetStandard(host, Eng1, c.Id, Std, file: stored));
            Assert.AreEqual((int)EnCaseStatus.TenderPreparation, c.Status);
            // the same code and evidence with another file: a change
            Refused(await SetStandard(host, Eng1, c.Id, Std), "دلیل تغییر استاندارد");
            Refused(await SetStandard(host, Eng1, c.Id, Std, withoutFile: true), "فایل دیتاشیت");
            c = Ok(await SetStandard(host, Eng1, c.Id, Std, note: "فایل دیتاشیت اصلاح‌شدهٔ کارفرما جایگزین شد"));
            Assert.AreNotEqual(stored, c.StandardAttachmentId.Value);
            Assert.AreEqual((int)EnCaseStatus.AwaitingEngConfirm, c.Status, "the approval bound to the old file is void");
            Assert.IsNull(c.CeoApprovedByName);
            string eventText = host.Read(u => u.Set<EnCaseEvent>().Where(e => e.CaseId == c.Id && e.Kind == "standard-changed").Select(e => e.Note).Single());
            StringAssert.Contains(eventText, "فایل دیتاشیت تغییر کرد");
        }

        // ------------------------------------------------------------------ M3

        [TestMethod]
        public async Task M3_The_Platform_Standard_Must_Be_An_Approved_List_Code_While_The_Client_Standard_Of_A_Case_May_Have_No_List()
        {
            AppHost host = Started("e1cfix_m3");
            int product = Product(host);
            Func<string, Task<Common.Results.ResponseDto<EnDossierDto>>> record = code => host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().DossierActionAsync(new EnDossierActionDto { ProductId = product, Action = "native-standard", ReferenceCode = code, StandardBasis = "دیتاشیت اصلی، بند ۴" }, Ct));
            Refused(await record("NOLIST-1"), "فهرست مدارک مرجع تاییدشده");
            await ApprovedList(host, "NOLIST-1", ("PID", true, true));
            Assert.AreEqual("NOLIST-1", Ok(await record("nolist-1")).ReferenceCode);
            // the client standard of a case is what the client wrote: a code without a list is recorded (the gap report exists for it)
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = Inquiry(host)));
            Assert.AreEqual("NOLIST-2", Ok(await SetStandard(host, Eng1, c.Id, "NOLIST-2")).ReferenceCode);
        }
    }
}
