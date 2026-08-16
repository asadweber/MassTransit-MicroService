using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.Property(o => o.TotalAmount).HasPrecision(18, 2);

        // Order 1 : OrderNotification 0..1
        builder.HasOne(o => o.OrderNotification)
            .WithOne(n => n.Order)
            .HasForeignKey<OrderNotification>(n => n.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

    }
}
