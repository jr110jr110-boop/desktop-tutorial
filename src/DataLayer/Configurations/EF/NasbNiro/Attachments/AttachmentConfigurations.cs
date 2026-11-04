using Entities.Model.NasbNiro.Attachments;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Attachments
{
    // PB3: the one table of record attachments. No existing table is changed.
    public static class AttachmentModelBuilderExtensions
    {
        public static void AddAttachmentConfigurations(this ModelBuilder builder)
        {
            builder.Entity<RecordAttachment>(b =>
            {
                b.ToTable("AttRecordAttachments");
                b.Property(x => x.Title).HasMaxLength(300);
                b.Property(x => x.Revision).HasMaxLength(30);
                b.Property(x => x.FileName).HasMaxLength(260);
                b.Property(x => x.VoidReason).HasMaxLength(1000);
                b.HasIndex(x => new { x.OwnerType, x.OwnerId });
                b.HasIndex(x => x.DocumentId);
                b.HasIndex(x => x.SupersededById);
                // one successor per file: two people replacing the same file at once cannot both win (the second save finds the link already set)
                b.Property(x => x.SupersededById).IsConcurrencyToken();
                b.HasOne(x => x.SupersededBy).WithMany().HasForeignKey(x => x.SupersededById).OnDelete(DeleteBehavior.Restrict);
                // a stored file is never removed while an attachment row points at it
                b.HasOne<Entities.Model.Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
