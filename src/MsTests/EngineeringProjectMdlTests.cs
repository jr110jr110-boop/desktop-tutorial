using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Engineering;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.EngineeringDtos;
using ViewModels.NasbNiro.ProjectControlDtos;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1g: the MDL of a PROJECT in the tender (employer rule 20): draft, file replacement, check, approve, void, client approval, the CEO permit, and the lock of the tender package release.</summary>
    [TestClass]
    public class EngineeringProjectMdlTests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        private const string Head = "docType,title,discipline,required,tender,units,clientSubmittal,clause,note\n";
        private const string PermitReason = "پروژهٔ خاص با مهلت کوتاه کارفرما؛ فهرست مدارک پروژه همزمان با اسناد مناقصه آماده نمی‌شود و مدیرعامل باید تصمیم بگیرد";
        private const string LockText = "انتشار بستهٔ فنی مناقصه بسته است: فهرست مدارک پروژه (MDL) هنوز توسط مدیر مهندسی تایید نشده است. در پروژهٔ خاص، مدیر مهندسی «مناقصه بدون MDL» را درخواست می‌کند و فقط مدیرعامل تصمیم می‌گیرد.";
        private const string NoticeFromFile = "فهرست مدارک پروژه (MDL) را از فایل بسازید یا برای مناقصه بدون MDL از مدیرعامل مجوز بگیرید.";
        private const string NoticeText = "بستهٔ فنی مناقصه تا تایید فهرست مدارک پروژه (MDL) منتشر نمی‌شود.";

        /// <summary>The standard list of the tests: BOM, OPC, DWG, NOTE (titles «مدرک BOM», ...).</summary>
        private static async Task<EnCaseDto> NewCase(AppHost host)
        {
            EnCaseDto c = await ApprovedCase(host, Inquiry(host), false);
            Assert.AreEqual((int)EnCaseStatus.TenderPreparation, c.Status);
            return c;
        }

        private static async Task<EnProjectMdlCaseDto> Draft(AppHost host, int caseId, ClaimsPrincipal who = null) =>
            Ok(await MdlAct(host, who ?? await SeatAsync(host), "draft", d => d.CaseId = caseId));

        private static async Task<EnProjectMdlCaseDto> Checked(AppHost host, int caseId, ClaimsPrincipal checker = null)
        {
            EnProjectMdlCaseDto v = Ok(await MdlView(host, Eng1, caseId));
            return Ok(await MdlAct(host, checker ?? Eng1, "check", d => { d.Id = v.Mdl.Id; d.RowVersion = v.Mdl.RowVersion; }));
        }

        /// <summary>A case in the tender preparation whose tender package is ready except for the project MDL (a new design: a concept note, a document added by Mgr2, who therefore does not release it; Mgr1 releases).</summary>
        private static async Task<EnCaseDto> Releasable(AppHost host)
        {
            EnCaseDto c = await NewCase(host);
            c = Ok(await CaseAct(host, await SeatAsync(host), c.Id, "draft-tender"));
            Ok(await host.As(Mgr2, p => p.GetRequiredService<Services.NasbNiro.ProjectControl.IEdmsService>().SaveTenderDocumentAsync(new PcTenderDocumentSaveDto { PackageId = c.TenderPackageId.Value, Title = "فهرست اجزا", DocumentType = "CONCEPT" }, Ct)));
            return Ok(await CaseAct(host, Eng1, c.Id, "save-concept", d => d.ConceptNote = "اجزای اصلی تعریف شده و ابعاد پوسته فرض شده است"));
        }

        private static async Task<byte[]> StoredAsync(AppHost host, Guid id)
        {
            var doc = await host.As(Eng1, p => p.GetRequiredService<Services.Contracts.IDocumentService>().DownloadDocumentAsync(id));
            using System.IO.MemoryStream ms = new System.IO.MemoryStream();
            await doc.FileStream.CopyToAsync(ms);
            return ms.ToArray();
        }

        private static int CaseEvents(AppHost host, int caseId, string kind) => host.Read(u => u.Set<EnCaseEvent>().Count(e => e.CaseId == caseId && e.Kind == kind));

        // ------------------------------------------------------------------------------------------ draft

        [TestMethod]
        public async Task The_Agent_Drafts_The_Project_Mdl_From_The_Approved_Standard_List_And_Only_Under_The_Right_Conditions()
        {
            AppHost host = Started("pm_draft");
            EnCaseDto c = await NewCase(host);
            EnProjectMdlCaseDto before = Ok(await MdlView(host, Eng1, c.Id));
            Assert.IsTrue(before.Applies);
            Assert.IsNull(before.Mdl);
            Assert.IsTrue(before.CanDraft);
            Assert.IsFalse(before.CanCheck);
            Assert.IsFalse(before.CanApprove);
            // roles: sales and a stranger do not draft; the agent seat and an engineer do
            Refused(await MdlAct(host, Sales1, "draft", d => d.CaseId = c.Id));
            Refused(await MdlAct(host, Nobody, "draft", d => d.CaseId = c.Id));
            EnProjectMdlCaseDto v = await Draft(host, c.Id);
            Assert.IsNotNull(v.Mdl);
            Assert.AreEqual((int)EnProjectMdlStatus.Draft, v.Mdl.Status);
            Assert.AreEqual(1, v.Mdl.Revision);
            StringAssert.StartsWith(v.Mdl.Number, "PMDL-");
            Assert.AreEqual(Std, v.Mdl.BasedOnCode);
            Assert.AreEqual(1, v.Mdl.BasedOnRevision);
            Assert.AreEqual(4, v.Mdl.Lines.Count);
            Assert.IsTrue(v.Mdl.Lines.All(l => l.Origin == 0 && !l.Removed));
            Assert.AreEqual(4, v.Mdl.StandardKeptCount);
            Assert.AreEqual(0, v.Mdl.RemovedCount);
            Assert.AreEqual(0, v.Mdl.AddedCount);
            Assert.AreEqual("مدرک BOM", v.Mdl.Lines[0].Title);
            Assert.IsTrue(v.Mdl.Lines[0].Required && v.Mdl.Lines[0].TenderStage);
            Assert.IsFalse(v.Mdl.Lines[1].TenderStage);
            Assert.IsFalse(v.Mdl.Lines[3].Required);
            Assert.AreEqual(1, CaseEvents(host, c.Id, "project-mdl-drafted"));
            // one live revision per case, also not by an engineer
            Assert.IsFalse(v.CanDraft);
            StringAssert.Contains(Refused(await MdlAct(host, Eng1, "draft", d => d.CaseId = c.Id)), v.Mdl.Number);
            Assert.AreEqual(Refused(await MdlAct(host, Eng1, "draft", d => d.CaseId = c.Id)), Ok(await MdlView(host, Eng1, c.Id)).DraftBlockedReason);
        }

        [TestMethod]
        public async Task The_Draft_Is_Refused_Without_An_Approved_Standard_List_For_A_Case_Outside_The_Tender_Preparation_For_Spare_Parts_And_With_An_Approved_Permit()
        {
            AppHost host = Started("pm_draft2");
            // not in the tender preparation
            EnCaseDto open = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = Inquiry(host)));
            Refused(await MdlAct(host, Eng1, "draft", d => d.CaseId = open.Id), "مرحلهٔ تهیهٔ بستهٔ فنی");
            Assert.IsFalse(Ok(await MdlView(host, Eng1, open.Id)).CanDraft);
            Assert.IsFalse(Ok(await MdlView(host, Eng1, open.Id)).Applies);
            // the approved list of the standard is gone
            EnCaseDto c = await NewCase(host);
            host.Write(u => { foreach (EnReferenceList l in u.Set<EnReferenceList>().Where(l => l.Code == Std)) l.Status = EnRefStatus.Superseded; u.SaveChanges(); }, Seeder);
            Refused(await MdlAct(host, Eng1, "draft", d => d.CaseId = c.Id), "فهرست MDL تاییدشده");
            host.Write(u => { foreach (EnReferenceList l in u.Set<EnReferenceList>().Where(l => l.Code == Std)) l.Status = EnRefStatus.Approved; u.SaveChanges(); }, Seeder);
            // an approved permit: a project MDL is not drafted
            Ok(await MdlAct(host, Mgr1, "request-permit", d => { d.CaseId = c.Id; d.Reason = PermitReason; }));
            int permit = Ok(await MdlView(host, Mgr1, c.Id)).Permit.Id;
            Assert.IsTrue(Ok(await MdlView(host, Mgr1, c.Id)).CanDraft, "a requested permit does not stop the draft");
            Ok(await MdlAct(host, Ceo, "decide-permit", d => { d.CaseId = c.Id; d.PermitId = permit; d.Approve = true; }));
            Refused(await MdlAct(host, Eng1, "draft", d => d.CaseId = c.Id), "مجوز");
            Assert.IsFalse(Ok(await MdlView(host, Eng1, c.Id)).CanDraft);
            // an unknown case
            Assert.IsFalse((await MdlAct(host, Eng1, "draft", d => d.CaseId = 99999)).Success);
        }

        // ------------------------------------------------------------------------------------------ csv and upload

        [TestMethod]
        public async Task The_Csv_Has_The_Format_Of_The_Reference_Upload_And_The_Removed_Standard_Lines_Are_Absent()
        {
            AppHost host = Started("pm_csv");
            EnCaseDto c = await NewCase(host);
            EnProjectMdlCaseDto v = await Draft(host, c.Id);
            var csv = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnProjectMdlService>().GetCsvAsync(v.Mdl.Id, Ct)));
            string text = System.Text.Encoding.UTF8.GetString(csv.Content).TrimStart('\uFEFF');
            Assert.AreEqual(EnReferenceCsv.HeaderText + ",clientSubmittal,clause,note", text.Split('\n')[0].Trim());
            StringAssert.Contains(csv.FileName, "MDL-" + c.Number);
            Assert.IsNull(EnReferenceCsv.Parse(csv.Content, out var lines, out var columns));
            Assert.AreEqual(4, lines.Count);
            Assert.IsTrue(columns.ClientSubmittal && columns.Clause && columns.Note);
            Assert.AreEqual("مدرک DWG", lines[2].Title);
            // the engineer edits the file: one standard line removed
            v = Ok(await MdlUpload(host, Eng1, v.Mdl, Head + "BOM,مدرک BOM,مکانیک,1,1,,0,,\nOPC,مدرک OPC,مکانیک,1,0,,0,,\nDWG,مدرک DWG,مکانیک,1,1,,0,,\n"));
            csv = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnProjectMdlService>().GetCsvAsync(v.Mdl.Id, Ct)));
            Assert.IsNull(EnReferenceCsv.Parse(csv.Content, out lines, out _));
            Assert.AreEqual(3, lines.Count);
            Assert.IsFalse(lines.Any(l => l.Title == "مدرک NOTE"), "the removed standard line is absent from the file");
            // a stranger does not download it
            Refused(await host.As(Qa1, p => p.GetRequiredService<IEnProjectMdlService>().GetCsvAsync(v.Mdl.Id, Ct)));
        }

        [TestMethod]
        public async Task The_Upload_Keeps_Standard_Lines_Marks_Removed_Ones_Adds_Project_Lines_And_Keeps_The_File_As_Evidence()
        {
            AppHost host = Started("pm_up");
            EnCaseDto c = await NewCase(host);
            EnProjectMdlCaseDto v = await Draft(host, c.Id);
            Guid originalDoc = Guid.Empty;
            string file = Head + "BOM,مدرک BOM,مکانیک,1,1,,0,,\nOPC,مدرک OPC,مکانیک,0,0,,0,,\nDWG,مدرک DWG,مکانیک,1,1,,0,,\nTST,روش آزمون ویژهٔ پروژه,کیفیت,1,1,QC;QA,1,بند ۵,برای این پروژه\n";
            // refusals: a seat, sales, a short note, a bad file, an empty file
            ClaimsPrincipal seatWriter = await SeatAsync(host);
            Refused(await MdlUpload(host, seatWriter, v.Mdl, file), "انسان");
            Refused(await MdlUpload(host, Sales1, v.Mdl, file));
            Refused(await MdlUpload(host, Eng1, v.Mdl, file, "کوتاه"), "یادداشت");
            Refused(await MdlUpload(host, Eng1, v.Mdl, "a,b\n1,2\n"), "سرستون");
            Refused(await MdlUpload(host, Eng1, v.Mdl, Head), "هیچ ردیف");
            Refused(await MdlUpload(host, Eng1, v.Mdl, file, version: "1"), "هم‌زمان");
            Assert.AreEqual(4, Ok(await MdlView(host, Eng1, c.Id)).Mdl.Lines.Count, "refusals change nothing");
            Assert.IsNull(Ok(await MdlView(host, Eng1, c.Id)).Mdl.SourceDocumentId);
            EnProjectMdlCaseDto after = Ok(await MdlUpload(host, Eng2, v.Mdl, file, "حذف مدرک عمومی و افزودن روش آزمون ویژه مطابق مشخصات کارفرما"));
            EnProjectMdlDto m = after.Mdl;
            Assert.AreEqual(3, m.StandardKeptCount);
            Assert.AreEqual(1, m.RemovedCount);
            Assert.AreEqual(1, m.AddedCount);
            Assert.AreEqual(4, m.LineCount);
            Assert.AreEqual(5, m.Lines.Count, "the removed standard line stays as a marked row");
            EnProjectMdlLineDto removed = m.Lines.Single(l => l.Removed);
            Assert.AreEqual("مدرک NOTE", removed.Title);
            Assert.AreEqual(0, removed.Origin);
            Assert.IsFalse(m.Lines.Where(l => !l.Removed).Any(l => l.Title == "مدرک NOTE"));
            EnProjectMdlLineDto added = m.Lines.Single(l => l.Origin == 1);
            Assert.AreEqual("روش آزمون ویژهٔ پروژه", added.Title);
            Assert.AreEqual("Qa,Qc", added.Units);
            Assert.IsTrue(added.ClientSubmittal);
            Assert.AreEqual("بند ۵", added.StandardClause);
            Assert.IsFalse(m.Lines.Single(l => l.Title == "مدرک OPC").Required, "a standard line keeps its origin and takes the values of the file");
            Assert.AreEqual(0, m.Lines.Single(l => l.Title == "مدرک OPC").Origin);
            Assert.AreEqual("project-mdl.csv", m.SourceFileName);
            Assert.IsNotNull(m.SourceDocumentId);
            Assert.AreEqual("حذف مدرک عمومی و افزودن روش آزمون ویژه مطابق مشخصات کارفرما", m.ChangeNote);
            Assert.AreEqual((int)EnProjectMdlStatus.Draft, m.Status);
            Assert.AreEqual(1, CaseEvents(host, c.Id, "project-mdl-uploaded"));
            originalDoc = m.SourceDocumentId.Value;
            CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes(file)).ToArray(), await StoredAsync(host, originalDoc));
            // a second upload: the removed line returns, the project line is dropped (never physically deleted), the file pointer changes
            EnProjectMdlCaseDto again = Ok(await MdlUpload(host, Eng1, m, Head + "BOM,مدرک BOM,مکانیک,1,1,,0,,\nNOTE,مدرک NOTE,مکانیک,0,0,,0,,\n", "بازگرداندن مدرک عمومی و حذف روش آزمون"));
            Assert.AreEqual(2, again.Mdl.Lines.Count(l => l.Removed), "OPC and DWG are removed now");
            Assert.AreEqual(0, again.Mdl.AddedCount);
            Assert.IsFalse(again.Mdl.Lines.Any(l => l.Title == "روش آزمون ویژهٔ پروژه"));
            Assert.IsFalse(again.Mdl.Lines.Single(l => l.Title == "مدرک NOTE").Removed);
            Assert.AreEqual(1, host.Read(u => u.Set<EnProjectMdlLine>().Count(l => l.ProjectMdlId == m.Id && l.Origin == EnProjectMdlLineOrigin.Project && l.Deleted)), "kept in the database for the audit");
            Assert.AreNotEqual(originalDoc, again.Mdl.SourceDocumentId);
            Assert.AreEqual(2, again.Mdl.LineCount);
            // the last line cannot be removed: a file with no line is refused (above); a file whose lines all match keeps them
            Assert.AreEqual(2, EnProjectMdlRulesKept(host, m.Id));
        }

        private static int EnProjectMdlRulesKept(AppHost host, int id) => host.Read(u => u.Set<EnProjectMdlLine>().Count(l => l.ProjectMdlId == id && !l.Deleted && !l.Removed));

        // ------------------------------------------------------------------------------------------ check, return, approve

        [TestMethod]
        public async Task Check_Return_And_Approve_Follow_The_Roles_The_Seat_Rule_The_Statuses_And_The_Checker_Rule()
        {
            AppHost host = Started("pm_chain");
            EnCaseDto c = await NewCase(host);
            ClaimsPrincipal seat = await SeatAsync(host);
            ClaimsPrincipal seatManager = await SeatAsync(host, "AG-ENG", "ENGINEERING");
            EnProjectMdlCaseDto v = await Draft(host, c.Id);
            string version = v.Mdl.RowVersion;
            // nothing to approve or return in a draft
            Assert.IsFalse(Ok(await MdlView(host, Mgr1, c.Id)).CanApprove);
            Refused(await MdlAct(host, Mgr1, "approve", d => { d.Id = v.Mdl.Id; }), "بررسی‌شده");
            Refused(await MdlAct(host, Mgr1, "return", d => { d.Id = v.Mdl.Id; d.Reason = "دلیل برگشت به اندازهٔ کافی طولانی"; }), "بررسی‌شده");
            // check: a seat, sales, a stranger refused; the uploader may check
            Assert.IsFalse(Ok(await MdlView(host, seat, c.Id)).CanCheck);
            Refused(await MdlAct(host, seat, "check", d => d.Id = v.Mdl.Id), "انسان");
            Refused(await MdlAct(host, seatManager, "check", d => d.Id = v.Mdl.Id), "انسان");
            Refused(await MdlAct(host, Sales1, "check", d => d.Id = v.Mdl.Id));
            Refused(await MdlAct(host, Eng1, "check", d => { d.Id = v.Mdl.Id; d.RowVersion = "1"; }), "هم‌زمان");
            EnProjectMdlCaseDto up = Ok(await MdlUpload(host, Eng1, v.Mdl, Head + "BOM,مدرک BOM,مکانیک,1,1,,0,,\nDWG,مدرک DWG,مکانیک,1,1,,0,,\n"));
            EnProjectMdlCaseDto chk = Ok(await MdlAct(host, Eng1, "check", d => { d.Id = v.Mdl.Id; d.RowVersion = up.Mdl.RowVersion; }));
            Assert.AreEqual((int)EnProjectMdlStatus.Checked, chk.Mdl.Status);
            Assert.AreEqual(2111, chk.Mdl.CheckedByUserId);
            Refused(await MdlAct(host, Eng2, "check", d => d.Id = v.Mdl.Id), "پیش‌نویس");
            // return: manager only, reason of 15 characters, never a seat
            Refused(await MdlAct(host, Eng2, "return", d => { d.Id = v.Mdl.Id; d.Reason = "دلیل برگشت به اندازهٔ کافی طولانی"; }));
            Refused(await MdlAct(host, seatManager, "return", d => { d.Id = v.Mdl.Id; d.Reason = "دلیل برگشت به اندازهٔ کافی طولانی"; }), "انسان");
            Refused(await MdlAct(host, Mgr1, "return", d => { d.Id = v.Mdl.Id; d.Reason = "کوتاه"; }), "دلیل");
            Assert.AreEqual((int)EnProjectMdlStatus.Draft, Ok(await MdlAct(host, Mgr1, "return", d => { d.Id = v.Mdl.Id; d.Reason = "ردیف نقشه باید دوباره دیده شود"; d.RowVersion = Ver(host, c.Id); })).Mdl.Status);
            Assert.IsNull(Ok(await MdlView(host, Eng1, c.Id)).Mdl.CheckedByName);
            // a manager checks (a manager is an engineering writer); the same manager does not approve, the other one does
            chk = await Checked(host, c.Id, Mgr1);
            Assert.IsFalse(Ok(await MdlView(host, Mgr1, c.Id)).CanApprove);
            Assert.AreEqual(Refused(await MdlAct(host, Mgr1, "approve", d => d.Id = v.Mdl.Id)), Ok(await MdlView(host, Mgr1, c.Id)).ApproveBlockedReason);
            StringAssert.Contains(Ok(await MdlView(host, Mgr1, c.Id)).ApproveBlockedReason, "بررسی کرده");
            Refused(await MdlAct(host, Eng2, "approve", d => d.Id = v.Mdl.Id));
            Refused(await MdlAct(host, Ceo, "approve", d => d.Id = v.Mdl.Id));
            Refused(await MdlAct(host, seatManager, "approve", d => d.Id = v.Mdl.Id), "انسان");
            Assert.IsTrue(Ok(await MdlView(host, Mgr2, c.Id)).CanApprove);
            EnProjectMdlCaseDto ok = Ok(await MdlAct(host, Mgr2, "approve", d => { d.Id = v.Mdl.Id; d.RowVersion = chk.Mdl.RowVersion; }));
            EnProjectMdlDto m = ok.Mdl;
            Assert.AreEqual((int)EnProjectMdlStatus.Approved, m.Status);
            Assert.AreEqual(2102, m.ApprovedByUserId);
            Assert.IsFalse(m.ApprovalStale);
            Assert.IsNotNull(m.IssuedDocumentId);
            Assert.AreEqual("MDL-" + c.Number + "-R1.csv", m.IssuedFileName);
            Assert.AreEqual(64, host.Read(u => u.Set<EnProjectMdl>().Single(x => x.Id == m.Id).ApprovedHash.Length));
            // the issued file is the CSV of the not-removed lines
            Assert.IsNull(EnReferenceCsv.Parse(await StoredAsync(host, m.IssuedDocumentId.Value), out var lines, out _));
            CollectionAssert.AreEqual(new[] { "مدرک BOM", "مدرک DWG" }, lines.Select(l => l.Title).ToArray());
            Assert.AreEqual(1, CaseEvents(host, c.Id, "project-mdl-approved"));
            // an approved list is not replaced or checked again, and does not return
            Assert.IsFalse(ok.CanUpload || ok.CanCheck || ok.CanReturn || ok.CanApprove);
            Refused(await MdlUpload(host, Eng1, m, Head + "BOM,مدرک BOM,مکانیک,1,1,,0,,\n", version: null), "تاییدشده");
            Refused(await MdlAct(host, Mgr1, "return", d => { d.Id = m.Id; d.Reason = "دلیل برگشت به اندازهٔ کافی طولانی"; }), "بررسی‌شده");
        }

        // ------------------------------------------------------------------------------------------ the lock

        [TestMethod]
        public async Task The_Tender_Release_Is_Locked_Without_An_Approved_Project_Mdl_And_The_Flag_Says_The_Same()
        {
            AppHost host = Started("pm_lock");
            EnCaseDto c = await Releasable(host);
            Assert.AreEqual(NoticeText, c.ProjectMdlNotice);
            Assert.AreEqual(NoticeText, Ok(await GetCase(host, Mgr1, c.Id)).ProjectMdlNotice);
            EnCaseDto asMgr = Ok(await GetCase(host, Mgr1, c.Id));
            Assert.IsFalse(asMgr.CanReleaseTender);
            Assert.AreEqual(LockText, asMgr.ReleaseTenderBlockedReason);
            Assert.AreEqual(LockText, Refused(await CaseAct(host, Mgr1, c.Id, "release-tender")));
            // a draft or a checked list is not enough
            await Draft(host, c.Id);
            Assert.AreEqual(LockText, Refused(await CaseAct(host, Mgr1, c.Id, "release-tender")));
            await Checked(host, c.Id);
            Assert.AreEqual(LockText, Refused(await CaseAct(host, Mgr1, c.Id, "release-tender")));
            Assert.AreEqual((int)EnCaseStatus.TenderPreparation, Ok(await GetCase(host, Mgr1, c.Id)).Status);
            // a requested or a rejected permit is not enough
            Ok(await MdlAct(host, Mgr1, "request-permit", d => { d.CaseId = c.Id; d.Reason = PermitReason; }));
            Assert.AreEqual(LockText, Refused(await CaseAct(host, Mgr1, c.Id, "release-tender")));
            int permit = Ok(await MdlView(host, Mgr1, c.Id)).Permit.Id;
            Ok(await MdlAct(host, Ceo, "decide-permit", d => { d.CaseId = c.Id; d.PermitId = permit; d.Approve = false; d.Note = "درخواست مجوز پذیرفته نشد؛ فهرست را تهیه کنید"; }));
            Assert.AreEqual(LockText, Refused(await CaseAct(host, Mgr1, c.Id, "release-tender")));
            Assert.AreEqual(LockText, Ok(await GetCase(host, Mgr1, c.Id)).ReleaseTenderBlockedReason);
        }

        [TestMethod]
        public async Task An_Approved_Project_Mdl_Opens_The_Release_Its_File_Joins_The_Package_Once_And_A_Changed_List_Closes_It_Again()
        {
            AppHost host = Started("pm_release");
            EnCaseDto c = await Releasable(host);
            EnProjectMdlCaseDto ap = await ApprovedMdl(host, c.Id);
            c = Ok(await GetCase(host, Mgr1, c.Id));
            Assert.IsNull(c.ProjectMdlNotice);
            Assert.IsTrue(c.CanReleaseTender);
            Assert.AreEqual(1, c.TenderDocumentCount);
            // the lines change after the approval (a direct change in the database): the approval is stale and the lock closes
            host.Write(u => { u.Set<EnProjectMdlLine>().Where(l => l.ProjectMdlId == ap.Mdl.Id).OrderBy(l => l.Id).First().Title = "مدرک تغییر کرده"; u.SaveChanges(); }, Seeder);
            Assert.IsTrue(Ok(await MdlView(host, Mgr1, c.Id)).Mdl.ApprovalStale);
            Assert.AreEqual(NoticeText, Ok(await GetCase(host, Mgr1, c.Id)).ProjectMdlNotice);
            Assert.IsFalse(Ok(await GetCase(host, Mgr1, c.Id)).CanReleaseTender);
            Assert.AreEqual(LockText, Refused(await CaseAct(host, Mgr1, c.Id, "release-tender")));
            host.Write(u => { u.Set<EnProjectMdlLine>().Where(l => l.ProjectMdlId == ap.Mdl.Id).OrderBy(l => l.Id).First().Title = "مدرک BOM"; u.SaveChanges(); }, Seeder);
            Assert.IsFalse(Ok(await MdlView(host, Mgr1, c.Id)).Mdl.ApprovalStale);
            // the release: the MDL file is added once, with its title, and it is not a reason to refuse the releasing manager
            c = Ok(await CaseAct(host, Mgr1, c.Id, "release-tender"));
            Assert.AreEqual((int)EnCaseStatus.TenderIssued, c.Status);
            Assert.AreEqual(2, c.TenderDocumentCount);
            EnTenderDocumentDto mdl = c.TenderDocuments.Single(d => d.DocumentId == ap.Mdl.IssuedDocumentId);
            Assert.AreEqual("فهرست مدارک پروژه (MDL) — ویرایش 1", mdl.Title);
            StringAssert.Contains(host.Read(u => u.Set<EnCaseEvent>().Single(e => e.CaseId == c.Id && e.Kind == "tender-released").Note), ap.Mdl.Number);
            Assert.IsNull(c.ProjectMdlNotice);
            // later statuses are not blocked: a document added after the release voids it, and the next release finds the MDL file already in the package
            Ok(await host.As(Mgr2, p => p.GetRequiredService<IEdmsService>().SaveTenderDocumentAsync(new PcTenderDocumentSaveDto { PackageId = c.TenderPackageId.Value, Title = "ملحق فنی", DocumentType = "TENDER" }, Ct)));
            c = Ok(await CaseAct(host, Eng1, c.Id, "revalidate-tender"));
            Assert.AreEqual((int)EnCaseStatus.TenderPreparation, c.Status);
            Assert.AreEqual(3, c.TenderDocumentCount);
            c = Ok(await CaseAct(host, Mgr1, c.Id, "release-tender"));
            Assert.AreEqual((int)EnCaseStatus.TenderIssued, c.Status);
            Assert.AreEqual(3, c.TenderDocumentCount, "the MDL file is not added twice");
            Assert.AreEqual(1, c.TenderDocuments.Count(d => d.DocumentId == ap.Mdl.IssuedDocumentId));
            // the project MDL of a released package is not voided
            Refused(await MdlAct(host, Mgr1, "void", d => { d.Id = ap.Mdl.Id; d.Reason = "ابطال پس از ابلاغ بسته نباید ممکن باشد"; }), "منتشر");
            Assert.IsFalse(Ok(await MdlView(host, Mgr1, c.Id)).CanVoid);
        }

        [TestMethod]
        public async Task An_Approved_Ceo_Permit_Opens_The_Release_Without_An_Mdl_And_The_Package_Says_So()
        {
            AppHost host = Started("pm_permit_release");
            EnCaseDto c = await Releasable(host);
            Ok(await MdlAct(host, Mgr1, "request-permit", d => { d.CaseId = c.Id; d.Reason = PermitReason; }));
            EnProjectMdlCaseDto v = Ok(await MdlView(host, Mgr1, c.Id));
            string number = v.Permit.Number;
            Ok(await MdlAct(host, Ceo, "decide-permit", d => { d.CaseId = c.Id; d.PermitId = v.Permit.Id; d.Approve = true; d.Note = "پروژهٔ خاص است"; }));
            c = Ok(await GetCase(host, Mgr1, c.Id));
            Assert.IsNull(c.ProjectMdlNotice);
            Assert.IsTrue(c.CanReleaseTender);
            c = Ok(await CaseAct(host, Mgr1, c.Id, "release-tender"));
            Assert.AreEqual((int)EnCaseStatus.TenderIssued, c.Status);
            Assert.AreEqual(1, c.TenderDocumentCount, "no MDL file");
            StringAssert.Contains(host.Read(u => u.Set<EnCaseEvent>().Single(e => e.CaseId == c.Id && e.Kind == "tender-released").Note), "بدون فهرست مدارک پروژه");
            StringAssert.Contains(host.Read(u => u.Set<EnCaseEvent>().Single(e => e.CaseId == c.Id && e.Kind == "tender-released").Note), number);
            StringAssert.Contains(host.Read(u => u.Set<PcTenderPackage>().Single(p => p.Id == c.TenderPackageId.Value).Note), number);
            StringAssert.Contains(host.Read(u => u.Set<PcTenderPackage>().Single(p => p.Id == c.TenderPackageId.Value).Note), "بدون MDL");
        }

        // ------------------------------------------------------------------------------------------ void

        [TestMethod]
        public async Task Void_Is_For_The_Manager_With_A_Reason_While_The_Package_Is_Not_Released_And_A_New_Draft_Follows()
        {
            AppHost host = Started("pm_void");
            EnCaseDto c = await Releasable(host);
            EnProjectMdlCaseDto v = await Draft(host, c.Id);
            Refused(await MdlAct(host, Eng1, "void", d => { d.Id = v.Mdl.Id; d.Reason = "دلیل ابطال به اندازهٔ کافی طولانی"; }));
            Refused(await MdlAct(host, await SeatAsync(host, "AG-ENG", "ENGINEERING"), "void", d => { d.Id = v.Mdl.Id; d.Reason = "دلیل ابطال به اندازهٔ کافی طولانی"; }), "انسان");
            Refused(await MdlAct(host, Mgr1, "void", d => { d.Id = v.Mdl.Id; d.Reason = "کوتاه"; }), "دلیل");
            Assert.IsTrue(Ok(await MdlView(host, Mgr1, c.Id)).CanVoid);
            Assert.IsFalse(Ok(await MdlView(host, Eng1, c.Id)).CanVoid);
            EnProjectMdlCaseDto voided = Ok(await MdlAct(host, Mgr1, "void", d => { d.Id = v.Mdl.Id; d.Reason = "فهرست استاندارد بازنگری شد و پیش‌نویس کهنه است"; d.RowVersion = Ver(host, c.Id); }));
            Assert.IsNull(voided.Mdl, "no live revision");
            Assert.AreEqual(1, voided.History.Count);
            Assert.AreEqual((int)EnProjectMdlStatus.Void, voided.History[0].Status);
            Assert.IsTrue(voided.CanDraft);
            Refused(await MdlAct(host, Mgr1, "void", d => { d.Id = v.Mdl.Id; d.Reason = "فهرست استاندارد بازنگری شد و پیش‌نویس کهنه است"; }));
            // a new draft is revision 2; an approved list may be voided before the release too
            EnProjectMdlCaseDto second = await Draft(host, c.Id);
            Assert.AreEqual(2, second.Mdl.Revision);
            Assert.AreNotEqual(v.Mdl.Number, second.Mdl.Number);
            Assert.AreEqual(2, second.History.Count);
            await Checked(host, c.Id);
            EnProjectMdlCaseDto approved = Ok(await MdlAct(host, Mgr1, "approve", d => { d.Id = second.Mdl.Id; d.RowVersion = Ver(host, c.Id); }));
            Assert.IsTrue(approved.CanVoid);
            Assert.IsNull(Ok(await MdlAct(host, Mgr2, "void", d => { d.Id = second.Mdl.Id; d.Reason = "فهرست تاییدشده پیش از ابلاغ ابطال شد"; d.RowVersion = Ver(host, c.Id); })).Mdl);
            Assert.AreEqual(LockText, Refused(await CaseAct(host, Mgr1, c.Id, "release-tender")));
            Assert.AreEqual(3, (await Draft(host, c.Id)).Mdl.Revision);
        }

        // ------------------------------------------------------------------------------------------ client approval

        [TestMethod]
        public async Task Commercial_Records_The_Client_Approval_Only_For_An_Approved_Mdl_After_The_Release_And_A_Correction_Needs_A_Note()
        {
            AppHost host = Started("pm_client");
            EnCaseDto c = await Releasable(host);
            EnProjectMdlCaseDto ap = await ApprovedMdl(host, c.Id);
            Guid letter = File(host, 2132);
            Action<EnProjectMdlActionDto> body = d => { d.Id = ap.Mdl.Id; d.LetterRef = "نامهٔ ۱۲۳"; d.DocumentId = letter; };
            // before the release
            Assert.IsFalse(Ok(await MdlView(host, Comm1, c.Id)).CanRecordClientApproval);
            StringAssert.Contains(Refused(await MdlAct(host, Comm1, "record-client-approval", body)), "منتشر نشده");
            c = Ok(await CaseAct(host, Mgr1, c.Id, "release-tender"));
            // roles
            Refused(await MdlAct(host, Eng1, "record-client-approval", body));
            Refused(await MdlAct(host, Mgr1, "record-client-approval", body));
            Assert.IsTrue(Ok(await MdlView(host, Comm1, c.Id)).CanRecordClientApproval);
            // the letter number and the file are required; the file is the caller's own
            Refused(await MdlAct(host, Comm1, "record-client-approval", d => { d.Id = ap.Mdl.Id; d.DocumentId = letter; }), "شمارهٔ نامه");
            Refused(await MdlAct(host, Comm1, "record-client-approval", d => { d.Id = ap.Mdl.Id; d.LetterRef = "نامهٔ ۱۲۳"; }), "بارگذاری");
            Refused(await MdlAct(host, Comm1, "record-client-approval", d => { d.Id = ap.Mdl.Id; d.LetterRef = new string('x', 101); d.DocumentId = letter; }), "100");
            Refused(await MdlAct(host, Comm1, "record-client-approval", d => { d.Id = ap.Mdl.Id; d.LetterRef = "نامهٔ ۱۲۳"; d.DocumentId = File(host, 2101); }), "خود شما");
            Assert.IsNull(Ok(await MdlView(host, Comm1, c.Id)).Mdl.ClientApprovalRef);
            EnProjectMdlDto m = Ok(await MdlAct(host, Comm1, "record-client-approval", body)).Mdl;
            Assert.AreEqual("نامهٔ ۱۲۳", m.ClientApprovalRef);
            Assert.AreEqual(letter, m.ClientApprovalDocumentId);
            Assert.IsNotNull(m.ClientApprovedAt);
            Assert.AreEqual(2132, host.Read(u => u.Set<EnProjectMdl>().Single(x => x.Id == ap.Mdl.Id).ClientApprovalRecordedByUserId));
            // recorded once: a second record is a correction and needs a note; the audit keeps the first values
            Guid letter2 = File(host, 2132);
            Refused(await MdlAct(host, Comm1, "record-client-approval", d => { d.Id = ap.Mdl.Id; d.LetterRef = "نامهٔ ۱۲۴"; d.DocumentId = letter2; }), "یادداشت");
            m = Ok(await MdlAct(host, Comm1, "record-client-approval", d => { d.Id = ap.Mdl.Id; d.LetterRef = "نامهٔ ۱۲۴"; d.DocumentId = letter2; d.Note = "شمارهٔ نامه در ثبت نخست اشتباه وارد شده بود"; })).Mdl;
            Assert.AreEqual("نامهٔ ۱۲۴", m.ClientApprovalRef);
            Assert.AreEqual(1, CaseEvents(host, c.Id, "project-mdl-client-approved"));
            Assert.AreEqual(1, CaseEvents(host, c.Id, "project-mdl-client-corrected"));
            // a seat never records it
            Refused(await MdlAct(host, await SeatAsync(host, "AG-ENG", "COMMERCIAL"), "record-client-approval", d => { d.Id = ap.Mdl.Id; d.LetterRef = "x"; d.DocumentId = letter; d.Note = "یادداشت اصلاح به اندازهٔ کافی"; }), "انسان");
        }

        // ------------------------------------------------------------------------------------------ the permit

        [TestMethod]
        public async Task The_Permit_Is_Requested_By_The_Manager_With_A_Long_Reason_Decided_Only_By_The_Ceo_And_Can_Be_Withdrawn()
        {
            AppHost host = Started("pm_permit");
            EnCaseDto c = await NewCase(host);
            string forty9 = new string('ا', 49), fifty = new string('ا', 50);
            Assert.IsFalse(Ok(await MdlView(host, Eng1, c.Id)).CanRequestPermit);
            Refused(await MdlAct(host, Eng1, "request-permit", d => { d.CaseId = c.Id; d.Reason = PermitReason; }));
            Refused(await MdlAct(host, await SeatAsync(host, "AG-ENG", "ENGINEERING"), "request-permit", d => { d.CaseId = c.Id; d.Reason = PermitReason; }), "انسان");
            Refused(await MdlAct(host, Ceo, "request-permit", d => { d.CaseId = c.Id; d.Reason = PermitReason; }));
            Assert.IsTrue(Ok(await MdlView(host, Mgr1, c.Id)).CanRequestPermit);
            StringAssert.Contains(Refused(await MdlAct(host, Mgr1, "request-permit", d => { d.CaseId = c.Id; d.Reason = forty9; })), "50");
            EnProjectMdlCaseDto v = Ok(await MdlAct(host, Mgr1, "request-permit", d => { d.CaseId = c.Id; d.Reason = fifty; }));
            Assert.AreEqual((int)EnMdlPermitStatus.Requested, v.Permit.Status);
            StringAssert.StartsWith(v.Permit.Number, "EMP-");
            Assert.AreEqual(2101, v.Permit.RequestedByUserId);
            Assert.IsFalse(v.CanRequestPermit, "one live permit per case");
            StringAssert.Contains(Refused(await MdlAct(host, Mgr2, "request-permit", d => { d.CaseId = c.Id; d.Reason = fifty; })), v.Permit.Number);
            // deciding: not the manager, not an engineer, not a seat, not the requester
            Assert.IsFalse(Ok(await MdlView(host, Mgr2, c.Id)).CanDecidePermit);
            Refused(await MdlAct(host, Mgr2, "decide-permit", d => { d.CaseId = c.Id; d.PermitId = v.Permit.Id; d.Approve = true; }));
            Refused(await MdlAct(host, Eng1, "decide-permit", d => { d.CaseId = c.Id; d.PermitId = v.Permit.Id; d.Approve = true; }));
            Refused(await MdlAct(host, await SeatAsync(host, "AG-ENG", "CEO"), "decide-permit", d => { d.CaseId = c.Id; d.PermitId = v.Permit.Id; d.Approve = true; }), "انسان");
            ClaimsPrincipal both = AppHost.Principal(2190, "ceo.and.manager", "ENGINEERING", "CEO");
            Ok(await MdlAct(host, both, "withdraw-permit", d => { d.CaseId = c.Id; d.PermitId = v.Permit.Id; }));   // the requester or any manager withdraws
            Assert.AreEqual((int)EnMdlPermitStatus.Void, Ok(await MdlView(host, Mgr1, c.Id)).Permit.Status);
            Refused(await MdlAct(host, Mgr1, "withdraw-permit", d => { d.CaseId = c.Id; d.PermitId = v.Permit.Id; }));
            // the same person requesting and deciding
            v = Ok(await MdlAct(host, both, "request-permit", d => { d.CaseId = c.Id; d.Reason = fifty; }));
            Assert.IsFalse(Ok(await MdlView(host, both, c.Id)).CanDecidePermit);
            Refused(await MdlAct(host, both, "decide-permit", d => { d.CaseId = c.Id; d.PermitId = v.Permit.Id; d.Approve = true; }), "شخص دیگری");
            Refused(await MdlAct(host, Eng2, "withdraw-permit", d => { d.CaseId = c.Id; d.PermitId = v.Permit.Id; }));
            // the CEO: a rejection needs a note, an approval does not
            Assert.IsTrue(Ok(await MdlView(host, Ceo, c.Id)).CanDecidePermit);
            Refused(await MdlAct(host, Ceo, "decide-permit", d => { d.CaseId = c.Id; d.PermitId = v.Permit.Id; d.Approve = false; d.Note = "کوتاه"; }), "دلیل");
            EnProjectMdlCaseDto rej = Ok(await MdlAct(host, Ceo, "decide-permit", d => { d.CaseId = c.Id; d.PermitId = v.Permit.Id; d.Approve = false; d.Note = "پروژه خاص نیست و فهرست باید تهیه شود"; }));
            Assert.AreEqual((int)EnMdlPermitStatus.Rejected, rej.Permit.Status);
            Refused(await MdlAct(host, Ceo, "decide-permit", d => { d.CaseId = c.Id; d.PermitId = v.Permit.Id; d.Approve = true; }));
            // a new request after a rejection, approved by the CEO
            EnProjectMdlCaseDto again = Ok(await MdlAct(host, Mgr1, "request-permit", d => { d.CaseId = c.Id; d.Reason = fifty; }));
            Assert.AreNotEqual(v.Permit.Number, again.Permit.Number);
            EnProjectMdlCaseDto ok = Ok(await MdlAct(host, Ceo, "decide-permit", d => { d.CaseId = c.Id; d.PermitId = again.Permit.Id; d.Approve = true; }));
            Assert.AreEqual((int)EnMdlPermitStatus.Approved, ok.Permit.Status);
            Assert.IsFalse(ok.CanWithdrawPermit);
            Assert.IsFalse(ok.CanRequestPermit);
            Refused(await MdlAct(host, Mgr1, "withdraw-permit", d => { d.CaseId = c.Id; d.PermitId = again.Permit.Id; }));
        }

        [TestMethod]
        public async Task The_Permit_Is_Not_Asked_Outside_The_Tender_Preparation_Or_When_The_Mdl_Is_Approved_And_An_Approved_Mdl_After_A_Permit_Is_Allowed()
        {
            AppHost host = Started("pm_permit2");
            EnCaseDto open = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = Inquiry(host)));
            Refused(await MdlAct(host, Mgr1, "request-permit", d => { d.CaseId = open.Id; d.Reason = PermitReason; }), "مرحلهٔ تهیهٔ بستهٔ فنی");
            EnCaseDto c = await NewCase(host);
            await Draft(host, c.Id);
            await Checked(host, c.Id);
            Ok(await MdlAct(host, Mgr1, "approve", d => { d.CaseId = c.Id; d.RowVersion = Ver(host, c.Id); }));
            Refused(await MdlAct(host, Mgr1, "request-permit", d => { d.CaseId = c.Id; d.Reason = PermitReason; }), "تایید شده");
            Assert.IsFalse(Ok(await MdlView(host, Mgr1, c.Id)).CanRequestPermit);
        }

        // ------------------------------------------------------------------------------------------ reads and flags

        [TestMethod]
        public async Task Only_The_Engineering_Readers_And_Commercial_Read_The_Project_Mdl_And_It_Has_No_Price()
        {
            AppHost host = Started("pm_read");
            EnCaseDto c = await NewCase(host);
            await Draft(host, c.Id);
            foreach (ClaimsPrincipal who in new[] { Eng1, Mgr1, Ceo, Pmo1, Sales1, Comm1 }) Assert.IsNotNull(Ok(await MdlView(host, who, c.Id)).Mdl);
            Refused(await MdlView(host, Qa1, c.Id));
            Refused(await MdlView(host, Prod1, c.Id));
            Refused(await MdlView(host, Nobody, c.Id));
            Assert.IsFalse((await MdlView(host, Eng1, 99999)).Success);
            string[] names = typeof(EnProjectMdlCaseDto).Assembly.GetTypes().Where(t => t.Namespace == "ViewModels.NasbNiro.EngineeringDtos" && t.Name.Contains("Mdl")).SelectMany(t => t.GetProperties()).Select(p => p.Name).ToArray();
            Assert.IsFalse(names.Any(n => n.Contains("Price") || n.Contains("Amount") || n.Contains("Cost")), "no price in the project MDL DTOs");
        }

        /// <summary>For every action whose flag is false the action answers exactly the reason the flag shows; for every flag that is true the action is not refused for a rule (checked on a copy of the chain by the other tests).</summary>
        private static async Task FlagsMatchActionsAsync(AppHost host, ClaimsPrincipal who, int caseId, Guid letter)
        {
            EnProjectMdlCaseDto v = Ok(await MdlView(host, who, caseId));
            string reason = "دلیل به اندازهٔ کافی طولانی برای این آزمون است و بیش از پنجاه نویسه دارد، پس از همین رو درست است";
            var table = new (bool Can, string Why, string Action, Action<EnProjectMdlActionDto> Set)[]
            {
                (v.CanDraft, v.DraftBlockedReason, "draft", d => { }),
                (v.CanCheck, v.CheckBlockedReason, "check", d => { }),
                (v.CanReturn, v.ReturnBlockedReason, "return", d => d.Reason = reason),
                (v.CanApprove, v.ApproveBlockedReason, "approve", d => { }),
                (v.CanVoid, v.VoidBlockedReason, "void", d => d.Reason = reason),
                (v.CanRecordClientApproval, v.RecordClientApprovalBlockedReason, "record-client-approval", d => { d.LetterRef = "نامه"; d.DocumentId = letter; d.Note = reason; }),
                (v.CanRequestPermit, v.RequestPermitBlockedReason, "request-permit", d => d.Reason = reason),
                (v.CanDecidePermit, v.DecidePermitBlockedReason, "decide-permit", d => d.Approve = true),
                (v.CanWithdrawPermit, v.WithdrawPermitBlockedReason, "withdraw-permit", d => { }),
            };
            foreach (var row in table)
            {
                Assert.AreEqual(row.Can, row.Why == null, row.Action + ": the flag and its reason agree");
                if (row.Can) continue;
                var r = await MdlAct(host, who, row.Action, d => { d.CaseId = caseId; if (v.Permit != null) d.PermitId = v.Permit.Id; row.Set(d); });
                Assert.IsFalse(r.Success, row.Action + " must be refused when its flag is false");
                Assert.AreEqual(row.Why, r.Message, row.Action + ": the action answers the reason of the flag");
            }
        }

        [TestMethod]
        public async Task The_Flags_And_Their_Reasons_Equal_What_The_Actions_Answer_In_Every_State_For_Every_Role()
        {
            AppHost host = Started("pm_flags");
            EnCaseDto c = await Releasable(host);
            ClaimsPrincipal seat = await SeatAsync(host);
            ClaimsPrincipal seatManager = await SeatAsync(host, "AG-ENG", "ENGINEERING");
            ClaimsPrincipal[] everyone = { Eng1, Eng2, Mgr1, Mgr2, Ceo, Comm1, Sales1, Pmo1, seat, seatManager };
            Guid letter = File(host, 2132);
            // no list
            foreach (ClaimsPrincipal who in everyone) await FlagsMatchActionsAsync(host, who, c.Id, letter);
            await Draft(host, c.Id);   // a draft
            foreach (ClaimsPrincipal who in everyone) await FlagsMatchActionsAsync(host, who, c.Id, letter);
            await Checked(host, c.Id, Mgr1);   // checked by Mgr1
            foreach (ClaimsPrincipal who in everyone) await FlagsMatchActionsAsync(host, who, c.Id, letter);
            Ok(await MdlAct(host, Mgr2, "approve", d => { d.CaseId = c.Id; d.RowVersion = Ver(host, c.Id); }));   // approved, tender not released
            foreach (ClaimsPrincipal who in everyone) await FlagsMatchActionsAsync(host, who, c.Id, letter);
            Ok(await CaseAct(host, Mgr1, c.Id, "release-tender"));   // released
            foreach (ClaimsPrincipal who in everyone) await FlagsMatchActionsAsync(host, who, c.Id, letter);
            EnProjectMdlCaseDto last = Ok(await MdlView(host, Comm1, c.Id));
            Assert.IsTrue(last.CanRecordClientApproval);
            Assert.IsFalse(last.CanVoid);
            // a requested permit
            AppHost host2 = Started("pm_flags2");
            EnCaseDto c2 = await NewCase(host2);
            Ok(await MdlAct(host2, Mgr1, "request-permit", d => { d.CaseId = c2.Id; d.Reason = PermitReason; }));
            foreach (ClaimsPrincipal who in new[] { Eng1, Mgr1, Mgr2, Ceo, Comm1, await SeatAsync(host2) }) await FlagsMatchActionsAsync(host2, who, c2.Id, File(host2, 2132));
            Assert.IsTrue(Ok(await MdlView(host2, Ceo, c2.Id)).CanDecidePermit);
            Assert.IsTrue(Ok(await MdlView(host2, Mgr2, c2.Id)).CanWithdrawPermit);
        }

        // ------------------------------------------------------------------------------------------ scope (every kind of tender)

        [TestMethod]
        public async Task The_Scope_Is_Every_Case_That_Has_A_Tender_Package_And_A_Direct_Contract_Needs_Nothing()
        {
            AppHost host = Started("pm_scope");
            EnCaseDto open = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = Inquiry(host)));
            Assert.IsNull(open.ProjectMdlNotice, "not in the tender preparation yet");
            EnProjectMdlCaseDto v = Ok(await MdlView(host, Eng1, open.Id));
            Assert.IsFalse(v.Applies);
            Assert.IsNotNull(v.NotApplicableText);
            EnCaseDto spare = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = Inquiry(host, type: Entities.Model.NasbNiro.Sales.SlProductType.SpareParts)));
            Assert.IsFalse(Ok(await MdlView(host, Eng1, spare.Id)).Applies, "a spare-parts case that is not in the tender preparation yet");
            Assert.IsNull(spare.ProjectMdlNotice);
            // spare parts and service in the tender preparation: it applies, whatever the kind
            EnCaseDto sp = await OtherKindCase(host, Entities.Model.NasbNiro.Sales.SlProductType.SpareParts, null);
            EnCaseDto sv = await OtherKindCase(host, Entities.Model.NasbNiro.Sales.SlProductType.Service, null);
            Assert.IsTrue(Ok(await MdlView(host, Eng1, sp.Id)).Applies);
            Assert.IsTrue(Ok(await MdlView(host, Eng1, sv.Id)).Applies);
            // a direct contract never has a tender package: nothing is needed, whatever the status
            Assert.IsFalse(EnProjectMdlRules.Applies(new EnCase { Status = EnCaseStatus.Awarded, Kind = EnCaseKind.SpareParts }));
            Assert.IsTrue(EnProjectMdlRules.Applies(new EnCase { Status = EnCaseStatus.Awarded, Kind = EnCaseKind.SpareParts, InquiryId = 5 }));
            Assert.IsNotNull(EnProjectMdlRules.AppliesText(new EnCase { Status = EnCaseStatus.InEngineering, Kind = EnCaseKind.Service }));
            Assert.IsNull(host.AsSystem(async p => (await EnProjectMdlRules.ReleaseLockAsync(p.GetRequiredService<DataLayer.Context.IUnitOfWork>(), new EnCase { Id = 9999, Status = EnCaseStatus.Awarded, Kind = EnCaseKind.Service }, Ct)).Refusal).GetAwaiter().GetResult());
            // a released case before this change: not affected (the lock is only for the tender preparation)
            EnCaseDto c = await Releasable(host);
            host.Write(u => { EnCase row = u.Set<EnCase>().Single(x => x.Id == c.Id); row.Status = EnCaseStatus.TenderIssued; u.SaveChanges(); }, Seeder);
            Assert.IsNull(Ok(await GetCase(host, Mgr1, c.Id)).ProjectMdlNotice);
            Assert.IsTrue(Ok(await MdlView(host, Eng1, c.Id)).Applies);
            Assert.IsNull(Ok(await MdlView(host, Eng1, c.Id)).ReleaseLockText);
        }

        [TestMethod]
        public async Task A_Spare_Parts_And_A_Service_Release_Is_Locked_Without_An_Mdl_Or_A_Permit_And_Passes_With_Either()
        {
            AppHost host = Started("pm_kinds");
            // spare parts without any standard: the lock, the notice that says what to do, the flags
            EnCaseDto sp = await OtherKindCase(host, Entities.Model.NasbNiro.Sales.SlProductType.SpareParts, null);
            StringAssert.Contains(Refused(await CaseAct(host, Mgr1, sp.Id, "release-tender")), "فهرست مدارک پروژه (MDL)");
            EnCaseDto view = Ok(await GetCase(host, Mgr1, sp.Id));
            Assert.AreEqual(NoticeFromFile, view.ProjectMdlNotice);
            Assert.IsFalse(view.CanReleaseTender);
            Assert.AreEqual((int)EnCaseStatus.TenderPreparation, view.Status);
            EnProjectMdlCaseDto v = Ok(await MdlView(host, Eng1, sp.Id));
            Assert.IsFalse(v.CanDraft);
            StringAssert.Contains(v.DraftBlockedReason, "از فایل");
            Assert.IsTrue(v.CanDraftFromFile);
            Assert.IsFalse(v.HasStandardList);
            Assert.IsTrue(Ok(await MdlView(host, Mgr1, sp.Id)).CanRequestPermit, "the CEO's permit is the other way out, for any kind");
            // the agent does not draft (no list to start from)
            Refused(await MdlAct(host, await SeatAsync(host), "draft", d => d.CaseId = sp.Id), "فایل");
            // the file path: create, check, approve, release; the file joins the package once
            v = Ok(await MdlFromFile(host, Eng1, sp.Id, FileCsv));
            Assert.AreEqual(NoticeText, Ok(await GetCase(host, Mgr1, sp.Id)).ProjectMdlNotice, "once a list exists the notice is the usual one");
            Ok(await MdlAct(host, Eng2, "check", d => { d.Id = v.Mdl.Id; d.RowVersion = v.Mdl.RowVersion; }));
            StringAssert.Contains(Refused(await CaseAct(host, Mgr1, sp.Id, "release-tender")), "فهرست مدارک پروژه (MDL)");   // checked is not approved
            v = Ok(await MdlView(host, Mgr1, sp.Id));
            v = Ok(await MdlAct(host, Mgr1, "approve", d => { d.Id = v.Mdl.Id; d.RowVersion = v.Mdl.RowVersion; }));
            Assert.AreEqual(3, v.Mdl.LineCount);
            Assert.IsNull(Ok(await GetCase(host, Mgr1, sp.Id)).ProjectMdlNotice);
            Assert.AreEqual((int)EnCaseStatus.TenderIssued, Ok(await CaseAct(host, Mgr1, sp.Id, "release-tender")).Status);
            Assert.AreEqual(1, host.Read(u => u.Set<PcTenderDocument>().Count(d => d.PackageId == sp.TenderPackageId.Value && !d.Deleted && d.DocumentType == "MDL")));
            // service with the CEO's permit: requested and rejected permits do not open the release, an approved one does and the package says so
            EnCaseDto sv = await OtherKindCase(host, Entities.Model.NasbNiro.Sales.SlProductType.Service, null);
            Refused(await CaseAct(host, Mgr2, sv.Id, "release-tender"), "فهرست مدارک پروژه (MDL)");
            Ok(await MdlAct(host, Mgr1, "request-permit", d => { d.CaseId = sv.Id; d.Reason = PermitReason; }));
            Refused(await CaseAct(host, Mgr2, sv.Id, "release-tender"), "فهرست مدارک پروژه (MDL)");
            int permit = Ok(await MdlView(host, Mgr1, sv.Id)).Permit.Id;
            Ok(await MdlAct(host, Ceo, "decide-permit", d => { d.CaseId = sv.Id; d.PermitId = permit; d.Approve = false; d.Note = "رد برای آزمون؛ فهرست باید ساخته شود"; }));
            Refused(await CaseAct(host, Mgr2, sv.Id, "release-tender"), "فهرست مدارک پروژه (MDL)");
            Ok(await MdlAct(host, Mgr1, "request-permit", d => { d.CaseId = sv.Id; d.Reason = PermitReason; }));
            permit = Ok(await MdlView(host, Mgr1, sv.Id)).Permit.Id;
            Ok(await MdlAct(host, Ceo, "decide-permit", d => { d.CaseId = sv.Id; d.PermitId = permit; d.Approve = true; }));
            Assert.IsNull(Ok(await GetCase(host, Mgr2, sv.Id)).ProjectMdlNotice);
            Assert.AreEqual((int)EnCaseStatus.TenderIssued, Ok(await CaseAct(host, Mgr2, sv.Id, "release-tender")).Status);
            StringAssert.Contains(host.Read(u => u.Set<EnCaseEvent>().Single(e => e.CaseId == sv.Id && e.Kind == "tender-released").Note), "بدون فهرست مدارک پروژه");
            Assert.AreEqual(0, host.Read(u => u.Set<PcTenderDocument>().Count(d => d.PackageId == sv.TenderPackageId.Value && !d.Deleted && d.DocumentType == "MDL")));
            // a spare-parts case WITH an approved standard list: the agent drafts from it (the same as a turbine)
            await EnsureMdl(host, "SP-STD");
            EnCaseDto sp2 = await OtherKindCase(host, Entities.Model.NasbNiro.Sales.SlProductType.SpareParts, "SP-STD");
            v = Ok(await MdlView(host, Eng1, sp2.Id));
            Assert.IsTrue(v.CanDraft);
            Assert.IsTrue(v.HasStandardList);
            Assert.AreEqual(NoticeText, Ok(await GetCase(host, Mgr1, sp2.Id)).ProjectMdlNotice);
            Assert.AreEqual(4, (await Draft(host, sp2.Id)).Mdl.StandardKeptCount);
            await ApprovedMdlFinish(host, sp2.Id);
            Assert.AreEqual((int)EnCaseStatus.TenderIssued, Ok(await CaseAct(host, Mgr1, sp2.Id, "release-tender")).Status);
        }

        private static async Task ApprovedMdlFinish(AppHost host, int caseId)
        {
            EnProjectMdlCaseDto v = Ok(await MdlView(host, Eng2, caseId));
            Ok(await MdlAct(host, Eng2, "check", d => { d.Id = v.Mdl.Id; d.RowVersion = v.Mdl.RowVersion; }));
            v = Ok(await MdlView(host, Mgr1, caseId));
            Ok(await MdlAct(host, Mgr1, "approve", d => { d.Id = v.Mdl.Id; d.RowVersion = v.Mdl.RowVersion; }));
        }

        // ------------------------------------------------------------------------------------------ draft from a file

        [TestMethod]
        public async Task Draft_From_File_Is_For_A_Person_When_There_Is_No_Approved_Standard_List_And_Every_Line_Is_A_Project_Line()
        {
            AppHost host = Started("pm_file");
            EnCaseDto sp = await OtherKindCase(host, Entities.Model.NasbNiro.Sales.SlProductType.SpareParts, "NO-LIST-STD");   // a standard code that has no approved list
            // roles: a person who writes engineering; not sales, not a stranger, not the agent seat
            Refused(await MdlFromFile(host, Sales1, sp.Id, FileCsv));
            Refused(await MdlFromFile(host, Nobody, sp.Id, FileCsv));
            StringAssert.Contains(Refused(await MdlFromFile(host, await SeatAsync(host), sp.Id, FileCsv)), "هوش مصنوعی");
            Assert.IsFalse(Ok(await MdlView(host, Sales1, sp.Id)).CanDraftFromFile);
            Assert.IsTrue(Ok(await MdlView(host, Eng1, sp.Id)).CanDraftFromFile);
            // the file and the note are checked
            Refused(await MdlFromFile(host, Eng1, sp.Id, FileCsv, "کوتاه"), "یادداشت");
            Refused(await MdlFromFile(host, Eng1, sp.Id, "a,b\r\n1,2\r\n"), "سرستون");
            Refused(await MdlFromFile(host, Eng1, sp.Id, Head.Replace("\n", "\r\n")), "ردیف");
            Refused(await MdlFromFile(host, Eng1, 987654, FileCsv));
            Assert.IsNull(Ok(await MdlView(host, Eng1, sp.Id)).Mdl, "a refusal leaves nothing");
            Assert.AreEqual(0, host.Read(u => u.Set<EnProjectMdl>().Count(m => m.CaseId == sp.Id)));
            // created: every line a project line, no standard list, the file stored
            EnProjectMdlCaseDto v = Ok(await MdlFromFile(host, Eng1, sp.Id, FileCsv));
            Assert.AreEqual((int)EnProjectMdlStatus.Draft, v.Mdl.Status);
            Assert.AreEqual(1, v.Mdl.Revision);
            StringAssert.StartsWith(v.Mdl.Number, "PMDL-");
            Assert.IsNull(v.Mdl.BasedOnListId);
            Assert.IsNull(v.Mdl.BasedOnCode);
            Assert.AreEqual(3, v.Mdl.Lines.Count);
            Assert.IsTrue(v.Mdl.Lines.All(l => l.Origin == 1 && !l.Removed && l.OriginText == "افزوده برای پروژه"));
            Assert.AreEqual(0, v.Mdl.StandardKeptCount);
            Assert.AreEqual(0, v.Mdl.RemovedCount);
            Assert.AreEqual(3, v.Mdl.AddedCount);
            Assert.AreEqual("project-mdl.csv", v.Mdl.SourceFileName);
            Assert.IsNotNull(v.Mdl.SourceDocumentId);
            CollectionAssert.AreEqual(new[] { "DWG", "CERT", "DATA" }, v.Mdl.Lines.Select(l => l.DocumentType).ToArray());
            Assert.AreEqual("Qa,Qc", v.Mdl.Lines[1].Units);
            Assert.IsTrue(v.Mdl.Lines[0].ClientSubmittal && v.Mdl.Lines[0].TenderStage && v.Mdl.Lines[0].Required);
            Assert.AreEqual("فهرست مدارک پروژه مطابق درخواست کارفرما از فایل ساخته شد", v.Mdl.ChangeNote);
            Assert.AreEqual(1, CaseEvents(host, sp.Id, "project-mdl-drafted"));
            Assert.IsTrue(v.CanCheck);
            Assert.IsFalse(v.CanDraftFromFile);
            // one live list per case
            StringAssert.Contains(Refused(await MdlFromFile(host, Eng1, sp.Id, FileCsv)), v.Mdl.Number);
            // upload-lines on such a list replaces every line (nothing of the standard to keep); a project line that the file no longer has is hidden
            string two = Head.Replace("\n", "\r\n") + "DWG,نقشهٔ قطعه یدکی,مکانیک,1,1,,1,,\r\nTEST,گزارش آزمون قطعه,کیفیت,1,0,,0,,\r\n";
            v = Ok(await MdlUpload(host, Eng1, v.Mdl, two));
            Assert.AreEqual(2, v.Mdl.Lines.Count);
            Assert.AreEqual(2, v.Mdl.LineCount);
            Assert.AreEqual(2, v.Mdl.AddedCount);
            Assert.AreEqual(0, v.Mdl.StandardKeptCount);
            Assert.AreEqual(0, v.Mdl.RemovedCount);
            Assert.IsTrue(v.Mdl.Lines.All(l => l.Origin == 1));
            CollectionAssert.AreEqual(new[] { "DWG", "TEST" }, v.Mdl.Lines.Select(l => l.DocumentType).ToArray());
            // check, approve: the approved list is bound to these lines; the CSV is the issued file
            v = Ok(await MdlAct(host, Eng2, "check", d => { d.Id = v.Mdl.Id; d.RowVersion = v.Mdl.RowVersion; }));
            v = Ok(await MdlAct(host, Mgr1, "approve", d => { d.Id = v.Mdl.Id; d.RowVersion = v.Mdl.RowVersion; }));
            Assert.IsFalse(v.Mdl.ApprovalStale);
            Assert.AreEqual("MDL-" + sp.Number + "-R1.csv", v.Mdl.IssuedFileName);
            string csv = System.Text.Encoding.UTF8.GetString(await StoredAsync(host, v.Mdl.IssuedDocumentId.Value));
            StringAssert.Contains(csv, "TEST,");
            Assert.IsFalse(csv.Contains("CERT,"));
            // a void lets a new draft from a file follow (revision 2)
            Ok(await MdlAct(host, Mgr1, "void", d => { d.Id = v.Mdl.Id; d.Reason = "فهرست باید با فایل دیگری ساخته شود"; d.RowVersion = Ver(host, v.CaseId); }));
            v = Ok(await MdlFromFile(host, Eng1, sp.Id, FileCsv));
            Assert.AreEqual(2, v.Mdl.Revision);
            Assert.AreEqual(1, v.History.Count(h => h.Status == (int)EnProjectMdlStatus.Void));
        }

        [TestMethod]
        public async Task Draft_From_File_Is_Refused_When_An_Approved_Standard_List_Exists_Or_A_Permit_Was_Approved_Or_The_Case_Is_Not_In_The_Tender_Preparation()
        {
            AppHost host = Started("pm_file2");
            // a turbine case whose standard has an approved list: draft from the standard, then change the lines by a file
            EnCaseDto c = await NewCase(host);
            EnProjectMdlCaseDto v = Ok(await MdlView(host, Eng1, c.Id));
            Assert.IsFalse(v.CanDraftFromFile);
            Assert.IsTrue(v.HasStandardList);
            string why = Refused(await MdlFromFile(host, Eng1, c.Id, FileCsv));
            StringAssert.Contains(why, "فهرست استاندارد");
            StringAssert.Contains(why, "با فایل تغییر دهید");
            Assert.AreEqual(why, v.DraftFromFileBlockedReason);
            Assert.AreEqual(0, host.Read(u => u.Set<EnProjectMdl>().Count(m => m.CaseId == c.Id)));
            // a case with no standard code at all is the same as one without a list (a turbine too)
            host.Write(u => { u.Set<EnCase>().Single(k => k.Id == c.Id).ReferenceCode = null; u.SaveChanges(); }, Seeder);
            Assert.IsTrue(Ok(await MdlView(host, Eng1, c.Id)).CanDraftFromFile);
            Assert.AreEqual(NoticeFromFile, Ok(await GetCase(host, Mgr1, c.Id)).ProjectMdlNotice);
            // an approved permit: no list is made
            Ok(await MdlAct(host, Mgr1, "request-permit", d => { d.CaseId = c.Id; d.Reason = PermitReason; }));
            int permit = Ok(await MdlView(host, Mgr1, c.Id)).Permit.Id;
            Assert.IsTrue(Ok(await MdlView(host, Eng1, c.Id)).CanDraftFromFile, "a requested permit does not stop it");
            Ok(await MdlAct(host, Ceo, "decide-permit", d => { d.CaseId = c.Id; d.PermitId = permit; d.Approve = true; }));
            Refused(await MdlFromFile(host, Eng1, c.Id, FileCsv), "مجوز");
            Assert.IsFalse(Ok(await MdlView(host, Eng1, c.Id)).CanDraftFromFile);
            // not in the tender preparation
            EnCaseDto open = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = Inquiry(host, type: Entities.Model.NasbNiro.Sales.SlProductType.SpareParts)));
            Refused(await MdlFromFile(host, Eng1, open.Id, FileCsv), "مرحلهٔ تهیهٔ بستهٔ فنی");
            Assert.IsFalse(Ok(await MdlView(host, Eng1, open.Id)).CanDraftFromFile);
        }

        // ------------------------------------------------------------------------------------------ fix round: an approval never outlives a new decision

        private const string StaleText = "فهرست مدارک پروژه (MDL) یا مجوز پیش از آخرین تصمیم پرونده تایید شده است؛ آن را باطل و دوباره تهیه کنید.";

        /// <summary>The manager sends the case back and the new decision round ends again in the tender preparation.</summary>
        private static async Task ReDecide(AppHost host, int caseId)
        {
            Ok(await CaseAct(host, Mgr2, caseId, "reclassify", d => d.Note = "دلیل تغییر نوع پرونده برای آزمون"));
            Ok(await CaseAct(host, Mgr1, caseId, "confirm"));
            await ConfirmGapIfAny(host, caseId);
            Ok(await CaseAct(host, Ceo, caseId, "approve"));
            Assert.AreEqual((int)EnCaseStatus.TenderPreparation, Ok(await GetCase(host, Mgr1, caseId)).Status);
        }

        [TestMethod]
        public async Task An_Approved_Project_Mdl_Does_Not_Survive_A_New_Decision_Of_The_Case_And_Can_Be_Voided_And_Redrafted()
        {
            AppHost host = Started("pm_stale_mdl");
            EnCaseDto c = await Releasable(host);
            EnProjectMdlCaseDto ap = await ApprovedMdl(host, c.Id);
            Assert.IsFalse(ap.Mdl.ApprovalStale);
            Assert.IsTrue(Ok(await GetCase(host, Mgr1, c.Id)).CanReleaseTender);
            await ReDecide(host, c.Id);
            // the old approval is still Approved with a matching hash, but it was given before the latest decision
            EnProjectMdlCaseDto v = Ok(await MdlView(host, Mgr1, c.Id));
            Assert.AreEqual((int)EnProjectMdlStatus.Approved, v.Mdl.Status);
            Assert.IsTrue(v.Mdl.ApprovalStale);
            Assert.AreEqual(StaleText, v.ReleaseLockText);
            EnCaseDto asMgr = Ok(await GetCase(host, Mgr1, c.Id));
            Assert.IsFalse(asMgr.CanReleaseTender);
            Assert.AreEqual(StaleText, asMgr.ReleaseTenderBlockedReason);
            Assert.AreEqual(StaleText, asMgr.ProjectMdlNotice);
            Assert.AreEqual(StaleText, Refused(await CaseAct(host, Mgr1, c.Id, "release-tender")));
            // nothing was voided automatically; the manager voids and the agent drafts again, which is checked, approved and releases
            Assert.IsTrue(v.CanVoid);
            Ok(await MdlAct(host, Mgr1, "void", d => { d.Id = v.Mdl.Id; d.Reason = "تصمیم پرونده تغییر کرد؛ فهرست دوباره تهیه شود"; d.RowVersion = v.Mdl.RowVersion; }));
            EnProjectMdlCaseDto second = await ApprovedMdl(host, c.Id);
            Assert.AreEqual(2, second.Mdl.Revision);
            Assert.IsFalse(second.Mdl.ApprovalStale);
            Assert.IsTrue(Ok(await GetCase(host, Mgr1, c.Id)).CanReleaseTender);
            Assert.AreEqual((int)EnCaseStatus.TenderIssued, Ok(await CaseAct(host, Mgr1, c.Id, "release-tender")).Status);
        }

        [TestMethod]
        public async Task An_Approved_Project_Mdl_Made_From_Another_Standard_Than_The_Current_One_Is_Stale()
        {
            AppHost host = Started("pm_stale_code");
            EnCaseDto c = await Releasable(host);
            EnProjectMdlCaseDto ap = await ApprovedMdl(host, c.Id);
            Assert.IsTrue(Ok(await GetCase(host, Mgr1, c.Id)).CanReleaseTender);
            host.Write(u => { u.Set<EnCase>().Single(k => k.Id == c.Id).ReferenceCode = "another-std"; u.SaveChanges(); }, Seeder);
            EnProjectMdlCaseDto other = Ok(await MdlView(host, Mgr1, c.Id));
            Assert.IsTrue(other.Mdl.ApprovalStale);
            Assert.AreEqual(StaleText, other.ReleaseLockText);
            // the code is compared normalised (case and spaces do not matter)
            host.Write(u => { u.Set<EnCase>().Single(k => k.Id == c.Id).ReferenceCode = " " + Std.ToLowerInvariant() + " "; u.SaveChanges(); }, Seeder);
            Assert.IsFalse(Ok(await MdlView(host, Mgr1, c.Id)).Mdl.ApprovalStale);
            Assert.IsTrue(Ok(await GetCase(host, Mgr1, c.Id)).CanReleaseTender);
            // a list made from a file has no standard: only the decision time decides
            host.Write(u => { u.Set<EnProjectMdl>().Single(m => m.Id == ap.Mdl.Id).BasedOnCode = null; u.SaveChanges(); }, Seeder);
            Assert.IsFalse(Ok(await MdlView(host, Mgr1, c.Id)).Mdl.ApprovalStale);
        }

        [TestMethod]
        public async Task An_Approved_Permit_Does_Not_Survive_A_New_Decision_The_Manager_Withdraws_It_With_A_Reason_And_Asks_Again()
        {
            AppHost host = Started("pm_stale_permit");
            EnCaseDto c = await Releasable(host);
            Ok(await MdlAct(host, Mgr1, "request-permit", d => { d.CaseId = c.Id; d.Reason = PermitReason; }));
            int permit = Ok(await MdlView(host, Mgr1, c.Id)).Permit.Id;
            Ok(await MdlAct(host, Ceo, "decide-permit", d => { d.CaseId = c.Id; d.PermitId = permit; d.Approve = true; }));
            Assert.IsTrue(Ok(await GetCase(host, Mgr1, c.Id)).CanReleaseTender);
            Assert.IsFalse(Ok(await MdlView(host, Mgr1, c.Id)).Permit.ApprovalStale);
            await ReDecide(host, c.Id);
            EnProjectMdlCaseDto v = Ok(await MdlView(host, Mgr1, c.Id));
            Assert.IsTrue(v.Permit.ApprovalStale);
            Assert.AreEqual(StaleText, v.ReleaseLockText);
            Assert.AreEqual(StaleText, Refused(await CaseAct(host, Mgr1, c.Id, "release-tender")));
            Assert.IsFalse(Ok(await GetCase(host, Mgr1, c.Id)).CanReleaseTender);
            // asking again needs the old one out first; the withdrawal of an approved permit needs a reason, a manager, never a seat
            StringAssert.Contains(Refused(await MdlAct(host, Mgr1, "request-permit", d => { d.CaseId = c.Id; d.Reason = PermitReason; })), "پس بگیرید");
            Assert.IsTrue(v.CanWithdrawPermit);
            Assert.IsFalse(Ok(await MdlView(host, Eng1, c.Id)).CanWithdrawPermit);
            Refused(await MdlAct(host, Eng1, "withdraw-permit", d => { d.CaseId = c.Id; d.PermitId = permit; d.Note = "دلیل پس‌گرفتن به اندازهٔ کافی طولانی"; }));
            Refused(await MdlAct(host, Mgr1, "withdraw-permit", d => { d.CaseId = c.Id; d.PermitId = permit; d.Note = "کوتاه"; }), "دلیل");
            Ok(await MdlAct(host, Mgr1, "withdraw-permit", d => { d.CaseId = c.Id; d.PermitId = permit; d.Note = "تصمیم پرونده تغییر کرد؛ مجوز تازه لازم است"; }));
            Assert.AreEqual(EnMdlPermitStatus.Void, host.Read(u => u.Set<EnMdlPermit>().Single(p => p.Id == permit).Status));
            Ok(await MdlAct(host, Mgr1, "request-permit", d => { d.CaseId = c.Id; d.Reason = PermitReason; }));
            int again = Ok(await MdlView(host, Mgr1, c.Id)).Permit.Id;
            Ok(await MdlAct(host, Ceo, "decide-permit", d => { d.CaseId = c.Id; d.PermitId = again; d.Approve = true; }));
            Assert.AreEqual((int)EnCaseStatus.TenderIssued, Ok(await CaseAct(host, Mgr1, c.Id, "release-tender")).Status);
            // an approved permit that is NOT stale cannot be withdrawn (no longer a way to undo the CEO's decision)
        }

        [TestMethod]
        public async Task The_Person_Who_Made_The_Lines_Does_Not_Approve_Them()
        {
            AppHost host = Started("pm_maker");
            EnCaseDto c = await NewCase(host);
            // the manager uploads the lines over the agent's draft, an engineer checks, the same manager may not approve: the other manager may
            EnProjectMdlCaseDto v = await Draft(host, c.Id);
            v = Ok(await MdlUpload(host, Mgr1, v.Mdl, Head + "BOM,مدرک BOM,مکانیک,1,1,,0,,\nDWG,مدرک DWG,مکانیک,1,1,,0,,\n"));
            v = Ok(await MdlAct(host, Eng2, "check", d => { d.Id = v.Mdl.Id; d.RowVersion = v.Mdl.RowVersion; }));
            Assert.IsFalse(Ok(await MdlView(host, Mgr1, c.Id)).CanApprove);
            string why = Refused(await MdlAct(host, Mgr1, "approve", d => { d.Id = v.Mdl.Id; d.RowVersion = v.Mdl.RowVersion; }), "بارگذاری");
            Assert.AreEqual(why, Ok(await MdlView(host, Mgr1, c.Id)).ApproveBlockedReason);
            Assert.IsTrue(Ok(await MdlView(host, Mgr2, c.Id)).CanApprove);
            Assert.AreEqual((int)EnProjectMdlStatus.Approved, Ok(await MdlAct(host, Mgr2, "approve", d => { d.Id = v.Mdl.Id; d.RowVersion = v.Mdl.RowVersion; })).Mdl.Status);
            // a list created from a file by the manager is the same
            AppHost host2 = Started("pm_maker_file");
            EnCaseDto c2 = await OtherKindCase(host2, Entities.Model.NasbNiro.Sales.SlProductType.SpareParts, null);
            Ok(await MdlFromFile(host2, Mgr1, c2.Id, FileCsv));
            EnProjectMdlCaseDto f = Ok(await MdlView(host2, Eng2, c2.Id));
            f = Ok(await MdlAct(host2, Eng2, "check", d => { d.Id = f.Mdl.Id; d.RowVersion = f.Mdl.RowVersion; }));
            Assert.IsFalse(Ok(await MdlView(host2, Mgr1, c2.Id)).CanApprove);
            Refused(await MdlAct(host2, Mgr1, "approve", d => { d.Id = f.Mdl.Id; d.RowVersion = f.Mdl.RowVersion; }), "ساخته");
            Assert.IsTrue(Ok(await MdlView(host2, Mgr2, c2.Id)).CanApprove);
            Ok(await MdlAct(host2, Mgr2, "approve", d => { d.Id = f.Mdl.Id; d.RowVersion = f.Mdl.RowVersion; }));
        }

        [TestMethod]
        public async Task A_List_Whose_Lines_Changed_After_The_Check_Is_Not_Approved()
        {
            AppHost host = Started("pm_race");
            EnCaseDto c = await NewCase(host);
            await Draft(host, c.Id);
            EnProjectMdlCaseDto v = await Checked(host, c.Id, Eng2);
            Assert.IsTrue(Ok(await MdlView(host, Mgr1, c.Id)).CanApprove);
            // an upload that raced the check: a line carries a later stamp although the status says Checked
            host.Write(u => { u.Set<EnProjectMdlLine>().Where(l => l.ProjectMdlId == v.Mdl.Id).OrderBy(l => l.Id).First().ModifyDate = DateTimeOffset.Now.AddMinutes(1); u.SaveChanges(); }, Seeder);
            EnProjectMdlCaseDto now = Ok(await MdlView(host, Mgr1, c.Id));
            Assert.IsFalse(now.CanApprove);
            StringAssert.Contains(now.ApproveBlockedReason, "دوباره بررسی");
            Assert.AreEqual(now.ApproveBlockedReason, Refused(await MdlAct(host, Mgr1, "approve", d => { d.Id = v.Mdl.Id; d.RowVersion = now.Mdl.RowVersion; })));
            Assert.AreEqual((int)EnProjectMdlStatus.Checked, Ok(await MdlView(host, Mgr1, c.Id)).Mdl.Status);
        }

        [TestMethod]
        public async Task A_Person_Must_Send_The_Concurrency_Token_And_The_Agent_Need_Not()
        {
            AppHost host = Started("pm_token");
            EnCaseDto c = await NewCase(host);
            EnProjectMdlCaseDto v = await Draft(host, c.Id);   // the agent: no token
            foreach (string empty in new[] { null, "", "  " })
                Refused(await MdlAct(host, Eng1, "check", d => { d.Id = v.Mdl.Id; d.RowVersion = empty; }), "نسخه");
            Refused(await MdlUpload(host, Eng1, v.Mdl, Head + "BOM,مدرک BOM,مکانیک,1,1,,0,,\n", version: ""), "نسخه");
            Assert.AreEqual((int)EnProjectMdlStatus.Draft, Ok(await MdlView(host, Eng1, c.Id)).Mdl.Status);
            v = Ok(await MdlAct(host, Eng1, "check", d => { d.Id = v.Mdl.Id; d.RowVersion = v.Mdl.RowVersion; }));
            Refused(await MdlAct(host, Mgr1, "return", d => { d.Id = v.Mdl.Id; d.Reason = "ردیف نقشه باید دوباره دیده شود"; }), "نسخه");
            Refused(await MdlAct(host, Mgr2, "approve", d => d.Id = v.Mdl.Id), "نسخه");
            Refused(await MdlAct(host, Mgr1, "void", d => { d.Id = v.Mdl.Id; d.Reason = "ابطال بدون شناسهٔ نسخه ممکن نیست"; }), "نسخه");
            Assert.AreEqual((int)EnProjectMdlStatus.Checked, Ok(await MdlView(host, Mgr1, c.Id)).Mdl.Status);
            Assert.AreEqual((int)EnProjectMdlStatus.Draft, Ok(await MdlAct(host, Mgr1, "return", d => { d.Id = v.Mdl.Id; d.Reason = "ردیف نقشه باید دوباره دیده شود"; d.RowVersion = v.Mdl.RowVersion; })).Mdl.Status);
        }
    }
}
