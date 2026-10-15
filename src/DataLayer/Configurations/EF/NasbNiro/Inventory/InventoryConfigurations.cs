using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro.Inventory
{
    // Warehouse (انبارداری) and traceability (ردیابی قطعات) tables. All new tables start with "Inv";
    // no existing table is changed. Codes are unique among rows that are not deleted.

    public static class InventoryModelBuilderExtensions
    {
        public static void AddInventoryConfigurations(this ModelBuilder builder)
        {
            builder.ApplyConfiguration(new ItemGroupConfiguration());
            builder.ApplyConfiguration(new InventoryItemConfiguration());
            builder.ApplyConfiguration(new WarehouseConfiguration());
            builder.ApplyConfiguration(new WarehouseLocationConfiguration());
            builder.ApplyConfiguration(new SepidarMappingConfiguration());
            builder.ApplyConfiguration(new InventoryLotConfiguration());
            builder.ApplyConfiguration(new SerialUnitConfiguration());
            builder.ApplyConfiguration(new InventoryDocumentConfiguration());
            builder.ApplyConfiguration(new InventoryDocumentLineConfiguration());
            builder.ApplyConfiguration(new InventoryDocumentLineSerialConfiguration());
            builder.ApplyConfiguration(new StockTransactionConfiguration());
            builder.ApplyConfiguration(new StockBalanceConfiguration());
            builder.ApplyConfiguration(new DocumentNumberSequenceConfiguration());
            builder.ApplyConfiguration(new OperationTemplateConfiguration());
            builder.ApplyConfiguration(new PartOperationConfiguration());
            builder.ApplyConfiguration(new PartOperationFileConfiguration());
            builder.ApplyConfiguration(new AssemblyLinkConfiguration());
        }
    }

    internal static class InventoryColumns
    {
        public const int Code = 50;
        public const int Title = 300;
        public const int Text = 1000;
        public const int LongText = 4000;
        public const string NotDeleted = "[Deleted] = 0";
    }

    public class ItemGroupConfiguration : IEntityTypeConfiguration<ItemGroup>
    {
        public void Configure(EntityTypeBuilder<ItemGroup> builder)
        {
            builder.ToTable("InvItemGroups");
            builder.Property(x => x.Code).HasMaxLength(InventoryColumns.Code).IsRequired();
            builder.Property(x => x.Title).HasMaxLength(InventoryColumns.Title).IsRequired();
            builder.Property(x => x.Description).HasMaxLength(InventoryColumns.Text);
            builder.HasIndex(x => x.Code).IsUnique().HasFilter(InventoryColumns.NotDeleted);
            builder.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        }
    }

    public class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
    {
        public void Configure(EntityTypeBuilder<InventoryItem> builder)
        {
            builder.ToTable("InvItems");
            builder.Property(x => x.Code).HasMaxLength(InventoryColumns.Code).IsRequired();
            builder.Property(x => x.Title).HasMaxLength(InventoryColumns.Title).IsRequired();
            builder.Property(x => x.Unit).HasMaxLength(50).IsRequired();
            builder.Property(x => x.TechnicalCode).HasMaxLength(100);
            builder.Property(x => x.Material).HasMaxLength(200);
            builder.Property(x => x.Standard).HasMaxLength(200);
            builder.Property(x => x.Specification).HasMaxLength(InventoryColumns.Text);
            builder.Property(x => x.SepidarCode).HasMaxLength(InventoryColumns.Code);
            builder.Property(x => x.Weight).HasPrecision(18, 3);
            builder.Property(x => x.MinStock).HasPrecision(18, 4);
            builder.Property(x => x.MaxStock).HasPrecision(18, 4);
            builder.HasIndex(x => x.Code).IsUnique().HasFilter(InventoryColumns.NotDeleted);
            builder.HasIndex(x => x.Title);
            builder.HasOne(x => x.ItemGroup).WithMany(x => x.Items).HasForeignKey(x => x.ItemGroupId).OnDelete(DeleteBehavior.Restrict);
        }
    }

    public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
    {
        public void Configure(EntityTypeBuilder<Warehouse> builder)
        {
            builder.ToTable("InvWarehouses");
            builder.Property(x => x.Code).HasMaxLength(InventoryColumns.Code).IsRequired();
            builder.Property(x => x.Title).HasMaxLength(InventoryColumns.Title).IsRequired();
            builder.Property(x => x.Description).HasMaxLength(InventoryColumns.Text);
            builder.Property(x => x.SepidarCode).HasMaxLength(InventoryColumns.Code);
            builder.HasIndex(x => x.Code).IsUnique().HasFilter(InventoryColumns.NotDeleted);
        }
    }

    public class WarehouseLocationConfiguration : IEntityTypeConfiguration<WarehouseLocation>
    {
        public void Configure(EntityTypeBuilder<WarehouseLocation> builder)
        {
            builder.ToTable("InvLocations");
            builder.Property(x => x.Code).HasMaxLength(InventoryColumns.Code).IsRequired();
            builder.Property(x => x.Title).HasMaxLength(InventoryColumns.Title);
            builder.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique().HasFilter(InventoryColumns.NotDeleted);
            builder.HasOne(x => x.Warehouse).WithMany(x => x.Locations).HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        }
    }

    public class SepidarMappingConfiguration : IEntityTypeConfiguration<SepidarMapping>
    {
        public void Configure(EntityTypeBuilder<SepidarMapping> builder)
        {
            builder.ToTable("InvSepidarMappings");
            builder.Property(x => x.EntityType).HasMaxLength(50).IsRequired();
            builder.Property(x => x.SepidarCode).HasMaxLength(InventoryColumns.Code).IsRequired();
            builder.HasIndex(x => new { x.EntityType, x.EntityId }).IsUnique();
        }
    }

    public class InventoryLotConfiguration : IEntityTypeConfiguration<InventoryLot>
    {
        public void Configure(EntityTypeBuilder<InventoryLot> builder)
        {
            builder.ToTable("InvLots");
            builder.Property(x => x.LotNumber).HasMaxLength(100).IsRequired();
            builder.Property(x => x.HeatNumber).HasMaxLength(100);
            builder.Property(x => x.CertificateNumber).HasMaxLength(100);
            builder.Property(x => x.CertificateType).HasMaxLength(100);
            builder.Property(x => x.Description).HasMaxLength(InventoryColumns.Text);
            builder.HasIndex(x => new { x.ItemId, x.LotNumber }).IsUnique().HasFilter(InventoryColumns.NotDeleted);
            builder.HasIndex(x => x.HeatNumber);
            builder.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Provider).WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
        }
    }

    public class SerialUnitConfiguration : IEntityTypeConfiguration<SerialUnit>
    {
        public void Configure(EntityTypeBuilder<SerialUnit> builder)
        {
            builder.ToTable("InvSerialUnits");
            builder.Property(x => x.SerialNumber).HasMaxLength(100).IsRequired();
            builder.Property(x => x.Description).HasMaxLength(InventoryColumns.Text);
            builder.HasIndex(x => new { x.ItemId, x.SerialNumber }).IsUnique().HasFilter(InventoryColumns.NotDeleted);
            builder.HasIndex(x => x.SerialNumber);
            builder.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.ParentSerialUnit).WithMany(x => x.Children).HasForeignKey(x => x.ParentSerialUnitId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
        }
    }

    public class InventoryDocumentConfiguration : IEntityTypeConfiguration<InventoryDocument>
    {
        public void Configure(EntityTypeBuilder<InventoryDocument> builder)
        {
            builder.ToTable("InvDocuments");
            builder.Property(x => x.Number).HasMaxLength(30);
            builder.Property(x => x.ReferenceNumber).HasMaxLength(100);
            builder.Property(x => x.Description).HasMaxLength(InventoryColumns.Text);
            builder.Property(x => x.CancelReason).HasMaxLength(InventoryColumns.Text);
            builder.Property(x => x.SepidarReference).HasMaxLength(100);
            builder.HasIndex(x => x.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
            builder.HasIndex(x => new { x.Type, x.Status, x.DocumentDate });
            builder.HasIndex(x => x.SepidarStatus);
            builder.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.DestinationWarehouse).WithMany().HasForeignKey(x => x.DestinationWarehouseId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Provider).WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.BuyOrder).WithMany().HasForeignKey(x => x.BuyOrderId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
        }
    }

    public class InventoryDocumentLineConfiguration : IEntityTypeConfiguration<InventoryDocumentLine>
    {
        public void Configure(EntityTypeBuilder<InventoryDocumentLine> builder)
        {
            builder.ToTable("InvDocumentLines");
            builder.Property(x => x.Quantity).HasPrecision(18, 4);
            builder.Property(x => x.UnitPrice).HasPrecision(22, 4);
            builder.Property(x => x.LotNumber).HasMaxLength(100);
            builder.Property(x => x.HeatNumber).HasMaxLength(100);
            builder.Property(x => x.CertificateNumber).HasMaxLength(100);
            builder.Property(x => x.Description).HasMaxLength(InventoryColumns.Text);
            builder.HasOne(x => x.Document).WithMany(x => x.Lines).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.DestinationLocation).WithMany().HasForeignKey(x => x.DestinationLocationId).OnDelete(DeleteBehavior.Restrict);
        }
    }

    public class InventoryDocumentLineSerialConfiguration : IEntityTypeConfiguration<InventoryDocumentLineSerial>
    {
        public void Configure(EntityTypeBuilder<InventoryDocumentLineSerial> builder)
        {
            builder.ToTable("InvDocumentLineSerials");
            builder.Property(x => x.SerialNumber).HasMaxLength(100).IsRequired();
            builder.HasOne(x => x.Line).WithMany(x => x.Serials).HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(x => x.SerialUnit).WithMany().HasForeignKey(x => x.SerialUnitId).OnDelete(DeleteBehavior.Restrict);
        }
    }

    public class StockTransactionConfiguration : IEntityTypeConfiguration<StockTransaction>
    {
        public void Configure(EntityTypeBuilder<StockTransaction> builder)
        {
            builder.ToTable("InvStockTransactions");
            builder.Property(x => x.Quantity).HasPrecision(18, 4);
            builder.Property(x => x.UnitPrice).HasPrecision(22, 4);
            builder.HasIndex(x => new { x.ItemId, x.WarehouseId, x.TransactionDate });
            builder.HasIndex(x => x.SerialUnitId);
            builder.HasIndex(x => x.LotId);
            builder.HasIndex(x => x.DocumentId);
            builder.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.SerialUnit).WithMany().HasForeignKey(x => x.SerialUnitId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        }
    }

    public class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
    {
        public void Configure(EntityTypeBuilder<StockBalance> builder)
        {
            builder.ToTable("InvStockBalances");
            builder.Property(x => x.Quantity).HasPrecision(18, 4);
            // One row per item/warehouse/location/lot; NULL location or lot counts as one value (no filter).
            builder.HasIndex(x => new { x.ItemId, x.WarehouseId, x.LocationId, x.LotId }).IsUnique().HasFilter(null);
            builder.HasIndex(x => new { x.WarehouseId, x.ItemId });
            builder.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
        }
    }

    public class DocumentNumberSequenceConfiguration : IEntityTypeConfiguration<DocumentNumberSequence>
    {
        public void Configure(EntityTypeBuilder<DocumentNumberSequence> builder)
        {
            builder.ToTable("InvNumberSequences");
            builder.HasIndex(x => new { x.Type, x.Year }).IsUnique();
        }
    }

    public class OperationTemplateConfiguration : IEntityTypeConfiguration<OperationTemplate>
    {
        public void Configure(EntityTypeBuilder<OperationTemplate> builder)
        {
            builder.ToTable("InvOperationTemplates");
            builder.Property(x => x.Title).HasMaxLength(InventoryColumns.Title).IsRequired();
            builder.Property(x => x.Description).HasMaxLength(InventoryColumns.Text);
            builder.HasIndex(x => new { x.ItemId, x.Sequence });
            builder.HasIndex(x => new { x.ItemGroupId, x.Sequence });
            builder.HasOne(x => x.ItemGroup).WithMany().HasForeignKey(x => x.ItemGroupId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
        }
    }

    public class PartOperationConfiguration : IEntityTypeConfiguration<PartOperation>
    {
        public void Configure(EntityTypeBuilder<PartOperation> builder)
        {
            builder.ToTable("InvPartOperations");
            builder.Property(x => x.Title).HasMaxLength(InventoryColumns.Title).IsRequired();
            builder.Property(x => x.OperatorName).HasMaxLength(200);
            builder.Property(x => x.Result).HasMaxLength(InventoryColumns.LongText);
            builder.Property(x => x.QcNote).HasMaxLength(InventoryColumns.Text);
            builder.Property(x => x.Notes).HasMaxLength(InventoryColumns.LongText);
            builder.HasIndex(x => x.SerialUnitId);
            builder.HasIndex(x => x.LotId);
            builder.HasIndex(x => new { x.QcStatus, x.Status });
            builder.HasOne(x => x.SerialUnit).WithMany().HasForeignKey(x => x.SerialUnitId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.OperationTemplate).WithMany().HasForeignKey(x => x.OperationTemplateId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
        }
    }

    public class PartOperationFileConfiguration : IEntityTypeConfiguration<PartOperationFile>
    {
        public void Configure(EntityTypeBuilder<PartOperationFile> builder)
        {
            builder.ToTable("InvPartOperationFiles");
            builder.Property(x => x.Title).HasMaxLength(InventoryColumns.Title);
            builder.HasOne(x => x.PartOperation).WithMany(x => x.Files).HasForeignKey(x => x.PartOperationId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class AssemblyLinkConfiguration : IEntityTypeConfiguration<AssemblyLink>
    {
        public void Configure(EntityTypeBuilder<AssemblyLink> builder)
        {
            builder.ToTable("InvAssemblyLinks");
            builder.Property(x => x.Quantity).HasPrecision(18, 4);
            builder.Property(x => x.Notes).HasMaxLength(InventoryColumns.Text);
            builder.HasIndex(x => x.ParentSerialUnitId);
            builder.HasIndex(x => x.ChildSerialUnitId);
            builder.HasIndex(x => x.ChildLotId);
            builder.HasOne(x => x.ParentSerialUnit).WithMany().HasForeignKey(x => x.ParentSerialUnitId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.ChildSerialUnit).WithMany().HasForeignKey(x => x.ChildSerialUnitId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.ChildLot).WithMany().HasForeignKey(x => x.ChildLotId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
        }
    }
}
