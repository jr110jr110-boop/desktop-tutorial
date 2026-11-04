using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProjectBalance;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Inventory
{
    // PB2: the project material balance. Tables start with "Pbal". Lines go with their run; every other delete is restricted.
    public static class ProjectBalanceModelBuilderExtensions
    {
        private const int Code = 50;
        private const int Title = 300;
        private const int Text = 1000;

        public static void AddProjectBalanceConfigurations(this ModelBuilder builder)
        {
            builder.Entity<ProjectMaterialBalance>(b =>
            {
                b.ToTable("PbalBalances");
                b.Property(x => x.BomCode).HasMaxLength(60);
                b.Property(x => x.Trigger).HasMaxLength(30);
                b.Property(x => x.ContentHash).HasMaxLength(64);
                b.Property(x => x.Note).HasMaxLength(Text);
                b.HasIndex(x => new { x.BaselineId, x.RunNumber }).IsUnique().HasFilter("[Deleted] = 0");
                b.HasIndex(x => x.WorkOrderId);
                b.HasIndex(x => x.ContractId);
                b.HasOne<PbProjectBaseline>().WithMany().HasForeignKey(x => x.BaselineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<ProjectMaterialBalance>().WithMany().HasForeignKey(x => x.PreviousRunId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<ProjectMaterialBalanceLine>(b =>
            {
                b.ToTable("PbalBalanceLines");
                b.Property(x => x.PartNumber).HasMaxLength(Code);
                b.Property(x => x.PartName).HasMaxLength(Title);
                b.Property(x => x.Unit).HasMaxLength(20);
                b.Property(x => x.Note).HasMaxLength(Text);
                foreach (var p in new[] { nameof(ProjectMaterialBalanceLine.GrossQty), nameof(ProjectMaterialBalanceLine.ScrapQty), nameof(ProjectMaterialBalanceLine.RequiredQty), nameof(ProjectMaterialBalanceLine.IssuedQty),
                    nameof(ProjectMaterialBalanceLine.ReservedQty), nameof(ProjectMaterialBalanceLine.ReservedNewQty), nameof(ProjectMaterialBalanceLine.OnHandQty), nameof(ProjectMaterialBalanceLine.AvailableQty),
                    nameof(ProjectMaterialBalanceLine.OnOrderPrQty), nameof(ProjectMaterialBalanceLine.OnOrderPoQty), nameof(ProjectMaterialBalanceLine.SubstituteQty), nameof(ProjectMaterialBalanceLine.ShortQty) })
                    b.Property(p).HasPrecision(18, 4);
                b.HasIndex(x => x.BalanceId);
                b.HasIndex(x => x.ItemId);
                b.HasIndex(x => x.WbsNodeId);
                b.HasIndex(x => x.ActivityId);
                b.HasOne<ProjectMaterialBalance>().WithMany().HasForeignKey(x => x.BalanceId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.SubstituteItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcWbsNode>().WithMany().HasForeignKey(x => x.WbsNodeId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<ProjectReservation>(b =>
            {
                b.ToTable("PbalReservations");
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.IssuedQuantity).HasPrecision(18, 4);
                b.Property(x => x.ReleasedQuantity).HasPrecision(18, 4);
                b.Property(x => x.Reason).HasMaxLength(500);
                b.Property(x => x.ReleaseReason).HasMaxLength(500);
                b.Property(x => x.ReEnableReason).HasMaxLength(500);
                b.HasIndex(x => new { x.ItemId, x.Status });
                // PB2 (M2): one automatic reservation per project, item, substitute-for, warehouse and lot, so two runs at once cannot double reserve (a second insert fails and is treated as "already done")
                b.HasIndex(x => new { x.BaselineId, x.ItemId, x.SubstituteForItemId, x.WarehouseId, x.LotId }).IsUnique().HasFilter("[Status] = 1 AND [Deleted] = 0 AND [SerialUnitId] IS NULL");
                b.HasIndex(x => x.BaselineId);
                b.HasIndex(x => x.ContractId);
                b.HasOne<PbProjectBaseline>().WithMany().HasForeignKey(x => x.BaselineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.SubstituteForItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<InventoryLot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<SerialUnit>().WithMany().HasForeignKey(x => x.SerialUnitId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcWbsNode>().WithMany().HasForeignKey(x => x.WbsNodeId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<PcActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<ItemSubstitute>(b =>
            {
                b.ToTable("PbalItemSubstitutes");
                b.Property(x => x.Ratio).HasPrecision(18, 6);
                b.Property(x => x.Reason).HasMaxLength(Text);
                b.Property(x => x.DecisionNote).HasMaxLength(Text);
                b.HasIndex(x => new { x.ItemId, x.Status });
                b.HasIndex(x => x.SubstituteItemId);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.SubstituteItemId).OnDelete(DeleteBehavior.Restrict);
            });

            // columns this package adds to existing tables
            builder.Entity<PurchaseRequest>(b =>
            {
                b.Property(x => x.BalanceGroupKey).HasMaxLength(60);
                // PB2 (M2): one open automatic draft per project and group
                b.HasIndex(x => new { x.ProjectBaselineId, x.BalanceGroupKey }).IsUnique().HasFilter("[ProjectBaselineId] IS NOT NULL AND [Status] = 0 AND [Deleted] = 0");
                b.HasOne<PbProjectBaseline>().WithMany().HasForeignKey(x => x.ProjectBaselineId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<PurchaseRequestLine>(b =>
            {
                b.HasIndex(x => x.WbsNodeId);
                b.HasOne<PcWbsNode>().WithMany().HasForeignKey(x => x.WbsNodeId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<MaterialRequest>(b =>
            {
                // PB2 (M2): one kit request per activity (a rejected one does not count)
                b.HasIndex(x => x.ActivityId).IsUnique().HasFilter("[ActivityId] IS NOT NULL AND [Deleted] = 0 AND [Status] <> 3");
                b.HasOne<PcActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<RecordAttachment>(b =>
            {
                b.HasIndex(x => x.LinkedFromAttachmentId);
            });
        }
    }
}
