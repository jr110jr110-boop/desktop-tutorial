using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Inventory
{
    // PB5: warehouse completion. New tables start with "Inv"; every foreign key is Restrict (the rows are history).
    public static class Pb5ModelBuilderExtensions
    {
        public static void AddPb5Configurations(this ModelBuilder builder)
        {
            builder.Entity<ItemGroup>(b =>
            {
                b.Property(x => x.DefaultMinStock).HasPrecision(18, 4);
                b.Property(x => x.DefaultMaxStock).HasPrecision(18, 4);
                b.Property(x => x.DefaultReorderQuantity).HasPrecision(18, 4);
            });

            builder.Entity<InventoryItem>(b =>
            {
                b.Property(x => x.Platform).HasMaxLength(60);
                b.Property(x => x.EquipmentType).HasMaxLength(60);
                b.Property(x => x.ValveModel).HasMaxLength(60);
                b.Property(x => x.IranCode).HasMaxLength(30);
                b.Property(x => x.ReorderQuantity).HasPrecision(18, 4);
                b.HasIndex(x => x.IranCode);
                b.HasIndex(x => x.Platform);
            });

            builder.Entity<Warehouse>(b =>
            {
                b.Property(x => x.Platform).HasMaxLength(60);
                b.HasIndex(x => x.WorkOrderId);
                b.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<InventoryDocument>(b =>
            {
                b.Property(x => x.RejectReason).HasMaxLength(500);
                b.Property(x => x.GoodsType).HasMaxLength(100);
                b.Property(x => x.BillOfLadingNumber).HasMaxLength(60);
                b.HasIndex(x => x.ConsignmentOfDocumentId);
                b.HasIndex(x => x.CorrectsDocumentId);
                b.HasIndex(x => x.ConsignmentDueDate);
                b.HasOne<InventoryDocument>().WithMany().HasForeignKey(x => x.ConsignmentOfDocumentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne<InventoryDocument>().WithMany().HasForeignKey(x => x.CorrectsDocumentId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<InventoryFreight>(b =>
            {
                b.ToTable("InvFreights");
                b.Property(x => x.Number).HasMaxLength(30);
                b.Property(x => x.VehiclePlate).HasMaxLength(50);
                b.Property(x => x.GoodsType).HasMaxLength(100);
                b.Property(x => x.BillOfLadingNumber).HasMaxLength(60);
                b.Property(x => x.Amount).HasPrecision(18, 2);
                b.Property(x => x.Description).HasMaxLength(500);
                b.Property(x => x.VoidReason).HasMaxLength(500);
                b.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
                b.HasIndex(x => x.ReceiptDocumentId);
                b.HasIndex(x => x.CarrierProviderId);
                b.HasOne(x => x.ReceiptDocument).WithMany().HasForeignKey(x => x.ReceiptDocumentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.CarrierProvider).WithMany().HasForeignKey(x => x.CarrierProviderId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<InventoryFreightAllocation>(b =>
            {
                b.ToTable("InvFreightAllocations");
                b.Property(x => x.BasisAmount).HasPrecision(18, 4);
                b.Property(x => x.Amount).HasPrecision(18, 2);
                b.Property(x => x.StockAmount).HasPrecision(18, 2);
                b.Property(x => x.RevaluationAmount).HasPrecision(18, 2);
                b.HasIndex(x => x.FreightId);
                b.HasIndex(x => x.DocumentLineId);
                b.HasIndex(x => x.ItemId);
                b.HasOne(x => x.Freight).WithMany(x => x.Allocations).HasForeignKey(x => x.FreightId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.DocumentLine).WithMany().HasForeignKey(x => x.DocumentLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<InventoryPackingLine>(b =>
            {
                b.ToTable("InvPackingLines");
                b.Property(x => x.BillOfLadingNumber).HasMaxLength(60);
                b.Property(x => x.PackageNumber).HasMaxLength(60);
                b.Property(x => x.Description).HasMaxLength(300);
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.GrossWeight).HasPrecision(18, 3);
                b.HasIndex(x => new { x.DocumentId, x.BillOfLadingNumber });
                b.HasIndex(x => x.ItemId);
                b.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ItemUnitConversion>(b =>
            {
                b.ToTable("InvItemUnitConversions");
                b.Property(x => x.FromUnit).HasMaxLength(30).IsRequired();
                b.Property(x => x.ToUnit).HasMaxLength(30).IsRequired();
                b.Property(x => x.Factor).HasPrecision(18, 6);
                b.HasIndex(x => new { x.ItemId, x.FromUnit, x.ToUnit }).IsUnique().HasFilter("[Deleted] = 0");
                b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<InventoryAsset>(b =>
            {
                b.ToTable("InvAssets");
                b.Property(x => x.Tag).HasMaxLength(40).IsRequired();
                b.Property(x => x.Title).HasMaxLength(200).IsRequired();
                b.Property(x => x.Cost).HasPrecision(18, 2);
                b.Property(x => x.CustodianUnit).HasMaxLength(100);
                b.Property(x => x.Description).HasMaxLength(500);
                b.HasIndex(x => x.Tag).IsUnique().HasFilter("[Deleted] = 0");
                b.HasIndex(x => x.ItemId);
                b.HasIndex(x => x.SerialUnitId);
                b.HasIndex(x => x.WarehouseId);
                b.HasIndex(x => x.LocationId);
                b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.SerialUnit).WithMany().HasForeignKey(x => x.SerialUnitId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<InventoryConsumptionCeiling>(b =>
            {
                b.ToTable("InvConsumptionCeilings");
                b.Property(x => x.MaxQuantity).HasPrecision(18, 4);
                b.Property(x => x.Note).HasMaxLength(300);
                b.HasIndex(x => new { x.ItemId, x.WorkOrderId }).IsUnique().HasFilter("[Deleted] = 0");
                b.HasIndex(x => x.WorkOrderId);
                b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
