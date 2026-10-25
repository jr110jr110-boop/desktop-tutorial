using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.OfficeDtos;

namespace MsTests
{
    /// <summary>Office answers of 1405/08: letter numbers, secretariat access, the Excel import of the attendance software, lateness and the monthly overtime cap.</summary>
    [TestClass]
    public class OfficeR2Tests
    {
        // ------------------------------------------------------------------------------------------ 1. letter numbers

        [TestMethod]
        public void Default_Patterns_Give_The_Company_Numbers()
        {
            Assert.AreEqual("INN-و-1405-25", LetterService.FormatNumber(LetterService.DefaultPattern(OfLetterKind.Incoming), 1405, 25, "OPS", OfLetterKind.Incoming));
            Assert.AreEqual("INN-1405-25", LetterService.FormatNumber(LetterService.DefaultPattern(OfLetterKind.Outgoing), 1405, 25, "OPS", OfLetterKind.Outgoing));
            Assert.AreEqual("INN-د-1405-25", LetterService.FormatNumber(LetterService.DefaultPattern(OfLetterKind.Internal), 1405, 25, "OPS", OfLetterKind.Internal));
        }

        [TestMethod]
        public void Code_Token_Adds_The_Code_And_Its_Dash_Only_When_There_Is_One()
        {
            Assert.AreEqual("INN-و-T-1405-0017-1405-3", LetterService.FormatNumber("INN-و-{code}{yyyy}-{seq}", 1405, 3, "CO", OfLetterKind.Incoming, "T-1405-0017"));
            Assert.AreEqual("INN-40501MA01-1405-3", LetterService.FormatNumber("INN-{code}{yyyy}-{seq}", 1405, 3, "CO", OfLetterKind.Outgoing, "40501MA01"));
            Assert.AreEqual("INN-د-40501MA01-1405-12", LetterService.FormatNumber("INN-د-{code}{yyyy}-{seq}", 1405, 12, "CO", OfLetterKind.Internal, "40501MA01"));
            Assert.AreEqual("INN-1405-3", LetterService.FormatNumber("INN-{code}{yyyy}-{seq}", 1405, 3, "CO", OfLetterKind.Outgoing, null));
            Assert.AreEqual("INN-1405-3", LetterService.FormatNumber("INN-{code}{yyyy}-{seq}", 1405, 3, "CO", OfLetterKind.Outgoing, "  "));
        }

        [TestMethod]
        public void Sequence_Without_Width_Is_Not_Padded_And_The_Old_Tokens_Still_Work()
        {
            Assert.AreEqual("INN-1405-1", LetterService.FormatNumber("INN-{yyyy}-{seq}", 1405, 1, "CO", OfLetterKind.Outgoing));
            Assert.AreEqual("INN-1405-12345", LetterService.FormatNumber("INN-{yyyy}-{seq}", 1405, 12345, "CO", OfLetterKind.Outgoing));
            Assert.AreEqual("05/ص/OPS/00007", LetterService.FormatNumber(LetterService.FirstDefaultPattern, 1405, 7, "OPS", OfLetterKind.Outgoing));
            Assert.AreEqual("05/و/CO/00007", LetterService.FormatNumber(LetterService.FirstDefaultPattern, 1405, 7, null, OfLetterKind.Incoming));
        }

        [TestMethod]
        public void Project_Code_Is_The_Project_Control_Code_With_The_Contract_Code_As_Fallback()
        {
            Assert.AreEqual("40501MA01", LetterService.ProjectCodeOf("40501MA01", "C-77"));
            Assert.AreEqual("C-77", LetterService.ProjectCodeOf(null, "C-77"));
            Assert.AreEqual("C-77", LetterService.ProjectCodeOf("  ", " C-77 "));
            Assert.IsNull(LetterService.ProjectCodeOf(null, ""));
        }

        // ------------------------------------------------------------------------------------------ 2. secretariat access

