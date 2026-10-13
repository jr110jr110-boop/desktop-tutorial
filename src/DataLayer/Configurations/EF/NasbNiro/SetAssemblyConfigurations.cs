using Entities.Model.NasbNiro.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetAssemblyConfigurations : IEntityTypeConfiguration<AssemblyEntity>
    {
        public void Configure(EntityTypeBuilder<AssemblyEntity> builder)
        {
            builder.ToTable("Assemblies");

            builder
                .HasOne(assembly => assembly.WorkOrder)
                .WithMany(workOrder => workOrder.Assemblies)
                .HasForeignKey(assembly => assembly.WorkOrderId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasMany(assembly => assembly.Boms)
                .WithOne(bom => bom.Assembly)
                .HasForeignKey(bom => bom.AssemblyId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasOne(x => x.Predecessor)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.PredecessorId);
        }
    }
}
