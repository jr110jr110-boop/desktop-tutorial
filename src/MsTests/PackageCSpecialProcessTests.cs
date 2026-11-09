using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Integrity;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Providers;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Integrity;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.QualityDtos;

namespace MsTests
{
    /// <summary>Package C: special processes (types, qualification, run, finish gate, supplier approval), welding ranges, material specifications and the structured EN 10204 certificate, and the registrations.</summary>
    [TestClass]
    public class PackageCSpecialProcessTests
    {
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1, "seed", "PMO");
        private static readonly ClaimsPrincipal Maker = AppHost.Principal(51, "eng.maker", "ENGINEER");
        private static readonly ClaimsPrincipal Qa = AppHost.Principal(52, "qa.one", "QA");
        private static readonly ClaimsPrincipal Qa2 = AppHost.Principal(53, "qa.two", "QA");
        private static readonly ClaimsPrincipal Qc = AppHost.Principal(54, "qc.one", "QC");
        private static readonly ClaimsPrincipal Qc2 = AppHost.Principal(55, "qc.two", "QC");
        private static readonly ClaimsPrincipal Commercial = AppHost.Principal(56, "commercial.one", "COMMERCIAL");
        private static readonly ClaimsPrincipal ProdManager = AppHost.Principal(57, "prod.manager", "PRODUCTIONMANAGER");
        private static readonly ClaimsPrincipal Qc3 = AppHost.Principal(58, "qc.three", "QC");

        private static AppHost NewHost(string name)
        {
            var host = new AppHost("PKC_" + name + "_" + Guid.NewGuid().ToString("N"));
            host.Write(u =>
            {
                foreach (int id in new[] { 51, 52, 53, 54, 55, 56, 57, 58 }) AppHost.Add(u, new User { Id = id, UserName = "pkc.user" + id, Email = "pkc.user" + id + "@example.com", IsActive = true });
                u.SaveChanges();
            }, Seeder);
            return host;
        }

        private static Task<ResponseDto<T>> Sp<T>(AppHost host, ClaimsPrincipal who, Func<ISpecialProcessService, Task<ResponseDto<T>>> call) => host.As(who, p => call(p.GetRequiredService<ISpecialProcessService>()));
        private static Task<ResponseDto<T>> Ms<T>(AppHost host, ClaimsPrincipal who, Func<IMaterialSpecService, Task<ResponseDto<T>>> call) => host.As(who, p => call(p.GetRequiredService<IMaterialSpecService>()));

        // ------------------------------------------------------------------------------------------------ pure rules

        [TestMethod]
        public void TheJudgeComparesEveryValueWithItsLimitsAndEdgesPass()
        {
            var limits = new List<JudgeLimit>
            {
                new JudgeLimit { Name = "Temp", Unit = "C", Min = 840m, Max = 860m },
                new JudgeLimit { Name = "Time", Unit = "min", Min = 50m },
                new JudgeLimit { Name = "Note", Max = 5m, Required = false },
            };
            Judgement ok = SpecialProcessRules.Judge(limits, new[] { new JudgeValue { Name = "temp", Value = 840m }, new JudgeValue { Name = "TIME", Value = 999m } });
            Assert.IsTrue(ok.Pass, "the edge of a limit passes, a missing optional row is fine, names are matched without case");
            Judgement high = SpecialProcessRules.Judge(limits, new[] { new JudgeValue { Name = "Temp", Value = 860.01m }, new JudgeValue { Name = "Time", Value = 50m } });
            Assert.IsFalse(high.Pass);
            Assert.AreEqual(1, high.Failed.Count);
            Judgement missing = SpecialProcessRules.Judge(limits, new[] { new JudgeValue { Name = "Temp", Value = 850m } });
            Assert.AreEqual("Time", missing.Missing.Single());
            Judgement unknown = SpecialProcessRules.Judge(limits, new[] { new JudgeValue { Name = "Temp", Value = 850m }, new JudgeValue { Name = "Time", Value = 60m }, new JudgeValue { Name = "Other", Value = 1m } });
            Assert.AreEqual("Other", unknown.Unknown.Single());
        }

        [TestMethod]
        public void AHeatNumberIsComparedWithZeroToleranceExceptCaseAndSpaces()
        {
            Assert.AreEqual(SpecialProcessRules.HeatKey(" h-0012 "), SpecialProcessRules.HeatKey("H-0012"));
            Assert.AreEqual(SpecialProcessRules.HeatKey("H 0012"), SpecialProcessRules.HeatKey("H0012"));
            Assert.AreNotEqual(SpecialProcessRules.HeatKey("H-0012"), SpecialProcessRules.HeatKey("H-012"), "a missing zero is another heat");
            Assert.AreNotEqual(SpecialProcessRules.HeatKey("H-0012"), SpecialProcessRules.HeatKey("H0012"), "a hyphen is significant");
            Assert.AreEqual(string.Empty, SpecialProcessRules.HeatKey(null));
        }

        [TestMethod]
        public void TheSpecKeyIgnoresPunctuationAndCase()
        {
            Assert.AreEqual(SpecialProcessRules.SpecKey("S355J2", "EN 10025-2"), SpecialProcessRules.SpecKey("s355j2", "en10025 2"));
            Assert.AreNotEqual(SpecialProcessRules.SpecKey("S355J2", "EN 10025-2"), SpecialProcessRules.SpecKey("S355J0", "EN 10025-2"));
        }

        [TestMethod]
        public void ValidityAndDaysLeftCountWholeDays()
        {
            DateTime d = new DateTime(2026, 3, 10);
            Assert.AreEqual(30, SpecialProcessRules.DaysLeft(d.AddDays(30), d));
            Assert.AreEqual(-1, SpecialProcessRules.DaysLeft(d.AddDays(-1), d));
            Assert.IsTrue(SpecialProcessRules.Covers(d, d.AddDays(5), d.AddDays(5).AddHours(20)));
            Assert.IsFalse(SpecialProcessRules.Covers(d, d.AddDays(5), d.AddDays(6)));
            Assert.IsFalse(SpecialProcessRules.Covers(d, d.AddDays(5), d.AddDays(-1)));
        }

        [TestMethod]
        public void AFurnaceSurveyMustExistPredateTheRunAndNotBeOverdue()
        {
            DateTime day = new DateTime(2026, 5, 1);
            Assert.IsNotNull(SpecialProcessRules.SurveyProblem(new SpQualEquipment { SurveyKind = SpSurveyKind.None }, day, "F1"), "no survey");
            Assert.IsNotNull(SpecialProcessRules.SurveyProblem(new SpQualEquipment { SurveyKind = SpSurveyKind.Tus, SurveyDate = day.AddDays(1), SurveyDueDate = day.AddDays(100) }, day, "F1"), "survey after the run");
            Assert.IsNotNull(SpecialProcessRules.SurveyProblem(new SpQualEquipment { SurveyKind = SpSurveyKind.Sat, SurveyDate = day.AddDays(-100), SurveyDueDate = day.AddDays(-1) }, day, "F1"), "overdue");
            Assert.IsNull(SpecialProcessRules.SurveyProblem(new SpQualEquipment { SurveyKind = SpSurveyKind.Tus, SurveyDate = day.AddDays(-10), SurveyDueDate = day }, day, "F1"), "due today still counts");
        }

        [TestMethod]
        public void TheReviewerIsNeitherTheRecorderNorTheOperator()
        {
            Assert.IsFalse(SpecialProcessRules.ReviewerAllowed(5, 5, null));
            Assert.IsFalse(SpecialProcessRules.ReviewerAllowed(5, 6, 5));
            Assert.IsTrue(SpecialProcessRules.ReviewerAllowed(5, 6, 7));
            Assert.IsTrue(SpecialProcessRules.ReviewerAllowed(5, 6, null));
        }

        private static Qualification Welder(string pnos = "1,8", string groups = "1,2", string fnos = "4", string positions = "1G,2G,3G", decimal? tmin = 3m, decimal? tmax = 20m, decimal? dmin = 25m, decimal? dmax = null) =>
            new Qualification { RangePNos = pnos, RangeGroupNos = groups, RangeFNos = fnos, RangePositions = positions, RangeThicknessMin = tmin, RangeThicknessMax = tmax, RangeDiameterMin = dmin, RangeDiameterMax = dmax };

        [TestMethod]
        public void AWelderInsideTheWpsRangeHasNoMismatch()
        {
            var wps = new Wps { BasePNo = "1", BaseGroupNo = "1", FillerFNo = "4", Positions = "1G,2G", ThicknessMin = 5m, ThicknessMax = 12m, DiameterMin = 50m, DiameterMax = 300m };
            Assert.AreEqual(0, WeldRangeRules.Mismatches(Welder(), wps).Count);
        }

        [TestMethod]
        public void EveryRangeOfTheWelderIsComparedWithTheWps()
        {
            Assert.AreEqual(1, WeldRangeRules.Mismatches(Welder(), new Wps { BasePNo = "3" }).Count, "P-No");
            Assert.AreEqual(1, WeldRangeRules.Mismatches(Welder(), new Wps { BasePNo = "1,3" }).Count, "one P-No of two outside");
            Assert.AreEqual(1, WeldRangeRules.Mismatches(Welder(), new Wps { BaseGroupNo = "3" }).Count, "group");
            Assert.AreEqual(1, WeldRangeRules.Mismatches(Welder(), new Wps { FillerFNo = "6" }).Count, "F-No");
            Assert.AreEqual(1, WeldRangeRules.Mismatches(Welder(), new Wps { Positions = "6G" }).Count, "position");
            Assert.AreEqual(1, WeldRangeRules.Mismatches(Welder(), new Wps { ThicknessMin = 2m, ThicknessMax = 10m }).Count, "thickness below the welder minimum");
            Assert.AreEqual(1, WeldRangeRules.Mismatches(Welder(), new Wps { ThicknessMin = 5m, ThicknessMax = 25m }).Count, "thickness above the welder maximum");
            Assert.AreEqual(1, WeldRangeRules.Mismatches(Welder(), new Wps { DiameterMin = 10m, DiameterMax = 100m }).Count, "diameter below the welder minimum");
            var many = WeldRangeRules.Mismatches(Welder(), new Wps { BasePNo = "3", FillerFNo = "6", Positions = "6G" });
            Assert.AreEqual(3, many.Count, "all mismatches are listed, not only the first");
            string text = WeldRangeRules.Message("Welder A", "SMAW", new Wps { Number = "WPS-1", Revision = "0" }, many);
            Assert.IsTrue(text.Contains("WPS-1") && text.Contains("P-No") && text.Contains("F-No"));
        }

        [TestMethod]
        public void AWpsRangeTheWelderCertificateDoesNotStateIsAMismatch()
        {
            var noRange = new Qualification();
            Assert.AreEqual(1, WeldRangeRules.Mismatches(noRange, new Wps { BasePNo = "1" }).Count, "the welder has no P-No on record");
            Assert.AreEqual(1, WeldRangeRules.Mismatches(noRange, new Wps { ThicknessMin = 5m, ThicknessMax = 10m }).Count);
            Assert.AreEqual(0, WeldRangeRules.Mismatches(noRange, new Wps()).Count, "a WPS that states nothing asks nothing");
        }

        private static MaterialSpec Spec(CertificateType type = CertificateType.T31)
        {
            var spec = new MaterialSpec { Grade = "S355J2", Standard = "EN 10025-2", SpecKey = SpecialProcessRules.SpecKey("S355J2", "EN 10025-2"), RequiredCertificateType = type, Version = 1, Status = MatSpecStatus.Approved };
            spec.Limits.Add(new MaterialSpecLimit { SortOrder = 0, Kind = CertValueKind.Chemistry, Name = "C", Unit = "%", Max = 0.20m });
            spec.Limits.Add(new MaterialSpecLimit { SortOrder = 1, Kind = CertValueKind.Chemistry, Name = "Mn", Unit = "%", Max = 1.60m, Required = false });
            spec.Limits.Add(new MaterialSpecLimit { SortOrder = 2, Kind = CertValueKind.Mechanical, Name = "Yield", Unit = "MPa", Min = 355m });
            return spec;
        }

        private static List<JudgeValue> Good() => new List<JudgeValue> { new JudgeValue { Kind = CertValueKind.Chemistry, Name = "C", Value = 0.18m }, new JudgeValue { Kind = CertValueKind.Mechanical, Name = "Yield", Value = 380m } };

        [TestMethod]
        public void ACertificateWithTheHeatOfTheLotAndValuesInsideTheLimitsPasses()
        {
            CertificateEvaluation ev = CertificateRules.Evaluate(CertificateType.T31, "H-77", "h-77", "S355J2", "EN 10025-2", Spec(), Good(), null, null, null, "A. Signatory");
            Assert.AreEqual(CertRowStatus.Pass, ev.Status, ev.Text);
            Assert.AreEqual(0, ev.Problems.Count);
        }

        [TestMethod]
        public void ACertificateOfAnotherHeatIsRefused()
        {
            CertificateEvaluation ev = CertificateRules.Evaluate(CertificateType.T31, "H-77", "H-78", "S355J2", "EN 10025-2", Spec(), Good(), null, null, null, "A. Signatory");
            Assert.AreEqual(CertRowStatus.Fail, ev.Status);
            Assert.IsTrue(ev.Text.Contains("H-77") && ev.Text.Contains("H-78"));
            Assert.AreEqual(CertRowStatus.Fail, CertificateRules.Evaluate(CertificateType.T31, "H-77", null, "S355J2", "EN 10025-2", Spec(), Good(), null, null, null, "A").Status, "a lot without a heat number cannot be matched");
            Assert.AreEqual(CertRowStatus.Fail, CertificateRules.Evaluate(CertificateType.T31, null, "H-77", "S355J2", "EN 10025-2", Spec(), Good(), null, null, null, "A").Status, "a certificate without a heat number");
        }

        [TestMethod]
        public void AValueOutsideTheLimitOrAMissingRequiredValueFailsTheCertificate()
        {
            var high = new List<JudgeValue> { new JudgeValue { Kind = CertValueKind.Chemistry, Name = "C", Value = 0.21m }, new JudgeValue { Kind = CertValueKind.Mechanical, Name = "Yield", Value = 380m } };
            CertificateEvaluation ev = CertificateRules.Evaluate(CertificateType.T31, "H-1", "H-1", "S355J2", "EN 10025-2", Spec(), high, null, null, null, "A");
            Assert.AreEqual(CertRowStatus.Fail, ev.Status);
            Assert.AreEqual(1, ev.Judgement.Failed.Count);
            var noYield = new List<JudgeValue> { new JudgeValue { Kind = CertValueKind.Chemistry, Name = "C", Value = 0.1m } };
            CertificateEvaluation miss = CertificateRules.Evaluate(CertificateType.T31, "H-1", "H-1", "S355J2", "EN 10025-2", Spec(), noYield, null, null, null, "A");
            Assert.AreEqual(CertRowStatus.Fail, miss.Status);
            Assert.AreEqual("Yield", miss.Judgement.Missing.Single());
            var edge = new List<JudgeValue> { new JudgeValue { Kind = CertValueKind.Chemistry, Name = "C", Value = 0.20m }, new JudgeValue { Kind = CertValueKind.Mechanical, Name = "Yield", Value = 355m } };
            Assert.AreEqual(CertRowStatus.Pass, CertificateRules.Evaluate(CertificateType.T31, "H-1", "H-1", "S355J2", "EN 10025-2", Spec(), edge, null, null, null, "A").Status, "the edges of the limits pass");
        }

