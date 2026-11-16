using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.EngInbox;
using Services.NasbNiro.Finance;

namespace MsTests
{
    /// <summary>ENG-F1 part 1: the package workbook (template, sample, parser, validator, canonical hash). Pure: no database.</summary>
    [TestClass]
    public class EngInboxPackageTests
    {
        private static EngParsedPackage Parse(EngTables t, Dictionary<string, EngFileInfo> files = null, params string[] omit)
        {
            files ??= Facts(t);
            return EngPackageParser.Parse(EngPackageWriter.Build(t, omit), "ENG-PACKAGE.xlsx", files);
        }

        private static Dictionary<string, EngFileInfo> Facts(EngTables t) =>
            t.Files.ToDictionary(f => f.Key, f => new EngFileInfo { Name = f.Key, Size = f.Value.Length, Sha256 = EngText.Sha256Hex(f.Value) }, StringComparer.Ordinal);

        private static List<EngFinding> Errors(EngParsedPackage p) => p.Findings.Where(f => f.IsError).ToList();

        private static void AssertOneError(EngParsedPackage p, string sheet, int row, string contains)
        {
            List<EngFinding> e = Errors(p);
            Assert.AreEqual(1, e.Count, "exactly one error: " + string.Join(" || ", e.Select(x => x.Describe())));
            Assert.AreEqual(sheet, e[0].Sheet, e[0].Describe());
            if (row >= 0) Assert.AreEqual(row, e[0].Row, e[0].Describe());
            StringAssert.Contains(e[0].Message, contains, e[0].Describe());
        }

        // the sample: row 1 is the header, the data starts at row 2
        private static int RowOfBom(string code) { EngTables t = EngPackageWriter.SampleTables(); return t.Bom.FindIndex(r => (string)r[2] == code) + 2; }

        [TestMethod]
        public void The_Sample_Passes_With_No_Error_And_No_Warning_And_Has_The_Promised_Size()
        {
            EngSamplePackage s = EngPackageWriter.Sample();
            EngParsedPackage p = EngPackageParser.Parse(s.Workbook, s.WorkbookName, EngPackageWriter.FactsOf(s));
            Assert.AreEqual(0, p.Findings.Count, string.Join(" || ", p.Findings.Select(f => f.Describe())));
            Assert.AreEqual(12, p.Bom.Count);
            Assert.AreEqual(13, p.Opc.Count);
            Assert.AreEqual(5, p.Docs.Count);
            Assert.AreEqual(EngPackageKind.Platform, p.Info.Kind);
            Assert.AreEqual("TEST-PLT-01", p.Info.PlatformCode);
            Assert.AreEqual("API 610", p.Info.Standard);
            Assert.AreEqual(1, p.Info.PackageVersion);
            Assert.AreEqual(new DateTime(2026, 11, 16), p.Info.Date, "1405/08/25 is 2026-11-16");
            Assert.AreEqual(3, p.Bom.Max(b => b.Level) - p.Bom.Min(b => b.Level) + 1, "three levels");
            Assert.AreEqual(8, p.Bom.Count(b => b.MakeBuy == EngMakeBuy.Make), "the make parts");
            Assert.AreEqual(4, p.Bom.Count(b => b.MakeBuy == EngMakeBuy.Purchase), "the purchased parts");
        }

        [TestMethod]
        public void Writing_The_Sample_Then_Parsing_It_Gives_A_Stable_Hash()
        {
            EngSamplePackage a = EngPackageWriter.Sample(), b = EngPackageWriter.Sample();
            string h1 = EngPackageParser.Parse(a.Workbook, a.WorkbookName, EngPackageWriter.FactsOf(a)).ContentHash;
            string h2 = EngPackageParser.Parse(b.Workbook, b.WorkbookName, EngPackageWriter.FactsOf(b)).ContentHash;
            Assert.AreEqual(64, h1.Length);
            Assert.AreEqual(h1, h2);
            // version 2 is another package
            EngSamplePackage v2 = EngPackageWriter.Sample(2);
            Assert.AreNotEqual(h1, EngPackageParser.Parse(v2.Workbook, v2.WorkbookName, EngPackageWriter.FactsOf(v2)).ContentHash);
        }