        [TestMethod]
        public void Secretariat_Reads_Normal_And_Confidential_But_Not_Secret_Letters()
        {
            Assert.IsTrue(LetterService.SecretariatReadsContent(OfClassification.Normal));
            Assert.IsTrue(LetterService.SecretariatReadsContent(OfClassification.Confidential));
            Assert.IsFalse(LetterService.SecretariatReadsContent(OfClassification.Secret));
        }

        [TestMethod]
        public void Office_Draft_Seat_Never_Receives_Confidential_Or_Secret_Text()
        {
            LetterDto Letter(OfClassification c, bool meta, OfLetterKind kind) => new LetterDto { Classification = c, MetadataOnly = meta, Kind = kind };
            Assert.IsTrue(OfficeDraftSeatJob.MayDraftFor(Letter(OfClassification.Normal, false, OfLetterKind.Incoming)));
            // the secretariat (and so the seat user) now reads a Confidential letter in full: the seat still refuses it
            Assert.IsFalse(OfficeDraftSeatJob.MayDraftFor(Letter(OfClassification.Confidential, false, OfLetterKind.Incoming)));
            Assert.IsFalse(OfficeDraftSeatJob.MayDraftFor(Letter(OfClassification.Secret, false, OfLetterKind.Incoming)));
            Assert.IsFalse(OfficeDraftSeatJob.MayDraftFor(Letter(OfClassification.Secret, true, OfLetterKind.Incoming)));
            Assert.IsFalse(OfficeDraftSeatJob.MayDraftFor(Letter(OfClassification.Normal, true, OfLetterKind.Incoming)));
            Assert.IsFalse(OfficeDraftSeatJob.MayDraftFor(Letter(OfClassification.Normal, false, OfLetterKind.Outgoing)));
            Assert.IsFalse(OfficeDraftSeatJob.MayDraftFor(null));
        }

        // ------------------------------------------------------------------------------------------ 5. lateness and overtime

        private static DayInput Day(int inMinute, int outMinute, decimal factor = 3m, int grace = 15)
        {
            DateTime date = new DateTime(2026, 8, 12); // a Wednesday
            return new DayInput
            {
                Date = date, HasShift = true, StartMinute = 8 * 60, EndMinute = 16 * 60, BreakMinutes = 30, GraceMinutes = grace, DelayPenaltyFactor = factor,
                OvertimeNeedsPreApproval = false, Punches = new System.Collections.Generic.List<DateTime> { date.AddMinutes(inMinute), date.AddMinutes(outMinute) },
            };
        }

        [TestMethod]
        public void Policy_Defaults_Are_The_Company_Answers()
        {
            var p = new OfAttendancePolicy();
            Assert.AreEqual(15, p.GraceMinutes);
            Assert.IsFalse(p.OvertimeNeedsPreApproval);
            Assert.AreEqual(1320, p.MonthlyOvertimeCapMinutes);
            Assert.AreEqual(3m, p.DelayPenaltyFactor);
            Assert.IsNull(p.MaxOvertimeMinutesPerDay);
            var dto = new AttendancePolicyDto();
            Assert.AreEqual(15, dto.GraceMinutes);
            Assert.AreEqual(1320, dto.MonthlyOvertimeCapMinutes);
            Assert.AreEqual(3m, dto.DelayPenaltyFactor);
        }

        [TestMethod]
        public void Lateness_Inside_The_Grace_Costs_Nothing()
        {
            DayResult r = AttendanceMath.Compute(Day(8 * 60 + 14, 16 * 60));
            Assert.AreEqual(0, r.DelayMinutes);
            Assert.AreEqual(0, r.DelayDeductionMinutes);
            DayResult edge = AttendanceMath.Compute(Day(8 * 60 + 15, 16 * 60));
            Assert.AreEqual(0, edge.DelayDeductionMinutes);
        }

