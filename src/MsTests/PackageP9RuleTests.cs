using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using Entities.Identity;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.AuditSeat;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.EngChange;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Integrity;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.OpsData;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Providers;
using Entities.Model.NasbNiro.Quality;
using DataLayer.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.AuditSeat;
using Services.NasbNiro.Quality;

namespace MsTests
{
    /// <summary>
    /// Package 9: one test per rule of the audit catalogue. Each seeds a record that breaks the rule next to a compliant one (or, for a rule about a singleton such as the restore test, a second host with only the compliant state)
    /// and runs the query of the rule itself: the violating record is found, the compliant one is not. The test method of a rule is named after its code (AUD-CAL-001 = AUD_CAL_001); the last test makes sure no rule is without one.
    /// </summary>
    [TestClass]
    public class PackageP9RuleTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;
        private static DateTime Today => DateTime.Today;
        private static DateTimeOffset Now => DateTimeOffset.Now;

        private static AppHost Host() => PackageP9AuditSeatTests.NewHost();

        private static T Put<T>(AppHost h, T e) where T : class
        {
            h.Write(u => { AppHost.Add(u, e); u.SaveChanges(); });
            return e;
        }

        private static List<AusHit> Run(AppHost h, string code, int shiftDays = 0, ClaimsPrincipal who = null) =>
            h.As(who ?? PackageP9AuditSeatTests.Auditor, async sp =>
            {
                var uow = sp.GetRequiredService<IUnitOfWork>();
                var st = await QualitySettingKeys.LoadAsync(uow, Ct);
                DateTimeOffset now = Now.AddDays(shiftDays);
                var ctx = new AusContext { Uow = uow, Services = sp, Today = now.LocalDateTime.Date, Now = now, Settings = st, Max = 2000, Ct = Ct };
                return await AusCatalog.Find(code).Query(ctx);
            }).GetAwaiter().GetResult();

        private static AusHit Only(List<AusHit> hits, string type, int id, string sub = null)
        {
            Assert.AreEqual(1, hits.Count, "hits: " + string.Join(" | ", hits.Select(x => x.Key)));
            Assert.AreEqual(type, hits[0].ObjectType);
            Assert.AreEqual(id, hits[0].ObjectId);
            if (sub != null) Assert.AreEqual(sub, hits[0].Sub);
            Assert.IsFalse(string.IsNullOrWhiteSpace(hits[0].Text));
            return hits[0];
        }

        private static void None(List<AusHit> hits) => Assert.AreEqual(0, hits.Count, "hits: " + string.Join(" | ", hits.Select(x => x.Key)));

        private static QualifiedPerson Person(AppHost h, string code, int? userId = null, bool active = true) => Put(h, new QualifiedPerson { Name = "n" + code, PersonnelCode = code, IsActive = active, UserId = userId });

        private static Qualification Qual(AppHost h, int personId, QualificationKind kind, DateTime? expiry, bool revoked = false, DateTime? issued = null, int? renews = null) =>
            Put(h, new Qualification { PersonId = personId, Kind = kind, Method = "UT", IssuedDate = issued ?? Today.AddYears(-3), ExpiryDate = expiry, Revoked = revoked, RenewsQualificationId = renews, CertificateNumber = "C" + Guid.NewGuid().ToString("N").Substring(0, 6) });

        // ------------------------------------------------------------------------------------------ integrity

        [TestMethod]
        public void AUD_INT_001()
        {
            var bad = Host();
            Put(bad, new AuditChainRun { Kind = IntegrityRunKind.Full, StartedAt = Now, Status = IntegrityRunStatus.Broken, BreakKind = "hash", BreakEntityType = "Capa", BreakRowId = 5 });
            List<AusHit> hits = Run(bad, "AUD-INT-001");
            Assert.IsTrue(hits.Any(x => x.Sub == "broken"));
            Assert.IsTrue(hits.All(x => x.ObjectType == "AuditChainRun"));

            var ok = Host();
            Put(ok, new AuditChainRun { Kind = IntegrityRunKind.Incremental, StartedAt = Now.AddHours(-2), Status = IntegrityRunStatus.Ok });
            None(Run(ok, "AUD-INT-001"));
            var stale = Host();
            Put(stale, new AuditChainRun { Kind = IntegrityRunKind.Incremental, StartedAt = Now.AddHours(-100), Status = IntegrityRunStatus.Ok });
            Only(Run(stale, "AUD-INT-001"), "AuditChainRun", 0, "stale");
        }

        [TestMethod]
        public void AUD_INT_002()
        {
            Only(Run(Host(), "AUD-INT-002"), "RestoreTestResult", 0, "never");
            var failed = Host();
            var r = Put(failed, new RestoreTestResult { RunId = "r1", StartedAt = Now, Success = false });
            Assert.IsTrue(Run(failed, "AUD-INT-002").Any(x => x.Sub == "failed" && x.ObjectId == r.Id));
            var ok = Host();
            Put(ok, new RestoreTestResult { RunId = "r2", StartedAt = Now.AddDays(-3), Success = true, BackupFile = "b.bak", BackupTakenAt = Now.AddDays(-3) });
            None(Run(ok, "AUD-INT-002"));
            var old = Host();
            Put(old, new RestoreTestResult { RunId = "r3", StartedAt = Now.AddDays(-40), Success = true, BackupFile = "b.bak", BackupTakenAt = Now.AddDays(-40) });
            Only(Run(old, "AUD-INT-002"), "RestoreTestResult", 0, "stale");
        }

        [TestMethod]
        public void AUD_INT_003()
        {
            var host = Host();
            int baseline = Run(host, "AUD-INT-003").Count;   // the shipped defaults may already differ from the strict profile; each of those is a finding too
            host.Write(u => { AppHost.Add(u, new QualitySetting { Key = AusSettingKeys.Master, Value = 0, Title = "t", Source = "test" }); u.SaveChanges(); });
            List<AusHit> hits = Run(host, "AUD-INT-003");
            Assert.IsTrue(hits.Any(x => x.Text.Contains(AusSettingKeys.Master)), string.Join(" | ", hits.Select(x => x.Key)));
            Assert.AreEqual(baseline + 1, hits.Count);
            Assert.IsTrue(hits.First(x => x.Text.Contains(AusSettingKeys.Master)).Severity == AusSeverity.Major, "a weakening setting is major");
        }

        [TestMethod]
        public void AUD_INT_004()
        {
            var host = Host();
            var none = Put(host, new RestoreTestResult { RunId = "a", StartedAt = Now, Success = true, BackupFile = null });
            var old = Put(host, new RestoreTestResult { RunId = "b", StartedAt = Now, Success = true, BackupFile = "f.bak", BackupTakenAt = Now.AddDays(-5) });
            var ok = Put(host, new RestoreTestResult { RunId = "c", StartedAt = Now, Success = true, BackupFile = "g.bak", BackupTakenAt = Now.AddHours(-5) });
            List<AusHit> hits = Run(host, "AUD-INT-004");
            Assert.AreEqual(2, hits.Count);
            Assert.IsTrue(hits.Any(x => x.ObjectId == none.Id && x.Sub == "nobackup"));
            Assert.IsTrue(hits.Any(x => x.ObjectId == old.Id && x.Sub == "oldbackup"));
            Assert.IsFalse(hits.Any(x => x.ObjectId == ok.Id));
        }

        // ------------------------------------------------------------------------------------------ governance

        [TestMethod]
        public void AUD_MC_001()
        {
            var host = Host();
            var rq = Put(host, new EcRequest { Number = "ECR1", RequestedByUserId = 1 });
            var bad = Put(host, new EcOrder { Number = "ECO1", RequestId = rq.Id, Status = EcoStatus.Approved, EngineeringApprovedByUserId = 5, QaApprovedByUserId = 5 });
            var ok = Put(host, new EcOrder { Number = "ECO2", RequestId = rq.Id, Status = EcoStatus.Approved, EngineeringApprovedByUserId = 5, QaApprovedByUserId = 6 });
            var draft = Put(host, new EcOrder { Number = "ECO3", RequestId = rq.Id, Status = EcoStatus.Draft, EngineeringApprovedByUserId = 5, QaApprovedByUserId = 5 });
            Only(Run(host, "AUD-MC-001"), "EcOrder", bad.Id);
            Assert.AreNotEqual(ok.Id, bad.Id);
            Assert.AreNotEqual(draft.Id, bad.Id);
        }