        [TestMethod]
        public void AType32NeedsTheInspectorsOrganisationAndIdentityAndAType31TheSignatory()
        {
            CertificateEvaluation noOrg = CertificateRules.Evaluate(CertificateType.T32, "H-1", "H-1", "S355J2", "EN 10025-2", Spec(CertificateType.T32), Good(), null, "I. Inspector", null, null);
            Assert.AreEqual(CertRowStatus.Fail, noOrg.Status);
            Assert.IsTrue(noOrg.Text.Contains("سازمان بازرس"));
            CertificateEvaluation noId = CertificateRules.Evaluate(CertificateType.T32, "H-1", "H-1", "S355J2", "EN 10025-2", Spec(CertificateType.T32), Good(), "Body", null, null, null);
            Assert.AreEqual(CertRowStatus.Fail, noId.Status);
            Assert.AreEqual(CertRowStatus.Pass, CertificateRules.Evaluate(CertificateType.T32, "H-1", "H-1", "S355J2", "EN 10025-2", Spec(CertificateType.T32), Good(), "Body", null, "STAMP-9", null).Status, "a stamp number identifies the inspector");
            CertificateEvaluation noSig = CertificateRules.Evaluate(CertificateType.T31, "H-1", "H-1", "S355J2", "EN 10025-2", Spec(), Good(), null, null, null, null);
            Assert.AreEqual(CertRowStatus.Fail, noSig.Status);
            Assert.IsTrue(noSig.Text.Contains("امضاکننده"));
        }

        [TestMethod]
        public void AGradeWithoutAnApprovedSpecificationFailsAndA22CertificateIsNotJudged()
        {
            CertificateEvaluation none = CertificateRules.Evaluate(CertificateType.T31, "H-1", "H-1", "S355J2", "EN 10025-2", null, Good(), null, null, null, "A");
            Assert.AreEqual(CertRowStatus.Fail, none.Status);
            Assert.IsTrue(none.Text.Contains("مشخصات ماده"));
            CertificateEvaluation t22 = CertificateRules.Evaluate(CertificateType.T22, null, null, null, null, null, new List<JudgeValue>(), null, null, null, null);
            Assert.AreEqual(CertRowStatus.NotJudged, t22.Status);
            Assert.IsFalse(CertificateRules.NeedsValues(CertificateType.T22));
            Assert.IsTrue(CertificateRules.NeedsValues(CertificateType.T31) && CertificateRules.NeedsValues(CertificateType.T32));
        }

        // ------------------------------------------------------------------------------------------------ registrations

        [TestMethod]
        public void TheNewRecordTypesAreInTheAuditChainWithPersianLabels()
        {
            foreach (Type type in new[] { typeof(SpProcessType), typeof(SpParameter), typeof(SpQualification), typeof(SpQualLimit), typeof(SpQualEquipment), typeof(SpQualOperator), typeof(SpRun), typeof(SpRunValue),
                typeof(SpRunInstrument), typeof(SpSupplierApproval), typeof(MaterialSpec), typeof(MaterialSpecLimit), typeof(MaterialSpecItem), typeof(CertificateValue) })
            {
                string label = AuditEntityLabels.Of(type.Name);
                Assert.IsFalse(string.IsNullOrWhiteSpace(label), type.Name + " has no label");
                Assert.IsTrue(label.Any(c => c >= '؀' && c <= 'ۿ'), type.Name + " label is not Persian: " + label);
            }
        }

        [TestMethod]
        public void TheSpecialProcessAndCertificateSwitchesAreOnTheSafetyProfileWithStrictValues()
        {
            var expected = new Dictionary<string, string>
            {
                { QualitySettingKeys.PkcSpEnforce, "1" }, { QualitySettingKeys.PkcSpSubcontractEvidence, "1" }, { QualitySettingKeys.PkcWeldRange, "1" }, { QualitySettingKeys.PkcCertEnforce, "1" },
                { QualitySettingKeys.PkcQualExpiryAlertDays, "30" },
            };
            foreach (var kv in expected)
            {
                SafetySettingSpec spec = SafetyProfile.Find(SafetyStore.Quality, kv.Key);
                Assert.IsNotNull(spec, kv.Key + " is not on the safety profile");
                Assert.AreEqual(kv.Value, spec.Strict, kv.Key);
            }
            Assert.IsTrue(SafetyProfile.IsWeaker(SafetyProfile.Find(SafetyStore.Quality, QualitySettingKeys.PkcSpEnforce), "0"), "switching the special process gate off is a weakening");
            Assert.IsTrue(SafetyProfile.IsWeaker(SafetyProfile.Find(SafetyStore.Quality, QualitySettingKeys.PkcCertEnforce), "0"), "switching the certificate judgement off is a weakening");
            Assert.IsTrue(SafetyProfile.IsWeaker(SafetyProfile.Find(SafetyStore.Quality, QualitySettingKeys.PkcQualExpiryAlertDays), "7"), "a shorter warning is a weakening");
            Assert.IsFalse(SafetyProfile.IsWeaker(SafetyProfile.Find(SafetyStore.Quality, QualitySettingKeys.PkcQualExpiryAlertDays), "45"));
        }

        [TestMethod]
        public void TheDefaultsOfTheNewSettingsAreTheCompanyDecisions()
        {
            var defaults = QualitySettingKeys.Defaults().ToDictionary(s => s.Key, s => s.Value);
            Assert.AreEqual(1m, defaults[QualitySettingKeys.PkcSpEnforce]);
            Assert.AreEqual(1m, defaults[QualitySettingKeys.PkcSpSubcontractEvidence]);
            Assert.AreEqual(30m, defaults[QualitySettingKeys.PkcQualExpiryAlertDays]);
            Assert.AreEqual(1m, defaults[QualitySettingKeys.PkcWeldRange]);
            Assert.AreEqual(1m, defaults[QualitySettingKeys.PkcCertEnforce]);
            Assert.AreEqual(6m, defaults[QualitySettingKeys.WeldContinuityMonths], "the six-month continuity of the welder is the existing setting");
            Assert.AreEqual(90m, QualitySettingRules.Clamp(QualitySettingKeys.PkcQualExpiryAlertDays, 500m), "the alert window is bounded");
        }

        // ------------------------------------------------------------------------------------------------ fixtures

        private sealed class Plant
        {
            public int TypeId, QualId, OrderId, OpId, MachineId, InstrumentId, PersonId, ItemId, WorkOrderId, ProcDeliverable, ProcRevision;
            public Guid Chart;
            public string OrderNumber;
        }

        private static List<SpParameterDto> HtLimits() => new List<SpParameterDto>
        {
            new SpParameterDto { Name = "دمای نگهداری", Unit = "C", Min = 840m, Max = 860m },
            new SpParameterDto { Name = "زمان نگهداری", Unit = "min", Min = 50m, Max = 70m },
            new SpParameterDto { Name = "نرخ گرمایش", Unit = "C/h", Max = 200m },
            new SpParameterDto { Name = "نرخ سرمایش", Unit = "C/h", Max = 100m },
        };

        private static List<SpParameterDto> PltLimits() => new List<SpParameterDto>
        {
            new SpParameterDto { Name = "ضخامت آبکاری", Unit = "um", Min = 8m, Max = 25m }, new SpParameterDto { Name = "دمای حمام", Unit = "C", Min = 20m, Max = 60m }, new SpParameterDto { Name = "زمان آبکاری", Unit = "min", Min = 5m, Max = 90m },
        };

        /// <summary>Writes the limits of the heat treatment into the master list (limits where there were none: a strengthening, applied at once).</summary>
        private static async Task SetHtMasterLimits(AppHost host, int typeId)
        {
            var types = await Sp(host, Maker, s => s.GetTypesAsync(new SpTypeQuery(), default));
            SpTypeDto t = types.Data.Single(x => x.Id == typeId);
            var save = await Sp(host, Maker, s => s.SaveTypeAsync(new SaveSpTypeDto { Id = t.Id, Code = t.Code, Title = t.Title, RequiresMachine = t.RequiresMachine, RequiresSurvey = t.RequiresSurvey, RequiredInstrumentClass = t.RequiredInstrumentClass, IsActive = true, RowVersion = t.RowVersion, Parameters = HtLimits() }, default));
            Assert.IsTrue(save.Success, save.Message);
            Assert.IsNull(save.Data.PendingChangeId);
        }

        private static List<SaveSpRunValueDto> GoodRunValues() => new List<SaveSpRunValueDto>
        {
            new SaveSpRunValueDto { Name = "دمای نگهداری", Value = 850m }, new SaveSpRunValueDto { Name = "زمان نگهداری", Value = 60m },
            new SaveSpRunValueDto { Name = "نرخ گرمایش", Value = 150m }, new SaveSpRunValueDto { Name = "نرخ سرمایش", Value = 80m },
        };

