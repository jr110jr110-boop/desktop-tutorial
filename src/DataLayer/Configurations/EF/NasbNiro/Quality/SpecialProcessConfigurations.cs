using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Quality
{
    // Package C (special processes and material verification): 14 new tables and new columns on QaMaterialCertificates, the qualification and WPS tables and the two operation tables of production.
    // No migration is written here.
    public static class SpecialProcessModelBuilderExtensions
    {
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddSpecialProcessConfigurations(this ModelBuilder builder)
        {
            builder.Entity<SpProcessType>(b =>
            {
                b.ToTable("QaSpProcessTypes");
                b.Property(x => x.Code).HasMaxLength(30).IsRequired();
                b.Property(x => x.Title).HasMaxLength(200).IsRequired();
                b.Property(x => x.Description).HasMaxLength(1000);
                b.HasIndex(x => x.Code).IsUnique().HasFilter(NotDeleted);
                b.HasMany(x => x.Parameters).WithOne(x => x.Type).HasForeignKey(x => x.TypeId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<SpParameter>(b =>
            {
                b.ToTable("QaSpParameters");
                b.Property(x => x.Name).HasMaxLength(100).IsRequired();
                b.Property(x => x.Unit).HasMaxLength(30);
                b.Property(x => x.Min).HasPrecision(18, 4);
                b.Property(x => x.Max).HasPrecision(18, 4);
                b.HasIndex(x => new { x.TypeId, x.Name }).IsUnique();
            });

            builder.Entity<SpQualification>(b =>
            {
                b.ToTable("QaSpQualifications");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300);
                b.Property(x => x.ProcedureNumber).HasMaxLength(200);
                b.Property(x => x.ProcedureRevisionCode).HasMaxLength(50);
                b.Property(x => x.Note).HasMaxLength(1000);
                b.Property(x => x.RejectReason).HasMaxLength(1000);
                b.Property(x => x.VoidReason).HasMaxLength(1000);
                b.Property(x => x.EditorUserIds).HasMaxLength(400);
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.TypeId, x.Status });
                b.HasIndex(x => x.ValidTo);
                b.HasOne(x => x.Type).WithMany().HasForeignKey(x => x.TypeId).OnDelete(DeleteBehavior.Restrict);
                b.HasMany(x => x.Limits).WithOne(x => x.Qualification).HasForeignKey(x => x.QualificationId).OnDelete(DeleteBehavior.Cascade);
                b.HasMany(x => x.Equipments).WithOne(x => x.Qualification).HasForeignKey(x => x.QualificationId).OnDelete(DeleteBehavior.Cascade);
                b.HasMany(x => x.Operators).WithOne(x => x.Qualification).HasForeignKey(x => x.QualificationId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<SpQualLimit>(b =>
            {
                b.ToTable("QaSpQualLimits");
                b.Property(x => x.Name).HasMaxLength(100).IsRequired();
                b.Property(x => x.Unit).HasMaxLength(30);
                b.Property(x => x.Min).HasPrecision(18, 4);
                b.Property(x => x.Max).HasPrecision(18, 4);
                b.HasIndex(x => new { x.QualificationId, x.Name }).IsUnique();
            });
            builder.Entity<SpQualEquipment>(b =>
            {
                b.ToTable("QaSpQualEquipments");
                b.Property(x => x.SurveyNote).HasMaxLength(500);
                b.HasIndex(x => x.QualificationId);
                b.HasIndex(x => x.MachineId);
                b.HasIndex(x => x.EquipmentId);
            });
            builder.Entity<SpQualOperator>(b =>
            {
                b.ToTable("QaSpQualOperators");
                b.HasIndex(x => new { x.QualificationId, x.PersonId }).IsUnique();
            });

            builder.Entity<SpRun>(b =>
            {
                b.ToTable("QaSpRuns");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.SerialNumbers).HasMaxLength(1000);
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.ChartFileName).HasMaxLength(260);
                b.Property(x => x.CertificateNumber).HasMaxLength(100);
                b.Property(x => x.ReviewNote).HasMaxLength(1000);
                b.Property(x => x.VoidReason).HasMaxLength(1000);
                b.Property(x => x.Note).HasMaxLength(1000);
                b.Property(x => x.ResultText).HasMaxLength(1000);
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.OperationId);
                b.HasIndex(x => x.PartOperationId);
                b.HasIndex(x => x.ReceiptDocumentId);
                b.HasIndex(x => new { x.TypeId, x.Status });
                b.HasOne(x => x.Type).WithMany().HasForeignKey(x => x.TypeId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Qualification).WithMany().HasForeignKey(x => x.QualificationId).OnDelete(DeleteBehavior.Restrict);
                b.HasMany(x => x.Values).WithOne(x => x.Run).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
                b.HasMany(x => x.Instruments).WithOne(x => x.Run).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<SpRunValue>(b =>
            {
                b.ToTable("QaSpRunValues");
                b.Property(x => x.Name).HasMaxLength(100).IsRequired();
                b.Property(x => x.Unit).HasMaxLength(30);
                b.Property(x => x.Value).HasPrecision(18, 4);
                b.Property(x => x.Min).HasPrecision(18, 4);
                b.Property(x => x.Max).HasPrecision(18, 4);
                b.HasIndex(x => new { x.RunId, x.Name }).IsUnique();
            });
            builder.Entity<SpRunInstrument>(b =>
            {
                b.ToTable("QaSpRunInstruments");
                b.HasIndex(x => new { x.RunId, x.EquipmentId }).IsUnique();
            });

            builder.Entity<SpSupplierApproval>(b =>
            {
                b.ToTable("QaSpSupplierApprovals");
                b.Property(x => x.AccreditationNumber).HasMaxLength(100);
                b.Property(x => x.Note).HasMaxLength(1000);
                b.Property(x => x.RejectReason).HasMaxLength(1000);
                b.Property(x => x.VoidReason).HasMaxLength(1000);
                b.Property(x => x.EditorUserIds).HasMaxLength(400);
                b.HasIndex(x => new { x.ProviderId, x.TypeId, x.Status });
                // one live approval (waiting or approved) per supplier and process
                b.HasIndex(x => new { x.ProviderId, x.TypeId }).IsUnique().HasFilter("[Deleted] = 0 AND [Status] IN (0, 1)").HasDatabaseName("UX_QaSpSupplierApprovals_LivePerProcess");
                b.HasOne(x => x.Type).WithMany().HasForeignKey(x => x.TypeId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<MaterialSpec>(b =>
            {
                b.ToTable("QaMaterialSpecs");
                b.Property(x => x.Grade).HasMaxLength(100).IsRequired();
                b.Property(x => x.Standard).HasMaxLength(100);
                b.Property(x => x.SpecKey).HasMaxLength(220).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300);
                b.Property(x => x.Reason).HasMaxLength(1000);
                b.Property(x => x.VoidReason).HasMaxLength(1000);
                b.Property(x => x.ContentHash).HasMaxLength(80);
                b.Property(x => x.EditorUserIds).HasMaxLength(400);
                b.HasIndex(x => new { x.SpecKey, x.Version }).IsUnique().HasFilter(NotDeleted);
                // one approved version per grade and standard
                b.HasIndex(x => x.SpecKey).IsUnique().HasFilter("[Deleted] = 0 AND [Status] = 2").HasDatabaseName("UX_QaMaterialSpecs_ApprovedPerKey");
                b.HasMany(x => x.Limits).WithOne(x => x.Spec).HasForeignKey(x => x.SpecId).OnDelete(DeleteBehavior.Cascade);
                b.HasMany(x => x.Items).WithOne(x => x.Spec).HasForeignKey(x => x.SpecId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<MaterialSpecLimit>(b =>
            {
                b.ToTable("QaMaterialSpecLimits");
                b.Property(x => x.Name).HasMaxLength(60).IsRequired();
                b.Property(x => x.Unit).HasMaxLength(30);
                b.Property(x => x.Min).HasPrecision(18, 4);
                b.Property(x => x.Max).HasPrecision(18, 4);
                b.HasIndex(x => new { x.SpecId, x.Kind, x.Name }).IsUnique();
            });
            builder.Entity<MaterialSpecItem>(b =>
            {
                b.ToTable("QaMaterialSpecItems");
                b.HasIndex(x => new { x.SpecId, x.ItemId }).IsUnique();
                b.HasIndex(x => x.ItemId);
            });

            builder.Entity<CertificateValue>(b =>
            {
                b.ToTable("QaCertificateValues");
                b.Property(x => x.Name).HasMaxLength(60).IsRequired();
                b.Property(x => x.Unit).HasMaxLength(30);
                b.Property(x => x.Value).HasPrecision(18, 4);
                b.Property(x => x.Min).HasPrecision(18, 4);
                b.Property(x => x.Max).HasPrecision(18, 4);
                b.HasIndex(x => new { x.CertificateId, x.Kind, x.Name }).IsUnique();
                b.HasOne(x => x.Certificate).WithMany(x => x.Values).HasForeignKey(x => x.CertificateId).OnDelete(DeleteBehavior.Cascade);
            });

            // new columns of existing tables
            builder.Entity<MaterialCertificate>(b =>
            {
                b.Property(x => x.HeatNumber).HasMaxLength(100);
                b.Property(x => x.Grade).HasMaxLength(100);
                b.Property(x => x.Standard).HasMaxLength(100);
                b.Property(x => x.InspectorOrganisation).HasMaxLength(200);
                b.Property(x => x.InspectorName).HasMaxLength(200);
                b.Property(x => x.InspectorStampNo).HasMaxLength(100);
                b.Property(x => x.ManufacturerSignatory).HasMaxLength(200);
                b.Property(x => x.EvaluationText).HasMaxLength(1000);
            });
            builder.Entity<Qualification>(b =>
            {
                b.Property(x => x.RangePNos).HasMaxLength(100);
                b.Property(x => x.RangeGroupNos).HasMaxLength(100);
                b.Property(x => x.RangeFNos).HasMaxLength(100);
                b.Property(x => x.RangePositions).HasMaxLength(100);
                b.Property(x => x.RangeThicknessMin).HasPrecision(18, 3);
                b.Property(x => x.RangeThicknessMax).HasPrecision(18, 3);
                b.Property(x => x.RangeDiameterMin).HasPrecision(18, 3);
                b.Property(x => x.RangeDiameterMax).HasPrecision(18, 3);
            });
            builder.Entity<Wps>(b =>
            {
                b.Property(x => x.BaseGroupNo).HasMaxLength(30);
                b.Property(x => x.DiameterMin).HasPrecision(18, 3);
                b.Property(x => x.DiameterMax).HasPrecision(18, 3);
            });
        }
    }
}
