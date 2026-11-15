using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Finance
{
    // Sepidar transfer (per document): the kinds that can be transferred, the queue and the attempts. Tables start with "Sp". Nothing cascades. The source document is referenced by type name and id (the documents are of many tables).
    public static class SepidarTransferModelBuilderExtensions
    {
        public static void AddSepidarTransferConfigurations(this ModelBuilder builder)
        {
            builder.Entity<SpTransferType>(b =>
            {
                b.ToTable("SpTransferTypes");
                b.Property(x => x.Code).HasMaxLength(40).IsRequired();
                b.Property(x => x.Title).HasMaxLength(200).IsRequired();
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.Code).IsUnique();
            });
            builder.Entity<SpOutboxItem>(b =>
            {
                b.ToTable("SpOutboxItems");
                b.Property(x => x.TransferTypeCode).HasMaxLength(40).IsRequired();
                b.Property(x => x.SourceType).HasMaxLength(60).IsRequired();
                b.Property(x => x.SourceNumber).HasMaxLength(60);
                b.Property(x => x.Title).HasMaxLength(300);
                b.Property(x => x.PayloadJson).IsRequired();
                b.Property(x => x.PayloadHash).HasMaxLength(64).IsRequired();
                b.Property(x => x.SourceRowVersion).HasMaxLength(8);
                b.Property(x => x.PreparedByAgent).HasMaxLength(50);
                b.Property(x => x.SourceMakerUserIds).HasMaxLength(2000);
                b.Property(x => x.RejectReason).HasMaxLength(1000);
                b.Property(x => x.CancelReason).HasMaxLength(1000);
                b.Property(x => x.LastError).HasMaxLength(500);
                b.Property(x => x.ExternalReference).HasMaxLength(200);
                // one live item per document and kind (a sent, rejected, cancelled or superseded item is history); the in-memory test database does not enforce it, so the service also looks before it inserts
                b.HasIndex(x => new { x.TransferTypeCode, x.SourceType, x.SourceId }).IsUnique().HasFilter("[Status] IN (1, 2, 3, 4, 6)").HasDatabaseName("IX_SpOutboxItems_Live");
                b.HasIndex(x => new { x.TransferTypeCode, x.SourceType, x.SourceId, x.Id });
                b.HasIndex(x => new { x.Status, x.NextAttemptAt });
                b.HasIndex(x => x.PreparedAt);
            });
            builder.Entity<SpOutboxAttempt>(b =>
            {
                b.ToTable("SpOutboxAttempts");
                b.Property(x => x.Error).HasMaxLength(500);
                b.Property(x => x.SenderName).HasMaxLength(50);
                b.Property(x => x.ExternalReference).HasMaxLength(200);
                b.HasOne(x => x.Item).WithMany(i => i.Attempts).HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => new { x.ItemId, x.AttemptNumber });
            });
        }
    }
}