        [TestMethod]
        public void The_Hash_Ignores_The_Date_And_The_Preparer_But_Not_The_Content_Or_The_Files()
        {
            EngTables t = EngPackageWriter.SampleTables();
            string h = Parse(t).ContentHash;
            t.Info.Single(r => (string)r[0] == "تاریخ")[1] = "2027-01-02";
            t.Info.Single(r => (string)r[0] == "تهیه‌کننده")[1] = "شخص دیگر";
            Assert.AreEqual(h, Parse(t).ContentHash, "date and preparer text are not content");
            t.Bom[3][4] = 2m;
            Assert.AreNotEqual(h, Parse(t).ContentHash, "a quantity is content");
            EngTables t2 = EngPackageWriter.SampleTables();
            t2.Files["DS-TST-01.txt"] = Encoding.UTF8.GetBytes("changed");
            Assert.AreNotEqual(h, Parse(t2).ContentHash, "a referenced file is part of the hash");
        }

        [TestMethod]
        public void The_Empty_Template_Has_The_Sheets_And_Headers_And_Explanation_Rows_Are_Skipped()
        {
            byte[] template = EngPackageWriter.Template();
            var names = Services.NasbNiro.Office.XlsxReader.SheetNames(template);
            CollectionAssert.AreEqual(new[] { "مشخصات", "BOM", "OPC", "مدارک", "MDL" }, names);
            EngParsedPackage p = EngPackageParser.Parse(template, "t.xlsx", new Dictionary<string, EngFileInfo>());
            Assert.AreEqual(0, p.Bom.Count, "the ※ row is not data");
            Assert.AreEqual(0, p.Opc.Count);
            Assert.AreEqual(0, p.Docs.Count);
            Assert.AreEqual(0, p.Mdl.Count);
            Assert.IsTrue(p.HasErrors, "an empty template is not a package");
            Assert.IsTrue(p.Findings.Any(f => f.Sheet == "BOM" && f.Message.Contains("هیچ سطری")));
        }

        [TestMethod]
        public void A_Missing_Sheet_Is_An_Error_With_The_Sheet_Name()
        {
            EngParsedPackage p = Parse(EngPackageWriter.SampleTables(), null, "OPC");
            Assert.IsTrue(Errors(p).Any(e => e.Sheet == "OPC" && e.Message.Contains("برگه «OPC»")), string.Join(" || ", p.Findings.Select(f => f.Describe())));
            p = Parse(EngPackageWriter.SampleTables(), null, "مدارک");
            Assert.IsTrue(Errors(p).Any(e => e.Sheet == "مدارک"));
        }

        [TestMethod]
        public void A_Missing_Required_Header_Is_An_Error()
        {
            EngTables t = EngPackageWriter.SampleTables();
            t.BomHead[4] = "مقدار";
            EngParsedPackage p = Parse(t);
            Assert.IsTrue(Errors(p).Any(e => e.Sheet == "BOM" && e.Row == 1 && e.Message.Contains("«تعداد»")), string.Join(" || ", p.Findings.Select(f => f.Describe())));
        }

        [TestMethod]
        public void Headers_Are_Matched_By_Text_Not_By_Position_With_Arabic_Letters_And_English_Aliases()
        {
            EngTables t = EngPackageWriter.SampleTables();
            // reverse the column order of the BOM sheet and use the Arabic kaf / yeh and the English alias for some headers
            Array.Reverse(t.BomHead);
            foreach (object[] r in t.Bom) Array.Reverse(r);
            t.BomHead[t.BomHead.Length - 3] = "كد قطعه";          // Arabic kaf
            t.BomHead[t.BomHead.Length - 4] = "Description";
            t.OpcHead[1] = "Operation No";
            EngParsedPackage p = Parse(t);
            Assert.AreEqual(0, Errors(p).Count, string.Join(" || ", p.Findings.Select(f => f.Describe())));
            Assert.AreEqual(12, p.Bom.Count);
        }

        [TestMethod]
        public void Persian_Digits_And_Arabic_Decimal_Separators_Are_Read()
        {
            EngTables t = EngPackageWriter.SampleTables();
            t.Bom[4][4] = "۲٫۵";            // TST-1130 gasket: 2.5
            t.Bom[3][4] = "1٬250";          // 1250
            t.Bom[2][0] = "۳";              // level text
            t.Opc[0][4] = "۳۰٫۵";
            t.Info.Single(r => (string)r[0] == "نسخه بسته")[1] = "۱";
            EngParsedPackage p = Parse(t);
            Assert.AreEqual(0, Errors(p).Count, string.Join(" || ", p.Findings.Select(f => f.Describe())));
            Assert.AreEqual(2.5m, p.Bom.Single(b => b.PartCode == "TST-1130").Qty);
            Assert.AreEqual(1250m, p.Bom.Single(b => b.PartCode == "TST-1120").Qty);
            Assert.AreEqual(30.5m, p.Opc[0].SetupMinutes);
            Assert.AreEqual(1, p.Info.PackageVersion);
        }

