using Entities.Model.NasbNiro.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetProcessFileConfigurations : IEntityTypeConfiguration<ProcessFile>
    {
        public void Configure(EntityTypeBuilder<ProcessFile> builder)
        {
            builder.ToTable("ProcessFiles");

            //builder.HasKey(pf => new { pf.ProcessId, pf.DocumentId });

            //builder
            //    .HasOne<ProcessEntity>(x => x.Process)
            //    .WithMany(p => p.ProcessFiles)
            //    .HasForeignKey(pf => pf.ProcessId)
            //    .OnDelete(DeleteBehavior.Restrict);

            //builder
            //    .HasOne(pf => pf.Document)
            //    .WithMany()
            //    .HasForeignKey(pf => pf.DocumentId)
            //    .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
