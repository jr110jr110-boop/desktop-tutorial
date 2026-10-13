using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Equipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetBomConfigurations : IEntityTypeConfiguration<Bom>
    {
        public void Configure(EntityTypeBuilder<Bom> builder)
        {
            builder.ToTable("Boms");

            builder
                .HasOne(bom => bom.Assembly)
                .WithMany(assembly => assembly.Boms)
                .HasForeignKey(bom => bom.AssemblyId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasMany(bom => bom.Processes)
                //.WithOne(p => p.Bom)
                .WithOne()
                .HasForeignKey(process => process.BomId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasOne(x => x.Predecessor)
                .WithMany()
                .HasForeignKey(x => x.PredecessorId);

            builder
                .HasOne<EquipmentBom>(x => x.EquipmentBom)
                .WithMany()
                .HasForeignKey(x => x.EquipmentBomId);
        }
    }
}