        [TestMethod]
        public void AUD_MC_002()
        {
            var host = Host();
            var bad = Put(host, new SafetySettingChange { Store = SafetyStore.Quality, Key = "K1", RequestedByUserId = 5, RequestedAt = Now, Status = SafetyChangeStatus.Approved, DecidedByUserId = 5, DecidedAt = Now });
            Put(host, new SafetySettingChange { Store = SafetyStore.Quality, Key = "K2", RequestedByUserId = 5, RequestedAt = Now, Status = SafetyChangeStatus.Approved, DecidedByUserId = 6, DecidedAt = Now });
            Put(host, new SafetySettingChange { Store = SafetyStore.Quality, Key = "K3", RequestedByUserId = 5, RequestedAt = Now, Status = SafetyChangeStatus.Pending });
            Only(Run(host, "AUD-MC-002"), "SafetySettingChange", bad.Id);
        }

        [TestMethod]
        public void AUD_MC_003()
        {
            var host = Host();
            var bad = Put(host, new OpsException { RuleCode = "R1", DocumentNumber = "D1", RequestedByUserId = 5, RequestedAt = Now, Status = OpsExceptionStatus.Approved, DecidedByUserId = 5, DecidedAt = Now });
            Put(host, new OpsException { RuleCode = "R1", DocumentNumber = "D2", RequestedByUserId = 5, RequestedAt = Now, Status = OpsExceptionStatus.Approved, DecidedByUserId = 6, DecidedAt = Now });
            Only(Run(host, "AUD-MC-003"), "OpsException", bad.Id);
        }

        [TestMethod]
        public void AUD_MC_004()
        {
            var host = Host();
            var bad = Put(host, new PbBomHeader { Code = "B1", Revision = 1, Status = PbBomStatus.Released, PreparedByUserId = 1, CheckedByUserId = 1, QcCheckedByUserId = 3, ManufacturingCheckedByUserId = 4, ApprovedByUserId = 5 });
            Put(host, new PbBomHeader { Code = "B2", Revision = 1, Status = PbBomStatus.Released, PreparedByUserId = 1, CheckedByUserId = 2, QcCheckedByUserId = 3, ManufacturingCheckedByUserId = 4, ApprovedByUserId = 5 });
            Put(host, new PbBomHeader { Code = "B3", Revision = 1, Status = PbBomStatus.Draft, PreparedByUserId = 1, CheckedByUserId = 1 });
            Put(host, new PbBomHeader { Code = "B4", Revision = 1, Status = PbBomStatus.Released, ImportedSigned = true, PreparedByUserId = 1, CheckedByUserId = 1 });
            Only(Run(host, "AUD-MC-004"), "PbBomHeader", bad.Id);
        }

        [TestMethod]
        public void AUD_MC_005()
        {
            var host = Host();
            var bad = Put(host, new PcDocumentRevision { DeliverableId = 1, Code = "A", Status = PcDocStatus.Released, PreparedByUserId = 1, ReviewerUserId = 1, ApproverUserId = 2 });
            Put(host, new PcDocumentRevision { DeliverableId = 2, Code = "A", Status = PcDocStatus.Released, PreparedByUserId = 1, ReviewerUserId = 3, ApproverUserId = 2 });
            Put(host, new PcDocumentRevision { DeliverableId = 3, Code = "A", Status = PcDocStatus.Released, ReceivedFromClient = true, PreparedByUserId = 1, ReviewerUserId = 1, ApproverUserId = 1 });
            Only(Run(host, "AUD-MC-005"), "PcDocumentRevision", bad.Id);
        }

        [TestMethod]
        public void AUD_MC_006()
        {
            var host = Host();
            int bad = PackageP9AuditSeatTests.SeedFinding(host, AusFindingStatus.Closed, tweak: f => { f.QaUserId = 302; f.PmoUserId = 302; f.CeoUserId = 306; });
            PackageP9AuditSeatTests.SeedFinding(host, AusFindingStatus.Closed, tweak: f => { f.QaUserId = 302; f.PmoUserId = 304; f.CeoUserId = 306; });
            int owner = PackageP9AuditSeatTests.SeedFinding(host, AusFindingStatus.Closed, tweak: f => { f.QaUserId = 302; f.PmoUserId = 304; f.CeoUserId = 308; });
            PackageP9AuditSeatTests.SeedFinding(host, AusFindingStatus.PmoProposed, tweak: f => { f.QaUserId = 302; f.PmoUserId = 302; });
            List<AusHit> hits = Run(host, "AUD-MC-006");
            Assert.AreEqual(2, hits.Count);
            CollectionAssert.AreEquivalent(new[] { bad, owner }, hits.Select(x => x.ObjectId).ToArray());
        }

        [TestMethod]
        public void AUD_EX_001()
        {
            var host = Host();
            var bad = Put(host, new OpsException { RuleCode = "R1", DocumentNumber = "D1", RequestedByUserId = 5, RequestedAt = Now.AddDays(-10), Status = OpsExceptionStatus.Pending });
            Put(host, new OpsException { RuleCode = "R1", DocumentNumber = "D2", RequestedByUserId = 5, RequestedAt = Now.AddHours(-3), Status = OpsExceptionStatus.Pending });
            Put(host, new OpsException { RuleCode = "R1", DocumentNumber = "D3", RequestedByUserId = 5, RequestedAt = Now.AddDays(-10), Status = OpsExceptionStatus.Approved, DecidedByUserId = 6, DecidedAt = Now });
            Only(Run(host, "AUD-EX-001"), "OpsException", bad.Id);
        }

        [TestMethod]
        public void AUD_EX_002()
        {
            var host = Host();
            var hard = Put(host, new OpsReadinessObligation { WorkOrderId = 1, ProductId = 1, ItemCode = "RD-1", Title = "t", Hard = true, DueDate = Today.AddDays(-5), Status = OpsObligationStatus.Open });
            Put(host, new OpsReadinessObligation { WorkOrderId = 1, ProductId = 1, ItemCode = "RD-2", Title = "t", DueDate = Today.AddDays(5), Status = OpsObligationStatus.Open });
            Put(host, new OpsReadinessObligation { WorkOrderId = 1, ProductId = 1, ItemCode = "RD-3", Title = "t", DueDate = Today.AddDays(-5), Status = OpsObligationStatus.Closed });
            Assert.AreEqual(AusSeverity.Major, Only(Run(host, "AUD-EX-002"), "OpsReadinessObligation", hard.Id).Severity);
        }

        [TestMethod]
        public void AUD_EX_003()
        {
            var host = Host();
            for (int i = 0; i < 4; i++) Put(host, new OpsException { RuleCode = "RX", DocumentNumber = "D" + i, RequestedByUserId = 5, RequestedAt = Now, Status = OpsExceptionStatus.Approved, DecidedByUserId = 6, DecidedAt = Now });
            for (int i = 0; i < 3; i++) Put(host, new OpsException { RuleCode = "RY", DocumentNumber = "E" + i, RequestedByUserId = 5, RequestedAt = Now, Status = OpsExceptionStatus.Approved, DecidedByUserId = 6, DecidedAt = Now });
            AusHit h = Only(Run(host, "AUD-EX-003"), "OpsRule", 0, "RX");
            Assert.AreEqual("4", h.Facts["count"]);
        }

