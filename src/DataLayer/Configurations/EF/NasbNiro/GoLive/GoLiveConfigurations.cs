using Entities.Model.NasbNiro.GoLive;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Inventory
{
    // Package G (go-live cut-over): four new tables (GlActivations, GlLegacyUses, GlCharAdoptions, GlConfigAdoptions) and new columns on PurchaseOrderLine, PcTransmittalLine and QcRecord (see Entities). No migration is written here.
    public static class GoLiveModelBuilderExtensions
    {
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddGoLiveConfigurations(this ModelBuilder builder)
        {
            builder.Entity<GlActivation>(b =>
            {
                b.ToTable("GlActivations");
                b.Property(x => x.Note).HasMaxLength(500);
            });
            builder.Entity<GlLegacyUse>(b =>
            {
                b.ToTable("GlLegacyUses");
                b.Property(x => x.ObjectKey).HasMaxLength(100).IsRequired();
                b.Property(x => x.Number).HasMaxLength(100);
                b.Property(x => x.Note).HasMaxLength(1000).IsRequired();
                // one row per object and kind
                b.HasIndex(x => new { x.Kind, x.ObjectKey }).IsUnique().HasFilter(NotDeleted).HasDatabaseName("UX_GlLegacyUses_KindObject");
                b.HasIndex(x => new { x.Kind, x.UsedAt });
            });
            builder.Entity<GlCharAdoption>(b =>
            {
                b.ToTable("GlCharAdoptions");
                b.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
                b.Property(x => x.EvidenceRef).HasMaxLength(500).IsRequired();
                b.Property(x => x.AcceptNote).HasMaxLength(1000);
                b.Property(x => x.VoidReason).HasMaxLength(1000);
                // one live adoption (requested or accepted) per drawing revision
                b.HasIndex(x => x.RevisionId).IsUnique().HasFilter("[Deleted] = 0 AND [Status] IN (0, 2)").HasDatabaseName("UX_GlCharAdoptions_LivePerRevision");
                b.HasIndex(x => x.DeliverableId);
            });
            builder.Entity<GlConfigAdoption>(b =>
            {
                b.ToTable("GlConfigAdoptions");
                b.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
                b.Property(x => x.EvidenceRef).HasMaxLength(500).IsRequired();
                b.Property(x => x.ComparisonNote).HasMaxLength(2000);
                b.Property(x => x.EngNote).HasMaxLength(1000);
                b.Property(x => x.PmoNote).HasMaxLength(1000);
                b.Property(x => x.VoidReason).HasMaxLength(1000);
                // one live adoption (requested, partly reviewed or accepted) per work order
                b.HasIndex(x => x.WorkOrderId).IsUnique().HasFilter("[Deleted] = 0 AND [Status] IN (0, 1, 2)").HasDatabaseName("UX_GlConfigAdoptions_LivePerWorkOrder");
            });
            builder.Entity<Entities.Model.NasbNiro.Procurement.PurchaseOrderLine>(b =>
            {
                b.Property(x => x.DrawingNumber).HasMaxLength(200);
                b.Property(x => x.DrawingRevision).HasMaxLength(50);
                b.Property(x => x.SupplierConfirmedRevision).HasMaxLength(50);
                b.Property(x => x.SupplierConfirmedRef).HasMaxLength(300);
                b.HasIndex(x => x.DrawingDeliverableId);
            });
            builder.Entity<Entities.Model.NasbNiro.Quality.QcRecord>(b => b.Property(x => x.LegacyNote).HasMaxLength(500));
        }
    }
}