        [TestMethod]
        public void Lateness_Beyond_The_Grace_Deducts_The_Whole_Delay_Times_Three()
        {
            DayResult r = AttendanceMath.Compute(Day(8 * 60 + 16, 16 * 60));
            Assert.AreEqual(16, r.DelayMinutes);
            Assert.AreEqual(48, r.DelayDeductionMinutes);
            Assert.AreEqual(90, AttendanceMath.Compute(Day(8 * 60 + 30, 16 * 60)).DelayDeductionMinutes);
        }

        [TestMethod]
        public void Delay_Coefficient_Comes_From_The_Policy()
        {
            Assert.AreEqual(32, AttendanceMath.Compute(Day(8 * 60 + 16, 16 * 60, factor: 2m)).DelayDeductionMinutes);
            Assert.AreEqual(0, AttendanceMath.Compute(Day(8 * 60 + 16, 16 * 60, factor: 0m)).DelayDeductionMinutes);
            Assert.AreEqual(24, AttendanceMath.DelayDeduction(16, 1.5m));
            Assert.AreEqual(0, AttendanceMath.DelayDeduction(0, 3m));
        }

        [TestMethod]
        public void Monthly_Overtime_Above_The_Cap_Is_Not_Counted()
        {
            // 25 hours in the month: 22 hours count
            Assert.AreEqual(22 * 60, AttendanceMath.MonthlyOvertime(new[] { 10 * 60, 8 * 60, 7 * 60 }, 1320));
            Assert.AreEqual(5 * 60, AttendanceMath.MonthlyOvertime(new[] { 3 * 60, 2 * 60 }, 1320));
            Assert.AreEqual(0, AttendanceMath.MonthlyOvertime(new[] { 120 }, 0));
            Assert.AreEqual(0, AttendanceMath.MonthlyOvertime(new int[0], 1320));
        }

        [TestMethod]
        public void Overtime_Without_Preapproval_Counts_Every_Minute_Per_Day_And_The_Month_Caps_It()
        {
            // in 08:00, out 18:30 on a 08:00-16:00 shift: 150 minutes of overtime
            DayResult r = AttendanceMath.Compute(Day(8 * 60, 18 * 60 + 30));
            Assert.AreEqual(150, r.OvertimeMinutes);
        }

        [TestMethod]
        public void Deducted_Minutes_Reach_Payroll_As_A_Part_Of_One_Day()
        {
            Assert.AreEqual(0.1m, AttendanceMath.DeductionDays(45, 450));
            Assert.AreEqual(1m, AttendanceMath.DeductionDays(900, 450));
            Assert.AreEqual(0m, AttendanceMath.DeductionDays(0, 450));
            Assert.AreEqual(0m, AttendanceMath.DeductionDays(30, 0));
        }

        [TestMethod]
        public void Policy_Columns_Keep_Zero_As_A_Real_Value()
        {
            var builder = new ModelBuilder();
            DataLayer.Configurations.EF.NasbNiro.Office.OfficeModelBuilderExtensions.AddOfficeConfigurations(builder);
            IMutableEntityType policy = builder.Model.FindEntityType(typeof(OfAttendancePolicy));
            foreach (string name in new[] { nameof(OfAttendancePolicy.MonthlyOvertimeCapMinutes), nameof(OfAttendancePolicy.DelayPenaltyFactor) })
                Assert.AreEqual(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never, policy.FindProperty(name).ValueGenerated, name);
            Assert.AreEqual(1320, policy.FindProperty(nameof(OfAttendancePolicy.MonthlyOvertimeCapMinutes)).GetDefaultValue());
            Assert.AreEqual(3m, policy.FindProperty(nameof(OfAttendancePolicy.DelayPenaltyFactor)).GetDefaultValue());
        }

        // ------------------------------------------------------------------------------------------ 3. Excel import