        [TestMethod]
        public void AUD_EX_004()
        {
            var host = Host();
            var block = Put(host, new OpsFinding { RuleCode = "R1", DocumentType = "x", DocumentNumber = "D1", Severity = OpsSeverity.Block, Status = OpsFindingStatus.Open, DetectedAt = Now.AddDays(-10) });
            Put(host, new OpsFinding { RuleCode = "R1", DocumentType = "x", DocumentNumber = "D2", Severity = OpsSeverity.Block, Status = OpsFindingStatus.Open, DetectedAt = Now.AddHours(-5) });
            Put(host, new OpsFinding { RuleCode = "R1", DocumentType = "x", DocumentNumber = "D3", Severity = OpsSeverity.Block, Status = OpsFindingStatus.Resolved, DetectedAt = Now.AddDays(-10) });
            Assert.AreEqual(AusSeverity.Major, Only(Run(host, "AUD-EX-004"), "OpsFinding", block.Id).Severity);
        }

        [TestMethod]
        public void AUD_EX_005()
        {
            var host = Host();
            var bad = Put(host, new OpsReadinessException { WorkOrderId = 1, ProductId = 1, ItemCodes = "RD-1", Justification = "j", RequestedByUserId = 5, RequestedAt = Now, Status = OpsReadinessExceptionStatus.Used });
            Put(host, new OpsReadinessException { WorkOrderId = 1, ProductId = 1, ItemCodes = "RD-2", Justification = "j", RequestedByUserId = 5, RequestedAt = Now, Status = OpsReadinessExceptionStatus.Pending });
            Only(Run(host, "AUD-EX-005"), "OpsReadinessException", bad.Id);
        }

        // ------------------------------------------------------------------------------------------ calibration and personnel

        [TestMethod]
        public void AUD_CAL_001()
        {
            var host = Host();
            var bad = Put(host, new MeasuringEquipment { Code = "E1", Name = "n", RequiresCalibration = true, Status = EquipmentStatus.Overdue, NextDue = Today.AddDays(-5) });
            var never = Put(host, new MeasuringEquipment { Code = "E2", Name = "n", RequiresCalibration = true, Status = EquipmentStatus.Calibrated, NextDue = null });
            Put(host, new MeasuringEquipment { Code = "E3", Name = "n", RequiresCalibration = true, Status = EquipmentStatus.Calibrated, NextDue = Today.AddDays(30) });
            Put(host, new MeasuringEquipment { Code = "E4", Name = "n", RequiresCalibration = true, Status = EquipmentStatus.OutOfService, NextDue = Today.AddDays(-30) });
            Put(host, new MeasuringEquipment { Code = "E5", Name = "n", RequiresCalibration = true, Status = EquipmentStatus.Overdue, NextDue = Today.AddDays(-30), CalibrationWaivedAt = Now });
            Put(host, new MeasuringEquipment { Code = "E6", Name = "n", RequiresCalibration = false, Status = EquipmentStatus.NotRequired, NextDue = null });
            List<AusHit> hits = Run(host, "AUD-CAL-001");
            CollectionAssert.AreEquivalent(new[] { bad.Id, never.Id }, hits.Select(x => x.ObjectId).ToArray());
        }

        [TestMethod]
        public void AUD_CAL_002()
        {
            var host = Host();
            var eqBad = Put(host, new MeasuringEquipment { Code = "E1", Name = "n", RequiresCalibration = true, Status = EquipmentStatus.Calibrated, NextDue = Today.AddDays(100) });
            var eqOk = Put(host, new MeasuringEquipment { Code = "E2", Name = "n", RequiresCalibration = true, Status = EquipmentStatus.Calibrated, NextDue = Today.AddDays(100) });
            Put(host, new CalibrationEvent { EquipmentId = eqOk.Id, EventDate = Today.AddDays(-10), NextDue = Today.AddDays(100) });
            var recBad = Put(host, new QcRecord { Number = "QR1", RecordDate = Today, InspectorUserId = 7 });
            var recOk = Put(host, new QcRecord { Number = "QR2", RecordDate = Today, InspectorUserId = 7 });
            Put(host, new QcMeasurement { RecordId = recBad.Id, Characteristic = "c", EquipmentId = eqBad.Id });
            Put(host, new QcMeasurement { RecordId = recOk.Id, Characteristic = "c", EquipmentId = eqOk.Id });
            Only(Run(host, "AUD-CAL-002"), "QcRecord", recBad.Id);
        }

        [TestMethod]
        public void AUD_CAL_003()
        {
            var host = Host();
            var open = Put(host, new OutOfToleranceCase { Number = "OOT1", EquipmentId = 1, Status = OotStatus.Open, ToDate = Today });
            var bad = Put(host, new OutOfToleranceItem { CaseId = open.Id, SourceKind = "qc", SourceId = 1, SourceNumber = "S1", SourceDate = Today, Assessment = OotAssessment.Pending });
            Put(host, new OutOfToleranceItem { CaseId = open.Id, SourceKind = "qc", SourceId = 2, SourceNumber = "S2", SourceDate = Today, Assessment = OotAssessment.NoImpact });
            var closed = Put(host, new OutOfToleranceCase { Number = "OOT2", EquipmentId = 1, Status = OotStatus.Closed, ToDate = Today });
            Put(host, new OutOfToleranceItem { CaseId = closed.Id, SourceKind = "qc", SourceId = 3, SourceNumber = "S3", SourceDate = Today, Assessment = OotAssessment.Pending });
            Only(Run(host, "AUD-CAL-003"), "OutOfToleranceItem", bad.Id);
        }

        [TestMethod]
        public void AUD_QL_001()
        {
            var host = Host();
            var p = Person(host, "W1");
            var expired = Qual(host, p.Id, QualificationKind.Welder, Today.AddDays(-30));
            var valid = Qual(host, p.Id, QualificationKind.Welder, Today.AddDays(300));
            var revoked = Qual(host, p.Id, QualificationKind.Welder, Today.AddDays(300), revoked: true);
            var bad = Put(host, new WeldLog { WeldDate = Today, WorkOrderId = 1, JointNo = "J1", QualificationId = expired.Id });
            Put(host, new WeldLog { WeldDate = Today, WorkOrderId = 1, JointNo = "J2", QualificationId = valid.Id });
            var bad2 = Put(host, new WeldLog { WeldDate = Today, WorkOrderId = 1, JointNo = "J3", QualificationId = revoked.Id });
            CollectionAssert.AreEquivalent(new[] { bad.Id, bad2.Id }, Run(host, "AUD-QL-001").Select(x => x.ObjectId).ToArray());
        }

        [TestMethod]
        public void AUD_QL_002()
        {
            var host = Host();
            var p = Person(host, "N1");
            var expired = Qual(host, p.Id, QualificationKind.NdtPersonnel, Today.AddDays(-30));
            var valid = Qual(host, p.Id, QualificationKind.NdtPersonnel, Today.AddDays(300));
            var bad = Put(host, new TestReport { Number = "TR1", Status = TestReportStatus.Submitted, ExaminerQualificationId = expired.Id, ReportDate = Today, ExaminerUserId = 7 });
            Put(host, new TestReport { Number = "TR2", Status = TestReportStatus.Submitted, ExaminerQualificationId = valid.Id, ReportDate = Today, ExaminerUserId = 7 });
            Put(host, new TestReport { Number = "TR3", Status = TestReportStatus.Draft, ExaminerQualificationId = expired.Id, ReportDate = Today, ExaminerUserId = 7 });
            Only(Run(host, "AUD-QL-002"), "TestReport", bad.Id);
        }

