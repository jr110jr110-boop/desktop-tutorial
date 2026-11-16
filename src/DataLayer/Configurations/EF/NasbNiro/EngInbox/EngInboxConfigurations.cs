using Entities.Model.NasbNiro.EngInbox;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.EngInbox
{
    // ENG-F1: the engineering inbox tables (EngInbox*). Nothing cascades. One live package per (kind, platform, standard, contract, project, version): a rejected, dismissed or superseded package is history.
    public static class EngInboxModelBuilderExtensions
    {
        public static void AddEngInboxConfigurations(this ModelBuilder builder)
        {
            builder.Entity<EngInboxPackage>(b =>
            {
                b.ToTable("EngInboxPackages");
                b.Property(x => x.SourceName).HasMaxLength(20).IsRequired();
                b.Property(x => x.PlatformCode).HasMaxLength(40).IsRequired();
                b.Property(x => x.Standard).HasMaxLength(60).IsRequired();
                b.Property(x => x.ContractCode).HasMaxLength(60).IsRequired();
                b.Property(x => x.ProjectCode).HasMaxLength(60).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300);
                b.Property(x => x.PreparedByText).HasMaxLength(200);
                b.Property(x => x.ContentHash).HasMaxLength(64);
                b.Property(x => x.OriginalPath).HasMaxLength(500);
                b.Property(x => x.StoredPath).HasMaxLength(500);
                b.Property(x => x.StatusNote).HasMaxLength(500);
                b.Property(x => x.RejectCode).HasMaxLength(30);
                b.Property(x => x.ApplyBlockReason).HasMaxLength(1500);
                b.Property(x => x.DismissReason).HasMaxLength(1000);
                b.Property(x => x.FindingsJson);
                // the unique index of the live packages; the in-memory test database does not enforce a filtered index, so the service also looks before it inserts
                b.HasIndex(x => new { x.Kind, x.PlatformCode, x.Standard, x.ContractCode, x.ProjectCode, x.PackageVersion }).IsUnique()
                    .HasFilter("[Status] IN (1, 3, 4, 5) AND [Deleted] = 0").HasDatabaseName("IX_EngInboxPackages_Live");
                b.HasIndex(x => x.ContentHash);
                b.HasIndex(x => new { x.Status, x.Id });
                b.HasIndex(x => x.IngestedAt);
            });
            builder.Entity<EngInboxBomLine>(b =>
            {
                b.ToTable("EngInboxBomLines");
                b.Property(x => x.ParentCode).HasMaxLength(60);
                b.Property(x => x.PartCode).HasMaxLength(60).IsRequired();
                b.Property(x => x.Description).HasMaxLength(300);
                b.Property(x => x.Qty).HasPrecision(18, 4);
                b.Property(x => x.Unit).HasMaxLength(30);
                b.Property(x => x.Material).HasMaxLength(200);
                b.Property(x => x.DrawingNumber).HasMaxLength(100);
                b.Property(x => x.DrawingRevision).HasMaxLength(30);
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasOne(x => x.Package).WithMany(p => p.BomLines).HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => new { x.PackageId, x.SortOrder });
                b.HasIndex(x => x.PartCode);
            });
            builder.Entity<EngInboxOpcLine>(b =>
            {
                b.ToTable("EngInboxOpcLines");
                b.Property(x => x.PartCode).HasMaxLength(60).IsRequired();
                b.Property(x => x.Description).HasMaxLength(300);
                b.Property(x => x.WorkCenterCode).HasMaxLength(50);
                b.Property(x => x.SetupMinutes).HasPrecision(18, 2);
                b.Property(x => x.RunMinutes).HasPrecision(18, 2);
                b.Property(x => x.Predecessors).HasMaxLength(200);
                b.Property(x => x.StageCode).HasMaxLength(2);
                b.HasOne(x => x.Package).WithMany(p => p.OpcLines).HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => new { x.PackageId, x.SortOrder });
            });
            builder.Entity<EngInboxDocRef>(b =>
            {
                b.ToTable("EngInboxDocRefs");
                b.Property(x => x.Number).HasMaxLength(100);
                b.Property(x => x.Title).HasMaxLength(300);
                b.Property(x => x.Revision).HasMaxLength(30);
                b.Property(x => x.DocType).HasMaxLength(60);
                b.Property(x => x.FileName).HasMaxLength(200);
                b.Property(x => x.FileSha256).HasMaxLength(64);
                b.HasOne(x => x.Package).WithMany(p => p.Documents).HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => x.PackageId);
            });
            builder.Entity<EngInboxMdlLine>(b =>
            {
                b.ToTable("EngInboxMdlLines");
                b.Property(x => x.Number).HasMaxLength(100);
                b.Property(x => x.Title).HasMaxLength(300);
                b.Property(x => x.DocType).HasMaxLength(60);
                b.Property(x => x.CommitDate).HasColumnType("date");
                b.HasOne(x => x.Package).WithMany(p => p.MdlLines).HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => x.PackageId);
            });
            builder.Entity<EngInboxScanState>(b =>
            {
                b.ToTable("EngInboxScanStates");
                b.Property(x => x.LastNote).HasMaxLength(500);
            });
        }
    }
}
