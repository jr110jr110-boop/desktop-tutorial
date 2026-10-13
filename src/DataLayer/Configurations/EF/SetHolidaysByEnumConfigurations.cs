using Entities.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF
{
    public class SetHolidaysByEnumConfigurations : IEntityTypeConfiguration<HolidaysDayOfWeek>
    {
        public void Configure(EntityTypeBuilder<HolidaysDayOfWeek> builder)
        {
            builder.ToTable("HolidaysDayOfWeek");

            builder.HasKey(x => x.Id);

            builder.HasIndex(x => x.DayOfWeek)
                .IsUnique();
        }
    }
}