        [TestMethod]
        public void AUD_QL_003()
        {
            var host = Host();
            var unqualified = Person(host, "S1");
            var expired = Person(host, "S2");
            Qual(host, expired.Id, QualificationKind.SpecialProcessOperator, Today.AddDays(-30));
            var good = Person(host, "S3");
            Qual(host, good.Id, QualificationKind.SpecialProcessOperator, Today.AddDays(300));
            var bad1 = Put(host, new SpRun { Number = "SR1", Status = SpRunStatus.Recorded, OperatorPersonId = unqualified.Id, StartedAt = Today, EndedAt = Today });
            var bad2 = Put(host, new SpRun { Number = "SR2", Status = SpRunStatus.Recorded, OperatorPersonId = expired.Id, StartedAt = Today, EndedAt = Today });
            Put(host, new SpRun { Number = "SR3", Status = SpRunStatus.Recorded, OperatorPersonId = good.Id, StartedAt = Today, EndedAt = Today });
            Put(host, new SpRun { Number = "SR4", Status = SpRunStatus.Void, OperatorPersonId = unqualified.Id, StartedAt = Today, EndedAt = Today });
            CollectionAssert.AreEquivalent(new[] { bad1.Id, bad2.Id }, Run(host, "AUD-QL-003").Select(x => x.ObjectId).ToArray());
        }

        [TestMethod]
        public void AUD_QL_004()
        {
            var host = Host();
            var p1 = Person(host, "I1", userId: 77);
            Qual(host, p1.Id, QualificationKind.Inspector, Today.AddDays(-30));
            var p2 = Person(host, "I2", userId: 78);
            Qual(host, p2.Id, QualificationKind.Inspector, Today.AddDays(300));
            var bad = Put(host, new QcRecord { Number = "QR1", RecordDate = Today, InspectorUserId = 77 });
            Put(host, new QcRecord { Number = "QR2", RecordDate = Today, InspectorUserId = 78 });
            Put(host, new QcRecord { Number = "QR3", RecordDate = Today, InspectorUserId = 79 });   // not a certified person: not judged here
            Only(Run(host, "AUD-QL-004"), "QcRecord", bad.Id);
        }

        [TestMethod]
        public void AUD_QL_005()
        {
            var host = Host();
            var p = Person(host, "T1");
            var bad = Qual(host, p.Id, QualificationKind.Welder, Today.AddDays(-30));
            var renewedOld = Qual(host, p.Id, QualificationKind.Inspector, Today.AddDays(-30));
            Qual(host, p.Id, QualificationKind.Inspector, Today.AddDays(300), renews: renewedOld.Id);
            Qual(host, p.Id, QualificationKind.Welder, Today.AddDays(200));
            var gone = Person(host, "T2", active: false);
            Qual(host, gone.Id, QualificationKind.Welder, Today.AddDays(-30));
            Only(Run(host, "AUD-QL-005"), "Qualification", bad.Id);
        }

        // ------------------------------------------------------------------------------------------ nonconformance, corrective action, complaints

        [TestMethod]
        public void AUD_NC_001()
        {
            var host = Host();
            var minor = Put(host, new NonConformance { Number = "N1", ReportDate = Today.AddDays(-10), Status = NcrStatus.Open, Severity = NcrSeverity.Minor });
            var major = Put(host, new NonConformance { Number = "N2", ReportDate = Today.AddDays(-20), Status = NcrStatus.Approved, Severity = NcrSeverity.Major });
            Put(host, new NonConformance { Number = "N3", ReportDate = Today.AddDays(-1), Status = NcrStatus.Open, Severity = NcrSeverity.Minor });
            Put(host, new NonConformance { Number = "N4", ReportDate = Today.AddDays(-90), Status = NcrStatus.Closed, Severity = NcrSeverity.Major });
            List<AusHit> hits = Run(host, "AUD-NC-001");
            CollectionAssert.AreEquivalent(new[] { minor.Id, major.Id }, hits.Select(x => x.ObjectId).ToArray());
            Assert.AreEqual(AusSeverity.Major, hits.Single(x => x.ObjectId == major.Id).Severity);
            Assert.IsNull(hits.Single(x => x.ObjectId == minor.Id).Severity);
        }

        [TestMethod]
        public void AUD_NC_002()
        {
            var host = Host();
            var late = Put(host, new Capa { Number = "C1", Status = CapaStatus.Open, DueDate = Today.AddDays(-3), Mandatory = true });
            var effect = Put(host, new Capa { Number = "C2", Status = CapaStatus.Implemented, DueDate = Today.AddDays(-30), EffectivenessDueDate = Today.AddDays(-2) });
            Put(host, new Capa { Number = "C3", Status = CapaStatus.Open, DueDate = Today.AddDays(10) });
            Put(host, new Capa { Number = "C4", Status = CapaStatus.Closed, DueDate = Today.AddDays(-30), EffectivenessResult = "ok" });
            List<AusHit> hits = Run(host, "AUD-NC-002");
            CollectionAssert.AreEquivalent(new[] { late.Id, effect.Id }, hits.Select(x => x.ObjectId).ToArray());
            Assert.AreEqual(AusSeverity.Major, hits.Single(x => x.ObjectId == late.Id).Severity);
        }

        [TestMethod]
        public void AUD_NC_003()
        {
            var host = Host();
            var bad = Put(host, new Capa { Number = "C1", Status = CapaStatus.Closed, EffectivenessResult = null, ClosedAt = Now });
            Put(host, new Capa { Number = "C2", Status = CapaStatus.Closed, EffectivenessResult = "ok", ClosedAt = Now });
            Put(host, new Capa { Number = "C3", Status = CapaStatus.Open });
            Only(Run(host, "AUD-NC-003"), "Capa", bad.Id, "noeffect");
        }

        [TestMethod]
        public void AUD_NC_004()
        {
            var host = Host();
            var bad = Put(host, new CustomerComplaint { Number = "CC1", Status = ComplaintStatus.UnderReview, ResponseDue = Today.AddDays(-2), ReceivedDate = Today.AddDays(-10) });
            Put(host, new CustomerComplaint { Number = "CC2", Status = ComplaintStatus.Received, ResponseDue = Today.AddDays(5), ReceivedDate = Today });
            Put(host, new CustomerComplaint { Number = "CC3", Status = ComplaintStatus.Responded, ResponseDue = Today.AddDays(-2), ReceivedDate = Today.AddDays(-10) });
            Only(Run(host, "AUD-NC-004"), "CustomerComplaint", bad.Id);
        }

        // ------------------------------------------------------------------------------------------ special processes, materials, design, production

        [TestMethod]
        public void AUD_SP_001()
        {
            var host = Host();
            var bad = Put(host, new MfOperation { OrderId = 1, Sequence = 10, Title = "heat treat", IsSpecialProcess = true, Status = MfOperationStatus.Done });
            var ok = Put(host, new MfOperation { OrderId = 1, Sequence = 20, Title = "paint", IsSpecialProcess = true, Status = MfOperationStatus.Done });
            Put(host, new SpRun { Number = "SR1", OperationId = ok.Id, Status = SpRunStatus.Recorded, StartedAt = Today, EndedAt = Today });
            Put(host, new MfOperation { OrderId = 1, Sequence = 30, Title = "waiting", IsSpecialProcess = true, Status = MfOperationStatus.Waiting });
            Put(host, new MfOperation { OrderId = 1, Sequence = 40, Title = "machining", IsSpecialProcess = false, Status = MfOperationStatus.Done });
            Only(Run(host, "AUD-SP-001"), "MfOperation", bad.Id);
        }

        [TestMethod]
        public void AUD_SP_002()
        {
            var host = Host();
            var bad = Put(host, new SpRun { Number = "SR1", Status = SpRunStatus.Recorded, ReviewStatus = SpReviewStatus.None, StartedAt = Today, EndedAt = Today, CreateDate = Now.AddDays(-10) });
            Put(host, new SpRun { Number = "SR2", Status = SpRunStatus.Recorded, ReviewStatus = SpReviewStatus.Approved, StartedAt = Today, EndedAt = Today, CreateDate = Now.AddDays(-10) });
            Put(host, new SpRun { Number = "SR3", Status = SpRunStatus.Recorded, ReviewStatus = SpReviewStatus.None, StartedAt = Today, EndedAt = Today, CreateDate = Now });   // just recorded: inside the waiting time
            Only(Run(host, "AUD-SP-002"), "SpRun", bad.Id, "review");
        }

