using Entities.Model.NasbNiro.Ceo;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Ceo
{
    // CEO executive dashboard, release D1. All tables start with "Ceo".
    public static class CeoModelBuilderExtensions
    {
        public static void AddCeoConfigurations(this ModelBuilder builder)
        {
            builder.Entity<CeoKpiSnapshot>(b =>
            {
                b.ToTable("CeoKpiSnapshots");
                b.Property(x => x.Date).HasColumnType("date");
                b.Property(x => x.Key).HasMaxLength(60).IsRequired();
                b.Property(x => x.Value).HasColumnType("decimal(18,2)");
                b.HasIndex(x => new { x.Date, x.Key }).IsUnique();
            });
        }
    }
}
