using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Quality
{
    // Quality module (QC + QA) tables. All start with "Qa"; the existing NCR and item tables only get new columns.
    // Deletes are restricted except for child lines, which go with their header.
    public static class QualityModelBuilderExtensions
    {
        private const int Code = 50;
        private const int Name = 200;
        private const int Title = 300;
        private const int Text = 1000;
        private const int LongText = 4000;
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddQualityConfigurations(this ModelBuilder builder)
        {
            // ------------------------------------------------------------------ ITP
            builder.Entity<ItpTemplate>(b =>
            {
                b.ToTable("QaItpTemplates");
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Code).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.ApplicableCodes).HasMaxLength(Text);
                b.Property(x => x.Revision).HasMaxLength(Code);
            });
            builder.Entity<ItpTemplateActivity>(b =>
            {
                b.ToTable("QaItpTemplateActivities");
                b.Property(x => x.ItemNo).HasMaxLength(Code);
                b.Property(x => x.Stage).HasMaxLength(Title);
                b.Property(x => x.Activity).HasMaxLength(Text).IsRequired();
                b.Property(x => x.AcceptanceReference).HasMaxLength(Text);
                b.Property(x => x.DocumentProduced).HasMaxLength(Title);
                b.Property(x => x.Remark).HasMaxLength(Text);
                b.HasOne(x => x.Template).WithMany(x => x.Activities).HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<ItpTemplateParameter>(b =>
            {
                b.ToTable("QaItpTemplateParameters");
                b.Property(x => x.Name).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Unit).HasMaxLength(Code);
                b.Property(x => x.Min).HasPrecision(18, 4);
                b.Property(x => x.Max).HasPrecision(18, 4);
                b.Property(x => x.Source).HasMaxLength(Text);
                b.HasOne(x => x.Activity).WithMany(x => x.Parameters).HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<Itp>(b =>
            {
                b.ToTable("QaItps");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.WorkOrderId).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.Property(x => x.ClientApprovalReference).HasMaxLength(Title);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.Property(x => x.EditorUserIds).HasMaxLength(Text);
                b.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<ItpRevision>(b =>
            {
                b.ToTable("QaItpRevisions");
                b.Property(x => x.Reason).HasMaxLength(Text);
                b.Property(x => x.ClientApprovalReference).HasMaxLength(Title);
                b.Property(x => x.Snapshot).HasColumnType("nvarchar(max)");
                b.HasOne(x => x.Itp).WithMany(x => x.Revisions).HasForeignKey(x => x.ItpId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<ItpActivity>(b =>
            {
                b.ToTable("QaItpActivities");
                b.Property(x => x.ItemNo).HasMaxLength(Code);
                b.Property(x => x.Stage).HasMaxLength(Title);
                b.Property(x => x.Activity).HasMaxLength(Text).IsRequired();
                b.Property(x => x.AcceptanceReference).HasMaxLength(Text);
                b.Property(x => x.DocumentProduced).HasMaxLength(Title);
                b.Property(x => x.Remark).HasMaxLength(Text);
                b.Property(x => x.StatusNote).HasMaxLength(Text);
                b.Property(x => x.NotApplicableReference).HasMaxLength(Title);
                b.Property(x => x.EvidenceNote).HasMaxLength(Text);
                b.HasIndex(x => x.EvidenceDocumentId);
                b.HasIndex(x => new { x.ItpId, x.Status });
                b.HasOne(x => x.Itp).WithMany(x => x.Activities).HasForeignKey(x => x.ItpId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<ItpActivityParameter>(b =>
            {
                b.ToTable("QaItpActivityParameters");
                b.Property(x => x.Name).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Unit).HasMaxLength(Code);
                b.Property(x => x.Min).HasPrecision(18, 4);
                b.Property(x => x.Max).HasPrecision(18, 4);
                b.Property(x => x.Source).HasMaxLength(Text);
                b.HasOne(x => x.Activity).WithMany(x => x.Parameters).HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<ItpPointRelease>(b =>
            {
                b.ToTable("QaItpPointReleases");
                b.Property(x => x.PersonName).HasMaxLength(Name);
                b.Property(x => x.Organisation).HasMaxLength(Name);
                b.Property(x => x.Reference).HasMaxLength(Title);
                b.HasOne(x => x.Activity).WithMany(x => x.Releases).HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Cascade);
            });

            // ------------------------------------------------------------------ inspection records and test reports
            builder.Entity<QcRecord>(b =>
            {
                b.ToTable("QaRecords");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.WorkOrderId);
                b.HasIndex(x => x.ItpActivityId);
                b.Property(x => x.SerialNumber).HasMaxLength(Name);
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.Operation).HasMaxLength(Title);
                b.Property(x => x.DrawingNumber).HasMaxLength(Name);
                b.Property(x => x.DrawingRevision).HasMaxLength(Code);
                b.Property(x => x.Note).HasMaxLength(LongText);
                b.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.ItpActivity).WithMany().HasForeignKey(x => x.ItpActivityId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Ncr).WithMany().HasForeignKey(x => x.NcrId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<QcMeasurement>(b =>
            {
                b.ToTable("QaMeasurements");
                b.Property(x => x.Characteristic).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Nominal).HasPrecision(18, 4);
                b.Property(x => x.TolPlus).HasPrecision(18, 4);
                b.Property(x => x.TolMinus).HasPrecision(18, 4);
                b.Property(x => x.Measured).HasPrecision(18, 4);
                b.Property(x => x.Unit).HasMaxLength(Code);
                b.HasIndex(x => x.EquipmentId);
                b.HasOne(x => x.Record).WithMany(x => x.Measurements).HasForeignKey(x => x.RecordId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<TestReport>(b =>
            {
                b.ToTable("QaTestReports");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.WorkOrderId);
                b.HasIndex(x => x.ItpActivityId);
                b.HasIndex(x => x.Status);
                b.Property(x => x.SerialNumber).HasMaxLength(Name);
                b.Property(x => x.ProcedureRef).HasMaxLength(Name);
                b.Property(x => x.ProcedureRevision).HasMaxLength(Code);
                b.Property(x => x.CodeEdition).HasMaxLength(Name);
                b.Property(x => x.Environment).HasMaxLength(Text);
                b.Property(x => x.RejectReason).HasMaxLength(Text);
                b.Property(x => x.Note).HasMaxLength(LongText);
                b.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.ItpActivity).WithMany().HasForeignKey(x => x.ItpActivityId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.ExaminerQualification).WithMany().HasForeignKey(x => x.ExaminerQualificationId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Ncr).WithMany().HasForeignKey(x => x.NcrId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<TestReportValue>(b =>
            {
                b.ToTable("QaTestReportValues");
                b.Property(x => x.Parameter).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Unit).HasMaxLength(Code);
                b.Property(x => x.Min).HasPrecision(18, 4);
                b.Property(x => x.Max).HasPrecision(18, 4);
                b.Property(x => x.Measured).HasPrecision(18, 4);
                b.Property(x => x.Source).HasMaxLength(Text);
                b.HasOne(x => x.Report).WithMany(x => x.Values).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<TestReportIndication>(b =>
            {
                b.ToTable("QaTestReportIndications");
                b.Property(x => x.Location).HasMaxLength(Title);
                b.Property(x => x.Type).HasMaxLength(Name);
                b.Property(x => x.Size).HasMaxLength(Name);
                b.Property(x => x.Orientation).HasMaxLength(Name);
                b.HasOne(x => x.Report).WithMany(x => x.Indications).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<TestReportEquipment>(b =>
            {
                b.ToTable("QaTestReportEquipment");
                b.HasIndex(x => x.EquipmentId);
                b.HasOne(x => x.Report).WithMany(x => x.Equipment).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<TestReportWitness>(b =>
            {
                b.ToTable("QaTestReportWitnesses");
                b.Property(x => x.Name).HasMaxLength(Name).IsRequired();
                b.HasOne(x => x.Report).WithMany(x => x.Witnesses).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
            });

            // ------------------------------------------------------------------ calibration
            builder.Entity<MeasuringEquipment>(b =>
            {
                b.ToTable("QaEquipment");
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Code).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Name).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Maker).HasMaxLength(Name);
                b.Property(x => x.Model).HasMaxLength(Name);
                b.Property(x => x.SerialNumber).HasMaxLength(Name);
                b.Property(x => x.OwnerUnit).HasMaxLength(Name);
                b.Property(x => x.Location).HasMaxLength(Name);
                b.Property(x => x.Range).HasMaxLength(Name);
                b.Property(x => x.Accuracy).HasMaxLength(Name);
                b.Property(x => x.OutOfServiceReason).HasMaxLength(Text);
                b.Property(x => x.CalibrationWaiverReason).HasMaxLength(Text);
                b.Property(x => x.CalibrationWaiverRequestReason).HasMaxLength(Text);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasIndex(x => x.NextDue);
            });
            builder.Entity<CalibrationIntervalDefault>(b =>
            {
                b.ToTable("QaCalibrationIntervals");
                b.HasIndex(x => x.EquipmentType).IsUnique();
                b.Property(x => x.Source).HasMaxLength(Title);
            });
            builder.Entity<CalibrationEvent>(b =>
            {
                b.ToTable("QaCalibrationEvents");
                b.Property(x => x.LabName).HasMaxLength(Name);
                b.Property(x => x.CertificateNumber).HasMaxLength(Name);
                b.Property(x => x.TraceabilityStatement).HasMaxLength(Text);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasIndex(x => new { x.EquipmentId, x.EventDate });
                b.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OutOfToleranceCase>(b =>
            {
                b.ToTable("QaOotCases");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasIndex(x => x.Status);
                b.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.CalibrationEvent).WithMany().HasForeignKey(x => x.CalibrationEventId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<OutOfToleranceItem>(b =>
            {
                b.ToTable("QaOotItems");
                b.Property(x => x.SourceKind).HasMaxLength(Code).IsRequired();
                b.Property(x => x.SourceNumber).HasMaxLength(Code);
                b.Property(x => x.AssessmentNote).HasMaxLength(Text);
                b.Property(x => x.ReinspectionKind).HasMaxLength(Code);
                b.HasOne(x => x.Case).WithMany(x => x.Items).HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Cascade);
            });

            // ------------------------------------------------------------------ personnel and welding
            builder.Entity<QualifiedPerson>(b =>
            {
                b.ToTable("QaPersons");
                b.Property(x => x.Name).HasMaxLength(Name).IsRequired();
                b.Property(x => x.PersonnelCode).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.PersonnelCode).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.StampId).HasMaxLength(Code);
                b.HasOne(x => x.Provider).WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<Qualification>(b =>
            {
                b.ToTable("QaQualifications");
                b.Property(x => x.Method).HasMaxLength(Code).IsRequired();
                b.Property(x => x.Scope).HasMaxLength(Text);
                b.Property(x => x.CertificateNumber).HasMaxLength(Name);
                b.Property(x => x.RevokeReason).HasMaxLength(Text);
                b.HasIndex(x => new { x.PersonId, x.Method });
                b.HasOne(x => x.Person).WithMany(x => x.Qualifications).HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<Wps>(b =>
            {
                b.ToTable("QaWps");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.Property(x => x.Revision).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => new { x.Number, x.Revision }).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Process).HasMaxLength(Code).IsRequired();
                b.Property(x => x.BasePNo).HasMaxLength(Name);
                b.Property(x => x.FillerFNo).HasMaxLength(Name);
                b.Property(x => x.FillerANo).HasMaxLength(Name);
                b.Property(x => x.ThicknessMin).HasPrecision(9, 2);
                b.Property(x => x.ThicknessMax).HasPrecision(9, 2);
                b.Property(x => x.Positions).HasMaxLength(Name);
                b.Property(x => x.PreheatMinC).HasPrecision(9, 2);
                b.Property(x => x.InterpassMaxC).HasPrecision(9, 2);
                b.Property(x => x.Pwht).HasMaxLength(Title);
                b.Property(x => x.ShieldingGas).HasMaxLength(Name);
                b.Property(x => x.Note).HasMaxLength(Text);
            });
            builder.Entity<Pqr>(b =>
            {
                b.ToTable("QaPqrs");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.CodeEdition).HasMaxLength(Name);
                b.Property(x => x.Process).HasMaxLength(Code);
                b.Property(x => x.Note).HasMaxLength(Text);
            });
            builder.Entity<WpsPqr>(b =>
            {
                b.ToTable("QaWpsPqrs");
                b.HasIndex(x => new { x.WpsId, x.PqrId }).IsUnique();
                b.HasOne(x => x.Wps).WithMany(x => x.Pqrs).HasForeignKey(x => x.WpsId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Pqr).WithMany().HasForeignKey(x => x.PqrId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<WeldLog>(b =>
            {
                b.ToTable("QaWeldLogs");
                b.Property(x => x.SerialNumber).HasMaxLength(Name);
                b.Property(x => x.JointNo).HasMaxLength(Code).IsRequired();
                b.Property(x => x.FillerBatch).HasMaxLength(Name);
                b.Property(x => x.PreheatC).HasPrecision(9, 2);
                b.Property(x => x.InterpassC).HasPrecision(9, 2);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasIndex(x => x.QualificationId);
                b.HasIndex(x => x.WorkOrderId);
                b.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Wps).WithMany().HasForeignKey(x => x.WpsId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Qualification).WithMany().HasForeignKey(x => x.QualificationId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<WeldLogReport>(b =>
            {
                b.ToTable("QaWeldLogReports");
                b.HasOne(x => x.WeldLog).WithMany(x => x.Reports).HasForeignKey(x => x.WeldLogId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.TestReport).WithMany().HasForeignKey(x => x.TestReportId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ material certificates
            builder.Entity<MaterialCertificate>(b =>
            {
                b.ToTable("QaMaterialCertificates");
                b.Property(x => x.Number).HasMaxLength(Name).IsRequired();
                b.Property(x => x.Issuer).HasMaxLength(Name);
                b.Property(x => x.Summary).HasMaxLength(LongText);
                b.Property(x => x.VerificationNote).HasMaxLength(Text);
                b.HasIndex(x => x.LotId);
                b.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ CAPA and complaints
            builder.Entity<Capa>(b =>
            {
                b.ToTable("QaCapas");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.Property(x => x.Problem).HasMaxLength(LongText);
                b.Property(x => x.Containment).HasMaxLength(LongText);
                b.Property(x => x.RootCauseAnalysis).HasMaxLength(LongText);
                b.Property(x => x.EffectivenessCriteria).HasMaxLength(Text);
                b.Property(x => x.EffectivenessResult).HasMaxLength(LongText);
                b.Property(x => x.CloseNote).HasMaxLength(Text);
                b.Property(x => x.CancelReason).HasMaxLength(Text);
                b.Property(x => x.EditorUserIds).HasMaxLength(Text);
                b.Property(x => x.Iteration).HasDefaultValue(1);
            });
            builder.Entity<CapaAction>(b =>
            {
                b.ToTable("QaCapaActions");
                b.Property(x => x.Description).HasMaxLength(Text).IsRequired();
                b.Property(x => x.EvidenceNote).HasMaxLength(Text);
                b.Property(x => x.Iteration).HasDefaultValue(1);
                b.HasOne(x => x.Capa).WithMany(x => x.Actions).HasForeignKey(x => x.CapaId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<CustomerComplaint>(b =>
            {
                b.ToTable("QaComplaints");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.Property(x => x.Channel).HasMaxLength(Name);
                b.Property(x => x.Description).HasMaxLength(LongText);
                b.Property(x => x.Response).HasMaxLength(LongText);
                b.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Capa).WithMany().HasForeignKey(x => x.CapaId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ release
            builder.Entity<ReleaseNote>(b =>
            {
                b.ToTable("QaReleaseNotes");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.WorkOrderId, x.Status });
                b.Property(x => x.ItemsText).HasMaxLength(LongText);
                b.Property(x => x.ChecksSnapshot).HasColumnType("nvarchar(max)");
                b.Property(x => x.CertificateOfConformityNumber).HasMaxLength(Code);
                b.Property(x => x.CancelReason).HasMaxLength(Text);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.Property(x => x.EditorUserIds).HasMaxLength(Text);
                b.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<ReleaseNoteLine>(b =>
            {
                b.ToTable("QaReleaseNoteLines");
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.SerialNumbers).HasMaxLength(LongText);
                b.HasIndex(x => x.ReleaseNoteId);
                b.HasOne(x => x.ReleaseNote).WithMany(x => x.Lines).HasForeignKey(x => x.ReleaseNoteId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ document control
            builder.Entity<ControlledDocument>(b =>
            {
                b.ToTable("QaDocuments");
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Code).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Department).HasMaxLength(Code);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.CurrentRevision).HasMaxLength(Code);
                b.Property(x => x.RetentionClass).HasMaxLength(Name);
                b.Property(x => x.ResponsibleUnit).HasMaxLength(Name);
            });
            builder.Entity<DocumentRevisionRecord>(b =>
            {
                b.ToTable("QaDocumentRevisions");
                b.Property(x => x.Revision).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => new { x.DocumentId, x.Revision }).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.ChangeSummary).HasMaxLength(LongText);
                b.HasOne(x => x.Document).WithMany(x => x.Revisions).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<DocumentDistribution>(b =>
            {
                b.ToTable("QaDocumentDistributions");
                b.Property(x => x.RecipientUnit).HasMaxLength(Name);
                b.HasOne(x => x.Revision).WithMany(x => x.Distributions).HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<RetentionRule>(b =>
            {
                b.ToTable("QaRetentionRules");
                b.Property(x => x.RecordType).HasMaxLength(Name).IsRequired();
                b.HasIndex(x => x.RecordType).IsUnique();
                b.Property(x => x.Source).HasMaxLength(Title);
            });

            // ------------------------------------------------------------------ audit and management review
            builder.Entity<AuditProgram>(b =>
            {
                b.ToTable("QaAuditPrograms");
                b.Property(x => x.Scope).HasMaxLength(LongText);
                b.Property(x => x.Note).HasMaxLength(Text);
            });
            builder.Entity<Audit>(b =>
            {
                b.ToTable("QaAudits");
                b.Property(x => x.Number).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.AuditedUnit).HasMaxLength(Name).IsRequired();
                b.Property(x => x.Criteria).HasMaxLength(LongText);
                b.Property(x => x.TeamUserIds).HasMaxLength(Text);
                b.HasOne(x => x.Program).WithMany().HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<AuditFinding>(b =>
            {
                b.ToTable("QaAuditFindings");
                b.Property(x => x.Clause).HasMaxLength(Name).IsRequired();
                b.Property(x => x.Evidence).HasMaxLength(LongText);
                b.HasOne(x => x.Audit).WithMany(x => x.Findings).HasForeignKey(x => x.AuditId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Capa).WithMany().HasForeignKey(x => x.CapaId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<ManagementReview>(b =>
            {
                b.ToTable("QaManagementReviews");
                b.Property(x => x.Attendees).HasMaxLength(LongText);
                b.HasIndex(x => x.OfMeetingId);
                b.HasOne<Entities.Model.NasbNiro.Office.OfMeeting>().WithMany().HasForeignKey(x => x.OfMeetingId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<ManagementReviewInput>(b =>
            {
                b.ToTable("QaReviewInputs");
                b.Property(x => x.Text).HasMaxLength(LongText);
                b.HasOne(x => x.Review).WithMany(x => x.Inputs).HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<ManagementReviewDecision>(b =>
            {
                b.ToTable("QaReviewDecisions");
                b.Property(x => x.Text).HasMaxLength(LongText).IsRequired();
                b.Property(x => x.DoneNote).HasMaxLength(Text);
                b.Property(x => x.DoneRequestNote).HasMaxLength(Text);
                b.HasOne(x => x.Review).WithMany(x => x.Decisions).HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Capa).WithMany().HasForeignKey(x => x.CapaId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<QualitySetting>(b =>
            {
                b.ToTable("QaSettings");
                b.Property(x => x.Key).HasMaxLength(100).IsRequired();
                b.HasIndex(x => x.Key).IsUnique();
                b.Property(x => x.Value).HasPrecision(18, 4);
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.Source).HasMaxLength(Title);
            });
        }
    }
}
