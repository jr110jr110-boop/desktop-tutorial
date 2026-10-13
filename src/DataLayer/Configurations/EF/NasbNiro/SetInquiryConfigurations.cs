using Entities.Model.NasbNiro.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Configurations.EF.NasbNiro
{
    public class SetInquiryConfigurations : IEntityTypeConfiguration<Inquiry>
    {
        public void Configure(EntityTypeBuilder<Inquiry> builder)
        {
            builder.ToTable("Inquiries");

            builder
                .HasOne(inquiry => inquiry.Provider)
                .WithMany(p => p.Inquiries)
                .HasForeignKey(inquiry => inquiry.ProviderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasOne(inquiry => inquiry.BuyOrder)
                .WithMany(bo => bo.Inquiries)
                .HasForeignKey(inquiry => inquiry.BuyOrderId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