        [TestMethod]
        public void AUD_MT_001()
        {
            var host = Host();
            var lotBad = Put(host, new InventoryLot { ItemId = 1, LotNumber = "L1", HeatNumber = "H1", CertificateNumber = "CN1" });
            var lotOk = Put(host, new InventoryLot { ItemId = 1, LotNumber = "L2", HeatNumber = "H2", CertificateNumber = "CN2" });
            var lotNoCert = Put(host, new InventoryLot { ItemId = 1, LotNumber = "L3", HeatNumber = "H3" });
            var bad = Put(host, new QcInspection { Number = "QI1", Status = InspectionStatus.Done, AcceptedQuantity = 5, LotId = lotBad.Id });
            Put(host, new QcInspection { Number = "QI2", Status = InspectionStatus.Done, AcceptedQuantity = 5, LotId = lotOk.Id });
            Put(host, new MaterialCertificate { LotId = lotOk.Id, Number = "MC1", Result = CertificateResult.Accepted, VerifiedAt = Now, VerifiedByUserId = 4 });
            Put(host, new QcInspection { Number = "QI3", Status = InspectionStatus.Done, AcceptedQuantity = 5, LotId = lotNoCert.Id });   // no certificate at all: no certificate is claimed, not judged here
            Only(Run(host, "AUD-MT-001"), "QcInspection", bad.Id);
        }

        [TestMethod]
        public void AUD_DS_001()
        {
            var host = Host();
            var lineBad = Put(host, new PbBomLine { BomHeaderId = 1, PartNumber = "P1", MakeOrBuy = PbMakeOrBuy.Make });
            var lineOk = Put(host, new PbBomLine { BomHeaderId = 1, PartNumber = "P2", MakeOrBuy = PbMakeOrBuy.Make });
            var lineBuy = Put(host, new PbBomLine { BomHeaderId = 1, PartNumber = "P3", MakeOrBuy = PbMakeOrBuy.Purchase });
            var revBad = Put(host, new PcDocumentRevision { DeliverableId = 1, Code = "A", Status = PcDocStatus.Released });
            var revOk = Put(host, new PcDocumentRevision { DeliverableId = 2, Code = "A", Status = PcDocStatus.Released });
            var revBuy = Put(host, new PcDocumentRevision { DeliverableId = 3, Code = "A", Status = PcDocStatus.Released });
            Put(host, new PcDocumentLink { DeliverableId = 1, BomLineId = lineBad.Id, Kind = PcDocLinkKind.BomLine });
            Put(host, new PcDocumentLink { DeliverableId = 2, BomLineId = lineOk.Id, Kind = PcDocLinkKind.BomLine });
            Put(host, new PcDocumentLink { DeliverableId = 3, BomLineId = lineBuy.Id, Kind = PcDocLinkKind.BomLine });
            Put(host, new QcCharacteristicList { DeliverableId = 2, RevisionId = revOk.Id, Status = CharListStatus.Approved, Version = 1 });
            Only(Run(host, "AUD-DS-001"), "PcDocumentRevision", revBad.Id);
            Assert.AreNotEqual(revBuy.Id, revBad.Id);
        }

        [TestMethod]
        public void AUD_DS_002()
        {
            var host = Host();
            var item = Put(host, new InventoryItem { Code = "IT1", PartNumber = "PN1", Title = "n" });
            var flagged = Put(host, new MfOperationStandard { OperationTemplateId = 501, FirstArticleRequired = true });
            var plain = Put(host, new MfOperationStandard { OperationTemplateId = 502, FirstArticleRequired = false });
            var bad = Put(host, new MfProductionOrder { Number = "PO1", WorkOrderId = 1, ItemId = item.Id, Status = MfOrderStatus.InProgress, Quantity = 1 });
            Put(host, new MfOperation { OrderId = bad.Id, Sequence = 10, Title = "weld", OperationTemplateId = 501, Status = MfOperationStatus.Done });
            var plainOrder = Put(host, new MfProductionOrder { Number = "PO2", WorkOrderId = 1, ItemId = item.Id, Status = MfOrderStatus.InProgress, Quantity = 1 });
            Put(host, new MfOperation { OrderId = plainOrder.Id, Sequence = 10, Title = "saw", OperationTemplateId = 502, Status = MfOperationStatus.Done });
            var approved = Put(host, new MfProductionOrder { Number = "PO3", WorkOrderId = 1, ItemId = item.Id, Status = MfOrderStatus.InProgress, Quantity = 1 });
            Put(host, new MfOperation { OrderId = approved.Id, Sequence = 10, Title = "weld", OperationTemplateId = 501, Status = MfOperationStatus.Done });
            Put(host, new QcFirstArticle { Number = "FAI1", ItemId = item.Id, ProductionOrderId = approved.Id, Status = FaiStatus.Approved, Fingerprint = "f" });
            Assert.IsTrue(flagged.Id > 0 && plain.Id > 0);
            Only(Run(host, "AUD-DS-002"), "MfProductionOrder", bad.Id, "fai");
        }

        [TestMethod]
        public void AUD_DS_003()
        {
            var host = Host();
            Put(host, new PcWorkOrderInfo { WorkOrderId = 11, ContractId = 1 });
            var bad = Put(host, new ReleaseNote { Number = "RN1", WorkOrderId = 11, ContractId = 1, Status = ReleaseNoteStatus.Released, ReleasedAt = Now });
            Put(host, new ReleaseNote { Number = "RN2", WorkOrderId = 12, ContractId = 1, Status = ReleaseNoteStatus.Released, ReleasedAt = Now });   // not a project work order: the audit does not apply
            Put(host, new ReleaseNote { Number = "RN3", WorkOrderId = 11, ContractId = 1, Status = ReleaseNoteStatus.Draft });
            Only(Run(host, "AUD-DS-003"), "ReleaseNote", bad.Id);
        }

        [TestMethod]
        public void AUD_DS_004()
        {
            var host = Host();
            string json = "{\"a\":1}";
            var bad = Put(host, new AsBuiltRecord { WorkOrderId = 1, ReleaseNoteId = 1, ItemId = 1, Version = 1, Status = AsBuiltStatus.Current, ProductCode = "P1", SerialNumber = "S1", SnapshotJson = json, Sha256 = "tampered" });
            Put(host, new AsBuiltRecord { WorkOrderId = 1, ReleaseNoteId = 1, ItemId = 1, Version = 1, Status = AsBuiltStatus.Current, ProductCode = "P1", SerialNumber = "S2", SnapshotJson = json, Sha256 = CharacteristicRules.Sha256Hex(json) });
            Only(Run(host, "AUD-DS-004"), "AsBuiltRecord", bad.Id);
        }

        // ------------------------------------------------------------------------------------------ engineering change and design control

        [TestMethod]
        public void AUD_EC_001()
        {
            var host = Host();
            var noImpact = Put(host, new EcOrder { Number = "ECO1", RequestId = 1, Status = EcoStatus.Approved, ImpactSha256 = null });
            var openItem = Put(host, new EcOrder { Number = "ECO2", RequestId = 1, Status = EcoStatus.Closed, ImpactSha256 = "abc" });
            Put(host, new EcDispositionItem { OrderId = openItem.Id, Text = "t", Disposition = null });
            var ok = Put(host, new EcOrder { Number = "ECO3", RequestId = 1, Status = EcoStatus.Approved, ImpactSha256 = "abc" });
            Put(host, new EcDispositionItem { OrderId = ok.Id, Text = "t", Disposition = EcDisposition.UseAsIs });
            Put(host, new EcOrder { Number = "ECO4", RequestId = 1, Status = EcoStatus.Draft, ImpactSha256 = null });
            CollectionAssert.AreEquivalent(new[] { noImpact.Id, openItem.Id }, Run(host, "AUD-EC-001").Select(x => x.ObjectId).ToArray());
        }

