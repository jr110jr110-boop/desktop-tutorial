using System.Collections.Generic;
using System.Linq;
using Entities.Model.NasbNiro.Quality;

namespace Services.NasbNiro.Quality
{
    /// <summary>
    /// Seed of the ITP templates. The steam turbine template is Appendix A of the quality specification (WI-QC-004-01);
    /// the pump, condenser and lube oil templates are minimal ★ provisional plans from the codes named in the brief.
    /// Limits that are not known are left empty on purpose and marked "to be set by engineering".
    /// </summary>
    internal static class ItpSeedData
    {
        public const string SteamCode = "ITP-STEAM-TURBINE";
        public const string PumpCode = "ITP-PUMP";
        public const string CondenserCode = "ITP-CONDENSER";
        public const string LubeCode = "ITP-LUBE-OIL";

        private const ItpPoint H = ItpPoint.H;
        private const ItpPoint W = ItpPoint.W;
        private const ItpPoint R = ItpPoint.R;
        private const ItpPoint RA = ItpPoint.RA;

        public static List<ItpTemplate> Build() => new List<ItpTemplate> { Steam(), Pump(), Condenser(), Lube() };

        private static ItpTemplateActivity A(int seq, string itemNo, string stage, string activity, QaTestType type, string acceptance,
            ItpPoint v, ItpPoint tpi, ItpPoint c, string doc = null, TestReportType? report = null, string remark = null,
            bool gates = true, params ItpTemplateParameter[] parameters) =>
            new ItpTemplateActivity
            {
                Seq = seq, ItemNo = itemNo, Stage = stage, Activity = activity, TestType = type, AcceptanceReference = acceptance,
                PointVendor = v, PointTpi = tpi, PointClient = c, DocumentProduced = doc, RequiredReportType = report, Remark = remark,
                GatesRelease = gates, Parameters = parameters.ToList(),
            };

        private static ItpTemplateParameter Prm(string name, string unit, decimal? min, decimal? max, string source, bool requires = false) =>
            new ItpTemplateParameter { Name = name, Unit = unit, Min = min, Max = max, Source = source, RequiresLimits = requires };

        /// <summary>The mandatory rows of the standard parameter list of a report type, as ITP parameters.</summary>
        private static ItpTemplateParameter[] Std(TestReportType type) =>
            QualityStandards.For(type).Where(p => p.Mandatory)
                .Select(p => Prm(p.Name, p.Unit, p.Min, p.Max, p.Source, p.RequiresLimits)).ToArray();

        private static ItpTemplateParameter[] OpenRows(string source, params string[] names) =>
            names.Select(n => Prm(n, null, null, null, source + " — " + QualityStandards.EngineeringToSet, true)).ToArray();