        /// <summary>A heat treatment step of a production order, a furnace with a survey, a thermocouple, an operator with a competence record and a released procedure; the qualification is drafted (not yet approved).</summary>
        private static async Task<Plant> NewPlant(AppHost host, bool approve)
        {
            var plant = new Plant();
            var types = await Sp(host, Maker, s => s.GetTypesAsync(new SpTypeQuery(), default));
            plant.TypeId = types.Data.Single(t => t.Code == "HT").Id;
            host.Write(u =>
            {
                var contract = AppHost.Add(u, new ContractEntity { Code = "C-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "contract", Status = ContractStatus.Current });
                u.SaveChanges();
                var wo = AppHost.Add(u, new WorkOrder { Code = "WO-" + Guid.NewGuid().ToString("N").Substring(0, 5), Title = "wo", ContractId = contract.Id });
                var item = AppHost.Add(u, new InventoryItem { Code = "IT-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "shaft", PartNumber = "PN-" + Guid.NewGuid().ToString("N").Substring(0, 4) });
                var machine = new Machine("Furnace 1", true); u.Set<Machine>().Add(machine);
                var tc = AppHost.Add(u, new MeasuringEquipment { Code = "TC-" + Guid.NewGuid().ToString("N").Substring(0, 5), Name = "thermocouple", Type = EquipmentType.TemperatureInstrument, RequiresCalibration = false, Status = EquipmentStatus.Calibrated });
                var person = AppHost.Add(u, new QualifiedPerson { Name = "Operator A", PersonnelCode = "P-" + Guid.NewGuid().ToString("N").Substring(0, 4), StampId = "HT-01", IsActive = true });
                var deliverable = AppHost.Add(u, new PcEngineeringDeliverable { ContractId = contract.Id, DocumentNumber = "PRC-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "heat treatment procedure", Discipline = "Mechanical", DocumentType = "Procedure" });
                u.SaveChanges();
                var rev = AppHost.Add(u, new PcDocumentRevision { DeliverableId = deliverable.Id, Sequence = 1, Code = "A", Status = PcDocStatus.Released, Reason = "first issue", PreparedByUserId = 1 });
                AppHost.Add(u, new Qualification { PersonId = person.Id, Kind = QualificationKind.SpecialProcessOperator, Method = "HT", Scheme = QualificationScheme.Other, CertificateNumber = "OP-1", IssuedDate = DateTime.Today.AddDays(-60), ExpiryDate = DateTime.Today.AddDays(300) });
                var order = AppHost.Add(u, new MfProductionOrder { Number = "MO-" + Guid.NewGuid().ToString("N").Substring(0, 5), WorkOrderId = wo.Id, ContractId = contract.Id, ItemId = item.Id, Quantity = 10, Unit = "EA" });
                u.SaveChanges();
                var op = AppHost.Add(u, new MfOperation { OrderId = order.Id, Sequence = 20, Title = "heat treat", Status = MfOperationStatus.InProgress, IsSpecialProcess = true, SpecialProcessTypeId = plant.TypeId });
                u.SaveChanges();
                plant.MachineId = machine.Id; plant.InstrumentId = tc.Id; plant.PersonId = person.Id; plant.ItemId = item.Id; plant.WorkOrderId = wo.Id; plant.OrderId = order.Id; plant.OpId = op.Id;
                plant.ProcDeliverable = deliverable.Id; plant.ProcRevision = rev.Id; plant.OrderNumber = order.Number;
            }, Seeder);
            plant.Chart = NewFile(host);
            var save = await Sp(host, Maker, s => s.SaveQualificationAsync(new SaveSpQualDto
            {
                TypeId = plant.TypeId, Title = "HT shaft qualification", ProcedureDeliverableId = plant.ProcDeliverable, ProcedureRevisionId = plant.ProcRevision, ValidFrom = DateTime.Today.AddDays(-5), ValidTo = DateTime.Today.AddDays(200),
                Limits = HtLimits(), OperatorPersonIds = new List<int> { plant.PersonId },
                Equipments = new List<SaveSpQualEquipmentDto>
                {
                    new SaveSpQualEquipmentDto { Kind = SpEquipmentKind.Machine, MachineId = plant.MachineId, SurveyKind = SpSurveyKind.Tus, SurveyDate = DateTime.Today.AddDays(-20), SurveyDueDate = DateTime.Today.AddDays(160) },
                    new SaveSpQualEquipmentDto { Kind = SpEquipmentKind.Instrument, EquipmentId = plant.InstrumentId },
                },
            }, default));
            Assert.IsTrue(save.Success, save.Message);
            plant.QualId = save.Data.Id;
            if (approve)
            {
                Assert.IsTrue((await Sp(host, Maker, s => s.QualificationActionAsync(new SpActionDto { Id = plant.QualId, Action = "submit" }, default))).Success);
                var ok = await Sp(host, Qa, s => s.QualificationActionAsync(new SpActionDto { Id = plant.QualId, Action = "approve" }, default));
                Assert.IsTrue(ok.Success, ok.Message);
            }
            return plant;
        }

        private static SaveSpRunDto RunDto(Plant p, List<SaveSpRunValueDto> values = null, decimal qty = 10m) => new SaveSpRunDto
        {
            OperationId = p.OpId, QualificationId = p.QualId, OperatorPersonId = p.PersonId, MachineId = p.MachineId, Quantity = qty, StartedAt = DateTime.Now.AddHours(-3), EndedAt = DateTime.Now.AddHours(-1),
            ChartDocumentId = p.Chart, ChartFileName = "chart.pdf", InstrumentIds = new List<int> { p.InstrumentId }, Values = values ?? GoodRunValues(),
        };

        private static async Task<string> Gate(AppHost host, Plant p, decimal required = 10m, decimal scrap = 0m) =>
            await host.As(Seeder, async sp =>
            {
                IUnitOfWork uow = sp.GetRequiredService<IUnitOfWork>();
                var order = uow.Set<MfProductionOrder>().Single(o => o.Id == p.OrderId);
                var op = uow.Set<MfOperation>().Single(o => o.Id == p.OpId);
                return await SpecialProcessGate.FinishProblemAsync(uow, order, op, required, scrap, default);
            });

        // ------------------------------------------------------------------------------------------------ master and qualification

        [TestMethod]
        public async Task TheSixProcessTypesAreSeededWithoutInventedNumericLimits()
        {
            AppHost host = NewHost("seed");
            var types = await Sp(host, Maker, s => s.GetTypesAsync(new SpTypeQuery(), default));
            CollectionAssert.AreEquivalent(new[] { "HT", "NIT", "COAT", "BRZ", "PLT", "NDT" }, types.Data.Select(t => t.Code).ToArray());
            Assert.IsTrue(types.Data.Single(t => t.Code == "NDT").ExistingControl, "NDT stays under the personnel qualification");
            Assert.IsTrue(types.Data.SelectMany(t => t.Parameters).All(p => !p.Min.HasValue && !p.Max.HasValue), "no limit is invented; the company writes them");
            Assert.IsTrue(types.Data.Single(t => t.Code == "HT").RequiresSurvey);
            var again = await Sp(host, Maker, s => s.GetTypesAsync(new SpTypeQuery(), default));
            Assert.AreEqual(6, again.Data.Count, "seeded once");
        }

        [TestMethod]
        public async Task TheTypeListIsEditableByEngineeringAndKeepsCodesUnique()
        {
            AppHost host = NewHost("typeedit");
            var types = await Sp(host, Maker, s => s.GetTypesAsync(new SpTypeQuery(), default));
            var plt = types.Data.Single(t => t.Code == "PLT");
            var save = await Sp(host, Maker, s => s.SaveTypeAsync(new SaveSpTypeDto
            {
                Id = plt.Id, Code = "PLT", Title = "Plating (edited)", RequiresMachine = true, RequiresSurvey = false, RequiredInstrumentClass = plt.RequiredInstrumentClass, IsActive = true, RowVersion = plt.RowVersion, Parameters = PltLimits(),
            }, default));
            Assert.IsTrue(save.Success, save.Message);
            Assert.IsNull(save.Data.PendingChangeId, "limits written where there were none only strengthen the control");
            Assert.AreEqual(3, save.Data.Parameters.Count);
            Assert.AreEqual(8m, save.Data.Parameters.Single(x => x.Name == "ضخامت آبکاری").Min);
            var dup = await Sp(host, Maker, s => s.SaveTypeAsync(new SaveSpTypeDto { Code = "ht", Title = "Duplicate", Parameters = new List<SpParameterDto>() }, default));
            Assert.IsFalse(dup.Success, "the code is unique");
            var denied = await Sp(host, Qc, s => s.SaveTypeAsync(new SaveSpTypeDto { Code = "X1", Title = "x" }, default));
            Assert.IsFalse(denied.Success, "QC does not edit the list");
        }

        [TestMethod]
        public async Task AQualificationWithoutALimitSurveyOrOperatorCannotBeSubmitted()
        {
            AppHost host = NewHost("incomplete");
            Plant p = await NewPlant(host, false);
            var noLimit = await Sp(host, Maker, s => s.SaveQualificationAsync(new SaveSpQualDto
            {
                Id = p.QualId, TypeId = p.TypeId, ProcedureDeliverableId = p.ProcDeliverable, ProcedureRevisionId = p.ProcRevision, ValidFrom = DateTime.Today, ValidTo = DateTime.Today.AddDays(100),
                Limits = HtLimits().Select((l, i) => i == 0 ? new SpParameterDto { Name = l.Name, Unit = l.Unit } : l).ToList(), OperatorPersonIds = new List<int> { p.PersonId },
                Equipments = new List<SaveSpQualEquipmentDto> { new SaveSpQualEquipmentDto { Kind = SpEquipmentKind.Machine, MachineId = p.MachineId, SurveyKind = SpSurveyKind.Tus, SurveyDate = DateTime.Today.AddDays(-1), SurveyDueDate = DateTime.Today.AddDays(100) },
                    new SaveSpQualEquipmentDto { Kind = SpEquipmentKind.Instrument, EquipmentId = p.InstrumentId } },
            }, default));
            Assert.IsTrue(noLimit.Success, noLimit.Message);
            var refused = await Sp(host, Maker, s => s.QualificationActionAsync(new SpActionDto { Id = p.QualId, Action = "submit" }, default));
            Assert.IsFalse(refused.Success);
            Assert.IsTrue(refused.Message.Contains("حداقل"), "a parameter without a limit is named: " + refused.Message);

            var noSurvey = await Sp(host, Maker, s => s.SaveQualificationAsync(new SaveSpQualDto
            {
                Id = p.QualId, TypeId = p.TypeId, ProcedureDeliverableId = p.ProcDeliverable, ProcedureRevisionId = p.ProcRevision, ValidFrom = DateTime.Today, ValidTo = DateTime.Today.AddDays(100),
                Limits = HtLimits(), OperatorPersonIds = new List<int> { p.PersonId },
                Equipments = new List<SaveSpQualEquipmentDto> { new SaveSpQualEquipmentDto { Kind = SpEquipmentKind.Machine, MachineId = p.MachineId }, new SaveSpQualEquipmentDto { Kind = SpEquipmentKind.Instrument, EquipmentId = p.InstrumentId } },
            }, default));
            Assert.IsTrue(noSurvey.Success, noSurvey.Message);
            var surveyRefused = await Sp(host, Maker, s => s.QualificationActionAsync(new SpActionDto { Id = p.QualId, Action = "submit" }, default));
            Assert.IsFalse(surveyRefused.Success);
            Assert.IsTrue(surveyRefused.Message.Contains("TUS"), surveyRefused.Message);

            var noOperator = await Sp(host, Maker, s => s.SaveQualificationAsync(new SaveSpQualDto
            {
                Id = p.QualId, TypeId = p.TypeId, ProcedureDeliverableId = p.ProcDeliverable, ProcedureRevisionId = p.ProcRevision, ValidFrom = DateTime.Today, ValidTo = DateTime.Today.AddDays(100),
                Limits = HtLimits(), OperatorPersonIds = new List<int>(),
                Equipments = new List<SaveSpQualEquipmentDto> { new SaveSpQualEquipmentDto { Kind = SpEquipmentKind.Machine, MachineId = p.MachineId, SurveyKind = SpSurveyKind.Tus, SurveyDate = DateTime.Today.AddDays(-1), SurveyDueDate = DateTime.Today.AddDays(100) },
                    new SaveSpQualEquipmentDto { Kind = SpEquipmentKind.Instrument, EquipmentId = p.InstrumentId } },
            }, default));
            Assert.IsTrue(noOperator.Success, noOperator.Message);
            Assert.IsFalse((await Sp(host, Maker, s => s.QualificationActionAsync(new SpActionDto { Id = p.QualId, Action = "submit" }, default))).Success, "an operator is needed");
        }

        [TestMethod]
        public async Task AnOperatorWithoutACompetenceRecordForTheProcessBlocksTheQualification()
        {
            AppHost host = NewHost("nocomp");
            Plant p = await NewPlant(host, false);
            int stranger = 0;
            host.Write(u => { var q = AppHost.Add(u, new QualifiedPerson { Name = "Operator B", PersonnelCode = "P-B" + Guid.NewGuid().ToString("N").Substring(0, 3), IsActive = true }); u.SaveChanges(); stranger = q.Id; }, Seeder);
            var save = await Sp(host, Maker, s => s.SaveQualificationAsync(new SaveSpQualDto
            {
                Id = p.QualId, TypeId = p.TypeId, ProcedureDeliverableId = p.ProcDeliverable, ProcedureRevisionId = p.ProcRevision, ValidFrom = DateTime.Today, ValidTo = DateTime.Today.AddDays(100),
                Limits = HtLimits(), OperatorPersonIds = new List<int> { stranger },
                Equipments = new List<SaveSpQualEquipmentDto> { new SaveSpQualEquipmentDto { Kind = SpEquipmentKind.Machine, MachineId = p.MachineId, SurveyKind = SpSurveyKind.Tus, SurveyDate = DateTime.Today.AddDays(-1), SurveyDueDate = DateTime.Today.AddDays(100) },
                    new SaveSpQualEquipmentDto { Kind = SpEquipmentKind.Instrument, EquipmentId = p.InstrumentId } },
            }, default));
            Assert.IsTrue(save.Success, save.Message);
            var refused = await Sp(host, Maker, s => s.QualificationActionAsync(new SpActionDto { Id = p.QualId, Action = "submit" }, default));
            Assert.IsFalse(refused.Success);
            Assert.IsTrue(refused.Message.Contains("Operator B") || refused.Message.Contains("صلاحیت"), refused.Message);
        }

        [TestMethod]
        public async Task TheMakerNeverApprovesAQualificationAndAnAiSeatNeverDoes()
        {
            AppHost host = NewHost("qualmc");
            Plant p = await NewPlant(host, false);
            Assert.IsTrue((await Sp(host, Maker, s => s.QualificationActionAsync(new SpActionDto { Id = p.QualId, Action = "submit" }, default))).Success);
            var wrongRole = await Sp(host, Maker, s => s.QualificationActionAsync(new SpActionDto { Id = p.QualId, Action = "approve" }, default));
            Assert.IsFalse(wrongRole.Success, "engineering does not approve");
            // a QA person who edited it is an editor and cannot approve either
            var edited = await Sp(host, Qa, s => s.SaveQualificationAsync(new SaveSpQualDto { Id = p.QualId, TypeId = p.TypeId, ProcedureDeliverableId = p.ProcDeliverable, ProcedureRevisionId = p.ProcRevision, ValidFrom = DateTime.Today.AddDays(-5), ValidTo = DateTime.Today.AddDays(200), Limits = HtLimits(),
                OperatorPersonIds = new List<int> { p.PersonId }, Equipments = new List<SaveSpQualEquipmentDto> { new SaveSpQualEquipmentDto { Kind = SpEquipmentKind.Machine, MachineId = p.MachineId, SurveyKind = SpSurveyKind.Tus, SurveyDate = DateTime.Today.AddDays(-20), SurveyDueDate = DateTime.Today.AddDays(160) },
                    new SaveSpQualEquipmentDto { Kind = SpEquipmentKind.Instrument, EquipmentId = p.InstrumentId } } }, default));
            Assert.IsFalse(edited.Success, "a submitted qualification is not edited");
            var seat = new AiActingUser { UserId = 99, UserName = "seat.qa", Roles = new[] { "QA" } };
            var ai = await AiActing.RunAsync(host.Provider.GetRequiredService<IServiceScopeFactory>(), seat, "SEAT-QA", sp => sp.GetRequiredService<ISpecialProcessService>().QualificationActionAsync(new SpActionDto { Id = p.QualId, Action = "approve" }, default));
            Assert.IsFalse(ai.Success, "an AI seat does not approve");
            var approved = await Sp(host, Qa, s => s.QualificationActionAsync(new SpActionDto { Id = p.QualId, Action = "approve" }, default));
            Assert.IsTrue(approved.Success, approved.Message);
            Assert.AreEqual(SpQualStatus.Approved, approved.Data.Status);
            var voided = await Sp(host, Qa, s => s.QualificationActionAsync(new SpActionDto { Id = p.QualId, Action = "void", Note = "furnace rebuilt" }, default));
            Assert.IsTrue(voided.Success);
            Assert.AreEqual(1, host.Read(u => u.Set<SpQualification>().Count(q => q.Id == p.QualId)), "voided, never deleted");
        }

        [TestMethod]
        public async Task TheCreatorOfAQualificationCannotApproveItEvenWithTheQaRole()
        {
            AppHost host = NewHost("creatorqa");
            Plant p = await NewPlant(host, false);
            // the QA user 52 makes a second qualification and tries to approve it
            var made = await Sp(host, Qa, s => s.SaveQualificationAsync(new SaveSpQualDto
            {
                TypeId = p.TypeId, Title = "by QA", ProcedureDeliverableId = p.ProcDeliverable, ProcedureRevisionId = p.ProcRevision, ValidFrom = DateTime.Today.AddDays(-1), ValidTo = DateTime.Today.AddDays(100), Limits = HtLimits(),
                OperatorPersonIds = new List<int> { p.PersonId }, Equipments = new List<SaveSpQualEquipmentDto> { new SaveSpQualEquipmentDto { Kind = SpEquipmentKind.Machine, MachineId = p.MachineId, SurveyKind = SpSurveyKind.Tus, SurveyDate = DateTime.Today.AddDays(-20), SurveyDueDate = DateTime.Today.AddDays(160) },
                    new SaveSpQualEquipmentDto { Kind = SpEquipmentKind.Instrument, EquipmentId = p.InstrumentId } },
            }, default));
            Assert.IsTrue(made.Success, made.Message);
            Assert.IsTrue((await Sp(host, Qa, s => s.QualificationActionAsync(new SpActionDto { Id = made.Data.Id, Action = "submit" }, default))).Success);
            var self = await Sp(host, Qa, s => s.QualificationActionAsync(new SpActionDto { Id = made.Data.Id, Action = "approve" }, default));
            Assert.IsFalse(self.Success, "the creator cannot approve");
            Assert.IsTrue((await Sp(host, Qa2, s => s.QualificationActionAsync(new SpActionDto { Id = made.Data.Id, Action = "approve" }, default))).Success, "another QA person can");
        }

        [TestMethod]
        public async Task ALimitWiderThanTheMasterIsRefused()
        {
            AppHost host = NewHost("widerthanmaster");
            var types = await Sp(host, Maker, s => s.GetTypesAsync(new SpTypeQuery(), default));
            var plt = types.Data.Single(t => t.Code == "PLT");
            Assert.IsTrue((await Sp(host, Maker, s => s.SaveTypeAsync(new SaveSpTypeDto { Id = plt.Id, Code = "PLT", Title = plt.Title, RequiresMachine = true, RequiredInstrumentClass = plt.RequiredInstrumentClass, IsActive = true, RowVersion = plt.RowVersion, Parameters = PltLimits() }, default))).Success);
            Plant p = await NewPlant(host, false);
            var wide = await Sp(host, Maker, s => s.SaveQualificationAsync(new SaveSpQualDto
            {
                TypeId = plt.Id, ProcedureDeliverableId = p.ProcDeliverable, ProcedureRevisionId = p.ProcRevision, ValidFrom = DateTime.Today, ValidTo = DateTime.Today.AddDays(100),
                Limits = new List<SpParameterDto> { new SpParameterDto { Name = "ضخامت آبکاری", Unit = "um", Min = 5m, Max = 20m }, new SpParameterDto { Name = "دمای حمام", Unit = "C", Min = 20m, Max = 60m }, new SpParameterDto { Name = "زمان آبکاری", Unit = "min", Min = 5m, Max = 90m } },
                OperatorPersonIds = new List<int>(), Equipments = new List<SaveSpQualEquipmentDto>(),
            }, default));
            Assert.IsFalse(wide.Success, "a minimum below the master is refused");
            Assert.IsTrue(wide.Message.Contains("کمتر"), wide.Message);
        }

        // ------------------------------------------------------------------------------------------------ run and gate

        [TestMethod]
        public async Task ARunIsJudgedOnTheServerAndAFailedRunOpensAnNcr()
        {
            AppHost host = NewHost("runjudge");
            Plant p = await NewPlant(host, true);
            var hot = GoodRunValues(); hot[0].Value = 875m;
            var failed = await Sp(host, Qc, s => s.SaveRunAsync(RunDto(p, hot), default));
            Assert.IsTrue(failed.Success, failed.Message);
            Assert.AreEqual(SpRunResult.Fail, failed.Data.Result, "the server decides, not the client");
            Assert.IsTrue(failed.Data.ResultText.Contains("875"), failed.Data.ResultText);
            Assert.IsTrue(failed.Data.NcrId.HasValue, "a failed run opens an NCR");
            Assert.AreEqual(1, host.Read(u => u.Set<NonConformance>().Count(n => n.Id == failed.Data.NcrId.Value && n.WorkOrderId == p.WorkOrderId)));
            var passed = await Sp(host, Qc, s => s.SaveRunAsync(RunDto(p), default));
            Assert.AreEqual(SpRunResult.Pass, passed.Data.Result);
            Assert.IsNull(passed.Data.NcrId);
            var missing = GoodRunValues(); missing.RemoveAt(1);
            var incomplete = await Sp(host, Qc, s => s.SaveRunAsync(RunDto(p, missing), default));
            Assert.IsFalse(incomplete.Success, "a required parameter without a value is incomplete, not a pass");
        }

        [TestMethod]
        public async Task ARunNeedsAValidQualificationAQualifiedOperatorTheFurnaceAndCalibratedInstruments()
        {
            AppHost host = NewHost("runchecks");
            Plant p = await NewPlant(host, true);
            var draft = await NewPlant(host, false);
            var noQual = await Sp(host, Qc, s => { var d = RunDto(p); d.QualificationId = draft.QualId; return s.SaveRunAsync(d, default); });
            Assert.IsFalse(noQual.Success, "a qualification that is not approved cannot be used");
            var wrongMachine = await Sp(host, Qc, s => { var d = RunDto(p); d.MachineId = null; return s.SaveRunAsync(d, default); });
            Assert.IsFalse(wrongMachine.Success, "the furnace is named");
            int other = 0;
            host.Write(u => { var m = new Machine("Other furnace", true); u.Set<Machine>().Add(m); u.SaveChanges(); other = m.Id; }, Seeder);
            Assert.IsFalse((await Sp(host, Qc, s => { var d = RunDto(p); d.MachineId = other; return s.SaveRunAsync(d, default); })).Success, "a furnace outside the qualification");
            Assert.IsFalse((await Sp(host, Qc, s => { var d = RunDto(p); d.InstrumentIds = new List<int>(); return s.SaveRunAsync(d, default); })).Success, "a thermocouple is needed");
            int stranger = 0;
            host.Write(u => { var q = AppHost.Add(u, new QualifiedPerson { Name = "Operator C", PersonnelCode = "P-C" + Guid.NewGuid().ToString("N").Substring(0, 3), IsActive = true }); u.SaveChanges(); stranger = q.Id; }, Seeder);
            Assert.IsFalse((await Sp(host, Qc, s => { var d = RunDto(p); d.OperatorPersonId = stranger; return s.SaveRunAsync(d, default); })).Success, "an operator outside the qualification");
            int overdue = 0;
            host.Write(u => { var e = AppHost.Add(u, new MeasuringEquipment { Code = "TC-" + Guid.NewGuid().ToString("N").Substring(0, 5), Name = "late thermocouple", Type = EquipmentType.TemperatureInstrument, RequiresCalibration = true, Status = EquipmentStatus.Overdue, LastCalibrated = DateTime.Today.AddDays(-400), NextDue = DateTime.Today.AddDays(-30), IntervalMonths = 12 }); u.SaveChanges(); overdue = e.Id; }, Seeder);
            Assert.IsFalse((await Sp(host, Qc, s => { var d = RunDto(p); d.InstrumentIds = new List<int> { overdue }; return s.SaveRunAsync(d, default); })).Success, "an instrument that is not in the qualification (and is overdue)");
            Assert.IsFalse((await Sp(host, Qc, s => { var d = RunDto(p); d.StartedAt = DateTime.Now.AddDays(-30); d.EndedAt = DateTime.Now.AddDays(-30).AddHours(2); return s.SaveRunAsync(d, default); })).Success, "outside the validity and the back-date limit");
            Assert.IsTrue((await Sp(host, Qc, s => s.SaveRunAsync(RunDto(p), default))).Success);
        }

        [TestMethod]
        public async Task TheRunIsReviewedByAnotherPersonAndAnAiSeatDoesNotReview()
        {
            AppHost host = NewHost("runreview");
            Plant p = await NewPlant(host, true);
            var run = await Sp(host, Qa, s => s.SaveRunAsync(RunDto(p), default));
            Assert.IsTrue(run.Success, run.Message);
            var self = await Sp(host, Qa, s => s.RunActionAsync(new SpActionDto { Id = run.Data.Id, Action = "review" }, default));
            Assert.IsFalse(self.Success, "the recorder does not review");
            var seat = new AiActingUser { UserId = 98, UserName = "seat.qc", Roles = new[] { "QC" } };
            var ai = await AiActing.RunAsync(host.Provider.GetRequiredService<IServiceScopeFactory>(), seat, "SEAT-QC", sp => sp.GetRequiredService<ISpecialProcessService>().RunActionAsync(new SpActionDto { Id = run.Data.Id, Action = "review" }, default));
            Assert.IsFalse(ai.Success, "an AI seat does not review");
            var ok = await Sp(host, Qc, s => s.RunActionAsync(new SpActionDto { Id = run.Data.Id, Action = "review" }, default));
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.AreEqual(SpReviewStatus.Approved, ok.Data.ReviewStatus);
            Assert.IsFalse((await Sp(host, Qc2, s => s.RunActionAsync(new SpActionDto { Id = run.Data.Id, Action = "review" }, default))).Success, "reviewed once");
            var failed = GoodRunValues(); failed[1].Value = 10m;
            var bad = await Sp(host, Qc, s => s.SaveRunAsync(RunDto(p, failed), default));
            Assert.IsFalse((await Sp(host, Qc2, s => s.RunActionAsync(new SpActionDto { Id = bad.Data.Id, Action = "review" }, default))).Success, "a failed run is not approved by a review");
        }

        [TestMethod]
        public async Task TheFinishGateRefusesUntilAPassedAndReviewedRunCoversTheQuantity()
        {
            AppHost host = NewHost("gate");
            Plant p = await NewPlant(host, true);
            string none = await Gate(host, p);
            Assert.IsNotNull(none);
            Assert.IsTrue(none.Contains("heat treat") || none.Contains(p.OrderNumber), "the message names the step: " + none);
            var run = await Sp(host, Qc, s => s.SaveRunAsync(RunDto(p), default));
            Assert.IsNotNull(await Gate(host, p), "recorded but not reviewed");
            Assert.IsTrue((await Sp(host, Qa, s => s.RunActionAsync(new SpActionDto { Id = run.Data.Id, Action = "review" }, default))).Success);
            Assert.IsNull(await Gate(host, p), "passed, reviewed, covers the 10 pieces");
            Assert.IsNotNull(await Gate(host, p, 12m), "more pieces than the run covers");
            Assert.IsNull(await Gate(host, p, 12m, 2m), "scrapped pieces need no run");
        }

        [TestMethod]
        public async Task TheFinishGateFollowsTheQualificationTheOperatorAndTheFurnaceOnTheRunDay()
        {
            AppHost host = NewHost("gatecond");
            Plant p = await NewPlant(host, true);
            var run = await Sp(host, Qc, s => s.SaveRunAsync(RunDto(p), default));
            Assert.IsTrue((await Sp(host, Qa, s => s.RunActionAsync(new SpActionDto { Id = run.Data.Id, Action = "review" }, default))).Success);
            Assert.IsNull(await Gate(host, p));
            host.Write(u => { var q = u.Set<Qualification>().Single(x => x.PersonId == p.PersonId); q.Revoked = true; q.RevokeReason = "test"; u.SaveChanges(); }, Seeder);
            Assert.IsNotNull(await Gate(host, p), "the operator's competence was revoked after the run");
            host.Write(u => { var q = u.Set<Qualification>().Single(x => x.PersonId == p.PersonId); q.Revoked = false; q.RevokeReason = null; u.SaveChanges(); }, Seeder);
            Assert.IsNull(await Gate(host, p));
            Assert.IsTrue((await Sp(host, Qa2, s => s.QualificationActionAsync(new SpActionDto { Id = p.QualId, Action = "void", Note = "furnace rebuilt" }, default))).Success);
            Assert.IsNotNull(await Gate(host, p), "the qualification was voided");
        }

        [TestMethod]
        public async Task AFailedRunBlocksTheStepUntilItsNcrIsClosedOrConceded()
        {
            AppHost host = NewHost("gatefail");
            Plant p = await NewPlant(host, true);
            var hot = GoodRunValues(); hot[0].Value = 900m;
            var failed = await Sp(host, Qc, s => s.SaveRunAsync(RunDto(p, hot, 2m), default));
            var good = await Sp(host, Qc, s => s.SaveRunAsync(RunDto(p, null, 8m), default));
            Assert.IsTrue((await Sp(host, Qa, s => s.RunActionAsync(new SpActionDto { Id = good.Data.Id, Action = "review" }, default))).Success);
            string open = await Gate(host, p);
            Assert.IsNotNull(open, "the NCR of the failed run is open");
            Assert.IsTrue(open.Contains("NCR") || open.Contains("عدم انطباق"), open);
            host.Write(u => { var n = u.Set<NonConformance>().Single(x => x.Id == failed.Data.NcrId.Value); n.Status = NcrStatus.Cancelled; u.SaveChanges(); }, Seeder);
            Assert.IsNotNull(await Gate(host, p), "8 of 10 pieces are covered; the other 2 failed");
            Assert.IsNull(await Gate(host, p, 10m, 2m), "the two failed pieces are scrapped");
        }

        [TestMethod]
        public async Task AVoidedRunDoesNotCountAndARunOfAFinishedStepIsNotVoided()
        {
            AppHost host = NewHost("runvoid");
            Plant p = await NewPlant(host, true);
            var run = await Sp(host, Qc, s => s.SaveRunAsync(RunDto(p), default));
            Assert.IsTrue((await Sp(host, Qa, s => s.RunActionAsync(new SpActionDto { Id = run.Data.Id, Action = "review" }, default))).Success);
            Assert.IsFalse((await Sp(host, Qa, s => s.RunActionAsync(new SpActionDto { Id = run.Data.Id, Action = "void" }, default))).Success, "a reason is required");
            Assert.IsTrue((await Sp(host, Qa, s => s.RunActionAsync(new SpActionDto { Id = run.Data.Id, Action = "void", Note = "wrong lot" }, default))).Success);
            Assert.IsNotNull(await Gate(host, p), "the voided run no longer counts");
            Assert.AreEqual(1, host.Read(u => u.Set<SpRun>().Count(r => r.Id == run.Data.Id)), "voided, never deleted");
        }

        [TestMethod]
        public async Task TheGateStatusEndpointShowsWhatIsMissing()
        {
            AppHost host = NewHost("gatestatus");
            Plant p = await NewPlant(host, true);
            var status = await Sp(host, Qc, s => s.GateStatusAsync(p.OpId, null, default));
            Assert.IsTrue(status.Data.Applies);
            Assert.IsFalse(status.Data.Ready);
            Assert.IsNotNull(status.Data.Problem);
            var run = await Sp(host, Qc, s => s.SaveRunAsync(RunDto(p), default));
            Assert.IsTrue((await Sp(host, Qa, s => s.RunActionAsync(new SpActionDto { Id = run.Data.Id, Action = "review" }, default))).Success);
            var ready = await Sp(host, Qc, s => s.GateStatusAsync(p.OpId, null, default));
            Assert.IsTrue(ready.Data.Ready);
            Assert.AreEqual(1, ready.Data.Runs.Count);
        }

        [TestMethod]
        public async Task TheGateIsSkippedForNdtAndForAStepWithoutTheFlagAndCanBeSwitchedOnlyBySetting()
        {
            AppHost host = NewHost("gateoff");
            Plant p = await NewPlant(host, true);
            Assert.IsNotNull(await Gate(host, p));
            host.Write(u => { var o = u.Set<MfOperation>().Single(x => x.Id == p.OpId); o.IsSpecialProcess = false; u.SaveChanges(); }, Seeder);
            Assert.IsNull(await Gate(host, p), "an unflagged step is not gated");
            var types = await Sp(host, Maker, s => s.GetTypesAsync(new SpTypeQuery(), default));
            int ndt = types.Data.Single(t => t.Code == "NDT").Id;
            host.Write(u => { var o = u.Set<MfOperation>().Single(x => x.Id == p.OpId); o.IsSpecialProcess = true; o.SpecialProcessTypeId = ndt; u.SaveChanges(); }, Seeder);
            Assert.IsNull(await Gate(host, p), "NDT is controlled by the personnel qualification and is not double-gated");
            host.Write(u => { var o = u.Set<MfOperation>().Single(x => x.Id == p.OpId); o.SpecialProcessTypeId = p.TypeId; u.Set<QualitySetting>().Add(new QualitySetting { Key = QualitySettingKeys.PkcSpEnforce, Value = 0m, Title = "t", Source = "s" }); u.SaveChanges(); }, Seeder);
            Assert.IsNull(await Gate(host, p), "the switch off is a safety-profile weakening that needs two persons; the gate honours the stored value");
        }

        [TestMethod]
        public async Task AFlaggedStepIsNotSkippedAndAnUnqualifiedPersonDoesNotStartIt()
        {
            AppHost host = NewHost("skipstart");
            Plant p = await NewPlant(host, true);
            string skip = await host.As(Seeder, async sp =>
            {
                IUnitOfWork uow = sp.GetRequiredService<IUnitOfWork>();
                return await SpecialProcessGate.SkipProblemAsync(uow, uow.Set<MfOperation>().Single(o => o.Id == p.OpId), default);
            });
            Assert.IsNotNull(skip, "a special process step is not skipped");
            int employeeId = 0;
            host.Write(u => { var e = AppHost.Add(u, new Entities.Model.NasbNiro.Finance.FnEmployee { FirstName = "Emp", LastName = "One", PersonnelCode = "E-" + Guid.NewGuid().ToString("N").Substring(0, 4) }); u.SaveChanges(); employeeId = e.Id; }, Seeder);
            string start = await host.As(Seeder, async sp =>
            {
                IUnitOfWork uow = sp.GetRequiredService<IUnitOfWork>();
                return await SpecialProcessGate.StartProblemAsync(uow, uow.Set<MfOperation>().Single(o => o.Id == p.OpId), uow.Set<Entities.Model.NasbNiro.Finance.FnEmployee>().Single(e => e.Id == employeeId), MfBookingKind.Run, DateTime.Today, default);
            });
            Assert.IsNotNull(start, "an employee without a personnel competence file does not start the step");
            host.Write(u => { var e = u.Set<Entities.Model.NasbNiro.Finance.FnEmployee>().Single(x => x.Id == employeeId); e.QualifiedPersonId = p.PersonId; u.SaveChanges(); }, Seeder);
            string again = await host.As(Seeder, async sp =>
            {
                IUnitOfWork uow = sp.GetRequiredService<IUnitOfWork>();
                return await SpecialProcessGate.StartProblemAsync(uow, uow.Set<MfOperation>().Single(o => o.Id == p.OpId), uow.Set<Entities.Model.NasbNiro.Finance.FnEmployee>().Single(e => e.Id == employeeId), MfBookingKind.Run, DateTime.Today, default);
            });
            Assert.IsNull(again, "the qualified operator starts it");
        }

        [TestMethod]
        public async Task AQualificationEndingSoonRaisesAnExpiryAlert()
        {
            AppHost host = NewHost("expiry");
            Plant p = await NewPlant(host, true);
            int none = await host.As(Seeder, sp => sp.GetRequiredService<ISpecialProcessService>().ExpiryAlertsAsync(default));
            Assert.AreEqual(0, none, "200 days left is not in the 30-day window");
            host.Write(u => { var q = u.Set<SpQualification>().Single(x => x.Id == p.QualId); q.ValidTo = DateTime.Today.AddDays(20); u.SaveChanges(); }, Seeder);
            int soon = await host.As(Seeder, sp => sp.GetRequiredService<ISpecialProcessService>().ExpiryAlertsAsync(default));
            Assert.AreEqual(1, soon);
            int again = await host.As(Seeder, sp => sp.GetRequiredService<ISpecialProcessService>().ExpiryAlertsAsync(default));
            Assert.AreEqual(1, again, "idempotent: the same alert, not a second one");
            var list = await Sp(host, Qa, s => s.GetQualificationsAsync(new SpQualQuery { ExpiringInDays = 30 }, default));
            Assert.AreEqual(1, list.Data.Count);
        }

        // ------------------------------------------------------------------------------------------------ subcontract

        private static async Task<int> NewSubcontractStep(AppHost host, Plant p, int supplier)
        {
            int op = 0;
            host.Write(u =>
            {
                var o = AppHost.Add(u, new MfOperation { OrderId = p.OrderId, Sequence = 30, Title = "outside nitriding", Status = MfOperationStatus.InProgress, IsOutside = true, OutsideSupplierId = supplier, IsSpecialProcess = true, SpecialProcessTypeId = p.TypeId });
                u.SaveChanges(); op = o.Id;
            }, Seeder);
            await Task.CompletedTask;
            return op;
        }

        [TestMethod]
        public async Task ASupplierIsApprovedPerProcessByASecondPersonWithEvidenceAndAValidity()
        {
            AppHost host = NewHost("supplier");
            Plant p = await NewPlant(host, false);
            int provider = 0;
            host.Write(u => { var pr = AppHost.Add(u, new Provider { Name = "Heat Treat Co" }); u.SaveChanges(); provider = pr.Id; }, Seeder);
            var noEvidence = await Sp(host, Commercial, s => s.SaveSupplierApprovalAsync(new SaveSpSupplierApprovalDto { ProviderId = provider, TypeId = p.TypeId, ValidFrom = DateTime.Today, ValidTo = DateTime.Today.AddDays(300) }, default));
            Assert.IsFalse(noEvidence.Success, "an audit or accreditation record is needed");
            var made = await Sp(host, Commercial, s => s.SaveSupplierApprovalAsync(new SaveSpSupplierApprovalDto { ProviderId = provider, TypeId = p.TypeId, ValidFrom = DateTime.Today, ValidTo = DateTime.Today.AddDays(300), AccreditationNumber = "NADCAP-1" }, default));
            Assert.IsTrue(made.Success, made.Message);
            Assert.IsFalse((await Sp(host, Commercial, s => s.SaveSupplierApprovalAsync(new SaveSpSupplierApprovalDto { ProviderId = provider, TypeId = p.TypeId, ValidFrom = DateTime.Today, ValidTo = DateTime.Today.AddDays(100), AccreditationNumber = "X" }, default))).Success, "one live approval per supplier and process");
            Assert.IsFalse((await Sp(host, Commercial, s => s.SupplierApprovalActionAsync(new SpActionDto { Id = made.Data.Id, Action = "approve" }, default))).Success, "commercial does not approve");
            Assert.IsTrue((await Sp(host, Qa, s => s.SupplierApprovalActionAsync(new SpActionDto { Id = made.Data.Id, Action = "approve" }, default))).Success);
            string ok = await host.As(Seeder, sp => SpecialProcessGate.SupplierProblemAsync(sp.GetRequiredService<IUnitOfWork>(), provider, host.Read(u => u.Set<SpProcessType>().Single(t => t.Id == p.TypeId)), DateTime.Today, default));
            Assert.IsNull(ok);
            string late = await host.As(Seeder, sp => SpecialProcessGate.SupplierProblemAsync(sp.GetRequiredService<IUnitOfWork>(), provider, host.Read(u => u.Set<SpProcessType>().Single(t => t.Id == p.TypeId)), DateTime.Today.AddDays(400), default));
            Assert.IsNotNull(late, "outside the validity");
            Assert.IsNotNull(await host.As(Seeder, sp => SpecialProcessGate.SupplierProblemAsync(sp.GetRequiredService<IUnitOfWork>(), provider + 99, host.Read(u => u.Set<SpProcessType>().Single(t => t.Id == p.TypeId)), DateTime.Today, default)), "another supplier is not approved");
        }

        [TestMethod]
        public async Task ASubcontractedStepNeedsAnApprovedSupplierAndTheEvidenceOfTheRun()
        {
            AppHost host = NewHost("subrun");
            Plant p = await NewPlant(host, false);
            await SetHtMasterLimits(host, p.TypeId);
            int provider = 0;
            host.Write(u => { var pr = AppHost.Add(u, new Provider { Name = "Coating Co" }); u.SaveChanges(); provider = pr.Id; }, Seeder);
            int op = await NewSubcontractStep(host, p, provider);
            SaveSpRunDto Sub() => new SaveSpRunDto
            {
                OperationId = op, IsSubcontract = true, SupplierId = provider, Quantity = 10m, StartedAt = DateTime.Now.AddHours(-30), EndedAt = DateTime.Now.AddHours(-10),
                ChartDocumentId = NewFile(host), CertificateDocumentId = NewFile(host), CertificateNumber = "SUP-CERT-1", Values = GoodRunValues(),
            };
            Assert.IsFalse((await Sp(host, Qc, s => s.SaveRunAsync(Sub(), default))).Success, "the supplier is not approved for the process");
            var appr = await Sp(host, Commercial, s => s.SaveSupplierApprovalAsync(new SaveSpSupplierApprovalDto { ProviderId = provider, TypeId = p.TypeId, ValidFrom = DateTime.Today.AddDays(-1), ValidTo = DateTime.Today.AddDays(300), AccreditationNumber = "AC-9" }, default));
            Assert.IsTrue((await Sp(host, Qa, s => s.SupplierApprovalActionAsync(new SpActionDto { Id = appr.Data.Id, Action = "approve" }, default))).Success);
            Assert.IsFalse((await Sp(host, Qc, s => { var d = Sub(); d.CertificateDocumentId = null; return s.SaveRunAsync(d, default); })).Success, "the certificate file is evidence");
            Assert.IsFalse((await Sp(host, Qc, s => { var d = Sub(); d.ChartDocumentId = null; return s.SaveRunAsync(d, default); })).Success, "the chart is evidence");
            Assert.IsFalse((await Sp(host, Qc, s => { var d = Sub(); d.IsSubcontract = false; d.QualificationId = p.QualId; return s.SaveRunAsync(d, default); })).Success, "an outside step takes the supplier's run, not an inside one");
            var run = await Sp(host, Qc, s => s.SaveRunAsync(Sub(), default));
            Assert.IsTrue(run.Success, run.Message);
            Assert.IsTrue((await Sp(host, Qa, s => s.RunActionAsync(new SpActionDto { Id = run.Data.Id, Action = "review" }, default))).Success);
            string gate = await host.As(Seeder, async sp =>
            {
                IUnitOfWork uow = sp.GetRequiredService<IUnitOfWork>();
                return await SpecialProcessGate.FinishProblemAsync(uow, uow.Set<MfProductionOrder>().Single(o => o.Id == p.OrderId), uow.Set<MfOperation>().Single(o => o.Id == op), 10m, 0m, default);
            });
            Assert.IsNull(gate, gate);
        }

        [TestMethod]
        public async Task TheReceiptOfASubcontractedSpecialStepIsNotAcceptedWithoutTheSuppliersRun()
        {
            AppHost host = NewHost("receipt");
            Plant p = await NewPlant(host, false);
            await SetHtMasterLimits(host, p.TypeId);
            int provider = 0, receipt = 0;
            host.Write(u => { var pr = AppHost.Add(u, new Provider { Name = "Plating Co" }); u.SaveChanges(); provider = pr.Id; }, Seeder);
            int op = await NewSubcontractStep(host, p, provider);
            host.Write(u =>
            {
                var d = AppHost.Add(u, new InventoryDocument { Number = "SR-" + Guid.NewGuid().ToString("N").Substring(0, 5), Type = InventoryDocumentType.SubcontractReturn, WorkOrderId = p.WorkOrderId, ProviderId = provider, ReferenceNumber = p.OrderNumber + "/30", Status = InventoryDocumentStatus.Confirmed });
                u.SaveChanges(); receipt = d.Id;
            }, Seeder);
            string before = await host.As(Seeder, async sp => { IUnitOfWork uow = sp.GetRequiredService<IUnitOfWork>(); return await SpecialProcessGate.ReceiptEvidenceProblemAsync(uow, uow.Set<InventoryDocument>().Single(d => d.Id == receipt), default); });
            Assert.IsNotNull(before, "no run, no certificate: no acceptance");
            var appr = await Sp(host, Commercial, s => s.SaveSupplierApprovalAsync(new SaveSpSupplierApprovalDto { ProviderId = provider, TypeId = p.TypeId, ValidFrom = DateTime.Today.AddDays(-1), ValidTo = DateTime.Today.AddDays(300), AccreditationNumber = "AC-9" }, default));
            Assert.IsTrue((await Sp(host, Qa, s => s.SupplierApprovalActionAsync(new SpActionDto { Id = appr.Data.Id, Action = "approve" }, default))).Success);
            var run = await Sp(host, Qc, s => s.SaveRunAsync(new SaveSpRunDto
            {
                OperationId = op, IsSubcontract = true, SupplierId = provider, Quantity = 10m, StartedAt = DateTime.Now.AddHours(-30), EndedAt = DateTime.Now.AddHours(-10), ChartDocumentId = NewFile(host), CertificateDocumentId = NewFile(host),
                CertificateNumber = "SUP-CERT-2", Values = GoodRunValues(),
            }, default));
            Assert.IsTrue(run.Success, run.Message);
            string unreviewed = await host.As(Seeder, async sp => { IUnitOfWork uow = sp.GetRequiredService<IUnitOfWork>(); return await SpecialProcessGate.ReceiptEvidenceProblemAsync(uow, uow.Set<InventoryDocument>().Single(d => d.Id == receipt), default); });
            Assert.IsNotNull(unreviewed, "a run the QA has not reviewed is not evidence for the receipt");
            Assert.IsTrue((await Sp(host, Qa, s => s.RunActionAsync(new SpActionDto { Id = run.Data.Id, Action = "review" }, default))).Success);
            string after = await host.As(Seeder, async sp => { IUnitOfWork uow = sp.GetRequiredService<IUnitOfWork>(); return await SpecialProcessGate.ReceiptEvidenceProblemAsync(uow, uow.Set<InventoryDocument>().Single(d => d.Id == receipt), default); });
            Assert.IsNull(after, after);
        }

        // ------------------------------------------------------------------------------------------------ material specification

        private static SaveMaterialSpecDto SpecDto(int[] items = null, CertificateType type = CertificateType.T31) => new SaveMaterialSpecDto
        {
            Grade = "S355J2", Standard = "EN 10025-2", Title = "structural steel", RequiredCertificateType = type, ItemIds = (items ?? new int[0]).ToList(),
            Limits = new List<MaterialSpecLimitDto>
            {
                new MaterialSpecLimitDto { Kind = CertValueKind.Chemistry, Name = "C", Unit = "%", Max = 0.20m },
                new MaterialSpecLimitDto { Kind = CertValueKind.Chemistry, Name = "Mn", Unit = "%", Max = 1.60m, Required = false },
                new MaterialSpecLimitDto { Kind = CertValueKind.Mechanical, Name = "Yield", Unit = "MPa", Min = 355m },
            },
        };

        private static async Task<int> ApprovedSpec(AppHost host, int[] items = null, CertificateType type = CertificateType.T31)
        {
            var made = await Ms(host, Maker, s => s.SaveAsync(SpecDto(items, type), default));
            Assert.IsTrue(made.Success, made.Message);
            Assert.IsTrue((await Ms(host, Maker, s => s.ActionAsync(new SpActionDto { Id = made.Data.Id, Action = "submit" }, default))).Success);
            var ok = await Ms(host, Qa, s => s.ActionAsync(new SpActionDto { Id = made.Data.Id, Action = "approve" }, default));
            Assert.IsTrue(ok.Success, ok.Message);
            return made.Data.Id;
        }

        [TestMethod]
        public async Task ASpecificationIsApprovedByASecondPersonAndAnAiSeatDoesNotApprove()
        {
            AppHost host = NewHost("specmc");
            var made = await Ms(host, Maker, s => s.SaveAsync(SpecDto(), default));
            Assert.IsTrue(made.Success, made.Message);
            Assert.IsFalse(string.IsNullOrEmpty(made.Data.ContentHash));
            Assert.IsFalse((await Ms(host, Maker, s => s.ActionAsync(new SpActionDto { Id = made.Data.Id, Action = "approve" }, default))).Success, "not submitted, and not the maker");
            Assert.IsTrue((await Ms(host, Maker, s => s.ActionAsync(new SpActionDto { Id = made.Data.Id, Action = "submit" }, default))).Success);
            Assert.IsFalse((await Ms(host, Maker, s => s.ActionAsync(new SpActionDto { Id = made.Data.Id, Action = "approve" }, default))).Success, "the maker never approves");
            var seat = new AiActingUser { UserId = 97, UserName = "seat.qa2", Roles = new[] { "QA" } };
            var ai = await AiActing.RunAsync(host.Provider.GetRequiredService<IServiceScopeFactory>(), seat, "SEAT-QA2", sp => sp.GetRequiredService<IMaterialSpecService>().ActionAsync(new SpActionDto { Id = made.Data.Id, Action = "approve" }, default));
            Assert.IsFalse(ai.Success, "an AI seat does not approve");
            Assert.IsTrue((await Ms(host, Qa, s => s.ActionAsync(new SpActionDto { Id = made.Data.Id, Action = "approve" }, default))).Success);
        }

        [TestMethod]
        public async Task ACorrectionIsANewVersionThatSupersedesTheApprovedOne()
        {
            AppHost host = NewHost("specver");
            int v1 = await ApprovedSpec(host);
            Assert.IsFalse((await Ms(host, Maker, s => s.SaveAsync(SpecDto(), default))).Success, "the family exists: a new version is asked for explicitly");
            var noReason = await Ms(host, Maker, s => { var d = SpecDto(); d.NewVersionOfId = v1; return s.SaveAsync(d, default); });
            Assert.IsFalse(noReason.Success, "a reason is required");
            var v2 = await Ms(host, Maker, s => { var d = SpecDto(); d.NewVersionOfId = v1; d.Reason = "yield limit corrected by the standard"; d.Limits[2].Min = 360m; return s.SaveAsync(d, default); });
            Assert.IsTrue(v2.Success, v2.Message);
            Assert.AreEqual(2, v2.Data.Version);
            Assert.IsTrue((await Ms(host, Maker, s => s.ActionAsync(new SpActionDto { Id = v2.Data.Id, Action = "submit" }, default))).Success);
            Assert.IsTrue((await Ms(host, Qa, s => s.ActionAsync(new SpActionDto { Id = v2.Data.Id, Action = "approve" }, default))).Success);
            Assert.AreEqual(MatSpecStatus.Superseded, host.Read(u => u.Set<MaterialSpec>().Single(x => x.Id == v1).Status));
            Assert.AreEqual(1, host.Read(u => u.Set<MaterialSpec>().Count(x => x.Status == MatSpecStatus.Approved)), "one approved version per grade and standard");
        }

        [TestMethod]
        public async Task ALimitRowWithoutAnyLimitOrWithMinAboveMaxIsRefused()
        {
            AppHost host = NewHost("specbad");
            Assert.IsFalse((await Ms(host, Maker, s => { var d = SpecDto(); d.Limits[0].Max = null; return s.SaveAsync(d, default); })).Success, "no min and no max");
            Assert.IsFalse((await Ms(host, Maker, s => { var d = SpecDto(); d.Limits[2].Min = 500m; d.Limits[2].Max = 400m; return s.SaveAsync(d, default); })).Success);
            Assert.IsFalse((await Ms(host, Maker, s => { var d = SpecDto(); d.Limits.Add(new MaterialSpecLimitDto { Kind = CertValueKind.Chemistry, Name = "c", Max = 1m }); return s.SaveAsync(d, default); })).Success, "duplicate name");
            Assert.IsFalse((await Ms(host, Qc, s => s.SaveAsync(SpecDto(), default))).Success, "QC does not write the specification");
        }

        // ------------------------------------------------------------------------------------------------ certificate

        private static int NewItemWithLot(AppHost host, string heat, out int lot)
        {
            int item = 0, lotId = 0;
            host.Write(u =>
            {
                var i = AppHost.Add(u, new InventoryItem { Code = "IT-" + Guid.NewGuid().ToString("N").Substring(0, 6), Title = "plate", PartNumber = "PL-" + Guid.NewGuid().ToString("N").Substring(0, 4) });
                u.SaveChanges();
                var l = AppHost.Add(u, new InventoryLot { ItemId = i.Id, LotNumber = "L-" + Guid.NewGuid().ToString("N").Substring(0, 5), HeatNumber = heat });
                u.SaveChanges();
                item = i.Id; lotId = l.Id;
            }, Seeder);
            lot = lotId;
            return item;
        }

        /// <summary>A stored file row. It does not touch the signed-in user of the host (it is called inside a call made as somebody else).</summary>
        private static Guid NewFile(AppHost host)
        {
            using (IServiceScope scope = host.Provider.CreateScope())
            {
                IUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var d = new Entities.Model.Document("files/" + Guid.NewGuid().ToString("N") + ".pdf", "application/pdf");
                uow.Set<Entities.Model.Document>().Add(d);
                uow.SaveChanges();
                return d.Id;
            }
        }

        private static SaveMaterialCertificateDto CertDto(int lot, Guid file, decimal c = 0.18m, decimal yield = 380m, string heat = "H-501", CertificateType type = CertificateType.T31) => new SaveMaterialCertificateDto
        {
            LotId = lot, Type = type, Number = "MTC-" + Guid.NewGuid().ToString("N").Substring(0, 5), Issuer = "Mill", DocumentId = file, CertHeatNumber = heat, Grade = "S355J2", Standard = "EN 10025-2",
            ManufacturerSignatory = type == CertificateType.T31 ? "A. Signatory" : null, InspectorOrganisation = type == CertificateType.T32 ? "Inspection Body" : null, InspectorName = type == CertificateType.T32 ? "I. Inspector" : null,
            Values = new List<SaveCertificateValueDto>
            {
                new SaveCertificateValueDto { Kind = CertValueKind.Chemistry, Name = "C", Unit = "%", Value = c }, new SaveCertificateValueDto { Kind = CertValueKind.Mechanical, Name = "Yield", Unit = "MPa", Value = yield },
            },
        };

        private static Task<ResponseDto<T>> Mat<T>(AppHost host, ClaimsPrincipal who, Func<IMaterialService, Task<ResponseDto<T>>> call) => host.As(who, p => call(p.GetRequiredService<IMaterialService>()));

        [TestMethod]
        public async Task TheRequiredCertificateTypeComesFromTheSpecificationAndTheBomLineNotFromTheReceipt()
        {
            AppHost host = NewHost("required");
            int item = NewItemWithLot(host, "H-501", out int lot);
            var none = await Ms(host, Qc, s => s.RequiredAsync(item, default));
            Assert.IsFalse(none.Data.Type.HasValue, "no specification, no BOM requirement");
            await ApprovedSpec(host, new[] { item }, CertificateType.T32);
            var linked = await Ms(host, Qc, s => s.RequiredAsync(item, default));
            Assert.AreEqual(CertificateType.T32, linked.Data.Type);
            Assert.AreEqual("3.2", linked.Data.TypeText);
            Assert.IsTrue(linked.Data.Sources.Any(x => x.Contains("S355J2")));
            // a second item is found through its released BOM line (material and standard text)
            int bomItem = NewItemWithLot(host, "H-9", out _);
            host.Write(u =>
            {
                var header = AppHost.Add(u, new Entities.Model.NasbNiro.ProductBank.PbBomHeader { Code = "BOM-" + Guid.NewGuid().ToString("N").Substring(0, 5), Status = Entities.Model.NasbNiro.ProductBank.PbBomStatus.Released });
                u.SaveChanges();
                AppHost.Add(u, new Entities.Model.NasbNiro.ProductBank.PbBomLine { BomHeaderId = header.Id, ItemId = bomItem, Material = "s355j2", Standard = "EN 10025 2" });
                u.SaveChanges();
            }, Seeder);
            var viaBom = await Ms(host, Qc, s => s.RequiredAsync(bomItem, default));
            Assert.AreEqual(CertificateType.T32, viaBom.Data.Type, "the BOM line material and standard find the specification");
            Assert.IsTrue(viaBom.Data.Sources.Any(x => x.Contains("BOM")));
        }

        [TestMethod]
        public async Task TheReceiptGateJudgesTheCertificateAndRefusesAnOutOfLimitValueAnotherHeatOrNoSpecification()
        {
            AppHost host = NewHost("receiptcert");
            int item = NewItemWithLot(host, "H-501", out int lot);
            // no specification for the grade: a 3.1 certificate cannot be judged
            var orphan = await Mat(host, Qc, m => m.SaveCertificateAsync(CertDto(lot, NewFile(host)), default));
            Assert.IsTrue(orphan.Success, orphan.Message);
            Assert.AreEqual(CertRowStatus.Fail, orphan.Data.Evaluation);
            Assert.IsTrue(orphan.Data.EvaluationText.Contains("مشخصات ماده"));
            await ApprovedSpec(host, new[] { item });
            Assert.IsNotNull(await host.As(Seeder, sp => sp.GetRequiredService<IMaterialService>().CheckRequiredCertificateAsync(item, lot, default)), "required 3.1 and nothing accepted");

            var high = await Mat(host, Qc, m => m.SaveCertificateAsync(CertDto(lot, NewFile(host), c: 0.25m), default));
            Assert.AreEqual(CertRowStatus.Fail, high.Data.Evaluation, "C 0.25 is above 0.20");
            Assert.IsTrue(high.Data.Values.Single(v => v.Name == "C").Status == CertRowStatus.Fail);
            var refused = await Mat(host, Qc2, m => m.VerifyCertificateAsync(new VerifyCertificateDto { Id = high.Data.Id, Accept = true }, default));
            Assert.IsFalse(refused.Success, "QC cannot accept a certificate whose value is out of limit");
            Assert.IsTrue(refused.Message.Contains("0.25") || refused.Message.Contains("خارج"), refused.Message);

            var otherHeat = await Mat(host, Qc, m => m.SaveCertificateAsync(CertDto(lot, NewFile(host), heat: "H-502"), default));
            Assert.AreEqual(CertRowStatus.Fail, otherHeat.Data.Evaluation);
            Assert.IsTrue(otherHeat.Data.EvaluationText.Contains("H-502") && otherHeat.Data.EvaluationText.Contains("H-501"));
            Assert.IsFalse((await Mat(host, Qc2, m => m.VerifyCertificateAsync(new VerifyCertificateDto { Id = otherHeat.Data.Id, Accept = true }, default))).Success);

            var good = await Mat(host, Qc, m => m.SaveCertificateAsync(CertDto(lot, NewFile(host)), default));
            Assert.AreEqual(CertRowStatus.Pass, good.Data.Evaluation, good.Data.EvaluationText);
            Assert.IsNotNull(await host.As(Seeder, sp => sp.GetRequiredService<IMaterialService>().CheckRequiredCertificateAsync(item, lot, default)), "not yet accepted by QC");
            Assert.IsFalse((await Mat(host, Qc, m => m.VerifyCertificateAsync(new VerifyCertificateDto { Id = good.Data.Id, Accept = true }, default))).Success, "the entering person does not verify");
            var accepted = await Mat(host, Qc2, m => m.VerifyCertificateAsync(new VerifyCertificateDto { Id = good.Data.Id, Accept = true }, default));
            Assert.IsTrue(accepted.Success, accepted.Message);
            Assert.IsNull(await host.As(Seeder, sp => sp.GetRequiredService<IMaterialService>().CheckRequiredCertificateAsync(item, lot, default)), "accepted, judged and inside the limits");
        }

        [TestMethod]
        public async Task AType32NeedsTheInspectorAndTheLotWithoutAHeatNumberCannotBeMatched()
        {
            AppHost host = NewHost("cert32");
            int item = NewItemWithLot(host, "H-601", out int lot);
            await ApprovedSpec(host, new[] { item }, CertificateType.T32);
            var no = CertDto(lot, NewFile(host), type: CertificateType.T32); no.InspectorOrganisation = null;
            var bad = await Mat(host, Qc, m => m.SaveCertificateAsync(no, default));
            Assert.AreEqual(CertRowStatus.Fail, bad.Data.Evaluation);
            var yes = await Mat(host, Qc, m => m.SaveCertificateAsync(CertDto(lot, NewFile(host), heat: "H-601", type: CertificateType.T32), default));
            Assert.AreEqual(CertRowStatus.Pass, yes.Data.Evaluation, yes.Data.EvaluationText);
            int itemNoHeat = NewItemWithLot(host, null, out int lotNoHeat);
            var unmatched = await Mat(host, Qc, m => m.SaveCertificateAsync(CertDto(lotNoHeat, NewFile(host)), default));
            Assert.AreEqual(CertRowStatus.Fail, unmatched.Data.Evaluation, "the lot has no heat number");
        }

        [TestMethod]
        public async Task AType22CertificateStaysAsItWasAndTheOldPerItemRequirementStillCounts()
        {
            AppHost host = NewHost("cert22");
            int item = NewItemWithLot(host, "H-700", out int lot);
            host.Write(u => { u.Set<InventoryItem>().Single(i => i.Id == item).RequiredCertificateType = CertificateType.T22; u.SaveChanges(); }, Seeder);
            Assert.IsNotNull(await host.As(Seeder, sp => sp.GetRequiredService<IMaterialService>().CheckRequiredCertificateAsync(item, lot, default)));
            var cert = await Mat(host, Qc, m => m.SaveCertificateAsync(new SaveMaterialCertificateDto { LotId = lot, Type = CertificateType.T22, Number = "C22", DocumentId = NewFile(host) }, default));
            Assert.IsTrue(cert.Success, cert.Message);
            Assert.AreEqual(CertRowStatus.NotJudged, cert.Data.Evaluation);
            Assert.IsTrue((await Mat(host, Qc2, m => m.VerifyCertificateAsync(new VerifyCertificateDto { Id = cert.Data.Id, Accept = true }, default))).Success);
            Assert.IsNull(await host.As(Seeder, sp => sp.GetRequiredService<IMaterialService>().CheckRequiredCertificateAsync(item, lot, default)), "a 2.2 certificate meets a 2.2 requirement without values");
        }

        [TestMethod]
        public async Task TheCertificateValuesImportFromExcelPreviewsAndThenWrites()
        {
            AppHost host = NewHost("certxlsx");
            int item = NewItemWithLot(host, "H-501", out int lot);
            await ApprovedSpec(host, new[] { item });
            var cert = await Mat(host, Qc, m => m.SaveCertificateAsync(new SaveMaterialCertificateDto { LotId = lot, Type = CertificateType.T31, Number = "MTC-X1", DocumentId = NewFile(host), CertHeatNumber = "H-501", Grade = "S355J2", Standard = "EN 10025-2", ManufacturerSignatory = "A. Signatory" }, default));
            Assert.AreEqual(CertRowStatus.Fail, cert.Data.Evaluation, "no values yet");
            byte[] template = host.Provider.GetRequiredService<IServiceScopeFactory>().CreateScope().ServiceProvider.GetRequiredService<IMaterialSpecService>().CertificateTemplate().Data;
            var preview = await Ms(host, Qc, s => s.ImportValuesAsync(new CertExcelRequestDto { CertificateId = cert.Data.Id, Preview = true }, template, default));
            Assert.IsTrue(preview.Success, preview.Message);
            Assert.AreEqual(2, preview.Data.ValidRows);
            Assert.AreEqual(0, host.Read(u => u.Set<CertificateValue>().Count(v => v.CertificateId == cert.Data.Id)), "a preview writes nothing");
            var applied = await Ms(host, Qc, s => s.ImportValuesAsync(new CertExcelRequestDto { CertificateId = cert.Data.Id, Preview = false }, template, default));
            Assert.IsTrue(applied.Success, applied.Message);
            Assert.AreEqual(CertRowStatus.Pass, applied.Data.Certificate.Evaluation, applied.Data.Certificate.EvaluationText);
            Assert.AreEqual(2, applied.Data.Certificate.Values.Count);
            var broken = await Ms(host, Qc, s => s.ImportValuesAsync(new CertExcelRequestDto { CertificateId = cert.Data.Id, Preview = false }, new byte[] { 1, 2, 3 }, default));
            Assert.IsFalse(broken.Success, "a file that is not xlsx is refused");
        }

        [TestMethod]
        public async Task TheVerificationSheetListsTheProblemsOfACertificate()
        {
            AppHost host = NewHost("sheet");
            int item = NewItemWithLot(host, "H-501", out int lot);
            await ApprovedSpec(host, new[] { item });
            var bad = await Mat(host, Qc, m => m.SaveCertificateAsync(CertDto(lot, NewFile(host), c: 0.3m), default));
            var sheet = await Ms(host, Qc, s => s.SheetAsync(bad.Data.Id, default));
            Assert.IsTrue(sheet.Success, sheet.Message);
            Assert.IsFalse(sheet.Data.ReceiptAcceptable);
            Assert.IsTrue(sheet.Data.Problems.Count >= 1);
            Assert.AreEqual(CertificateType.T31, sheet.Data.Required.Type);
            Assert.IsNotNull(sheet.Data.Spec);
            var good = await Mat(host, Qc, m => m.SaveCertificateAsync(CertDto(lot, NewFile(host)), default));
            Assert.IsTrue((await Mat(host, Qc2, m => m.VerifyCertificateAsync(new VerifyCertificateDto { Id = good.Data.Id, Accept = true }, default))).Success);
            var ok = await Ms(host, Qc, s => s.SheetAsync(good.Data.Id, default));
            Assert.IsTrue(ok.Data.ReceiptAcceptable, string.Join(" | ", ok.Data.Problems));
        }

        // ------------------------------------------------------------------------------------------------ fix round 1

        private static SaveSpTypeDto TypeSave(SpTypeDto t, Action<SaveSpTypeDto> change = null)
        {
            var d = new SaveSpTypeDto
            {
                Id = t.Id, Code = t.Code, Title = t.Title, Description = t.Description, RequiresMachine = t.RequiresMachine, RequiresSurvey = t.RequiresSurvey, RequiredInstrumentClass = t.RequiredInstrumentClass,
                ProcedureDeliverableId = t.ProcedureDeliverableId, ExistingControl = t.ExistingControl, IsActive = t.IsActive, RowVersion = t.RowVersion,
                Parameters = t.Parameters.Select(x => new SpParameterDto { Id = x.Id, SortOrder = x.SortOrder, Name = x.Name, Unit = x.Unit, Min = x.Min, Max = x.Max, Required = x.Required }).ToList(),
            };
            change?.Invoke(d);
            return d;
        }

        private static async Task<SpTypeDto> TypeOf(AppHost host, string code) =>
            (await Sp(host, Maker, s => s.GetTypesAsync(new SpTypeQuery { IncludeInactive = true }, default))).Data.Single(t => t.Code == code);

        [TestMethod]
        public async Task AChangeThatWeakensAProcessTypeIsOnlyARequestUntilASecondPersonApprovesIt()
        {
            AppHost host = NewHost("typeweaken");
            SpTypeDto ht = await TypeOf(host, "HT");
            Assert.IsFalse((await Sp(host, Maker, s => s.SaveTypeAsync(TypeSave(ht, d => d.RequiresSurvey = false), default))).Success, "a weakening needs a reason");
            var asked = await Sp(host, Maker, s => s.SaveTypeAsync(TypeSave(ht, d => { d.RequiresSurvey = false; d.Reason = "the customer surveys the furnace"; }), default));
            Assert.IsTrue(asked.Success, asked.Message);
            Assert.IsTrue(asked.Data.RequiresSurvey, "nothing changes before the second person decides");
            Assert.IsTrue(asked.Data.PendingChangeId.HasValue);
            Assert.IsTrue(asked.Data.PendingSummary.Contains("TUS"));
            int change = asked.Data.PendingChangeId.Value;
            Assert.IsFalse((await Sp(host, Maker, s => s.SaveTypeAsync(TypeSave(ht, d => d.Title = "other title"), default))).Success, "no other change while one waits");
            Assert.IsFalse((await Sp(host, Maker, s => s.TypeChangeActionAsync(new SpActionDto { Id = change, Action = "approve" }, default))).Success, "engineering does not decide");
            Assert.IsFalse((await Sp(host, Qc, s => s.TypeChangeActionAsync(new SpActionDto { Id = change, Action = "approve" }, default))).Success, "QC does not decide");
            Assert.IsFalse((await Sp(host, Qa, s => s.TypeChangeActionAsync(new SpActionDto { Id = change, Action = "reject" }, default))).Success, "a rejection needs a reason");
            var rejected = await Sp(host, Qa, s => s.TypeChangeActionAsync(new SpActionDto { Id = change, Action = "reject", Note = "survey stays" }, default));
            Assert.IsTrue(rejected.Success, rejected.Message);
            Assert.IsTrue(rejected.Data.RequiresSurvey);
            Assert.IsNull(rejected.Data.PendingChangeId);

            ht = await TypeOf(host, "HT");
            var again = await Sp(host, Qa, s => s.SaveTypeAsync(TypeSave(ht, d => { d.RequiresSurvey = false; d.Reason = "second request"; }), default));
            Assert.IsTrue(again.Success, again.Message);
            int second = again.Data.PendingChangeId.Value;
            Assert.IsFalse((await Sp(host, Qa, s => s.TypeChangeActionAsync(new SpActionDto { Id = second, Action = "approve" }, default))).Success, "the requester never approves his own request");
            var seat = new AiActingUser { UserId = 99, UserName = "seat.qa", Roles = new[] { "QA" } };
            var ai = await AiActing.RunAsync(host.Provider.GetRequiredService<IServiceScopeFactory>(), seat, "SEAT-QA", sp => sp.GetRequiredService<ISpecialProcessService>().TypeChangeActionAsync(new SpActionDto { Id = second, Action = "approve" }, default));
            Assert.IsFalse(ai.Success, "an AI seat never decides");
            var approved = await Sp(host, Qa2, s => s.TypeChangeActionAsync(new SpActionDto { Id = second, Action = "approve" }, default));
            Assert.IsTrue(approved.Success, approved.Message);
            Assert.IsFalse(approved.Data.RequiresSurvey, "approved by a second person: applied");
            Assert.AreEqual(2, host.Read(u => u.Set<SpTypeChange>().Count(c => c.TypeId == ht.Id)), "both requests are kept for ever");
            Assert.AreEqual(1, host.Read(u => u.Set<SpTypeChange>().Count(c => c.TypeId == ht.Id && c.Status == SpChangeStatus.Approved && c.DecidedByUserId == 53)));

            ht = await TypeOf(host, "HT");
            var back = await Sp(host, Maker, s => s.SaveTypeAsync(TypeSave(ht, d => d.RequiresSurvey = true), default));
            Assert.IsTrue(back.Success && back.Data.RequiresSurvey && !back.Data.PendingChangeId.HasValue, "a strengthening applies at once");
        }

        [TestMethod]
        public async Task EveryKindOfWeakeningOfAProcessTypeWaitsAndStrengtheningDoesNot()
        {
            AppHost host = NewHost("typekinds");
            SpTypeDto ht0 = await TypeOf(host, "HT");
            await SetHtMasterLimits(host, ht0.Id);
            var weakening = new List<Action<SaveSpTypeDto>>
            {
                d => d.RequiresMachine = false, d => d.RequiredInstrumentClass = null, d => d.IsActive = false, d => d.Parameters[0].Max = 900m, d => d.Parameters[0].Min = 800m, d => d.Parameters[0].Max = null,
                d => d.Parameters.RemoveAt(3), d => d.Parameters[1].Required = false,
            };
            int n = 0;
            foreach (var change in weakening)
            {
                SpTypeDto ht = await TypeOf(host, "HT");
                var asked = await Sp(host, Maker, s => s.SaveTypeAsync(TypeSave(ht, d => { change(d); d.Reason = "case " + n; }), default));
                Assert.IsTrue(asked.Success && asked.Data.PendingChangeId.HasValue, "weakening " + n + ": " + asked.Message);
                var done = await Sp(host, Qa, s => s.TypeChangeActionAsync(new SpActionDto { Id = asked.Data.PendingChangeId.Value, Action = "reject", Note = "no" }, default));
                Assert.IsTrue(done.Success, done.Message);
                n++;
            }
            SpTypeDto now = await TypeOf(host, "HT");
            Assert.IsTrue(now.RequiresMachine && now.IsActive && now.RequiredInstrumentClass.HasValue && now.Parameters.Count == 4, "nothing was applied");
            var narrower = await Sp(host, Maker, s => s.SaveTypeAsync(TypeSave(now, d => { d.Parameters[0].Min = 845m; d.Parameters[0].Max = 855m; d.Parameters[2].Required = true; d.Parameters.Add(new SpParameterDto { Name = "Atmosphere", Unit = "-", Max = 5m }); }), default));
            Assert.IsTrue(narrower.Success && !narrower.Data.PendingChangeId.HasValue, narrower.Message);
            Assert.AreEqual(5, narrower.Data.Parameters.Count, "narrowing a limit and adding a parameter only strengthen");
        }

        [TestMethod]
        public async Task TheCodeOfAUsedTypeAndTheExistingControlFlagAreFixed()
        {
            AppHost host = NewHost("typecode");
            Plant p = await NewPlant(host, false);
            SpTypeDto ht = await TypeOf(host, "HT");
            var renamed = await Sp(host, Maker, s => s.SaveTypeAsync(TypeSave(ht, d => d.Code = "HT9"), default));
            Assert.IsFalse(renamed.Success, "a qualification uses the code");
            Assert.IsTrue(renamed.Message.Contains("استفاده شده"), renamed.Message);
            SpTypeDto nit = await TypeOf(host, "NIT"), ndt = await TypeOf(host, "NDT");
            var unused = await Sp(host, Maker, s => s.SaveTypeAsync(TypeSave(nit, d => d.Code = "NIT2"), default));
            Assert.IsTrue(unused.Success, "a type nothing uses may still be renamed: " + unused.Message);
            Assert.IsFalse((await Sp(host, Maker, s => s.SaveTypeAsync(TypeSave(ht, d => d.ExistingControl = true), default))).Success, "the existing-control flag is not switched on through the API");
            Assert.IsFalse((await Sp(host, Maker, s => s.SaveTypeAsync(TypeSave(ndt, d => d.ExistingControl = false), default))).Success, "nor off");
            Assert.IsFalse((await Sp(host, Maker, s => s.SaveTypeAsync(new SaveSpTypeDto { Code = "NEW1", Title = "new", RequiresMachine = false, ExistingControl = true, Parameters = new List<SpParameterDto>() }, default))).Success, "nor on creation");
            Assert.IsTrue((await Sp(host, Maker, s => s.SaveTypeAsync(new SaveSpTypeDto { Code = "NEW1", Title = "new", RequiresMachine = false, Parameters = new List<SpParameterDto>() }, default))).Success);
        }

        [TestMethod]
        public async Task AQualificationMustCarryEveryParameterTheMasterRequires()
        {
            AppHost host = NewHost("allparams");
            Plant p = await NewPlant(host, false);
            var missing = await Sp(host, Maker, s => s.SaveQualificationAsync(new SaveSpQualDto
            {
                Id = p.QualId, TypeId = p.TypeId, ProcedureDeliverableId = p.ProcDeliverable, ProcedureRevisionId = p.ProcRevision, ValidFrom = DateTime.Today, ValidTo = DateTime.Today.AddDays(100),
                Limits = HtLimits().Take(2).ToList(), OperatorPersonIds = new List<int> { p.PersonId }, Equipments = new List<SaveSpQualEquipmentDto>(),
            }, default));
            Assert.IsFalse(missing.Success);
            Assert.IsTrue(missing.Message.Contains("نرخ گرمایش"), "the missing required parameter is named: " + missing.Message);
        }

        private static SaveSpRunDto DatedRun(Plant p, int daysAgo)
        {
            SaveSpRunDto d = RunDto(p);
            d.StartedAt = DateTime.Now.AddDays(-daysAgo).AddHours(-3); d.EndedAt = DateTime.Now.AddDays(-daysAgo).AddHours(-1);
            return d;
        }

        [TestMethod]
        public async Task ARunIsJudgedByTheStateAtTheRunSoARenewalOrALaterCalibrationLapseDoesNotBlockTheFinish()
        {
            AppHost host = NewHost("history");
            Plant p0 = await NewPlant(host, true);
            // the instrument was calibrated on the day of the run (3 days ago) and fell overdue two days ago (a run of "today" starts 3 hours ago: just after midnight that is still yesterday)
            host.Write(u =>
            {
                var tc = u.Set<MeasuringEquipment>().Single(e => e.Id == p0.InstrumentId); tc.RequiresCalibration = true;
                AppHost.Add(u, new CalibrationEvent { EquipmentId = tc.Id, EventDate = DateTime.Today.AddDays(-30), NextDue = DateTime.Today.AddDays(-2), AsLeftPass = true, AsFoundPass = true, IntervalMonths = 1, LabName = "Lab" });
                u.SaveChanges();
            }, Seeder);
            var run = await Sp(host, Qc, s => s.SaveRunAsync(DatedRun(p0, 3), default));
            Assert.IsTrue(run.Success, "valid on the day of the run: " + run.Message);
            Assert.IsTrue((await Sp(host, Qa, s => s.RunActionAsync(new SpActionDto { Id = run.Data.Id, Action = "review" }, default))).Success);
            Assert.IsNull(await Gate(host, p0), "the instrument is overdue today but was calibrated at the run");
            var today = await Sp(host, Qc, s => s.SaveRunAsync(RunDto(p0), default));
            Assert.IsFalse(today.Success, "a run of today with the lapsed instrument is refused");

            // a renewal supersedes the qualification; the finished runs under the old one keep counting
            var renewal = await Sp(host, Maker, s => s.SaveQualificationAsync(new SaveSpQualDto
            {
                TypeId = p0.TypeId, Title = "renewal", SupersedesId = p0.QualId, ProcedureDeliverableId = p0.ProcDeliverable, ProcedureRevisionId = p0.ProcRevision, ValidFrom = DateTime.Today, ValidTo = DateTime.Today.AddDays(300),
                Limits = HtLimits(), OperatorPersonIds = new List<int> { p0.PersonId },
                Equipments = new List<SaveSpQualEquipmentDto>
                {
                    new SaveSpQualEquipmentDto { Kind = SpEquipmentKind.Machine, MachineId = p0.MachineId, SurveyKind = SpSurveyKind.Tus, SurveyDate = DateTime.Today.AddDays(-20), SurveyDueDate = DateTime.Today.AddDays(160) },
                    new SaveSpQualEquipmentDto { Kind = SpEquipmentKind.Instrument, EquipmentId = p0.InstrumentId },
                },
            }, default));
            Assert.IsTrue(renewal.Success, renewal.Message);
            host.Write(u => { var tc = u.Set<MeasuringEquipment>().Single(e => e.Id == p0.InstrumentId); tc.RequiresCalibration = false; u.SaveChanges(); }, Seeder);
            Assert.IsTrue((await Sp(host, Maker, s => s.QualificationActionAsync(new SpActionDto { Id = renewal.Data.Id, Action = "submit" }, default))).Success);
            var approved = await Sp(host, Qa, s => s.QualificationActionAsync(new SpActionDto { Id = renewal.Data.Id, Action = "approve" }, default));
            Assert.IsTrue(approved.Success, approved.Message);
            Assert.AreEqual(SpQualStatus.Superseded, host.Read(u => u.Set<SpQualification>().Single(q => q.Id == p0.QualId).Status));
            host.Write(u => { var tc = u.Set<MeasuringEquipment>().Single(e => e.Id == p0.InstrumentId); tc.RequiresCalibration = true; u.SaveChanges(); }, Seeder);
            Assert.IsNull(await Gate(host, p0), "the old qualification was valid at the run; its renewal does not undo the run");
            host.Write(u => { u.Set<SpQualification>().Single(q => q.Id == p0.QualId).Status = SpQualStatus.Void; u.SaveChanges(); }, Seeder);
            Assert.IsNotNull(await Gate(host, p0), "a voided qualification does invalidate its runs");
        }

        [TestMethod]
        public async Task ASupplierRenewalSitsBesideTheCurrentApprovalAndSupersedesItOnApproval()
        {
            AppHost host = NewHost("suprenew");
            Plant p = await NewPlant(host, false);
            int provider = 0;
            host.Write(u => { var pr = AppHost.Add(u, new Provider { Name = "Renew Co" }); u.SaveChanges(); provider = pr.Id; }, Seeder);
            SaveSpSupplierApprovalDto Dto(string acc) => new SaveSpSupplierApprovalDto { ProviderId = provider, TypeId = p.TypeId, ValidFrom = DateTime.Today.AddDays(-1), ValidTo = DateTime.Today.AddDays(300), AccreditationNumber = acc };
            var first = await Sp(host, Commercial, s => s.SaveSupplierApprovalAsync(Dto("AC-1"), default));
            Assert.IsTrue((await Sp(host, Qa, s => s.SupplierApprovalActionAsync(new SpActionDto { Id = first.Data.Id, Action = "approve" }, default))).Success);
            var renewal = await Sp(host, Commercial, s => s.SaveSupplierApprovalAsync(Dto("AC-2"), default));
            Assert.IsTrue(renewal.Success, renewal.Message);
            Assert.AreEqual(SpApprovalStatus.Pending, renewal.Data.Status);
            Assert.AreEqual(SpApprovalStatus.Approved, host.Read(u => u.Set<SpSupplierApproval>().Single(a => a.Id == first.Data.Id).Status), "the current approval is not voided by the request");
            Assert.IsFalse((await Sp(host, Commercial, s => s.SaveSupplierApprovalAsync(Dto("AC-3"), default))).Success, "one pending version at a time");
            SpProcessType type = host.Read(u => u.Set<SpProcessType>().Single(t => t.Id == p.TypeId));
            Assert.IsNull(await host.As(Seeder, sp => SpecialProcessGate.SupplierProblemAsync(sp.GetRequiredService<IUnitOfWork>(), provider, type, DateTime.Today, default)), "the supplier stays approved while the renewal waits");
            Assert.IsFalse((await Sp(host, Commercial, s => s.SupplierApprovalActionAsync(new SpActionDto { Id = renewal.Data.Id, Action = "approve" }, default))).Success);
            var approved = await Sp(host, Qa, s => s.SupplierApprovalActionAsync(new SpActionDto { Id = renewal.Data.Id, Action = "approve" }, default));
            Assert.IsTrue(approved.Success, approved.Message);
            Assert.AreEqual(SpApprovalStatus.Superseded, host.Read(u => u.Set<SpSupplierApproval>().Single(a => a.Id == first.Data.Id).Status), "the new version supersedes the old one");
            Assert.AreEqual(1, host.Read(u => u.Set<SpSupplierApproval>().Count(a => a.ProviderId == provider && a.Status == SpApprovalStatus.Approved)));
            Assert.IsNull(await host.As(Seeder, sp => SpecialProcessGate.SupplierProblemAsync(sp.GetRequiredService<IUnitOfWork>(), provider, type, DateTime.Today.AddDays(-1), default)), "a run made under the superseded approval still counts");
            host.Write(u => { foreach (var a in u.Set<SpSupplierApproval>().Where(a => a.ProviderId == provider)) a.Status = SpApprovalStatus.Void; u.SaveChanges(); }, Seeder);
            Assert.IsNotNull(await host.As(Seeder, sp => SpecialProcessGate.SupplierProblemAsync(sp.GetRequiredService<IUnitOfWork>(), provider, type, DateTime.Today, default)), "a voided approval counts for nothing");
        }

        [TestMethod]
        public async Task ASubcontractRunWithoutDefinedLimitsCannotBeRecordedAndMissingFilesAreRefused()
        {
            AppHost host = NewHost("sublimits");
            Plant p = await NewPlant(host, false);
            int provider = 0;
            host.Write(u => { var pr = AppHost.Add(u, new Provider { Name = "Limits Co" }); u.SaveChanges(); provider = pr.Id; }, Seeder);
            int op = await NewSubcontractStep(host, p, provider);
            var appr = await Sp(host, Commercial, s => s.SaveSupplierApprovalAsync(new SaveSpSupplierApprovalDto { ProviderId = provider, TypeId = p.TypeId, ValidFrom = DateTime.Today.AddDays(-1), ValidTo = DateTime.Today.AddDays(300), AccreditationNumber = "AC-L" }, default));
            Assert.IsTrue((await Sp(host, Qa, s => s.SupplierApprovalActionAsync(new SpActionDto { Id = appr.Data.Id, Action = "approve" }, default))).Success);
            SaveSpRunDto Sub() => new SaveSpRunDto
            {
                OperationId = op, IsSubcontract = true, SupplierId = provider, Quantity = 10m, StartedAt = DateTime.Now.AddHours(-30), EndedAt = DateTime.Now.AddHours(-10),
                ChartDocumentId = NewFile(host), CertificateDocumentId = NewFile(host), CertificateNumber = "SUP-L", Values = GoodRunValues(),
            };
            var none = await Sp(host, Qc, s => s.SaveRunAsync(Sub(), default));
            Assert.IsFalse(none.Success, "the master list has no limits: whatever value would pass, so nothing is recorded");
            Assert.IsTrue(none.Message.Contains("حد قبول تعریف نشده"), none.Message);
            await SetHtMasterLimits(host, p.TypeId);
            Assert.IsTrue((await Sp(host, Qc, s => s.SaveRunAsync(Sub(), default))).Success, "with limits the run is judged");
            var missingFile = await Sp(host, Qc, s => { var d = Sub(); d.ChartDocumentId = Guid.NewGuid(); return s.SaveRunAsync(d, default); });
            Assert.IsFalse(missingFile.Success);
            Assert.IsTrue(missingFile.Message.Contains("پیدا نشد"), missingFile.Message);
            Assert.IsFalse((await Sp(host, Commercial, s => s.SaveSupplierApprovalAsync(new SaveSpSupplierApprovalDto { ProviderId = provider, TypeId = p.TypeId, ValidFrom = DateTime.Today, ValidTo = DateTime.Today.AddDays(50), EvidenceDocumentId = Guid.NewGuid() }, default))).Success, "evidence file that does not exist");
            Guid exe; using (IServiceScope scope = host.Provider.CreateScope()) { var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>(); var d = new Entities.Model.Document("files/x.exe", "application/x-msdownload"); uow.Set<Entities.Model.Document>().Add(d); uow.SaveChanges(); exe = d.Id; }
            var badType = await Sp(host, Qc, s => { var d = Sub(); d.CertificateDocumentId = exe; return s.SaveRunAsync(d, default); });
            Assert.IsFalse(badType.Success);
            Assert.IsTrue(badType.Message.Contains("مجاز نیست"), badType.Message);
            Guid ext; using (IServiceScope scope = host.Provider.CreateScope()) { var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>(); var d = new Entities.Model.Document("files/y.pdf", ".pdf"); uow.Set<Entities.Model.Document>().Add(d); uow.SaveChanges(); ext = d.Id; }
            Assert.IsNull(await QualityChecks.FileProblemAsync(host.Provider.CreateScope().ServiceProvider.GetRequiredService<IUnitOfWork>(), ext, "x", default), "the store keeps the extension as the type of older rows");
        }

        [TestMethod]
        public async Task ARunHasABoundedQuantityAndBoundedNumbers()
        {
            AppHost host = NewHost("runbounds");
            Plant p = await NewPlant(host, true);
            var more = await Sp(host, Qc, s => s.SaveRunAsync(RunDto(p, qty: 11m), default));
            Assert.IsFalse(more.Success, "the order is for 10 pieces");
            Assert.IsTrue(more.Message.Contains("بیشتر"), more.Message);
            var huge = GoodRunValues(); huge[0].Value = 1e15m;
            var big = await Sp(host, Qc, s => s.SaveRunAsync(RunDto(p, huge), default));
            Assert.IsFalse(big.Success, "a value that would overflow the column is a message, not an error 500");
            Assert.IsTrue(big.Message.Contains("بزرگ"), big.Message);
        }

        private static async Task<(Plant Plant, int NcrId)> RejectedStepWithConcession(AppHost host)
        {
            Plant p = await NewPlant(host, true);
            int ncrId = 0;
            host.Write(u =>
            {
                var ncr = AppHost.Add(u, new NonConformance
                {
                    ItemId = p.ItemId, WorkOrderId = p.WorkOrderId, Number = "NCR-C-" + Guid.NewGuid().ToString("N").Substring(0, 6), ReportDate = DateTime.Today, Status = NcrStatus.Approved, Severity = NcrSeverity.Minor,
                    Disposition = NcrDisposition.Concession, CreatorUserId = 1, Quantity = 1,
                });
                u.SaveChanges(); ncrId = ncr.Id;
                u.Set<MfProductionOrder>().Single(o => o.Id == p.OrderId).Status = MfOrderStatus.Released;
                var op = u.Set<MfOperation>().Single(o => o.Id == p.OpId); op.Status = MfOperationStatus.Rejected; op.NcrId = ncr.Id; op.GoodQty = 10m;
                u.SaveChanges();
            }, Seeder);
            return (p, ncrId);
        }

        [TestMethod]
        public async Task AConcessionOnAFlaggedStepStillNeedsThePassedAndReviewedRun()
        {
            AppHost host = NewHost("concession");
            var (p, ncr) = await RejectedStepWithConcession(host);
            var refused = await host.As(ProdManager, sp => sp.GetRequiredService<Services.NasbNiro.Production.IMfProductionService>().OperationActionAsync(new ViewModels.NasbNiro.ProductionDtos.OperationActionDto { Id = p.OpId, Action = "accept-by-concession", NcrId = ncr, Reason = "customer agreed" }, default));
            Assert.IsFalse(refused.Success);
            Assert.IsTrue(refused.Message.Contains("بدون ثبت اجرای فرایند"), refused.Message);
            Assert.AreEqual(MfOperationStatus.Rejected, host.Read(u => u.Set<MfOperation>().Single(o => o.Id == p.OpId).Status), "the step stays rejected");
            var run = await Sp(host, Qc, s => s.SaveRunAsync(RunDto(p), default));
            Assert.IsTrue(run.Success, run.Message);
            Assert.IsTrue((await Sp(host, Qa, s => s.RunActionAsync(new SpActionDto { Id = run.Data.Id, Action = "review" }, default))).Success);
            var accepted = await host.As(ProdManager, sp => sp.GetRequiredService<Services.NasbNiro.Production.IMfProductionService>().OperationActionAsync(new ViewModels.NasbNiro.ProductionDtos.OperationActionDto { Id = p.OpId, Action = "accept-by-concession", NcrId = ncr, Reason = "customer agreed" }, default));
            Assert.IsTrue(accepted.Success, accepted.Message);
            Assert.AreEqual(MfOperationStatus.Done, host.Read(u => u.Set<MfOperation>().Single(o => o.Id == p.OpId).Status));
        }

        [TestMethod]
        public async Task AReworkStepKeepsTheSpecialProcessFlagsSoItIsGatedToo()
        {
            AppHost host = NewHost("reworkflags");
            var (p, ncr) = await RejectedStepWithConcession(host);
            host.Write(u => { u.Set<NonConformance>().Single(n => n.Id == ncr).Disposition = NcrDisposition.Rework; u.SaveChanges(); }, Seeder);
            var made = await host.As(ProdManager, sp => sp.GetRequiredService<Services.NasbNiro.Production.IMfProductionService>().ReworkAsync(new ViewModels.NasbNiro.ProductionDtos.ReworkOperationDto { OperationId = p.OpId, NcrId = ncr }, default));
            Assert.IsTrue(made.Success, made.Message);
            MfOperation rework = host.Read(u => u.Set<MfOperation>().Single(o => o.IsRework && o.ReworkOfOperationId == p.OpId));
            Assert.IsTrue(rework.IsSpecialProcess && rework.SpecialProcessTypeId == p.TypeId, "the re-heat-treatment is a special process step too");
        }

        [TestMethod]
        public async Task AReceiptWithAReferenceThatMatchesNoSpecialStepIsRefusedNotSkipped()
        {
            AppHost host = NewHost("receiptref");
            Plant p = await NewPlant(host, false);
            int provider = 0, receipt = 0;
            host.Write(u => { var pr = AppHost.Add(u, new Provider { Name = "Ref Co" }); u.SaveChanges(); provider = pr.Id; }, Seeder);
            await NewSubcontractStep(host, p, provider);
            host.Write(u =>
            {
                var d = AppHost.Add(u, new InventoryDocument { Number = "SR-" + Guid.NewGuid().ToString("N").Substring(0, 5), Type = InventoryDocumentType.SubcontractReturn, WorkOrderId = p.WorkOrderId, ProviderId = provider, ReferenceNumber = "SOMETHING-ELSE", Status = InventoryDocumentStatus.Draft });
                u.SaveChanges(); receipt = d.Id;
            }, Seeder);
            string problem = await host.As(Seeder, async sp => { IUnitOfWork uow = sp.GetRequiredService<IUnitOfWork>(); return await SpecialProcessGate.ReceiptEvidenceProblemAsync(uow, uow.Set<InventoryDocument>().Single(d => d.Id == receipt), default); });
            Assert.IsNotNull(problem, "an unlinked reference on a work order with a live outside special step of the same supplier");
            Assert.IsTrue(problem.Contains("شماره مرجع"), problem);
        }

        [TestMethod]
        public async Task ACertificateMustBeOfOneOfTheSpecificationsTheItemRequiresWithTheSameStandard()
        {
            AppHost host = NewHost("certrequired");
            int item = NewItemWithLot(host, "H-501", out int lot);
            await ApprovedSpec(host, new[] { item });
            // another grade is approved too, but the plate does not require it
            var other = await Ms(host, Maker, s => { var d = SpecDto(); d.Grade = "S275JR"; return s.SaveAsync(d, default); });
            Assert.IsTrue((await Ms(host, Maker, s => s.ActionAsync(new SpActionDto { Id = other.Data.Id, Action = "submit" }, default))).Success);
            Assert.IsTrue((await Ms(host, Qa, s => s.ActionAsync(new SpActionDto { Id = other.Data.Id, Action = "approve" }, default))).Success);
            var wrongGrade = CertDto(lot, NewFile(host)); wrongGrade.Grade = "S275JR";
            var judged = await Mat(host, Qc, m => m.SaveCertificateAsync(wrongGrade, default));
            Assert.AreEqual(CertRowStatus.Fail, judged.Data.Evaluation, "an approved grade the item does not require");
            Assert.IsTrue(judged.Data.EvaluationText.Contains("گرید لازم: S355J2"), judged.Data.EvaluationText);
            var blankStandard = CertDto(lot, NewFile(host)); blankStandard.Standard = null;
            var blank = await Mat(host, Qc, m => m.SaveCertificateAsync(blankStandard, default));
            Assert.AreEqual(CertRowStatus.Fail, blank.Data.Evaluation, "the required specification names a standard; a blank one does not match");
            Assert.IsTrue(blank.Data.EvaluationText.Contains("EN 10025-2"), blank.Data.EvaluationText);
            var right = await Mat(host, Qc, m => m.SaveCertificateAsync(CertDto(lot, NewFile(host)), default));
            Assert.AreEqual(CertRowStatus.Pass, right.Data.Evaluation, right.Data.EvaluationText);
            // an item without any requirement: the standard still matches exactly
            int free = NewItemWithLot(host, "H-777", out int freeLot);
            var freeBlank = CertDto(freeLot, NewFile(host), heat: "H-777"); freeBlank.Standard = null;
            var freeJudged = await Mat(host, Qc, m => m.SaveCertificateAsync(freeBlank, default));
            Assert.AreEqual(CertRowStatus.Fail, freeJudged.Data.Evaluation, "a blank standard matches no specification that names one");
        }

        [TestMethod]
        public void TheRuleNamesTheRequiredGradeAndNeverMatchesAnotherStandard()
        {
            MaterialSpec spec = Spec(); spec.Id = 7;
            CertificateEvaluation wrongStd = CertificateRules.Evaluate(CertificateType.T31, "H-1", "H-1", "S355J2", "EN 10025-3", spec, Good(), null, null, null, "A");
            Assert.AreEqual(CertRowStatus.Fail, wrongStd.Status, "same grade, another standard");
            Assert.IsTrue(wrongStd.Text.Contains("EN 10025-3"), wrongStd.Text);
            MaterialSpec other = Spec(); other.Id = 8; other.Grade = "S275JR";
            CertificateEvaluation notRequired = CertificateRules.Evaluate(CertificateType.T31, "H-1", "H-1", "S275JR", "EN 10025-2", other, Good(), null, null, null, "A", new List<MaterialSpec> { spec });
            Assert.AreEqual(CertRowStatus.Fail, notRequired.Status);
            Assert.IsTrue(notRequired.Text.Contains("گرید لازم: S355J2 / EN 10025-2"), notRequired.Text);
            Assert.AreEqual(CertRowStatus.Pass, CertificateRules.Evaluate(CertificateType.T31, "H-1", "H-1", "S355J2", "EN 10025-2", spec, Good(), null, null, null, "A", new List<MaterialSpec> { spec }).Status);
        }

        [TestMethod]
        public async Task WhoeverEnteredEditedOrImportedTheValuesOfACertificateCannotVerifyIt()
        {
            AppHost host = NewHost("certeditor");
            int item = NewItemWithLot(host, "H-501", out int lot);
            await ApprovedSpec(host, new[] { item });
            var cert = await Mat(host, Qc, m => m.SaveCertificateAsync(CertDto(lot, NewFile(host)), default));
            Assert.AreEqual(CertRowStatus.Pass, cert.Data.Evaluation);
            var body = CertDto(lot, NewFile(host)); body.Id = cert.Data.Id; body.Number = cert.Data.Number; body.Values[1].Value = 381m;
            var edited = await Mat(host, Qc2, m => m.SaveCertificateAsync(body, default));
            Assert.IsTrue(edited.Success, edited.Message);
            Assert.IsFalse((await Mat(host, Qc, m => m.VerifyCertificateAsync(new VerifyCertificateDto { Id = cert.Data.Id, Accept = true }, default))).Success, "the creator");
            var editorTry = await Mat(host, Qc2, m => m.VerifyCertificateAsync(new VerifyCertificateDto { Id = cert.Data.Id, Accept = true }, default));
            Assert.IsFalse(editorTry.Success, "the one who edited the values");
            Assert.IsTrue(editorTry.Message.Contains("تفکیک وظایف"), editorTry.Message);
            Assert.IsTrue((await Mat(host, Qc3, m => m.VerifyCertificateAsync(new VerifyCertificateDto { Id = cert.Data.Id, Accept = true }, default))).Success, "a third person verifies");
            var fakeFile = CertDto(lot, Guid.NewGuid());
            var noFile = await Mat(host, Qc, m => m.SaveCertificateAsync(fakeFile, default));
            Assert.IsFalse(noFile.Success, "a certificate file that is not in the store");
        }

        [TestMethod]
        public async Task TheSpecificationApprovalAndTheTransactionHelperKeepTheGradeApproved()
        {
            AppHost host = NewHost("specsingle");
            int first = await ApprovedSpec(host);
            var v2 = await Ms(host, Maker, s => { var d = SpecDto(); d.NewVersionOfId = first; d.Reason = "tightened carbon limit"; d.Limits[0].Max = 0.18m; return s.SaveAsync(d, default); });
            Assert.IsTrue(v2.Success, v2.Message);
            Assert.IsTrue((await Ms(host, Maker, s => s.ActionAsync(new SpActionDto { Id = v2.Data.Id, Action = "submit" }, default))).Success);
            Assert.IsTrue((await Ms(host, Qa, s => s.ActionAsync(new SpActionDto { Id = v2.Data.Id, Action = "approve" }, default))).Success);
            Assert.AreEqual(1, host.Read(u => u.Set<MaterialSpec>().Count(x => x.Status == MatSpecStatus.Approved)), "exactly one approved version of the grade at any time");
            Assert.AreEqual(MatSpecStatus.Superseded, host.Read(u => u.Set<MaterialSpec>().Single(x => x.Id == first).Status));
            int runs = 0;
            string result = await host.As(Seeder, sp => QualityChecks.InTransactionAsync(sp.GetRequiredService<IUnitOfWork>(), () => { runs++; return Task.FromResult("done"); }, r => r == "done", default));
            Assert.AreEqual("done", result);
            Assert.AreEqual(1, runs, "the work runs once");
        }

        [TestMethod]
        public async Task TheExpiryAlertsLinkToTheRealPagesAndTheGateStatusAsksTheTerminalQuantity()
        {
            AppHost host = NewHost("alertlinks");
            Plant p = await NewPlant(host, true);
            host.Write(u => { u.Set<SpQualification>().Single(x => x.Id == p.QualId).ValidTo = DateTime.Today.AddDays(10); u.SaveChanges(); }, Seeder);
            Assert.AreEqual(1, await host.As(Seeder, sp => sp.GetRequiredService<ISpecialProcessService>().ExpiryAlertsAsync(default)));
            var links = host.Read(u => u.Set<Entities.Model.NasbNiro.Office.OfInboxItem>().Where(i => i.SourceKey == "spq-expiry:" + p.QualId).Select(i => i.Link).ToList());
            Assert.IsTrue(links.Count > 0 && links.All(l => l == "/quality/special-process-qualifications"), string.Join(",", links));
            var status = await Sp(host, Qa, s => s.GateStatusAsync(p.OpId, null, default));
            Assert.IsTrue(status.Success && status.Data.Applies, "the gate status answers with the quantity the terminal expects (10 for the first step)");
        }
    }
}
