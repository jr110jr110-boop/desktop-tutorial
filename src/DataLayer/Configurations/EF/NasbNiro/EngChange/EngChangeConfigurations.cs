using Entities.Model.NasbNiro.EngChange;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.EngChange
{
    // Package A2 (engineering change, effectivity, single BOM master): six new tables and two new columns on the production order. No migration is written here.
    public static class EngChangeModelBuilderExtensions
    {
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddEngChangeConfigurations(this ModelBuilder builder)
        {
            builder.Entity<EcRequest>(b =>
            {
                b.ToTable("EcRequests");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
                b.Property(x => x.Description).HasMaxLength(4000);
                b.Property(x => x.DecisionNote).HasMaxLength(1000);
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.Status);
                b.HasMany(x => x.Targets).WithOne(x => x.Request).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<EcRequestTarget>(b =>
            {
                b.ToTable("EcRequestTargets");
                b.Property(x => x.PartNumber).HasMaxLength(60);
                b.Property(x => x.Reference).HasMaxLength(300);
                b.Property(x => x.Note).HasMaxLength(1000);
                b.HasIndex(x => x.RequestId);
                b.HasIndex(x => x.PartNumber);
            });

            builder.Entity<EcOrder>(b =>
            {
                b.ToTable("EcOrders");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.Summary).HasMaxLength(4000);
                b.Property(x => x.EffectiveFromOrderNumber).HasMaxLength(60);
                b.Property(x => x.EffectiveFromSerial).HasMaxLength(60);
                b.Property(x => x.EffectivityNote).HasMaxLength(1000);
                b.Property(x => x.AffectedParts).HasMaxLength(4000);
                b.Property(x => x.ImpactSha256).HasMaxLength(64);
                b.Property(x => x.ContentSha256).HasMaxLength(64);
                b.Property(x => x.EngineeringNote).HasMaxLength(1000);
                b.Property(x => x.QaNote).HasMaxLength(1000);
                b.Property(x => x.RejectReason).HasMaxLength(1000);
                b.Property(x => x.CancelReason).HasMaxLength(1000);
                b.Property(x => x.EditorUserIds).HasMaxLength(400);
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.RequestId);
                b.HasIndex(x => x.Status);
                b.HasOne(x => x.Request).WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
                b.HasMany(x => x.Changes).WithOne(x => x.Order).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
                b.HasMany(x => x.Dispositions).WithOne(x => x.Order).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
                b.HasMany(x => x.Tasks).WithOne(x => x.Order).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<EcOrderChange>(b =>
            {
                b.ToTable("EcOrderChanges");
                b.Property(x => x.Code).HasMaxLength(100);
                b.Property(x => x.PartNumber).HasMaxLength(60);
                b.Property(x => x.Reference).HasMaxLength(300);
                b.Property(x => x.Note).HasMaxLength(1000);
                b.HasIndex(x => new { x.Kind, x.TargetId });
                b.HasIndex(x => x.OrderId);
            });
            builder.Entity<EcDispositionItem>(b =>
            {
                b.ToTable("EcDispositions");
                b.Property(x => x.ImpactKey).HasMaxLength(100).IsRequired();
                b.Property(x => x.Text).HasMaxLength(600);
                b.Property(x => x.PartNumber).HasMaxLength(60);
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.Note).HasMaxLength(1000);
                b.HasIndex(x => new { x.OrderId, x.ImpactKey }).IsUnique();
            });
            builder.Entity<EcTask>(b =>
            {
                b.ToTable("EcTasks");
                b.Property(x => x.Title).HasMaxLength(600).IsRequired();
                b.Property(x => x.DoneNote).HasMaxLength(1000);
                b.Property(x => x.VerifyNote).HasMaxLength(1000);
                b.HasIndex(x => new { x.OrderId, x.Status });
                b.HasIndex(x => new { x.OwnerUserId, x.Status });
                b.HasOne<EcDispositionItem>().WithMany().HasForeignKey(x => x.DispositionItemId).OnDelete(DeleteBehavior.NoAction);
            });

            // the production order says where its components come from
            builder.Entity<MfProductionOrder>(b =>
            {
                b.Property(x => x.ComponentSource).HasMaxLength(12);
                b.Property(x => x.ComponentNote).HasMaxLength(500);
            });
        }
    }
}