        [TestMethod]
        public void AUD_EC_002()
        {
            var host = Host();
            var bad = Put(host, new EcTask { OrderId = 1, Title = "t1", OwnerUserId = 5, DueDate = Today.AddDays(-2), Status = EcTaskStatus.Open });
            Put(host, new EcTask { OrderId = 1, Title = "t2", OwnerUserId = 5, DueDate = Today.AddDays(5), Status = EcTaskStatus.Open });
            Put(host, new EcTask { OrderId = 1, Title = "t3", OwnerUserId = 5, DueDate = Today.AddDays(-2), Status = EcTaskStatus.Done });
            Only(Run(host, "AUD-EC-002"), "EcTask", bad.Id);
        }

        [TestMethod]
        public void AUD_EC_003()
        {
            var host = Host();
            var bad = Put(host, new PbBomHeader { Code = "B1", Revision = 1, Status = PbBomStatus.Released, ReleasedAt = Now, PreparedByUserId = 1 });
            Put(host, new PbBomHeader { Code = "B2", Revision = 1, Status = PbBomStatus.Released, ReleasedAt = Today.AddDays(-30), PreparedByUserId = 1 });   // released before the design control started: legacy
            Put(host, new PbBomHeader { Code = "B3", Revision = 1, Status = PbBomStatus.Draft, PreparedByUserId = 1 });
            Only(Run(host, "AUD-EC-003"), "PbBomHeader", bad.Id, "verification");
        }

        [TestMethod]
        public void AUD_LB_001()
        {
            var after = Host();
            Put(after, new AusRun { StartedAt = Now.AddDays(-1) });
            var bom = Put(after, new Bom { Title = "legacy line", AssemblyId = 1, ModifyDate = Now });
            Only(Run(after, "AUD-LB-001"), "Bom", bom.Id, "bom");

            var before = Host();
            Put(before, new Bom { Title = "legacy line", AssemblyId = 1, ModifyDate = Now });
            Put(before, new AusRun { StartedAt = Now.AddHours(1) });
            None(Run(before, "AUD-LB-001"));
            None(Run(Host(), "AUD-LB-001"));   // no run yet: no freeze moment
        }

        [TestMethod]
        public void AUD_DC_001()
        {
            var host = Host();
            var old = Put(host, new PcDocumentRevision { DeliverableId = 1, Code = "A", Status = PcDocStatus.Superseded, SupersededAt = Now.AddDays(-2) });
            var cur = Put(host, new PcDocumentRevision { DeliverableId = 1, Code = "B", Status = PcDocStatus.Released });
            var bad = Put(host, new QcRecord { Number = "QR1", RecordDate = Today, InspectorUserId = 7, DrawingRevisionId = old.Id, DrawingNumber = "DR1", CreateDate = Now });
            Put(host, new QcRecord { Number = "QR2", RecordDate = Today, InspectorUserId = 7, DrawingRevisionId = cur.Id, DrawingNumber = "DR1", CreateDate = Now });
            Only(Run(host, "AUD-DC-001"), "QcRecord", bad.Id);
        }

        [TestMethod]
        public void AUD_DC_002()
        {
            var host = Host();
            var draft = Put(host, new PcDocumentRevision { DeliverableId = 1, Code = "A", Status = PcDocStatus.Draft });
            var rel = Put(host, new PcDocumentRevision { DeliverableId = 2, Code = "A", Status = PcDocStatus.Released });
            var bad = Put(host, new QcRecord { Number = "QR1", RecordDate = Today, InspectorUserId = 7, WorkOrderId = 3, DrawingRevisionId = draft.Id, DrawingNumber = "DR1" });
            Put(host, new QcRecord { Number = "QR2", RecordDate = Today, InspectorUserId = 7, WorkOrderId = 3, DrawingRevisionId = rel.Id, DrawingNumber = "DR2" });
            Put(host, new QcRecord { Number = "QR3", RecordDate = Today, InspectorUserId = 7, WorkOrderId = null, DrawingRevisionId = draft.Id, DrawingNumber = "DR1" });   // not a production record
            Only(Run(host, "AUD-DC-002"), "QcRecord", bad.Id);
        }

        // ------------------------------------------------------------------------------------------ project

        [TestMethod]
        public void AUD_BL_001()
        {
            var host = Host();
            var cr = Put(host, new PcChangeRequest { Number = "CR1", ContractId = 1, Decision = PcChangeDecision.Approve, Status = PcChangeStatus.Decided });
            var bad = Put(host, new PcBaseline { ContractId = 1, Number = 2, Status = PcBaselineStatus.Approved, ChangeRequestId = null, Reason = "r" });
            Put(host, new PcBaseline { ContractId = 1, Number = 3, Status = PcBaselineStatus.Approved, ChangeRequestId = cr.Id, Reason = "r" });
            Put(host, new PcBaseline { ContractId = 1, Number = 1, Status = PcBaselineStatus.Approved, ChangeRequestId = null, Reason = "r" });   // the first baseline needs no change
            var rejected = Put(host, new PcChangeRequest { Number = "CR2", ContractId = 1, Decision = PcChangeDecision.Reject, Status = PcChangeStatus.Rejected });
            var bad2 = Put(host, new PcBaseline { ContractId = 2, Number = 2, Status = PcBaselineStatus.Superseded, ChangeRequestId = rejected.Id, Reason = "r" });
            CollectionAssert.AreEquivalent(new[] { bad.Id, bad2.Id }, Run(host, "AUD-BL-001").Select(x => x.ObjectId).ToArray());
        }

        [TestMethod]
        public void AUD_EVM_001()
        {
            var host = Host();
            var stale = Put(host, new ContractEntity { Code = "K1", Status = ContractStatus.Current });
            var fresh = Put(host, new ContractEntity { Code = "K2", Status = ContractStatus.Current });
            var noBaseline = Put(host, new ContractEntity { Code = "K3", Status = ContractStatus.Current });
            var none = Put(host, new ContractEntity { Code = "K4", Status = ContractStatus.Current });
            foreach (var k in new[] { stale, fresh, none }) Put(host, new PcBaseline { ContractId = k.Id, Number = 1, Status = PcBaselineStatus.Approved, Reason = "r" });
            Put(host, new PcProgressSnapshot { ContractId = stale.Id, DataDate = Today.AddDays(-30), Hash = "h" });
            Put(host, new PcProgressSnapshot { ContractId = fresh.Id, DataDate = Today.AddDays(-1), Hash = "h" });
            Assert.IsTrue(noBaseline.Id > 0);
            CollectionAssert.AreEquivalent(new[] { stale.Id, none.Id }, Run(host, "AUD-EVM-001").Select(x => x.ObjectId).ToArray());
        }

        [TestMethod]
        public void AUD_EVM_002()
        {
            var host = Host();
            var bad = Put(host, new CostEntry { ContractId = 1, WbsNodeId = null, EntryDate = Today, SourceType = "po", SourceNumber = "S1", CreatedAt = Now, Amount = 5 });
            Put(host, new CostEntry { ContractId = 1, WbsNodeId = 9, EntryDate = Today, SourceType = "po", SourceNumber = "S2", CreatedAt = Now, Amount = 5 });
            Put(host, new CostEntry { ContractId = null, WbsNodeId = null, EntryDate = Today, SourceType = "po", SourceNumber = "S3", CreatedAt = Now, Amount = 5 });
            AusHit h = Only(Run(host, "AUD-EVM-002"), "CostEntry", bad.Id);
            Assert.IsFalse(h.Facts.ContainsKey("amount"), "the amount can be a protected labour figure and is not copied");
        }

        [TestMethod]
        public void AUD_RK_001()
        {
            var host = Host();
            var bad = Put(host, new PcRisk { ContractId = 1, Number = "RK1", Title = "t", Status = PcRiskStatus.Assessed, ReviewDate = Today.AddDays(-3) });
            Put(host, new PcRisk { ContractId = 1, Number = "RK2", Title = "t", Status = PcRiskStatus.Assessed, ReviewDate = Today.AddDays(30) });
            Put(host, new PcRisk { ContractId = 1, Number = "RK3", Title = "t", Status = PcRiskStatus.Closed, ReviewDate = Today.AddDays(-30) });
            Put(host, new PcRisk { ContractId = 1, Number = "RK4", Title = "t", Status = PcRiskStatus.Identified, ReviewDate = null, ModifyDate = Now });   // just created: not yet stale
            Only(Run(host, "AUD-RK-001"), "PcRisk", bad.Id);
        }