        private static ItpTemplate Steam()
        {
            const string comp = "Component tests (Table 1)";
            const string major = "Major Part";
            const string proc = "approved procedure; drawing";
            var items = new List<ItpTemplateActivity>
            {
                // Table 1: component tests, all "Major Part"
                A(1, "T1-1", comp, "Blade — MT", QaTestType.MT, proc, H, W, W, "MT report", TestReportType.MT, major, true, Std(TestReportType.MT)),
                A(2, "T1-2", comp, "Blade — visual", QaTestType.Visual, proc, H, W, W, null, null, major),
                A(3, "T1-3", comp, "Disk — UT", QaTestType.UT, proc, H, W, W, "UT report", TestReportType.UT, major),
                A(4, "T1-4", comp, "Disk — MT on machined disk", QaTestType.MT, proc, H, W, W, "MT report", TestReportType.MT, major, true, Std(TestReportType.MT)),
                A(5, "T1-5", comp, "Disk — visual", QaTestType.Visual, proc, H, W, W, null, null, major),
                A(6, "T1-6", comp, "Shaft — UT (if repair occurred; otherwise mark not applicable with the reason)", QaTestType.UT, proc, H, W, W, "UT report", TestReportType.UT, major),
                A(7, "T1-7", comp, "Shaft — MT", QaTestType.MT, proc, H, W, W, "MT report", TestReportType.MT, major, true, Std(TestReportType.MT)),
                A(8, "T1-8", comp, "Shaft — visual", QaTestType.Visual, proc, H, W, W, null, null, major),
                A(9, "T1-9", comp, "Casing — visual check of rough cast", QaTestType.Visual, "MSS SP-55; approved procedure", H, W, W, null, null, major),
                A(10, "T1-10", comp, "Casing — MT", QaTestType.MT, "MSS SP-55; approved procedure", H, W, W, "MT report", TestReportType.MT, major, true, Std(TestReportType.MT)),
                A(11, "T1-11", comp, "Casing — UT (if repair occurred; otherwise mark not applicable with the reason)", QaTestType.UT, "MSS SP-55; approved procedure", H, W, W, "UT report", TestReportType.UT, major),
                A(12, "T1-12", comp, "Valve — MT", QaTestType.MT, "approved procedure; MSS SP-55", H, W, W, "MT report", TestReportType.MT, major, true, Std(TestReportType.MT)),
                A(13, "T1-13", comp, "Valve — visual", QaTestType.Visual, "approved procedure; MSS SP-55", H, R, R, null, null, major),
                A(14, "T1-14", comp, "Valve — UT (if repair occurred; otherwise mark not applicable with the reason)", QaTestType.UT, "approved procedure; MSS SP-55", H, W, W, "UT report", TestReportType.UT, major),

                // Table 2: stage activities
                A(15, "1-1", "1 Material inspection", "Material certificate (chemical composition and mechanical test: hardness, tensile)", QaTestType.DocumentReview, "ASME Sec II, ASTM", H, RA, RA, "Certificate EN 10204"),
                A(16, "1-2", "1 Material inspection", "Casting material", QaTestType.DocumentReview, "ASME Sec II, ASTM", H, W, RA),
                A(17, "2-1", "2 Inspection and test before fabrication", "Sandblasting visual inspection", QaTestType.Visual, "ISO 8501-02 and approved procedure", H, W, RA),
                A(18, "2-2", "2 Inspection and test before fabrication", "Painting: primer and top layers (thickness, adherence, RAL inspection)", QaTestType.Painting,
                    "SSPC / IPS-E-TP-100 / NIOEC SP-80-02 / ASTM D3359, D7091, E797", H, H, W, "Painting report", TestReportType.Painting),
                A(19, "3-1", "3 NDT", "MT", QaTestType.MT, "ASTM E 709, ASME Sec V Art.7", H, W, RA, "MT report", TestReportType.MT, null, true, Std(TestReportType.MT)),
                A(20, "3-2", "3 NDT", "UT", QaTestType.UT, "ASME Sec V Art.5", H, W, RA, "UT report", TestReportType.UT),
                A(21, "4-1", "4 Inspection and test after fabrication", "Repair (if any)", QaTestType.Other, "ASME Sec VIII D1 and Sec IX", H, W, W),
                A(22, "4-2", "4 Inspection and test after fabrication", "Cold clearance", QaTestType.Dimensional, "cold clearance table", H, RA, RA, "Cold clearance record", TestReportType.Dimensional),
                A(23, "4-3", "4 Inspection and test after fabrication", "Rotor run-out check", QaTestType.Dimensional, "API 611", H, H, H, "Run-out record", TestReportType.Dimensional),
                A(24, "4-4", "4 Inspection and test after fabrication", "Rotor balancing", QaTestType.Balancing,
                    "ISO 21940-11 grade G1.0 (the plan prints ISO 1940-1, withdrawn and replaced by ISO 21940-11 with the same grades)", H, H, H, "Balancing report", TestReportType.Balancing, null, true,
                    Prm("Balance quality grade achieved (G)", "mm/s", null, 1.0m, "WI-QC-004 4-4 grade 1.0; ISO 21940-11")),
                A(25, "4-5", "4 Inspection and test after fabrication", "Outline dimensional check", QaTestType.Dimensional, "outline drawing", H, H, R, "Dimensional report", TestReportType.Dimensional),
                A(26, "5-1", "5 Inspection and test", "Hydrostatic test", QaTestType.Hydro, "API 611, approved procedure", H, H, W, "Hydrostatic test report", TestReportType.Hydro, null, true,
                    OpenRows("API 611 6.3 / approved procedure", "Test pressure", "Hold time")),
                A(27, "5-2", "5 Inspection and test", "Instrument check (site)", QaTestType.Other, "NEMA SM23", H, H, W),
                A(28, "5-3", "5 Inspection and test", "Mechanical running test (site), no load", QaTestType.RunTest, "API 611", H, H, W, "Run test report", TestReportType.RunTest, null, true,
                    Std(TestReportType.RunTest).Concat(OpenRows("WI-QC-006 / API 611 6.3.3", "Vibration at each speed", "Bearing temperature")).ToArray()),
                A(29, "5-4", "5 Inspection and test", "Vibration and temperature check (site) — recorded in the run test report of 5-3", QaTestType.RunTest, "API 611 and NEMA", H, H, W),
                A(30, "5-5", "5 Inspection and test", "Appearance and dimensional inspection", QaTestType.Dimensional, "INNCO outline drawings", H, H, W, "Dimensional report", TestReportType.Dimensional),
                A(31, "5-6", "5 Inspection and test", "Performance test (in the site, hot)", QaTestType.Performance, "approved procedure", H, H, W, "Performance test report", TestReportType.Performance),
                A(32, "6-1", "6 Packing and delivery", "Final inspection", QaTestType.Visual, "General Arrangement drawing", H, H, W),
                A(33, "6-2", "6 Packing and delivery", "Packing and shipping", QaTestType.Packing, "approved procedure", H, H, W),
                A(34, "6-3", "6 Packing and delivery", "Commissioning and 2-year spare part check", QaTestType.DocumentReview, "spare part list", H, H, W, "Packing list"),
                A(35, "6-4", "6 Packing and delivery", "Final book", QaTestType.DocumentReview, "final book", H, RA, R, null, null, null, false),
                A(36, "6-5", "6 Packing and delivery", "Release note", QaTestType.DocumentReview, null, R, H, R, null, null, null, false),
                A(37, "6-6", "6 Packing and delivery", "Certificate of conformity to MR and SPEC", QaTestType.DocumentReview, null, H, R, R, null, null, null, false),
            };
            return new ItpTemplate
            {
                Code = SteamCode, Title = "Steam turbine — WI-QC-004", ProductType = ItpProductType.SteamTurbine, Revision = "01",
                ApplicableCodes = "API 611; API 612; ASME II, V, VIII-1, IX; MSS SP-55; NEMA SM23; ISO 21940-11",
                IsActive = true, Activities = items,
            };
        }