        [TestMethod]
        public void A_Duplicate_Part_Under_The_Same_Parent_Is_An_Error()
        {
            EngTables t = EngPackageWriter.SampleTables();
            t.Bom.Add((object[])t.Bom[3].Clone());
            EngParsedPackage p = Parse(t);
            AssertOneError(p, "BOM", t.Bom.Count + 1, "تکراری");
        }

        [TestMethod]
        public void A_Parent_That_Is_Not_In_The_Bom_Is_An_Error()
        {
            EngTables t = EngPackageWriter.SampleTables();
            t.Bom[7][1] = "TST-9999";
            EngParsedPackage p = Parse(t);
            Assert.IsTrue(Errors(p).Any(e => e.Sheet == "BOM" && e.Row == 9 && e.Message.Contains("TST-9999")), string.Join(" || ", p.Findings.Select(f => f.Describe())));
        }

        [TestMethod]
        public void A_Cycle_In_The_Bom_Is_An_Error()
        {
            EngTables t = EngPackageWriter.SampleTables();
            // the casing assembly becomes a child of its own body: 1100 -> 1110 -> 1100
            t.Bom[1][1] = "TST-1110";
            EngParsedPackage p = Parse(t);
            Assert.IsTrue(Errors(p).Any(e => e.Message.Contains("حلقه در BOM")), string.Join(" || ", p.Findings.Select(f => f.Describe())));
        }

        [TestMethod]
        public void A_Quantity_That_Is_Zero_Negative_Or_Not_A_Number_Is_An_Error()
        {
            foreach (var bad in new object[] { 0m, -1m, "abc", "" })
            {
                EngTables t = EngPackageWriter.SampleTables();
                t.Bom[2][4] = bad;
                EngParsedPackage p = Parse(t);
                Assert.IsTrue(Errors(p).Any(e => e.Sheet == "BOM" && e.Row == 4 && e.Message.Contains("«تعداد»")), "quantity " + bad + ": " + string.Join(" || ", p.Findings.Select(f => f.Describe())));
            }
        }

        [TestMethod]
        public void An_Unknown_Unit_Or_Make_Buy_Value_Is_An_Error()
        {
            EngTables t = EngPackageWriter.SampleTables();
            t.Bom[2][5] = "بشکه‌ی عجیب";
            t.Bom[3][6] = "شاید";
            EngParsedPackage p = Parse(t);
            Assert.AreEqual(2, Errors(p).Count, string.Join(" || ", p.Findings.Select(f => f.Describe())));
            Assert.IsTrue(Errors(p).Any(e => e.Row == 4 && e.Message.Contains("واحد")));
            Assert.IsTrue(Errors(p).Any(e => e.Row == 5 && e.Message.Contains("ساخت/خرید")));
            // English aliases are accepted
            t = EngPackageWriter.SampleTables();
            t.Bom[2][5] = "pcs"; t.Bom[3][6] = "Make";
            Assert.AreEqual(0, Errors(Parse(t)).Count);
        }

        [TestMethod]
        public void An_Opc_Part_That_Is_Not_In_The_Bom_Is_An_Error()
        {
            EngTables t = EngPackageWriter.SampleTables();
            t.Opc[0][0] = "TST-7777";
            EngParsedPackage p = Parse(t);
            Assert.IsTrue(Errors(p).Any(e => e.Sheet == "OPC" && e.Row == 2 && e.Message.Contains("TST-7777")), string.Join(" || ", p.Findings.Select(f => f.Describe())));
        }

        [TestMethod]
        public void A_Duplicate_Operation_Number_Per_Part_Is_An_Error_But_The_Same_Number_On_Two_Parts_Is_Fine()
        {
            EngTables t = EngPackageWriter.SampleTables();
            t.Opc[1][1] = 10;     // TST-1110 has 10 twice
            EngParsedPackage p = Parse(t);
            Assert.IsTrue(Errors(p).Any(e => e.Sheet == "OPC" && e.Row == 3 && e.Message.Contains("تکراری")), string.Join(" || ", p.Findings.Select(f => f.Describe())));
            Assert.AreEqual(0, Errors(Parse(EngPackageWriter.SampleTables())).Count, "operation 10 exists on several parts of the sample");
        }