        // ------------------------------------------------------------------------------------------ procurement

        [TestMethod]
        public void AUD_PR_001()
        {
            var host = Host();
            var cat = Put(host, new ProviderCategory { Category = "steel" });
            var noProfile = Put(host, new Provider { Name = "A", CategoryId = cat.Id });
            var good = Put(host, new Provider { Name = "B", CategoryId = cat.Id });
            var wrongCategory = Put(host, new Provider { Name = "C", CategoryId = cat.Id });
            var blocked = Put(host, new Provider { Name = "D", CategoryId = cat.Id });
            Put(host, new SupplierProfile { ProviderId = good.Id, Grade = SupplierGrade.A, Categories = "steel, pipe" });
            Put(host, new SupplierProfile { ProviderId = wrongCategory.Id, Grade = SupplierGrade.B, Categories = "valves" });
            Put(host, new SupplierProfile { ProviderId = blocked.Id, Grade = SupplierGrade.A, Blocked = true });
            var poBad = Put(host, new PurchaseOrder { Number = "PO1", Status = PurchaseOrderStatus.Released, ProviderId = noProfile.Id, OrderDate = Today });
            Put(host, new PurchaseOrder { Number = "PO2", Status = PurchaseOrderStatus.Released, ProviderId = good.Id, OrderDate = Today });
            var poCat = Put(host, new PurchaseOrder { Number = "PO3", Status = PurchaseOrderStatus.Received, ProviderId = wrongCategory.Id, OrderDate = Today });
            var poBlocked = Put(host, new PurchaseOrder { Number = "PO4", Status = PurchaseOrderStatus.Released, ProviderId = blocked.Id, OrderDate = Today });
            Put(host, new PurchaseOrder { Number = "PO5", Status = PurchaseOrderStatus.Draft, ProviderId = noProfile.Id, OrderDate = Today });   // a draft is not an order yet
            List<AusHit> hits = Run(host, "AUD-PR-001");
            CollectionAssert.AreEquivalent(new[] { poBad.Id, poCat.Id, poBlocked.Id }, hits.Select(x => x.ObjectId).ToArray());
            Assert.AreEqual("category", hits.Single(x => x.ObjectId == poCat.Id).Sub);
        }

        [TestMethod]
        public void AUD_PR_002()
        {
            var host = Host();
            var stale = Put(host, new Provider { Name = "A" });
            var fresh = Put(host, new Provider { Name = "B" });
            var idle = Put(host, new Provider { Name = "C" });
            var spStale = Put(host, new SupplierProfile { ProviderId = stale.Id, Grade = SupplierGrade.A, EvaluatedAt = Today.AddDays(-400) });
            Put(host, new SupplierProfile { ProviderId = fresh.Id, Grade = SupplierGrade.A, EvaluatedAt = Today.AddDays(-10), NextEvaluation = Today.AddDays(100) });
            Put(host, new SupplierProfile { ProviderId = idle.Id, Grade = SupplierGrade.A, EvaluatedAt = Today.AddDays(-400) });   // no order in the last year
            Put(host, new PurchaseOrder { Number = "PO1", Status = PurchaseOrderStatus.Released, ProviderId = stale.Id, OrderDate = Today });
            Put(host, new PurchaseOrder { Number = "PO2", Status = PurchaseOrderStatus.Released, ProviderId = fresh.Id, OrderDate = Today });
            Only(Run(host, "AUD-PR-002"), "SupplierProfile", spStale.Id);
        }

        // ------------------------------------------------------------------------------------------ management review and internal audit

        [TestMethod]
        public void AUD_MR_001()
        {
            Only(Run(Host(), "AUD-MR-001"), "ManagementReview", 0, "overdue");
            var old = Host();
            Put(old, new ManagementReview { ReviewDate = Today.AddDays(-400), Status = ReviewStatus.Held });
            Only(Run(old, "AUD-MR-001"), "ManagementReview", 0, "overdue");
            var ok = Host();
            Put(ok, new ManagementReview { ReviewDate = Today.AddDays(-30), Status = ReviewStatus.Closed });
            None(Run(ok, "AUD-MR-001"));
            var draft = Host();
            Put(draft, new ManagementReview { ReviewDate = Today.AddDays(-30), Status = ReviewStatus.Draft });
            Only(Run(draft, "AUD-MR-001"), "ManagementReview", 0, "overdue");   // a draft is not a held review
        }

        [TestMethod]
        public void AUD_MR_002()
        {
            var host = Host();
            var bad = Put(host, new ManagementReviewDecision { ReviewId = 1, Text = "decision", DueDate = Today.AddDays(-4), Status = ReviewDecisionStatus.Open });
            Put(host, new ManagementReviewDecision { ReviewId = 1, Text = "decision", DueDate = Today.AddDays(4), Status = ReviewDecisionStatus.Open });
            Put(host, new ManagementReviewDecision { ReviewId = 1, Text = "decision", DueDate = Today.AddDays(-4), Status = ReviewDecisionStatus.Done });
            Only(Run(host, "AUD-MR-002"), "ManagementReviewDecision", bad.Id);
        }

        [TestMethod]
        public void AUD_IA_001()
        {
            var host = Host();
            var qms = Put(host, new Audit { Number = "IA1", AuditedUnit = "QC", PlannedDate = Today.AddDays(-5), Status = AuditStatus.Planned, LeadAuditorUserId = 9 });
            Put(host, new Audit { Number = "IA2", AuditedUnit = "QC", PlannedDate = Today.AddDays(5), Status = AuditStatus.Planned, LeadAuditorUserId = 9 });
            Put(host, new Audit { Number = "IA3", AuditedUnit = "QC", PlannedDate = Today.AddDays(-5), Status = AuditStatus.Done, LeadAuditorUserId = 9 });
            var proc = Put(host, new AusAudit { Number = "PA1", ProcessCode = "QUALITY", PlannedDate = Today.AddDays(-5), Status = AusAuditStatus.InProgress, LeadAuditorUserId = 9 });
            Put(host, new AusAudit { Number = "PA2", ProcessCode = "QUALITY", PlannedDate = Today.AddDays(-5), Status = AusAuditStatus.Completed, LeadAuditorUserId = 9 });
            List<AusHit> hits = Run(host, "AUD-IA-001");
            Assert.AreEqual(2, hits.Count);
            Assert.IsTrue(hits.Any(x => x.ObjectType == "Audit" && x.ObjectId == qms.Id));
            Assert.IsTrue(hits.Any(x => x.ObjectType == "AusAudit" && x.ObjectId == proc.Id));
        }

        [TestMethod]
        public void AUD_IA_002()
        {
            var host = Host();
            var capaOpen = Put(host, new Capa { Number = "C1", Status = CapaStatus.Open });
            var capaClosed = Put(host, new Capa { Number = "C2", Status = CapaStatus.Closed, EffectivenessResult = "ok" });
            var noCapa = Put(host, new AuditFinding { AuditId = 1, Clause = "8.5", Type = AuditFindingType.MajorNC, DueDate = Today.AddDays(-5), CapaId = null });
            var openCapa = Put(host, new AuditFinding { AuditId = 1, Clause = "8.6", Type = AuditFindingType.MinorNC, DueDate = Today.AddDays(-5), CapaId = capaOpen.Id });
            Put(host, new AuditFinding { AuditId = 1, Clause = "8.7", Type = AuditFindingType.MinorNC, DueDate = Today.AddDays(-5), CapaId = capaClosed.Id });
            Put(host, new AuditFinding { AuditId = 1, Clause = "8.8", Type = AuditFindingType.Opportunity, DueDate = Today.AddDays(-5), CapaId = null });
            Put(host, new AuditFinding { AuditId = 1, Clause = "8.9", Type = AuditFindingType.MajorNC, DueDate = Today.AddDays(5), CapaId = null });
            CollectionAssert.AreEquivalent(new[] { noCapa.Id, openCapa.Id }, Run(host, "AUD-IA-002").Select(x => x.ObjectId).ToArray());
        }

