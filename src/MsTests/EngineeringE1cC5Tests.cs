using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Entities.Model.NasbNiro.Engineering;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using static MsTests.EngineeringHandoverTests;
using static MsTests.EnKit;

namespace MsTests
{
    /// <summary>ENG-E1c: C5 csv upload.</summary>
    [TestClass]
    public class EngineeringE1cC5Tests
    {
        private static AppHost Started(string name) { AppHost h = NewHost(name); StartControl(h); return h; }

        // ------------------------------------------------------------------ C5b

        private const string Head = "docType,title,discipline,required,tender,units\n";
        private static byte[] Csv(string text, bool bom = true) => (bom ? new byte[] { 0xEF, 0xBB, 0xBF } : new byte[0]).Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        private static async Task<EnReferenceListDto> NewDraft(AppHost host, string code = "VDDR-CSV") =>
            Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().SaveListAsync(Ref(code, ("BOM", true, false)), Ct)));
        private static Task<Common.Results.ResponseDto<EnReferenceListDto>> Upload(AppHost host, System.Security.Claims.ClaimsPrincipal who, EnReferenceListDto d, byte[] file) =>
            host.As(who, p => p.GetRequiredService<IEnReferenceService>().UploadLinesAsync(d.Id, d.RowVersion, file, Ct));

        [TestMethod]
        public async Task C5b_Good_File_Replaces_The_Lines_Of_The_Draft()
        {
            AppHost host = Started("e1ccsv1");
            EnReferenceListDto d = await NewDraft(host);
            string csv = Head + "DWG,نقشه مونتاژ,مکانیک,بله,1,\n\"DATASHEET\",\"برگه اطلاعات، نسخه نهایی\",,1,0,QA;کنترل کیفیت\nNOTE,یادداشت,,خیر,,تولید; Planning\n";
            EnReferenceListDto r = Ok(await Upload(host, Eng1, d, Csv(csv)));
            Assert.AreEqual(3, r.Lines.Count);
            Assert.IsFalse(r.Lines.Any(l => l.DocumentType == "BOM"), "the old line is gone (the draft was never submitted)");
            EnReferenceLineDto a = r.Lines.Single(l => l.DocumentType == "DWG");
            Assert.IsTrue(a.Required); Assert.IsTrue(a.TenderStage); Assert.IsTrue(string.IsNullOrEmpty(a.Units));
            EnReferenceLineDto b = r.Lines.Single(l => l.DocumentType == "DATASHEET");
            Assert.AreEqual("برگه اطلاعات، نسخه نهایی", b.Title);
            Assert.AreEqual("Qa,Qc", b.Units);
            EnReferenceLineDto c = r.Lines.Single(l => l.DocumentType == "NOTE");
            Assert.IsFalse(c.Required); Assert.IsFalse(c.TenderStage);
            Assert.AreEqual("Production,Planning", c.Units);
            // the file without a byte order mark is read the same
            EnReferenceListDto again = Ok(await Upload(host, Eng1, r, Csv(Head + "DWG,نقشه,,1,0,\n", false)));
            Assert.AreEqual(1, again.Lines.Count);
        }

        [TestMethod]
        public async Task C5b_Bad_Header_Bad_Row_Unit_Seven_And_Not_A_Draft_Are_Refused_With_Row_Numbers()
        {
            AppHost host = Started("e1ccsv2");
            EnReferenceListDto d = await NewDraft(host);
            Refused(await Upload(host, Eng1, d, Csv("type,title,discipline,required,tender,units\nDWG,x,,1,0,\n")), "سرستون");
            Refused(await Upload(host, Eng1, d, Csv("")), "خالی");
            string bad = Refused(await Upload(host, Eng1, d, Csv(Head + "DWG,نقشه,,1,0,\n,بدون نوع,,1,0,\nDWG,نقشه,,1,0,\nBOM,فهرست,,شاید,0,\nBOM,فهرست دو,,1,0,QA;QA;X-unit\nBOM,ستون کم,1\n")));
            StringAssert.Contains(bad, "ردیف 3: نوع مدرک (docType) خالی است.");
            StringAssert.Contains(bad, "ردیف 4: این نوع و عنوان قبلاً در ردیف 2 آمده است.");
            StringAssert.Contains(bad, "ردیف 5: مقدار ستون required");
            StringAssert.Contains(bad, "ردیف 6: واحد «X-unit» شناخته‌شده نیست");
            StringAssert.Contains(bad, "ردیف 7: تعداد ستون‌ها باید ۶ باشد");
            Refused(await Upload(host, Eng1, d, Csv(Head + "DWG,نقشه,,1,0,مهندسی\n")), "ردیف 2");
            Refused(await Upload(host, Eng1, d, Csv(Head + "DWG,نقشه,,1,0,Engineering\n")), "مهندسی");
            Refused(await Upload(host, Eng1, d, Csv(Head + "DWG,نقشه,,1,0,7\n")), "ردیف 2");
            Refused(await Upload(host, Eng1, d, new byte[] { 0xFF, 0xFE, 0x41, 0x00 }), "UTF-8");
            // nothing was changed by the refused files
            Assert.AreEqual(1, Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().GetListAsync(d.Id, Ct))).Lines.Count);

            // other people and seats
            Refused(await Upload(host, AppHost.Principal(2998, "stranger", "QA"), d, Csv(Head + "DWG,نقشه,,1,0,\n")));
            var seat = await SeatAsync(host);
            Refused(await Upload(host, seat, d, Csv(Head + "DWG,نقشه,,1,0,\n")));

            // not a draft any more
            Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().ListActionAsync(new EnReferenceActionDto { Id = d.Id, Action = "submit", RowVersion = d.RowVersion }, Ct)));
            EnReferenceListDto submitted = Ok(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().GetListAsync(d.Id, Ct)));
            Refused(await Upload(host, Eng1, submitted, Csv(Head + "DWG,نقشه,,1,0,\n")), "پیش‌نویس");
        }

        [TestMethod]
        public async Task C5b_A_New_List_Is_Made_From_A_File_And_A_Seat_Cannot()
        {
            AppHost host = Started("e1ccsv3");
            var svc = host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().CreateFromFileAsync(new EnSaveReferenceDto { Code = "VDDR-NEWFILE", Title = "فهرست از فایل" }, Csv(Head + "DWG,نقشه,,1,1,\nBOM,فهرست قطعات,,1,0,تولید\n"), Ct));
            EnReferenceListDto made = Ok(await svc);
            Assert.AreEqual("VDDR-NEWFILE", made.Code);
            Assert.AreEqual(2, made.Lines.Count);
            Refused(await host.As(Eng1, p => p.GetRequiredService<IEnReferenceService>().CreateFromFileAsync(new EnSaveReferenceDto { Code = "VDDR-BAD", Title = "x" }, Csv("a,b\n1,2\n"), Ct)), "سرستون");
            Refused(await host.As(await SeatAsync(host), p => p.GetRequiredService<IEnReferenceService>().CreateFromFileAsync(new EnSaveReferenceDto { Code = "VDDR-SEAT", Title = "x" }, Csv(Head + "DWG,نقشه,,1,1,\n"), Ct)));
        }
    }
}
