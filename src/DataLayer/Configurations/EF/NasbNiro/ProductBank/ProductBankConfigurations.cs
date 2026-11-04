using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Inventory
{
    // PB1: the product bank. Tables start with "Pb". Child lines go with their header; every other delete is restricted. Codes are unique among rows that are not deleted.
    public static class ProductBankModelBuilderExtensions
    {
        private const int Code = 50;
        private const int Name = 200;
        private const int Title = 300;
        private const int Text = 1000;
        private const string NotDeleted = "[Deleted] = 0";

        public static void AddProductBankConfigurations(this ModelBuilder builder)
        {
            builder.Entity<PbSetting>(b =>
            {
                b.ToTable("PbSettings");
                b.Property(x => x.Key).HasMaxLength(100).IsRequired();
                b.HasIndex(x => x.Key).IsUnique();
                b.Property(x => x.Value).HasMaxLength(Name);
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.Source).HasMaxLength(Title);
            });
            builder.Entity<PbStage>(b =>
            {
                b.ToTable("PbStages");
                b.Property(x => x.Code).HasMaxLength(2).IsRequired();
                b.HasIndex(x => x.Code).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.TitleFa).HasMaxLength(Name).IsRequired();
                b.Property(x => x.TitleEn).HasMaxLength(Name);
                b.Property(x => x.IsActive).HasDefaultValue(true).HasSentinel(true);
            });
            builder.Entity<PbLegacyMapping>(b =>
            {
                b.ToTable("PbLegacyMappings");
                b.Property(x => x.LegacyCode).HasMaxLength(100).IsRequired();
                b.Property(x => x.LegacyTitle).HasMaxLength(Title);
                b.Property(x => x.LegacyMaterial).HasMaxLength(Name);
                b.Property(x => x.PartNumber).HasMaxLength(Code);
                b.Property(x => x.StageCode2).HasMaxLength(2);
                b.Property(x => x.PartTitle).HasMaxLength(Title);
                b.Property(x => x.Confidence).HasPrecision(5, 2);
                b.Property(x => x.ReviewNote).HasMaxLength(Text);
                // one live mapping (draft or approved) per legacy code; a rejected one is kept for the history and may be replaced
                b.HasIndex(x => x.LegacyCode).IsUnique().HasFilter("[Deleted] = 0 AND [Status] <> 2");
                b.HasIndex(x => x.PartNumber);
                b.HasIndex(x => x.Status);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.LegacyItemId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PbProduct>(b =>
            {
                b.ToTable("PbProducts");
                b.Property(x => x.Code).HasMaxLength(Code).IsRequired();
                b.HasIndex(x => x.Code).IsUnique().HasFilter(NotDeleted);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Platform).HasMaxLength(Code);
                b.Property(x => x.Description).HasMaxLength(Text);
                b.Property(x => x.IsActive).HasDefaultValue(true).HasSentinel(true);
                b.HasIndex(x => x.EquipmentId);
            });
            builder.Entity<PbBomHeader>(b =>
            {
                b.ToTable("PbBomHeaders");
                b.Property(x => x.AssemblyPartNumber).HasMaxLength(Code).IsRequired();
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.Code).HasMaxLength(60).IsRequired();
                b.Property(x => x.Platform).HasMaxLength(Code);
                b.Property(x => x.IssueFor).HasMaxLength(100);
                b.Property(x => x.ChangeReason).HasMaxLength(Text);
                b.Property(x => x.ContentHash).HasMaxLength(64);
                b.Property(x => x.PaperSignatureNote).HasMaxLength(Text);
                b.Property(x => x.ReturnNote).HasMaxLength(Text);
                b.HasIndex(x => new { x.Code, x.Revision }).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.ProductId);
                b.HasIndex(x => x.Status);
                b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Supersedes).WithMany().HasForeignKey(x => x.SupersedesId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ProductItemId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PbBomLine>(b =>
            {
                b.ToTable("PbBomLines");
                b.Property(x => x.PartNumber).HasMaxLength(Code);
                b.Property(x => x.PartName).HasMaxLength(Title);
                b.Property(x => x.Qty).HasPrecision(18, 4);
                b.Property(x => x.Unit).HasMaxLength(20);
                b.Property(x => x.Standard).HasMaxLength(Name);
                b.Property(x => x.Material).HasMaxLength(Name);
                b.Property(x => x.RawDiameter).HasPrecision(18, 3);
                b.Property(x => x.RawLength).HasPrecision(18, 3);
                b.Property(x => x.RawWidth).HasPrecision(18, 3);
                b.Property(x => x.RawThickness).HasPrecision(18, 3);
                b.Property(x => x.NetWeight).HasPrecision(18, 4);
                b.Property(x => x.GrossWeight).HasPrecision(18, 4);
                b.Property(x => x.ScrapFactor).HasPrecision(9, 4);
                b.Property(x => x.Description).HasMaxLength(Text);
                b.HasIndex(x => x.BomHeaderId);
                b.HasIndex(x => x.PartNumber);
                b.HasIndex(x => x.ItemId);
                b.HasOne(x => x.BomHeader).WithMany(h => h.Lines).HasForeignKey(x => x.BomHeaderId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.ParentLine).WithMany().HasForeignKey(x => x.ParentLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.ChildBom).WithMany().HasForeignKey(x => x.ChildBomId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.AlternativeOfLine).WithMany().HasForeignKey(x => x.AlternativeOfLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PbOpcRouting>(b =>
            {
                b.ToTable("PbOpcRoutings");
                b.Property(x => x.PartNumber).HasMaxLength(Code).IsRequired();
                b.Property(x => x.Title).HasMaxLength(Title);
                b.Property(x => x.ProjectNumber).HasMaxLength(Code);
                b.Property(x => x.Platform).HasMaxLength(Code);
                b.Property(x => x.ChangeReason).HasMaxLength(Text);
                b.Property(x => x.ContentHash).HasMaxLength(64);
                b.HasIndex(x => new { x.PartNumber, x.Revision }).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.ProductId);
                b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Supersedes).WithMany().HasForeignKey(x => x.SupersedesId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PbOpcStep>(b =>
            {
                b.ToTable("PbOpcSteps");
                b.Property(x => x.PartNumber).HasMaxLength(Code);
                b.Property(x => x.PartName).HasMaxLength(Title);
                b.Property(x => x.StageCode2).HasMaxLength(2);
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.ResourceClass).HasMaxLength(100);
                b.Property(x => x.StandardHours).HasPrecision(9, 2);
                b.Property(x => x.RequiredEvidence).HasMaxLength(300);
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.RoutingId);
                b.HasIndex(x => x.StageItemId);
                b.HasOne(x => x.Routing).WithMany(r => r.Steps).HasForeignKey(x => x.RoutingId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<MfWorkCenter>().WithMany().HasForeignKey(x => x.WorkCenterId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.StageItemId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PbWbsTemplate>(b =>
            {
                b.ToTable("PbWbsTemplates");
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.Source).HasMaxLength(100);
                b.Property(x => x.TotalDurationDays).HasPrecision(12, 3);
                b.HasIndex(x => new { x.ProductId, x.Version }).IsUnique().HasFilter(NotDeleted);
                b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PbWbsTemplateNode>(b =>
            {
                b.ToTable("PbWbsTemplateNodes");
                b.Property(x => x.Title).HasMaxLength(Title).IsRequired();
                b.Property(x => x.PhaseCode).HasMaxLength(1);
                b.Property(x => x.DurationDays).HasPrecision(12, 3);
                b.Property(x => x.StartOffsetDays).HasPrecision(12, 3);
                b.Property(x => x.PartNumber).HasMaxLength(Code);
                b.Property(x => x.ResourceClass).HasMaxLength(100);
                b.HasIndex(x => x.TemplateId);
                b.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PbWbsTemplateLink>(b =>
            {
                b.ToTable("PbWbsTemplateLinks");
                b.Property(x => x.LagDays).HasPrecision(12, 3);
                b.HasIndex(x => x.TemplateId);
                b.HasOne<PbWbsTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<PbWbsTemplateNode>().WithMany().HasForeignKey(x => x.FromNodeId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PbWbsTemplateNode>().WithMany().HasForeignKey(x => x.ToNodeId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PbProjectBaseline>(b =>
            {
                b.ToTable("PbProjectBaselines");
                b.Property(x => x.SourceBomCode).HasMaxLength(60);
                b.Property(x => x.IsCurrent).HasDefaultValue(true).HasSentinel(true);
                b.HasIndex(x => x.WorkOrderId).IsUnique().HasFilter("[Deleted] = 0 AND [IsCurrent] = 1").HasDatabaseName("IX_PbProjectBaselines_WorkOrderId_Current");
                b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PbProduct>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PbBomHeader>().WithMany().HasForeignKey(x => x.BomHeaderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PbWbsTemplate>().WithMany().HasForeignKey(x => x.WbsTemplateId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcChangeRequest>().WithMany().HasForeignKey(x => x.ChangeRequestId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => x.ChangeRequestId);
            });
            builder.Entity<PbProjectBomLine>(b =>
            {
                b.ToTable("PbProjectBomLines");
                b.Property(x => x.PartNumber).HasMaxLength(Code);
                b.Property(x => x.AssemblyPartNumber).HasMaxLength(Code);
                b.Property(x => x.PartName).HasMaxLength(Title);
                b.Property(x => x.Qty).HasPrecision(18, 4);
                b.Property(x => x.TotalQty).HasPrecision(18, 4);
                b.Property(x => x.Unit).HasMaxLength(20);
                b.Property(x => x.Material).HasMaxLength(Name);
                b.HasIndex(x => x.BaselineId);
                b.HasIndex(x => x.PartNumber);
                b.HasOne<PbProjectBaseline>().WithMany().HasForeignKey(x => x.BaselineId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PbProjectRouting>(b =>
            {
                b.ToTable("PbProjectRoutings");
                b.Property(x => x.PartNumber).HasMaxLength(Code);
                b.HasIndex(x => x.BaselineId);
                b.HasOne<PbProjectBaseline>().WithMany().HasForeignKey(x => x.BaselineId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<PbOpcRouting>().WithMany().HasForeignKey(x => x.OpcRoutingId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PbProjectBomChange>(b =>
            {
                b.ToTable("PbProjectBomChanges");
                b.Property(x => x.Number).HasMaxLength(30).IsRequired();
                b.Property(x => x.PartNumber).HasMaxLength(Code);
                b.Property(x => x.PartName).HasMaxLength(Title);
                b.Property(x => x.NewQty).HasPrecision(18, 4);
                b.Property(x => x.Unit).HasMaxLength(20);
                b.Property(x => x.Material).HasMaxLength(Name);
                b.Property(x => x.Reason).HasMaxLength(Text).IsRequired();
                b.Property(x => x.RejectReason).HasMaxLength(Text);
                b.HasIndex(x => x.Number).IsUnique().HasFilter(NotDeleted);
                b.HasIndex(x => x.BaselineId);
                b.HasOne<PbProjectBaseline>().WithMany().HasForeignKey(x => x.BaselineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PbProjectBomLine>().WithMany().HasForeignKey(x => x.TargetLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcChangeRequest>().WithMany().HasForeignKey(x => x.ChangeRequestId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