        [TestMethod]
        public void A_Predecessor_That_Is_Missing_Or_Cyclic_Is_An_Error()
        {
            EngTables t = EngPackageWriter.SampleTables();
            t.Opc[1][8] = "90";
            Assert.IsTrue(Errors(Parse(t)).Any(e => e.Sheet == "OPC" && e.Row == 3 && e.Message.Contains("پیش‌نیاز 90")));
            t = EngPackageWriter.SampleTables();
            t.Opc[0][8] = "20";            // 10 needs 20 and 20 needs 10
            EngParsedPackage p = Parse(t);
            Assert.IsTrue(Errors(p).Any(e => e.Message.Contains("حلقه")), string.Join(" || ", p.Findings.Select(f => f.Describe())));
            t = EngPackageWriter.SampleTables();
            t.Opc[0][8] = "10";
            Assert.IsTrue(Errors(Parse(t)).Any(e => e.Message.Contains("پیش‌نیاز خودش")));
            t = EngPackageWriter.SampleTables();
            t.Opc[2][8] = "10, 20";        // several, comma separated, with a space
            Assert.AreEqual(0, Errors(Parse(t)).Count);
        }

        [TestMethod]
        public void Negative_Times_Are_Errors()
        {
            EngTables t = EngPackageWriter.SampleTables();
            t.Opc[0][4] = -5m;
            t.Opc[1][5] = -1m;
            EngParsedPackage p = Parse(t);
            Assert.AreEqual(2, Errors(p).Count, string.Join(" || ", p.Findings.Select(f => f.Describe())));
            Assert.IsTrue(Errors(p).Any(e => e.Row == 2 && e.Message.Contains("زمان آماده")));
            Assert.IsTrue(Errors(p).Any(e => e.Row == 3 && e.Message.Contains("زمان اجرا")));
        }

        [TestMethod]
        public void A_Make_Part_Without_Operations_Is_Only_A_Warning()
        {
            EngTables t = EngPackageWriter.SampleTables();
            t.Opc.RemoveAll(r => (string)r[0] == "TST-1500");
            EngParsedPackage p = Parse(t);
            Assert.AreEqual(0, Errors(p).Count);
            List<EngFinding> w = p.Findings.Where(f => !f.IsError).ToList();
            Assert.AreEqual(1, w.Count);
            Assert.AreEqual("BOM", w[0].Sheet);
            Assert.AreEqual(RowOfBom("TST-1500"), w[0].Row);
            StringAssert.Contains(w[0].Message, "TST-1500");
            Assert.IsFalse(p.HasErrors);
            Assert.IsNotNull(p.ContentHash);
        }

        [TestMethod]
        public void A_Referenced_File_That_Is_Missing_Is_An_Error_When_Required_And_A_Warning_Otherwise()
        {
            EngTables t = EngPackageWriter.SampleTables();
            Dictionary<string, EngFileInfo> facts = Facts(t);
            facts.Remove("GA-TST-1000.txt");    // required
            facts.Remove("DS-TST-01.txt");      // optional
            EngParsedPackage p = Parse(t, facts);
            Assert.AreEqual(1, Errors(p).Count, string.Join(" || ", p.Findings.Select(f => f.Describe())));
            Assert.AreEqual(2, Errors(p)[0].Row);
            StringAssert.Contains(Errors(p)[0].Message, "GA-TST-1000.txt");
            List<EngFinding> w = p.Findings.Where(f => !f.IsError).ToList();
            Assert.AreEqual(1, w.Count);
            Assert.AreEqual(4, w[0].Row);
            Assert.IsNull(p.ContentHash, "a package with an error has no hash");
        }

        [TestMethod]
        public void A_File_Name_With_A_Path_Dot_Dot_Or_An_Absolute_Path_Is_An_Error()
        {
            foreach (string bad in new[] { "sub/GA.txt", "sub\\GA.txt", "..\\x.txt", "a..b.txt", "/etc/passwd", "C:\\x.txt", "C:x.txt" })
            {
                EngTables t = EngPackageWriter.SampleTables();
                t.Docs[0][4] = bad;
                EngParsedPackage p = Parse(t);
                Assert.IsTrue(Errors(p).Any(e => e.Sheet == "مدارک" && e.Row == 2 && e.Message.Contains("«نام فایل»")), bad + ": " + string.Join(" || ", p.Findings.Select(f => f.Describe())));
            }
            Assert.IsNull(EngPackageParser.FileNameProblem("drawing 1.pdf"));
        }

