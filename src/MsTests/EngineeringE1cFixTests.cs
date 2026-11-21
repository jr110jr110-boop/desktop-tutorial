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
using static MsTests.EngineeringHandoverTests;

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
            Guid ds = File(host, 2111);
            Func<string, Task<Common.Results.ResponseDto<EnDossierDto>>> record = code => host.As(Eng1, p => p.GetRequiredService<IEnPlatformService>().DossierActionAsync(new EnDossierActionDto { ProductId = product, Action = "native-standard", StandardAttachmentId = ds, ReferenceCode = code, StandardBasis = "دیتاشیت اصلی، بند ۴" }, Ct));
            Refused(await record("NOLIST-1"), "فهرست MDL تاییدشده");
            await ApprovedList(host, "NOLIST-1", ("PID", true, true));
            Assert.AreEqual("NOLIST-1", Ok(await record("nolist-1")).ReferenceCode);
            // the client standard of a case is what the client wrote: a code without a list is recorded (the gap report exists for it)
            EnCaseDto c = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = Inquiry(host)));
            Assert.AreEqual("NOLIST-2", Ok(await SetStandard(host, Eng1, c.Id, "NOLIST-2")).ReferenceCode);
        }

        // ------------------------------------------------------------------ M1

        /// <summary>A draft handover (lines DWG and DATASHEET, both missing), accepted; the debt of DWG is settled, the debt of DATASHEET is open.</summary>
        private static async Task<(EnHandoverDto H, int Case)> AcceptedWithDebtsAsync(AppHost host)
        {
            (int contract, int caseId) = await NewDesign(host);
            EnHandoverDto h = Ok(await Act(host, Eng1, "draft", d => d.CaseId = caseId));
            host.Write(u =>
            {
                u.Set<EnHandover>().First(x => x.Id == h.Id).Status = EnHandoverStatus.Accepted;
                EnWaiver w = AppHost.Add(u, new EnWaiver { Number = "EWV-T1", HandoverId = h.Id, CaseId = caseId, Reason = "آزمون", DebtHours = 24, Status = EnWaiverStatus.Approved, RequestedByUserId = 2121, RequestedAt = DateTimeOffset.Now, UsedAt = DateTimeOffset.Now });
                u.SaveChanges();
                u.Set<EnHandover>().First(x => x.Id == h.Id).WaiverId = w.Id;   // the order before ENG-E1f: the permit was used at the submit; such a handover keeps releasing
                u.SaveChanges();
                foreach (EnHandoverLine l in u.Set<EnHandoverLine>().Where(x => x.HandoverId == h.Id).ToList())
                    AppHost.Add(u, new EnDocumentDebt { WaiverId = w.Id, HandoverLineId = l.Id, CaseId = caseId, ContractId = contract, DocumentType = l.DocumentType, Title = l.Title, DueAt = DateTimeOffset.Now.AddHours(20), Status = l.DocumentType == "DWG" ? EnDebtStatus.Settled : EnDebtStatus.Open });
                u.SaveChanges();
            }, Seeder);
            return (h, caseId);
        }

        [TestMethod]
        public async Task M1_The_Unit_Notice_Treats_A_Settled_Debt_As_Arrived_And_An_Open_Debt_As_Not_Yet_Arrived()
        {
            AppHost host = Started("e1cfix_m1a");
            (EnHandoverDto h, int caseId) = await AcceptedWithDebtsAsync(host);
            EnHandoverDto drafted = Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            string note = drafted.Distribution.Single(x => x.Unit == (int)EnUnit.Qc).ScopeNote;
            StringAssert.Contains(note, "مدرک DWG؛");
            Assert.IsFalse(note.Contains("مدرک DWG ("), "the line flag says missing but its debt is settled: it has arrived");
            StringAssert.Contains(note, "مدرک DATASHEET (هنوز نرسیده، مهلت تا ");
        }

        [TestMethod]
        public void M1_A_Line_With_A_Settled_Debt_Reads_As_Arrived_And_A_Void_Debt_Does_Not()
        {
            List<EnHandoverLine> lines = new List<EnHandoverLine>
            {
                new EnHandoverLine { Id = 1, Seq = 1, Title = "الف", State = EnLineState.Missing, Units = "Qc" },
                new EnHandoverLine { Id = 2, Seq = 2, Title = "ب", State = EnLineState.Missing, Units = "Qc" },
                new EnHandoverLine { Id = 3, Seq = 3, Title = "سند-ویژهٔ-تولید", State = EnLineState.Missing, Units = "Production" },
            };
            string note = EnHandoverRules.ScopeNote("K", "E-1", "API", EnUnit.Qc, lines, out int count, new Dictionary<int, DateTimeOffset>(), new HashSet<int> { 1 });
            Assert.AreEqual(2, count);
            StringAssert.Contains(note, "الف؛");
            StringAssert.Contains(note, "ب (هنوز نرسیده)");
            Assert.IsFalse(note.Contains("سند-ویژهٔ-تولید"), "a document of another unit is not in the note");
        }

        [TestMethod]
        public async Task M1_At_The_Release_A_Draft_Text_The_PMO_Did_Not_Edit_Is_Rebuilt_And_An_Edited_One_Is_Kept()
        {
            AppHost host = Started("e1cfix_m1b");
            (EnHandoverDto h, int caseId) = await AcceptedWithDebtsAsync(host);
            Ok(await Act(host, Pmo1, "draft-distribution", d => d.Id = h.Id));
            // the datasheet arrives after the draft: its debt settles
            host.Write(u => { u.Set<EnDocumentDebt>().First(x => x.CaseId == caseId && x.Status == EnDebtStatus.Open).Status = EnDebtStatus.Settled; u.SaveChanges(); }, Seeder);
            EnHandoverDto cur = Ok(await host.As(Pmo1, p => p.GetRequiredService<IEnHandoverService>().GetHandoverAsync(h.Id, Ct)));
            Ok(await Act(host, Pmo1, "edit-scope", d => { d.Id = h.Id; d.DistributionId = cur.Distribution.Single(x => x.Unit == (int)EnUnit.Qc).Id; d.ScopeNote = "متن ویرایش‌شدهٔ PMO برای کنترل کیفیت"; }));
            EnHandoverDto rel = Ok(await Act(host, Pmo2, "release", d => d.Id = h.Id));
            Assert.AreEqual("متن ویرایش‌شدهٔ PMO برای کنترل کیفیت", rel.Distribution.Single(x => x.Unit == (int)EnUnit.Qc).ScopeNote, "an edited text is never touched");
            string qa = rel.Distribution.Single(x => x.Unit == (int)EnUnit.Qa).ScopeNote;
            Assert.IsFalse(qa.Contains("هنوز نرسیده"), "the unedited text was rebuilt: both documents have arrived");
        }

        // ------------------------------------------------------------------ M5 / M6

        [TestMethod]
        public async Task M5_A_Header_Save_Without_Lines_Keeps_The_Lines_And_A_New_List_Still_Needs_Them()
        {
            AppHost host = Started("e1cfix_m5");
            EnReferenceListDto d = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(Ref("VDDR-HDR", ("BOM", true, false), ("DWG", true, true)), Ct)));
            EnSaveReferenceDto header = new EnSaveReferenceDto { Id = d.Id, RowVersion = d.RowVersion, Code = "VDDR-HDR", Title = "عنوان تازهٔ سربرگ", StandardSource = "API 617", Note = "یادداشت", Lines = null };
            EnReferenceListDto r = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(header, Ct)));
            Assert.AreEqual("عنوان تازهٔ سربرگ", r.Title);
            Assert.AreEqual(2, r.Lines.Count, "the lines were not touched");
            Refused(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(new EnSaveReferenceDto { Code = "VDDR-NOLINES", Title = "x" }, Ct)), "ردیف");
            header.RowVersion = r.RowVersion; header.Lines = new List<EnReferenceLineDto>();
            Refused(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(header, Ct)), "ردیف");   // an explicit empty list is still refused
        }

        [TestMethod]
        public async Task M6_The_Seat_Is_Refused_On_Both_Upload_Paths_With_The_Ai_Text()
        {
            AppHost host = Started("e1cfix_m6seat");
            EnReferenceListDto d = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(Ref("VDDR-SEAT2", ("BOM", true, false)), Ct)));
            var seat = await SeatAsync(host);
            Refused(await Upload(host, seat, d, Csv(RevisionLines)), "هوش مصنوعی");
            Refused(await host.As(seat, p => p.GetRequiredService<IEnReferenceService>().CreateFromFileAsync(new EnSaveReferenceDto { Code = "VDDR-SEAT3", Title = "x" }, Csv(RevisionLines), Ct)), "هوش مصنوعی");
        }

        [TestMethod]
        public async Task M6_Crlf_Quoted_Line_Breaks_Upper_Case_Header_And_Lines_Of_Only_Commas_Are_Read()
        {
            AppHost host = Started("e1cfix_m6csv");
            EnReferenceListDto d = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(Ref("VDDR-CRLF", ("BOM", true, false)), Ct)));
            string csv = "DOCTYPE,Title,Discipline,REQUIRED,Tender,Units\r\nDWG,\"نقشه\r\nدو خطی\",مکانیک,1,1,\r\n,,,,,\r\n   ,  ,,,,\r\nBOM,فهرست,,0,0,\r\n,,,,,";
            EnReferenceListDto r = Ok(await Upload(host, Eng1, d, Csv(csv)));
            Assert.AreEqual(2, r.Lines.Count);
            StringAssert.Contains(r.Lines.Single(l => l.DocumentType == "DWG").Title, "دو خطی");
        }

        [TestMethod]
        public async Task M6_Approved_Codes_List_Only_The_Approved_Revision_With_Its_Title()
        {
            AppHost host = Started("e1cfix_m6codes");
            EnReferenceListDto v1 = await ApprovedList(host, "AC-1", ("PID", true, true));
            EnReferenceListDto d2 = Ok(await RefAction(host, Eng1, v1.Id, "revise"));
            EnSaveReferenceDto save = Ref("AC-1", ("PID", true, true)); save.Id = d2.Id; save.RowVersion = d2.RowVersion; save.Title = "عنوان بازنگری دو";
            d2 = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(save, Ct)));
            var before = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().GetApprovedCodesAsync(Ct)));
            Assert.AreEqual("فهرست مدارک مرجع آزمون", before.Single(x => x.Code == "AC-1").Title, "a draft revision is not offered");
            Ok(await RefAction(host, Eng1, d2.Id, "submit"));
            Ok(await RefAction(host, Mgr1, d2.Id, "approve"));
            Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(Ref("AC-DRAFT", ("PID", true, true)), Ct)));
            var after = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().GetApprovedCodesAsync(Ct)));
            Assert.AreEqual(1, after.Count, "the superseded revision and the draft list are not offered");
            Assert.AreEqual("عنوان بازنگری دو", after.Single().Title);
        }
    }
}