        // ------------------------------------------------------------------------------------------ finance, AI and the audit system itself

        [TestMethod]
        public void AUD_FIN_001()
        {
            var host = Host();
            var rejected = Put(host, new FnOutboxPackage { Number = "FO1", Year = 1405, Status = FnOutboxStatus.Rejected, CreatedByUserId = 4 });
            var errored = Put(host, new FnOutboxPackage { Number = "FO2", Year = 1405, Status = FnOutboxStatus.Ready, Error = "boom", CreatedByUserId = 4 });
            Put(host, new FnOutboxPackage { Number = "FO3", Year = 1405, Status = FnOutboxStatus.Accepted, CreatedByUserId = 4 });
            var fixedOne = Put(host, new FnOutboxPackage { Number = "FO4", Year = 1405, Status = FnOutboxStatus.Rejected, CreatedByUserId = 4 });
            Put(host, new FnOutboxPackage { Number = "FO5", Year = 1405, Status = FnOutboxStatus.Accepted, CorrectsPackageId = fixedOne.Id, CreatedByUserId = 4 });
            CollectionAssert.AreEquivalent(new[] { rejected.Id, errored.Id }, Run(host, "AUD-FIN-001").Select(x => x.ObjectId).ToArray());
        }

        [TestMethod]
        public void AUD_AI_001()
        {
            var host = Host();
            var bad = Put(host, new AiMessage { ConversationId = 1, Role = AiMessageRole.Assistant, Verified = false, CreatedAt = Now, Text = "t" });
            Put(host, new AiMessage { ConversationId = 1, Role = AiMessageRole.Assistant, Verified = true, CreatedAt = Now, Text = "t" });
            Put(host, new AiMessage { ConversationId = 1, Role = AiMessageRole.User, Verified = null, CreatedAt = Now, Text = "t" });
            AusHit h = Only(Run(host, "AUD-AI-001"), "AiMessage", bad.Id);
            Assert.IsFalse(h.Facts.Values.Any(v => v == "t"), "the text of the answer is not copied into the evidence");
        }

        [TestMethod]
        public void AUD_AI_002()
        {
            var host = Host();
            string seatUser = AiSeatCatalog.All.First(s => s.Code == "AUDIT").UserName;
            None(Run(host, "AUD-AI-002"));
            int uid = 0;
            host.Write(u =>
            {
                User user = u.Set<User>().FirstOrDefault(x => x.UserName == seatUser) ?? AppHost.Add(u, new User { UserName = seatUser, Email = "seat@example.com", IsActive = true });
                u.SaveChanges();
                uid = user.Id;
                u.Set<UserRole>().Add(new UserRole { UserId = uid, RoleId = u.Set<Role>().Single(r => r.Name == "AUDITOR").Id });
                u.SaveChanges();
            });
            None(Run(host, "AUD-AI-002"));   // the role of the seat definition only
            host.Write(u => { u.Set<UserRole>().Add(new UserRole { UserId = uid, RoleId = u.Set<Role>().Single(r => r.Name == "QA").Id }); u.SaveChanges(); });
            AusHit h = Only(Run(host, "AUD-AI-002"), "User", uid);
            Assert.AreEqual("QA", h.Facts["extraRoles"]);
        }

        [TestMethod]
        public void AUD_AI_003()
        {
            var host = Host();
            var bad = Put(host, new AiSeat { Code = "ZZ1", Title = "t", ServiceUserName = "seat.zz1", Roles = "QA", Schedule = "daily 01:00", PromptVersionCode = "v", Enabled = true, OutputKind = "k" });
            var ok = Put(host, new AiSeat { Code = "ZZ2", Title = "t", ServiceUserName = "seat.zz2", Roles = "QA", Schedule = "daily 01:00", PromptVersionCode = "v", Enabled = true, OutputKind = "k" });
            var failedRun = Put(host, new AiSeatRun { SeatId = bad.Id, StartedAt = Now, Status = AiSeatRunStatus.Failed, Error = "e" });
            Put(host, new AiSeatRun { SeatId = ok.Id, StartedAt = Now.AddHours(-2), Status = AiSeatRunStatus.Failed, Error = "e" });
            Put(host, new AiSeatRun { SeatId = ok.Id, StartedAt = Now.AddHours(-1), Status = AiSeatRunStatus.Succeeded });
            Only(Run(host, "AUD-AI-003"), "AiSeatRun", failedRun.Id, "ZZ1");
        }

        [TestMethod]
        public void AUD_SYS_001()
        {
            var host = Host();
            var bad = Put(host, new AusRuleState { RuleCode = "AUD-INT-001", LastStatus = "error", LastError = "e", LastRunAt = Now });
            Put(host, new AusRuleState { RuleCode = "AUD-INT-002", LastStatus = "ok", LastRunAt = Now });
            Put(host, new AusRuleState { RuleCode = "AUD-SYS-001", LastStatus = "error", LastRunAt = Now });   // the rule does not judge itself
            Only(Run(host, "AUD-SYS-001"), "AusRuleState", bad.Id, "AUD-INT-001");
        }

        [TestMethod]
        public void AUD_SYS_002()
        {
            var host = Host();
            int bad = PackageP9AuditSeatTests.SeedFinding(host, AusFindingStatus.Open, tweak: f => f.DueDate = Today.AddDays(-3));
            PackageP9AuditSeatTests.SeedFinding(host, AusFindingStatus.Open, tweak: f => f.DueDate = Today.AddDays(5));
            PackageP9AuditSeatTests.SeedFinding(host, AusFindingStatus.Closed, tweak: f => f.DueDate = Today.AddDays(-3));
            PackageP9AuditSeatTests.SeedFinding(host, AusFindingStatus.Open, rule: "AUD-SYS-002", tweak: f => f.DueDate = Today.AddDays(-3));   // its own findings are not counted twice
            Only(Run(host, "AUD-SYS-002"), "AusFinding", bad, "overdue");
        }

        // ------------------------------------------------------------------------------------------ the catalogue and the tests agree

        [TestMethod]
        public void Every_Rule_Of_The_Catalogue_Has_Its_Own_Test_Here()
        {
            HashSet<string> tests = typeof(PackageP9RuleTests).GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.GetCustomAttribute<TestMethodAttribute>() != null).Select(m => m.Name).ToHashSet();
            foreach (AusRuleDef r in AusCatalog.All) Assert.IsTrue(tests.Contains(r.Code.Replace('-', '_')), "no test for " + r.Code);
            foreach (string t in tests.Where(t => t.StartsWith("AUD_", StringComparison.Ordinal))) Assert.IsNotNull(AusCatalog.Find(t.Replace('_', '-')), "no rule for the test " + t);
        }

        [TestMethod]
        public void Every_Rule_Reads_Without_Writing_And_Returns_Nothing_On_An_Empty_Database_Except_The_Known_Absences()
        {
            // a fresh database has no restore test, no chain run, no management review (these absences ARE findings); nothing else may be found
            var known = new HashSet<string> { "AUD-INT-001", "AUD-INT-002", "AUD-MR-001" };
            const string deviations = "AUD-INT-003";   // depends on the shipped defaults versus the strict profile
            var host = Host();
            foreach (AusRuleDef r in AusCatalog.All)
            {
                List<AusHit> hits = Run(host, r.Code);
                if (known.Contains(r.Code)) Assert.IsTrue(hits.Count >= 1, r.Code); else if (r.Code != deviations) None(hits);
                foreach (AusHit h in hits) Assert.IsFalse(string.IsNullOrWhiteSpace(h.Text) || string.IsNullOrWhiteSpace(h.ObjectType), r.Code);
            }
        }
    }
}
