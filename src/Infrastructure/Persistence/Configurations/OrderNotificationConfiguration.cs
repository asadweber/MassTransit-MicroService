using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(n => n.EmailResult).HasMaxLength(1000);
        builder.Property(n => n.SMSResult).HasMaxLength(1000);
        builder.Property(n => n.PaciResult).HasMaxLength(1000);

        builder.HasIndex(n => n.OrderId).IsUnique();

        builder.HasOne(n => n.Order)
            .WithOne(o => o.OrderNotification)
            .HasForeignKey<OrderNotification>(n => n.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