        private static byte[] Xlsx(string sheetXml, string sharedXml = null, string stylesXml = null)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                void Add(string name, string text)
                {
                    using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                    w.Write(text);
                }
                Add("[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"xml\" ContentType=\"application/xml\"/></Types>");
                Add("xl/workbook.xml", "<?xml version=\"1.0\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"گزارش\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Add("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
                Add("xl/worksheets/sheet1.xml", "<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" + sheetXml + "</sheetData></worksheet>");
                if (sharedXml != null) Add("xl/sharedStrings.xml", "<?xml version=\"1.0\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" + sharedXml + "</sst>");
                if (stylesXml != null) Add("xl/styles.xml", "<?xml version=\"1.0\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" + stylesXml + "</styleSheet>");
            }
            return ms.ToArray();
        }

        [TestMethod]
        public void Xlsx_Reader_Reads_Inline_Shared_And_Numeric_Cells()
        {
            string rows =
                "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c><c r=\"D1\" t=\"inlineStr\"><is><t>چهارم</t></is></c></row>" +
                "<row r=\"2\"><c r=\"A2\"><v>00123</v></c><c r=\"B2\" s=\"1\"><v>46245.3368055556</v></c><c r=\"C2\" t=\"str\"><f>1+1</f><v>x</v></c></row>";
            string styles = "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"yyyy\\/mm\\/dd\\ hh:mm\"/></numFmts><cellXfs count=\"2\"><xf numFmtId=\"0\"/><xf numFmtId=\"164\"/></cellXfs>";
            XlsxSheet sheet = XlsxReader.Read(Xlsx(rows, "<si><t>شماره</t></si><si><r><t>زمان</t></r><r><t> تردد</t></r></si>", styles), null);
            Assert.AreEqual("گزارش", sheet.Name);
            Assert.AreEqual(2, sheet.Rows.Count);
            Assert.AreEqual("شماره", sheet.Rows[0].Cell(0).Text);
            Assert.AreEqual("زمان تردد", sheet.Rows[0].Cell(1).Text);
            Assert.AreEqual("چهارم", sheet.Rows[0].Cell(3).Text);
            Assert.AreEqual(4, sheet.MaxColumn);
            Assert.AreEqual(123d, sheet.Rows[1].Cell(0).Number);
            Assert.IsFalse(sheet.Rows[1].Cell(0).IsDateFormat);
            Assert.IsTrue(sheet.Rows[1].Cell(1).IsDateFormat);
            Assert.AreEqual("x", sheet.Rows[1].Cell(2).Text);
        }

        [TestMethod]
        public void Xlsx_Reader_Refuses_A_File_That_Is_Not_Xlsx()
        {
            Assert.ThrowsException<XlsxFormatException>(() => XlsxReader.Read(Encoding.UTF8.GetBytes("a,b,c\n1,2,3"), null));
            Assert.ThrowsException<XlsxFormatException>(() => XlsxReader.Read(Xlsx("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>"), "نبود"));
        }

        [TestMethod]
        public void Column_Letters_Round_Trip()
        {
            Assert.AreEqual("A", XlsxReader.ColumnLetter(0));
            Assert.AreEqual("Z", XlsxReader.ColumnLetter(25));
            Assert.AreEqual("AA", XlsxReader.ColumnLetter(26));
            Assert.AreEqual(27, XlsxReader.ColumnIndex("ab"));
            Assert.AreEqual(-1, XlsxReader.ColumnIndex("A1"));
            Assert.AreEqual(-1, XlsxReader.ColumnIndex(""));
        }

