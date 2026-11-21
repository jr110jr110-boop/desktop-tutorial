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
            Assert.AreEqual((int)EnProjectMdlStatus.Draft, Ok(await MdlAct(host, Mgr1, "return", d => { d.Id = v.Mdl.Id; d.Reason = "ردیف نقشه باید دوباره دیده شود"; })).Mdl.Status);
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
            EnProjectMdlCaseDto voided = Ok(await MdlAct(host, Mgr1, "void", d => { d.Id = v.Mdl.Id; d.Reason = "فهرست استاندارد بازنگری شد و پیش‌نویس کهنه است"; }));
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
            EnProjectMdlCaseDto approved = Ok(await MdlAct(host, Mgr1, "approve", d => d.Id = second.Mdl.Id));
            Assert.IsTrue(approved.CanVoid);
            Assert.IsNull(Ok(await MdlAct(host, Mgr2, "void", d => { d.Id = second.Mdl.Id; d.Reason = "فهرست تاییدشده پیش از ابلاغ ابطال شد"; })).Mdl);
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
            Ok(await MdlAct(host, Mgr1, "approve", d => d.CaseId = c.Id));
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
            Ok(await MdlAct(host, Mgr2, "approve", d => d.CaseId = c.Id));   // approved, tender not released
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

        // ------------------------------------------------------------------------------------------ scope

        [TestMethod]
        public async Task A_Case_That_Is_Not_A_Turbine_Needs_No_Project_Mdl_And_The_Notice_Is_Only_For_The_Tender_Preparation()
        {
            AppHost host = Started("pm_scope");
            EnCaseDto open = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = Inquiry(host)));
            Assert.IsNull(open.ProjectMdlNotice, "not in the tender preparation yet");
            EnProjectMdlCaseDto v = Ok(await MdlView(host, Eng1, open.Id));
            Assert.IsFalse(v.Applies);
            Assert.IsNotNull(v.NotApplicableText);
            EnCaseDto spare = Ok(await CaseAct(host, Eng1, null, "open", d => d.InquiryId = Inquiry(host, type: Entities.Model.NasbNiro.Sales.SlProductType.SpareParts)));
            Assert.IsFalse(Ok(await MdlView(host, Eng1, spare.Id)).Applies);
            StringAssert.Contains(Refused(await MdlAct(host, Eng1, "draft", d => d.CaseId = spare.Id)), "توربین");
            Assert.IsNull(spare.ProjectMdlNotice);
            // a released case before this change: not affected (the lock is only for the tender preparation)
            EnCaseDto c = await Releasable(host);
            host.Write(u => { EnCase row = u.Set<EnCase>().Single(x => x.Id == c.Id); row.Status = EnCaseStatus.TenderIssued; u.SaveChanges(); }, Seeder);
            Assert.IsNull(Ok(await GetCase(host, Mgr1, c.Id)).ProjectMdlNotice);
            Assert.IsTrue(Ok(await MdlView(host, Eng1, c.Id)).Applies);
            Assert.IsNull(Ok(await MdlView(host, Eng1, c.Id)).ReleaseLockText);
        }
    }
}