        private static ItpTemplate Pump()
        {
            const string code = "API 610 8.3";
            var items = new List<ItpTemplateActivity>
            {
                A(1, "1", "Material", "Material certificates of pressure-containing parts", QaTestType.DocumentReview, "EN 10204 3.1", H, RA, RA, "Certificate EN 10204"),
                A(2, "2", "Fabrication", "Casing and impeller dimensional check", QaTestType.Dimensional, "approved drawing", H, W, R, "Dimensional report", TestReportType.Dimensional),
                A(3, "3", "Test", "Hydrostatic test: 1.5 × MAWP, hold at least 30 min", QaTestType.Hydro, code + " (hydrostatic test)", H, H, W, "Hydrostatic test report", TestReportType.Hydro, null, true,
                    Prm("Test pressure", null, null, null, "API 610 8.3: 1.5 × MAWP — MAWP " + QualityStandards.EngineeringToSet, true),
                    Prm("Hold time", "min", 30, null, "API 610 8.3: at least 30 min")),
                A(4, "4", "Test", "Mechanical running test", QaTestType.RunTest, code + ".4", H, W, W, "Run test report", TestReportType.RunTest, null, true,
                    OpenRows("API 610 8.3.4", "Vibration", "Bearing temperature")),
                A(5, "5", "Test", "Performance and NPSH test", QaTestType.Performance, code, H, W, W, "Performance test report", TestReportType.Performance),
                A(6, "6", "Delivery", "Final inspection, packing and release", QaTestType.Packing, "approved procedure", H, R, R, null, null, null, false),
            };
            return new ItpTemplate
            {
                Code = PumpCode, Title = "Pump — API 610 (minimal ★)", ProductType = ItpProductType.Pump, Revision = "01",
                ApplicableCodes = "API 610", IsActive = true, Activities = items,
            };
        }

