using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.OpsData;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.OpsData
{
    // PB8: operational data control. Tables start with "Od". Document references are plain ids (documents of several tables), so there are no foreign keys; the rows are history.
    public static class OpsDataModelBuilderExtensions
    {
        public static void AddOpsDataConfigurations(this ModelBuilder builder)
        {
            builder.Entity<InventoryDocumentLine>(b =>
            {
                b.Property(x => x.WeighedWeightKg).HasPrecision(18, 4);
                b.Property(x => x.CertificateWeightKg).HasPrecision(18, 4);
            });

            builder.Entity<OpsFinding>(b =>
            {
                b.ToTable("OdFindings");
                b.Property(x => x.DocumentType).HasMaxLength(40).IsRequired();
                b.Property(x => x.DocumentNumber).HasMaxLength(60);
                b.Property(x => x.ItemTitle).HasMaxLength(300);
                b.Property(x => x.RuleCode).HasMaxLength(30).IsRequired();
                b.Property(x => x.Message).HasMaxLength(1000);
                b.Property(x => x.MessageMasked).HasMaxLength(1000);
                b.Property(x => x.EnteredValue).HasPrecision(18, 4);
                b.Property(x => x.ReferenceValue).HasPrecision(18, 4);
                b.Property(x => x.DeviationPercent).HasPrecision(18, 2);
                b.Property(x => x.ResolutionNote).HasMaxLength(500);
                // one finding per document, line and rule (a null line is one value of its own): re-evaluation updates the row
                b.Property(x => x.Field).HasMaxLength(40);
                b.HasIndex(x => new { x.DocumentType, x.DocumentId, x.LineId, x.RowNumber, x.ItemId, x.RuleCode, x.Field }).IsUnique().HasFilter("[Deleted] = 0");
                b.HasIndex(x => new { x.Unit, x.Status });
                b.HasIndex(x => x.RuleCode);
                b.HasIndex(x => x.DetectedAt);
                b.HasIndex(x => x.ExceptionId);
            });

            builder.Entity<OpsException>(b =>
            {
                b.ToTable("OdExceptions");
                b.Property(x => x.DocumentType).HasMaxLength(40).IsRequired();
                b.Property(x => x.DocumentNumber).HasMaxLength(60);
                b.Property(x => x.RuleCode).HasMaxLength(30).IsRequired();
                b.Property(x => x.Message).HasMaxLength(1000);
                b.Property(x => x.EnteredValue).HasPrecision(18, 4);
                b.Property(x => x.ReferenceValue).HasPrecision(18, 4);
                b.Property(x => x.DeviationPercent).HasPrecision(18, 2);
                b.Property(x => x.Justification).HasMaxLength(1000).IsRequired();
                b.Property(x => x.DecisionReason).HasMaxLength(500);
                b.Property(x => x.Field).HasMaxLength(40);
                b.HasIndex(x => new { x.DocumentType, x.DocumentId, x.RuleCode });
                b.HasIndex(x => new { x.DocumentType, x.DocumentId, x.LineId, x.RowNumber, x.ItemId, x.RuleCode, x.Field }).IsUnique().HasFilter("[Deleted] = 0 AND [Status] = 0").HasDatabaseName("IX_OdExceptions_OnePending");
                b.HasIndex(x => new { x.Unit, x.Status });
                b.HasIndex(x => x.RequestedByUserId);
            });

            builder.Entity<OpsReadinessException>(b =>
            {
                b.ToTable("OdReadinessExceptions");
                b.Property(x => x.ItemCodes).HasMaxLength(400);
                b.Property(x => x.Justification).HasMaxLength(1000).IsRequired();
                b.Property(x => x.PmoNote).HasMaxLength(500);
                b.Property(x => x.CeoNote).HasMaxLength(500);
                b.Property(x => x.RejectReason).HasMaxLength(500);
                b.HasIndex(x => new { x.WorkOrderId, x.Status });
            });

            builder.Entity<OpsReadinessObligation>(b =>
            {
                b.ToTable("OdReadinessObligations");
                b.Property(x => x.ItemCode).HasMaxLength(20).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300);
                b.Property(x => x.OwnerUnit).HasMaxLength(30);
                b.Property(x => x.Detail).HasMaxLength(2000);
                b.HasIndex(x => new { x.WorkOrderId, x.ItemCode }).IsUnique().HasFilter("[Deleted] = 0");
                b.HasIndex(x => new { x.Status, x.DueDate });
            });
        }
    }
}
