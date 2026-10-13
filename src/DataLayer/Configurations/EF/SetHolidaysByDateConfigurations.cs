using Entities.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF
{
    internal class SetHolidaysByDateConfigurations : IEntityTypeConfiguration<HolidaysByDate>
    {
        public void Configure(EntityTypeBuilder<HolidaysByDate> builder)
        {
            builder.ToTable("HolidayDates");

            builder.HasKey(x => x.Id);
        }
    }
}
