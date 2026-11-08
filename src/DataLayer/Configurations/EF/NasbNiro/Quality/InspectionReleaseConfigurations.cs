using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Quality
{
    // Package B (inspection and release integrity): five new tables, two new columns on QaRecords and two on QaMeasurements. No migration is written here.
    public static class InspectionReleaseModelBuilderExtensions
    {
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddInspectionReleaseConfigurations(this ModelBuilder builder)
        {
            builder.Entity<QcCharacteristicList>(b =>
            {
                b.ToTable("QaCharacteristicLists");
                b.Property(x => x.DocumentNumber).HasMaxLength(200);
                b.Property(x => x.RevisionCode).HasMaxLength(50);
                b.Property(x => x.Reason).HasMaxLength(1000);
                b.Property(x => x.ContentHash).HasMaxLength(80);
                b.Property(x => x.ReturnNote).HasMaxLength(1000);
                b.Property(x => x.VoidReason).HasMaxLength(1000);
                b.Property(x => x.EditorUserIds).HasMaxLength(400);
                b.HasIndex(x => new { x.RevisionId, x.Version }).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => new { x.DeliverableId, x.Status });
                b.HasMany(x => x.Items).WithOne(x => x.List).HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<QcCharacteristic>(b =>
            {
                b.ToTable("QaCharacteristics");
                b.Property(x => x.No).HasMaxLength(30).IsRequired();
                b.Property(x => x.Name).HasMaxLength(200).IsRequired();
                b.Property(x => x.Nominal).HasPrecision(18, 4);
                b.Property(x => x.TolPlus).HasPrecision(18, 4);
                b.Property(x => x.TolMinus).HasPrecision(18, 4);
                b.Property(x => x.Unit).HasMaxLength(30);
                b.Property(x => x.Method).HasMaxLength(300);
                b.Property(x => x.StageCode2).HasMaxLength(10);
                b.HasIndex(x => new { x.ListId, x.No }).IsUnique();
            });
            builder.Entity<NoDrawingFlag>(b =>
            {
                b.ToTable("QaNoDrawingFlags");
                b.Property(x => x.PartNumber).HasMaxLength(100);
                b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
                b.Property(x => x.DecisionNote).HasMaxLength(500);
                b.Property(x => x.VoidReason).HasMaxLength(500);
                b.HasIndex(x => x.ItemId);
                // one live flag (waiting or active) per part
                b.HasIndex(x => x.ItemId).IsUnique().HasFilter("[Deleted] = 0 AND [Status] IN (0, 1)").HasDatabaseName("UX_QaNoDrawingFlags_LivePerItem");
            });
            builder.Entity<QcFirstArticle>(b =>
            {
                b.ToTable("QaFirstArticles");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.PartNumber).HasMaxLength(100);
                b.Property(x => x.Fingerprint).HasMaxLength(400).IsRequired();
                b.Property(x => x.FingerprintText).HasMaxLength(1000);
                b.Property(x => x.Trigger).HasMaxLength(300);
                b.Property(x => x.QcAcceptNote).HasMaxLength(500);
                b.Property(x => x.QaApproveNote).HasMaxLength(500);
                b.Property(x => x.RejectReason).HasMaxLength(500);
                b.Property(x => x.VoidReason).HasMaxLength(500);
                b.Property(x => x.Note).HasMaxLength(1000);
                b.HasIndex(x => new { x.ItemId, x.Fingerprint, x.Status });
                b.HasIndex(x => x.ProductionOrderId);
                // one live first article (open, measured, accepted or approved) per part and configuration
                b.HasIndex(x => new { x.ItemId, x.Fingerprint }).IsUnique().HasFilter("[Deleted] = 0 AND [Status] IN (0, 1, 2, 3)").HasDatabaseName("UX_QaFirstArticles_LivePerConfiguration");
            });
            builder.Entity<AsBuiltRecord>(b =>
            {
                b.ToTable("QaAsBuiltRecords");
                b.Property(x => x.SerialNumber).HasMaxLength(200);
                b.Property(x => x.Reason).HasMaxLength(1000);
                b.Property(x => x.ProductCode).HasMaxLength(100);
                b.Property(x => x.BomText).HasMaxLength(200);
                b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
                b.HasIndex(x => new { x.WorkOrderId, x.ReleaseNoteId });
                b.HasIndex(x => new { x.ItemId, x.SerialNumber, x.Version });
                // one current record per unit
                b.HasIndex(x => new { x.WorkOrderId, x.ItemId, x.SerialNumber }).IsUnique().HasFilter("[Deleted] = 0 AND [Status] = 0").HasDatabaseName("UX_QaAsBuiltRecords_CurrentPerUnit");
            });
        }
    }
}