        [TestMethod]
        public void Dates_Are_Read_As_Persian_Or_Gregorian()
        {
            DateTime d;
            Assert.IsTrue(ClockCellParser.TryDateText("1405/05/20", out d)); Assert.AreEqual(new DateTime(2026, 8, 11), d);
            Assert.IsTrue(ClockCellParser.TryDateText("۱۴۰۵/۰۵/۲۰", out d)); Assert.AreEqual(new DateTime(2026, 8, 11), d);
            Assert.IsTrue(ClockCellParser.TryDateText("2026-08-11", out d)); Assert.AreEqual(new DateTime(2026, 8, 11), d);
            Assert.IsTrue(ClockCellParser.TryDateText("20/05/1405", out d)); Assert.AreEqual(new DateTime(2026, 8, 11), d);
            Assert.IsTrue(ClockCellParser.TryDateText("14050520", out d)); Assert.AreEqual(new DateTime(2026, 8, 11), d);
            Assert.IsTrue(ClockCellParser.TryDateText("1405/05/20 08:05", out d)); Assert.AreEqual(new DateTime(2026, 8, 11), d);
            Assert.IsFalse(ClockCellParser.TryDateText("1405/13/20", out _));
            Assert.IsFalse(ClockCellParser.TryDateText("سلام", out _));
            // an Excel serial with a date format
            Assert.IsTrue(ClockCellParser.TryDate(new XlsxCell { Number = 46245d, IsDateFormat = true }, out d)); Assert.AreEqual(new DateTime(2026, 8, 11), d);
            // a plain small number is no date
            Assert.IsFalse(ClockCellParser.TryDate(new XlsxCell { Number = 123d }, out _));
            Assert.IsTrue(ClockCellParser.TryDate(new XlsxCell { Number = 14050520d }, out d)); Assert.AreEqual(new DateTime(2026, 8, 11), d);
        }

        [TestMethod]
        public void Times_Are_Read_From_Text_And_From_Fractions()
        {
            int m;
            Assert.IsTrue(ClockCellParser.TryTimeText("08:05", out m)); Assert.AreEqual(485, m);
            Assert.IsTrue(ClockCellParser.TryTimeText("۸:۰۵:۵۹", out m)); Assert.AreEqual(485, m);
            Assert.IsTrue(ClockCellParser.TryTimeText("1405/05/20 17:30", out m)); Assert.AreEqual(1050, m);
            Assert.IsTrue(ClockCellParser.TryTimeText("5:30 PM", out m)); Assert.AreEqual(1050, m);
            Assert.IsFalse(ClockCellParser.TryTimeText("25:00", out _));
            Assert.IsTrue(ClockCellParser.TryTime(new XlsxCell { Number = 485d / 1440d, IsDateFormat = true }, out m)); Assert.AreEqual(485, m);
            Assert.IsTrue(ClockCellParser.TryTime(new XlsxCell { Number = 46245d + 1050d / 1440d, IsDateFormat = true }, out m)); Assert.AreEqual(1050, m);
            Assert.IsFalse(ClockCellParser.TryTime(new XlsxCell { Number = 8d }, out _));
        }

        [TestMethod]
        public void Date_And_Time_In_One_Cell()
        {
            DateTime v;
            Assert.IsTrue(ClockCellParser.TryDateTime(new XlsxCell { Text = "1405/05/20 08:05:12" }, out v)); Assert.AreEqual(new DateTime(2026, 8, 11, 8, 5, 0), v);
            Assert.IsTrue(ClockCellParser.TryDateTime(new XlsxCell { Number = 46245d + 485d / 1440d, IsDateFormat = true }, out v)); Assert.AreEqual(new DateTime(2026, 8, 11, 8, 5, 0), v);
            Assert.IsFalse(ClockCellParser.TryDateTime(new XlsxCell { Text = "1405/05/20" }, out _));
        }

        [TestMethod]
        public void Personnel_Codes_Ignore_Leading_Zeros_And_Persian_Digits()
        {
            Assert.AreEqual(ClockCellParser.CodeKey("00123"), ClockCellParser.CodeKey("۱۲۳"));
            Assert.AreEqual("123", ClockCellParser.CodeKey(" 0123 "));
            Assert.AreEqual("0", ClockCellParser.CodeKey("000"));
            Assert.AreEqual("AB-1", ClockCellParser.CodeKey("ab-1"));
        }