        private static ItpTemplate Condenser()
        {
            var items = new List<ItpTemplateActivity>
            {
                A(1, "1", "Material", "Material certificates of pressure parts", QaTestType.DocumentReview, "EN 10204 3.1", H, RA, RA, "Certificate EN 10204"),
                A(2, "2", "Welding", "Visual examination of welds", QaTestType.VisualWeld, "ASME VIII-1 UW-35", H, W, R),
                A(3, "3", "NDT", "RT of butt welds", QaTestType.RT, "ASME VIII-1 UW-51", H, W, W, "RT report", TestReportType.RT),
                A(4, "4", "NDT", "UT of butt welds (where RT is not used)", QaTestType.UT, "ASME VIII-1 UW-52", H, W, W, "UT report", TestReportType.UT),
                A(5, "5", "Fabrication", "Dimensional check", QaTestType.Dimensional, "approved drawing", H, R, R, "Dimensional report", TestReportType.Dimensional),
                A(6, "6", "Test", "Hydrostatic test: 1.3 × MAWP × stress ratio", QaTestType.Hydro, "ASME VIII-1 UG-99", H, H, W, "Hydrostatic test report", TestReportType.Hydro, null, true,
                    Prm("Test pressure", null, null, null, "ASME VIII-1 UG-99: 1.3 × MAWP × stress ratio — MAWP " + QualityStandards.EngineeringToSet, true),
                    Prm("Hold time", "min", null, null, "ASME VIII-1 UG-99 — " + QualityStandards.EngineeringToSet, true)),
                A(7, "7", "Delivery", "Final inspection, packing and release", QaTestType.Packing, "approved procedure", H, R, R, null, null, null, false),
            };
            return new ItpTemplate
            {
                Code = CondenserCode, Title = "Condenser — ASME VIII-1 (minimal ★)", ProductType = ItpProductType.Condenser, Revision = "01",
                ApplicableCodes = "ASME VIII-1; TEMA", IsActive = true, Activities = items,
            };
        }

        private static ItpTemplate Lube()
        {
            var items = new List<ItpTemplateActivity>
            {
                A(1, "1", "Material", "Material certificates of piping and vessels", QaTestType.DocumentReview, "EN 10204 3.1", H, RA, RA, "Certificate EN 10204"),
                A(2, "2", "Test", "Hydrostatic test of piping and vessels", QaTestType.Hydro, "API 614", H, H, W, "Hydrostatic test report", TestReportType.Hydro, null, true,
                    OpenRows("API 614", "Test pressure", "Hold time")),
                A(3, "3", "Test", "Cleanliness and flushing", QaTestType.Other, "API 614", H, W, W, "Flushing record", TestReportType.Other, null, true,
                    OpenRows("API 614", "Oil cleanliness")),
                A(4, "4", "Test", "Run test", QaTestType.RunTest, "API 614", H, W, W, "Run test report", TestReportType.RunTest, null, true,
                    OpenRows("API 614", "Vibration", "Bearing temperature")),
                A(5, "5", "Delivery", "Final inspection, packing and release", QaTestType.Packing, "approved procedure", H, R, R, null, null, null, false),
            };
            return new ItpTemplate
            {
                Code = LubeCode, Title = "Lube oil system — API 614 (minimal ★)", ProductType = ItpProductType.LubeOilSystem, Revision = "01",
                ApplicableCodes = "API 614", IsActive = true, Activities = items,
            };
        }
    }
}
