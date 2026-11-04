using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Inventory
{
    // PB4: two-way genealogy. Every foreign key is restricted: the rows are history.
    public static class GenealogyModelBuilderExtensions
    {
        public static void AddGenealogyConfigurations(this ModelBuilder builder)
        {
            builder.Entity<GenealogyLink>(b =>
            {
                b.ToTable("InvGenealogyLinks");
                b.Property(x => x.Quantity).HasPrecision(18, 4);
                b.Property(x => x.Note).HasMaxLength(300);
                b.HasIndex(x => x.ProductionOrderId);
                b.HasIndex(x => x.OutputLotId);
                b.HasIndex(x => x.OutputSerialUnitId);
                b.HasIndex(x => x.InputLotId);
                b.HasIndex(x => x.InputSerialUnitId);
                b.HasIndex(x => x.ReceiptDocumentId);
                b.HasOne(x => x.ProductionOrder).WithMany().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Operation).WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.OutputItem).WithMany().HasForeignKey(x => x.OutputItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.OutputLot).WithMany().HasForeignKey(x => x.OutputLotId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.OutputSerialUnit).WithMany().HasForeignKey(x => x.OutputSerialUnitId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.InputItem).WithMany().HasForeignKey(x => x.InputItemId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.InputLot).WithMany().HasForeignKey(x => x.InputLotId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.InputSerialUnit).WithMany().HasForeignKey(x => x.InputSerialUnitId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.IssueDocument).WithMany().HasForeignKey(x => x.IssueDocumentId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.ReceiptDocument).WithMany().HasForeignKey(x => x.ReceiptDocumentId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