        [TestMethod]
        public void A_Duplicate_Document_Number_And_Revision_Is_An_Error()
        {
            EngTables t = EngPackageWriter.SampleTables();
            t.Docs.Add((object[])t.Docs[0].Clone());
            AssertOneError(Parse(t), "مدارک", 7, "تکراری");
        }

        [TestMethod]
        public void The_Info_Sheet_Rules_Are_Checked()
        {
            EngTables t = EngPackageWriter.SampleTables();
            t.Info.Single(r => (string)r[0] == "نسخه بسته")[1] = 0;
            AssertOneError(Parse(t), "مشخصات", 5, "نسخه بسته");
            t = EngPackageWriter.SampleTables();
            t.Info.Single(r => (string)r[0] == "نوع بسته")[1] = "ناشناخته";
            Assert.IsTrue(Errors(Parse(t)).Any(e => e.Message.Contains("باید «پلتفرم»")));
            t = EngPackageWriter.SampleTables();
            t.Info.Single(r => (string)r[0] == "قالب")[1] = 2;
            Assert.IsTrue(Errors(Parse(t)).Any(e => e.Message.Contains("قالب")));
            t = EngPackageWriter.SampleTables();
            t.Info.Single(r => (string)r[0] == "تاریخ")[1] = "نامعلوم";
            Assert.IsTrue(Errors(Parse(t)).Any(e => e.Message.Contains("تاریخ")));
            // a project package needs the contract and the project code
            t = EngPackageWriter.SampleTables();
            t.Info.Single(r => (string)r[0] == "نوع بسته")[1] = "پروژه";
            EngParsedPackage p = Parse(t);
            Assert.AreEqual(2, Errors(p).Count(e => e.Sheet == "مشخصات" && (e.Message.Contains("کد قرارداد") || e.Message.Contains("کد پروژه"))), string.Join(" || ", p.Findings.Select(f => f.Describe())));
            t.Info.Add(new object[] { "کد قرارداد", "CT-TEST-1", null });
            t.Info.Add(new object[] { "کد پروژه", "PRJ-TEST-1", null });
            t.Mdl.Add(new object[] { "MDL-001", "نقشهٔ کلی", "نقشه", "1405/09/10" });
            p = Parse(t);
            Assert.AreEqual(0, Errors(p).Count, string.Join(" || ", p.Findings.Select(f => f.Describe())));
            Assert.AreEqual(EngPackageKind.Project, p.Info.Kind);
            Assert.AreEqual(1, p.Mdl.Count);
            Assert.AreEqual(new DateTime(2026, 12, 1), p.Mdl[0].CommitDate, "1405/09/10");
        }

        [TestMethod]
        public void A_Part_Code_That_The_Product_Bank_Would_Refuse_Is_An_Error_Here_Too()
        {
            EngTables t = EngPackageWriter.SampleTables();
            t.Bom[2][2] = "TST 1110";
            Assert.IsTrue(Errors(Parse(t)).Any(e => e.Sheet == "BOM" && e.Row == 4));
        }

        [TestMethod]
        public void All_Findings_Are_Collected_In_One_Pass()
        {
            EngTables t = EngPackageWriter.SampleTables();
            t.Bom[2][4] = 0m; t.Bom[3][5] = "؟"; t.Opc[0][4] = -1m; t.Docs[0][4] = "a/b.txt";
            t.Info.Single(r => (string)r[0] == "نسخه بسته")[1] = 0;
            EngParsedPackage p = Parse(t);
            Assert.AreEqual(5, Errors(p).Count, string.Join(" || ", p.Findings.Select(f => f.Describe())));
        }

        // ------------------------------------------------------------------------------------------------ the workbook itself