        [TestMethod]
        public void Direction_Texts_Follow_The_Profile_Or_The_Defaults()
        {
            Assert.AreEqual(OfClockDirection.In, ClockCellParser.Direction("ورود", null, null));
            Assert.AreEqual(OfClockDirection.Out, ClockCellParser.Direction("Out", null, null));
            Assert.AreEqual(OfClockDirection.Unknown, ClockCellParser.Direction("سایر", null, null));
            Assert.AreEqual(OfClockDirection.In, ClockCellParser.Direction("ت", "ت,ش", "خ"));
            Assert.AreEqual(OfClockDirection.Out, ClockCellParser.Direction("خ", "ت,ش", "خ"));
            Assert.AreEqual(OfClockDirection.Unknown, ClockCellParser.Direction("ورود", "ت", "خ"));
        }

        [TestMethod]
        public void Mapping_Is_Checked_Against_The_Heading_Row()
        {
            var header = new XlsxRow { RowNumber = 1 };
            header.Cells[0] = new XlsxCell { Text = "کد پرسنلی" };
            header.Cells[1] = new XlsxCell { Text = "تاریخ" };
            header.Cells[2] = new XlsxCell { Text = "ساعت" };
            var good = new ClockFileMappingDto { HeaderRow = 1, CodeColumn = "کد پرسنلی", DateColumn = "تاریخ", TimeColumn = "C" };
            Assert.IsNull(AttendanceService.CheckMapping(good, header, out var map));
            Assert.AreEqual(0, map.Code); Assert.AreEqual(1, map.Date); Assert.AreEqual(2, map.Time); Assert.AreEqual(-1, map.Direction);
            Assert.IsNotNull(AttendanceService.CheckMapping(new ClockFileMappingDto { CodeColumn = "A" }, null, out _));
            Assert.IsNotNull(AttendanceService.CheckMapping(new ClockFileMappingDto { CodeColumn = "A", DateColumn = "B" }, null, out _));
            Assert.IsNull(AttendanceService.CheckMapping(new ClockFileMappingDto { HeaderRow = 0, CodeColumn = "A", DateTimeColumn = "B" }, null, out _));
            Assert.IsNotNull(AttendanceService.CheckMapping(new ClockFileMappingDto { HeaderRow = 1, CodeColumn = "نامی که نیست", DateTimeColumn = "B" }, header, out _));
        }

        // ------------------------------------------------------------------------------------------ 4. archive PDFs

        [TestMethod]
        public void Archive_File_Name_Carries_The_Old_Letter_Number()
        {
            Assert.AreEqual("1399_OUT_45", LetterService.ArchiveNumberOf("1399_OUT_45.pdf", null));
            Assert.AreEqual("1399_OUT_45", LetterService.ArchiveNumberOf("C:\\export\\1399_OUT_45.PDF", ""));
            Assert.AreEqual("12", LetterService.ArchiveNumberOf("Letter-12-final.pdf", @"^Letter-(?<number>\d+)-"));
            Assert.AreEqual("12", LetterService.ArchiveNumberOf("Letter-12.pdf", @"Letter-(\d+)"));
            Assert.IsNull(LetterService.ArchiveNumberOf("other.pdf", @"^Letter-(\d+)"));
        }

        [TestMethod]
        public void Archive_Numbers_Match_Despite_Characters_A_File_Name_Cannot_Hold()
        {
            Assert.AreEqual(LetterService.ArchiveKey("1399/OUT/45"), LetterService.ArchiveKey("1399_out_45"));
            Assert.AreEqual(LetterService.ArchiveKey("۱۳۹۹/ص/۱۲۳"), LetterService.ArchiveKey("1399_ص_123"));
            Assert.AreEqual(LetterService.ArchiveKey("12ي"), LetterService.ArchiveKey("12ی"));
            Assert.AreNotEqual(LetterService.ArchiveKey("1399/OUT/45"), LetterService.ArchiveKey("1399/OUT/46"));
        }
    }
}
