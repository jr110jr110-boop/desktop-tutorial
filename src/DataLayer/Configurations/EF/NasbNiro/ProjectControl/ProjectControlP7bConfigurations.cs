using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.ProjectControl
{
    // Package P-B7b: engineering document control (EDMS) tables. All foreign keys are Restrict: nothing is deleted.
    public static class ProjectControlP7bModelBuilderExtensions
    {
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddProjectControlP7bConfigurations(this ModelBuilder builder)
        {
            builder.Entity<PcDocumentRevision>(b =>
            {
                b.ToTable("PcDocumentRevisions");
                b.Property(x => x.Code).HasMaxLength(20).IsRequired();
                b.Property(x => x.Reason).HasMaxLength(1000);
                b.Property(x => x.ReviewNote).HasMaxLength(1000);
                b.Property(x => x.RejectReason).HasMaxLength(1000);
                b.Property(x => x.ObsoleteReason).HasMaxLength(1000);
                b.HasIndex(x => new { x.DeliverableId, x.Sequence }).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.ChangeRequestId);
                b.HasOne(x => x.Deliverable).WithMany().HasForeignKey(x => x.DeliverableId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcChangeRequest>().WithMany().HasForeignKey(x => x.ChangeRequestId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcDocumentRevisionFile>(b =>
            {
                b.ToTable("PcDocumentRevisionFiles");
                b.Property(x => x.FileName).HasMaxLength(260);
                b.Property(x => x.Title).HasMaxLength(300);
                b.Property(x => x.RemoveReason).HasMaxLength(1000);
                b.HasIndex(x => x.RevisionId);
                b.HasIndex(x => x.DocumentId);
                b.HasOne(x => x.Revision).WithMany(x => x.Files).HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Entities.Model.Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcDocumentQualityLink>(b =>
            {
                b.ToTable("PcDocumentQualityLinks");
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.RevisionId);
                b.HasIndex(x => new { x.Kind, x.TargetId });
                b.HasOne(x => x.Revision).WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcDocumentLink>(b =>
            {
                b.ToTable("PcDocumentLinks");
                b.Property(x => x.PartNumber).HasMaxLength(60);
                b.Property(x => x.StageCode2).HasMaxLength(5);
                b.Property(x => x.Note).HasMaxLength(500);
                b.Property(x => x.UnlinkReason).HasMaxLength(1000);
                b.HasIndex(x => x.DeliverableId);
                b.HasIndex(x => x.ControlledDocumentId);
                b.HasIndex(x => x.ItemId);
                b.HasIndex(x => x.PartNumber);
                b.HasIndex(x => x.ActivityId);
                b.HasIndex(x => x.WbsNodeId);
                b.HasIndex(x => x.TagId);
                b.HasOne(x => x.Deliverable).WithMany().HasForeignKey(x => x.DeliverableId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Entities.Model.NasbNiro.Quality.ControlledDocument>().WithMany().HasForeignKey(x => x.ControlledDocumentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Entities.Model.NasbNiro.Inventory.InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Entities.Model.NasbNiro.ProductBank.PbBomLine>().WithMany().HasForeignKey(x => x.BomLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcWbsNode>().WithMany().HasForeignKey(x => x.WbsNodeId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcEquipmentTag>().WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcEquipmentTag>(b =>
            {
                b.ToTable("PcEquipmentTags");
                b.Property(x => x.Code).HasMaxLength(60).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300);
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => new { x.ContractId, x.Code }).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.ItemId);
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Entities.Model.NasbNiro.Inventory.InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcDocumentDistribution>(b =>
            {
                b.ToTable("PcDocumentDistributions");
                b.Property(x => x.Discipline).HasMaxLength(100).IsRequired();
                b.HasIndex(x => new { x.Discipline, x.UserId, x.ContractId });
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcDisciplineApprover>(b =>
            {
                b.ToTable("PcDisciplineApprovers");
                b.Property(x => x.Discipline).HasMaxLength(100).IsRequired();
                b.HasIndex(x => new { x.Discipline, x.UserId, x.ContractId });
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcDocumentNotice>(b =>
            {
                b.ToTable("PcDocumentNotices");
                b.HasIndex(x => new { x.RevisionId, x.UserId }).IsUnique();
                b.HasIndex(x => new { x.UserId, x.AcknowledgedAt });
                b.HasOne(x => x.Revision).WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcDocumentEvent>(b =>
            {
                b.ToTable("PcDocumentEvents");
                b.Property(x => x.Note).HasMaxLength(1000);
                b.HasIndex(x => new { x.DeliverableId, x.At });
                b.HasIndex(x => x.RevisionId);
                b.HasOne<PcEngineeringDeliverable>().WithMany().HasForeignKey(x => x.DeliverableId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcTenderPackage>(b =>
            {
                b.ToTable("PcTenderPackages");
                b.Property(x => x.Code).HasMaxLength(50).IsRequired();
                b.HasIndex(x => x.Code).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.TenderCode).HasMaxLength(60);
                b.Property(x => x.Note).HasMaxLength(1000);
                b.HasIndex(x => x.ContractId);
                b.HasOne<ContractEntity>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PcTenderDocument>(b =>
            {
                b.ToTable("PcTenderDocuments");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.Discipline).HasMaxLength(100);
                b.Property(x => x.DocumentType).HasMaxLength(100);
                b.Property(x => x.Revision).HasMaxLength(20);
                b.Property(x => x.VoidReason).HasMaxLength(1000);
                b.HasIndex(x => x.PackageId);
                b.HasOne(x => x.Package).WithMany(x => x.Documents).HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Entities.Model.Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