        [TestMethod]
        public void Only_Xlsx_Is_Accepted_And_A_Bad_File_Is_An_Error_Not_An_Exception()
        {
            byte[] wb = EngPackageWriter.Sample().Workbook;
            foreach (string name in new[] { "x.xlsm", "x.xls", "x.xlsb" })
                Assert.IsTrue(EngPackageParser.Parse(wb, name, null).Findings.Single().Message.Contains("xlsx"), name);
            Assert.IsTrue(EngPackageParser.Parse(wb, "x.csv", null).HasErrors);
            Assert.IsTrue(EngPackageParser.Parse(new byte[] { 1, 2, 3, 4, 5 }, "x.xlsx", null).HasErrors);
            Assert.IsTrue(EngPackageParser.Parse(Array.Empty<byte>(), "x.xlsx", null).HasErrors);
            Assert.IsTrue(EngPackageParser.Parse(Encoding.UTF8.GetBytes("PK\u0003\u0004not a zip"), "x.xlsx", null).HasErrors);
        }

        [TestMethod]
        public void A_Workbook_Over_20_Megabytes_Is_Refused()
        {
            byte[] big = new byte[(int)EngPackageLimits.MaxWorkbookBytes + 1];
            big[0] = 0x50; big[1] = 0x4B;
            EngParsedPackage p = EngPackageParser.Parse(big, "x.xlsx", null);
            StringAssert.Contains(p.Findings.Single().Message, "۲۰ مگابایت");
        }

        [TestMethod]
        public void A_Zip_Bomb_Is_Refused_By_Entry_Count_And_By_Uncompressed_Size()
        {
            byte[] wb = EngPackageWriter.Sample().Workbook;
            // too many entries
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                    for (int i = 0; i < EngPackageLimits.MaxZipEntries + 1; i++) zip.CreateEntry("x/" + i + ".txt");
                StringAssert.Contains(EngPackageParser.Parse(ms.ToArray(), "x.xlsx", null).Findings.Single().Message, "تعداد اجزا");
            }
            // 201 MB of zeros inside a small zip
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                using (Stream s = zip.CreateEntry("xl/big.bin", CompressionLevel.Optimal).Open())
                {
                    byte[] chunk = new byte[1024 * 1024];
                    for (int i = 0; i < 201; i++) s.Write(chunk, 0, chunk.Length);
                }
                Assert.IsTrue(ms.Length < 1024 * 1024, "the zip itself is small: " + ms.Length);
                StringAssert.Contains(EngPackageParser.Parse(ms.ToArray(), "x.xlsx", null).Findings.Single().Message, "۲۰۰ مگابایت");
            }
            Assert.IsFalse(EngPackageParser.Parse(wb, "x.xlsx", null).Findings.Any(f => f.Message.Contains("مگابایت")));
        }

        [TestMethod]
        public void A_Macro_Part_Is_Refused()
        {
            byte[] wb = EngPackageWriter.Sample().Workbook;
            using (var ms = new MemoryStream())
            {
                ms.Write(wb, 0, wb.Length);
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, true)) zip.CreateEntry("xl/vbaProject.bin");
                StringAssert.Contains(EngPackageParser.Parse(ms.ToArray(), "x.xlsx", null).Findings.Single().Message, "ماکرو");
            }
        }

        [TestMethod]
        public void A_Formula_Is_Never_Calculated_And_A_Formula_Without_A_Stored_Value_Is_An_Error()
        {
            byte[] wb = EngPackageWriter.Sample().Workbook;
            // sheet 2 is the BOM: cell E3 (quantity of the second line) becomes a formula with no stored value
            byte[] bad = Patch(wb, "xl/worksheets/sheet2.xml", xml =>
            {
                int at = xml.IndexOf("r=\"E3\"", StringComparison.Ordinal);
                Assert.IsTrue(at > 0);
                int start = xml.LastIndexOf("<c ", at, StringComparison.Ordinal);
                int end = xml.IndexOf("</c>", at, StringComparison.Ordinal) + 4;
                return xml.Substring(0, start) + "<c r=\"E3\"><f>1+1</f></c>" + xml.Substring(end);
            });
            EngParsedPackage p = EngPackageParser.Parse(bad, "x.xlsx", EngPackageWriter.FactsOf(EngPackageWriter.Sample()));
            Assert.IsTrue(p.Findings.Any(f => f.IsError && f.Sheet == "BOM" && f.Row == 3 && f.Message.Contains("فرمول")), string.Join(" || ", p.Findings.Select(f => f.Describe())));
            // a formula that has its stored value is read as that value, and is not an error
            byte[] ok = Patch(wb, "xl/worksheets/sheet2.xml", xml =>
            {
                int at = xml.IndexOf("r=\"E3\"", StringComparison.Ordinal);
                int start = xml.LastIndexOf("<c ", at, StringComparison.Ordinal);
                int end = xml.IndexOf("</c>", at, StringComparison.Ordinal) + 4;
                return xml.Substring(0, start) + "<c r=\"E3\"><f>1+1</f><v>7</v></c>" + xml.Substring(end);
            });
            p = EngPackageParser.Parse(ok, "x.xlsx", EngPackageWriter.FactsOf(EngPackageWriter.Sample()));
            Assert.AreEqual(0, p.Findings.Count, string.Join(" || ", p.Findings.Select(f => f.Describe())));
            Assert.AreEqual(7m, p.Bom[1].Qty, "the stored value, not the formula");
        }

        [TestMethod]
        public void More_Than_20000_Bom_Rows_Are_Refused()
        {
            EngTables t = EngPackageWriter.SampleTables();
            for (int i = 0; i < EngPackageLimits.MaxBomRows + 5; i++) t.Bom.Add(new object[] { 2, "TST-1000", "X-" + i, "x", 1m, "عدد", "خرید", null, null, null, null });
            EngParsedPackage p = Parse(t);
            Assert.IsTrue(p.Findings.Any(f => f.IsError && f.Sheet == "BOM" && f.Message.Contains("20,000")), string.Join(" || ", p.Findings.Take(3).Select(f => f.Describe())));
        }

        private static byte[] Patch(byte[] workbook, string entry, Func<string, string> change)
        {
            using (var ms = new MemoryStream())
            {
                using (var src = new ZipArchive(new MemoryStream(workbook), ZipArchiveMode.Read))
                using (var dst = new ZipArchive(ms, ZipArchiveMode.Create, true))
                    foreach (ZipArchiveEntry e in src.Entries)
                    {
                        ZipArchiveEntry n = dst.CreateEntry(e.FullName);
                        using (Stream i = e.Open())
                        using (Stream o = n.Open())
                        {
                            if (e.FullName != entry) { i.CopyTo(o); continue; }
                            string xml = new StreamReader(i, Encoding.UTF8).ReadToEnd();
                            byte[] bytes = new UTF8Encoding(false).GetBytes(change(xml));
                            o.Write(bytes, 0, bytes.Length);
                        }
                    }
                return ms.ToArray();
            }
        }

        // ------------------------------------------------------------------------------------------------ fix round: hash boundaries and the formula scan

        private static string OldStyleBomText(EngParsedPackage p) => string.Concat(p.Bom.Select(b =>
            "BOM|" + b.Level + "|" + b.ParentCode + "|" + b.PartCode + "|" + b.Description + "|" + EngText.Num(b.Qty) + "|" + b.Unit + "|" + (int)b.MakeBuy + "|" + b.Material + "|" + b.DrawingNumber + "|" + b.DrawingRevision + "|" + b.Note + "\n"));

        [TestMethod]
        public void A_Pipe_Or_A_Line_Break_Inside_A_Text_Cannot_Move_A_Boundary_Of_The_Hash()
        {
            EngBomRow Row(int level, string parent, string part, string note) => new EngBomRow { Level = level, ParentCode = parent, PartCode = part, Description = "d", Qty = 1, Unit = "u", MakeBuy = EngMakeBuy.Make, Note = note };
            var two = new EngParsedPackage();
            two.Bom.Add(Row(1, null, "A", "n"));
            two.Bom.Add(Row(2, "A", "B", null));
            string secondRow = OldStyleBomText(new EngParsedPackage { Bom = { Row(2, "A", "B", null) } }).TrimEnd('\n');
            var one = new EngParsedPackage();
            one.Bom.Add(Row(1, null, "A", "n\n" + secondRow));
            Assert.AreEqual(OldStyleBomText(two), OldStyleBomText(one), "with the old format the two packages wrote the same text (that was the collision)");
            Assert.AreNotEqual(EngPackageHash.Of(two, null), EngPackageHash.Of(one, null));
        }

        private static byte[] ManySheets(Action<EngTables> change = null)
        {
            EngTables t = EngPackageWriter.SampleTables();
            change?.Invoke(t);
            XlsxSheet S(string name, string[] head, IEnumerable<object[]> rows)
            {
                var sheet = new XlsxSheet { Name = name };
                sheet.Header.AddRange(head);
                sheet.Rows.AddRange(rows);
                return sheet;
            }
            var sheets = new List<XlsxSheet>
            {
                S(EngPackageParser.SheetInfo, new[] { "مورد", "مقدار", "توضیح" }, t.Info), S(EngPackageParser.SheetBom, t.BomHead, t.Bom), S(EngPackageParser.SheetOpc, t.OpcHead, t.Opc), S(EngPackageParser.SheetDocs, t.DocHead, t.Docs),
            };
            for (int i = 1; i <= 8; i++) sheets.Add(S("X" + i, new[] { "a" }, new[] { new object[] { "v" } }));
            return SimpleXlsx.Build(sheets);
        }

        private static byte[] WithFormulaInPart(byte[] workbook, string part)
        {
            using var input = new ZipArchive(new MemoryStream(workbook), ZipArchiveMode.Read);
            var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
                foreach (ZipArchiveEntry e in input.Entries)
                {
                    byte[] data;
                    using (Stream s = e.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); data = ms.ToArray(); }
                    if (e.FullName == part) data = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(data).Replace("</sheetData>", "<row r=\"900\"><c r=\"A900\"><f>1+1</f></c></row></sheetData>"));
                    ZipArchiveEntry n = zip.CreateEntry(e.FullName);
                    using (Stream s = n.Open()) s.Write(data, 0, data.Length);
                }
            return output.ToArray();
        }

        [TestMethod]
        public void The_Formula_Scan_Maps_Parts_To_Sheets_Through_The_Relationships_Reads_Only_The_Five_Known_Sheets_And_Copes_With_Twelve_Sheets()
        {
            var files = EngPackageWriter.Sample().Files.ToDictionary(f => f.Key, f => new EngFileInfo { Name = f.Key, Size = f.Value.Length, Sha256 = EngText.Sha256Hex(f.Value) });
            byte[] clean = ManySheets();
            Assert.AreEqual(0, EngPackageParser.Parse(clean, "ENG-PACKAGE.xlsx", files).Findings.Count(f => f.IsError && f.Message.Contains("فرمول")), "no formula, no finding");
            // a formula without a stored value on the BOM sheet (part sheet2.xml) is reported on «BOM», once
            List<EngFinding> bom = EngPackageParser.Parse(WithFormulaInPart(clean, "xl/worksheets/sheet2.xml"), "ENG-PACKAGE.xlsx", files).Findings.Where(f => f.Message.Contains("فرمول")).ToList();
            Assert.AreEqual(1, bom.Count);
            Assert.AreEqual("BOM", bom[0].Sheet);
            Assert.AreEqual(900, bom[0].Row);
            // the same on the last of twelve sheets (sheet12.xml sorts before sheet2.xml in a file listing) is not a sheet of the package: never scanned, never reported
            Assert.AreEqual(0, EngPackageParser.Parse(WithFormulaInPart(clean, "xl/worksheets/sheet12.xml"), "ENG-PACKAGE.xlsx", files).Findings.Count(f => f.Message.Contains("فرمول")));
            // and one on the Info sheet is reported on its own sheet, not on whichever sheet came first in the zip
            List<EngFinding> info = EngPackageParser.Parse(WithFormulaInPart(clean, "xl/worksheets/sheet1.xml"), "ENG-PACKAGE.xlsx", files).Findings.Where(f => f.Message.Contains("فرمول")).ToList();
            Assert.AreEqual(1, info.Count);
            Assert.AreEqual("مشخصات", info[0].Sheet);
        }

        [TestMethod]
        public void A_Text_Longer_Than_Its_Column_Is_A_Finding_With_The_Sheet_And_Row_For_Every_Sheet()
        {
            EngTables t = EngPackageWriter.SampleTables();
            t.Bom[3][3] = new string('d', 301);
            t.Opc[0][2] = new string('o', 301);
            t.Docs[0][1] = new string('t', 301);
            EngParsedPackage p = EngPackageParser.Parse(EngPackageWriter.Build(t), "ENG-PACKAGE.xlsx", EngPackageWriter.FactsOf(EngPackageWriter.Sample()));
            var errors = p.Findings.Where(f => f.IsError).Select(f => f.Sheet + "|" + f.Row + "|" + f.Message).ToList();
            CollectionAssert.AreEquivalent(new[]
            {
                "BOM|5|«شرح» حداکثر ۳۰۰ نویسه است.",
                "OPC|2|«شرح عملیات» حداکثر ۳۰۰ نویسه است.",
                "مدارک|2|«عنوان» بیش از 300 نویسه است (301 نویسه)؛ کوتاه‌تر بنویسید.",
            }, errors, string.Join(" || ", errors));
        }
    }
}
