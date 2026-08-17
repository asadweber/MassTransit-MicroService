using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class OrderSagaStateConfiguration : IEntityTypeConfiguration<OrderSagaState>
{
    public void Configure(EntityTypeBuilder<OrderSagaState> builder)
    {
        builder.ToTable("OrderSagaStates");

        builder.HasKey(s => s.CorrelationId);

        builder.Property(s => s.Version).IsConcurrencyToken();

        builder.Property(s => s.CurrentState).HasMaxLength(50);

        builder.Property(s => s.CustomerName).HasMaxLength(200);
        builder.Property(s => s.TotalAmount).HasPrecision(18, 2);
        builder.Property(s => s.Status).HasMaxLength(50);

        builder.HasIndex(s => s.OrderId);

        builder.OwnsMany(s => s.OrderDetails, detail =>
        {
            detail.ToTable("SagaOrderDetails");
            detail.WithOwner().HasForeignKey(d => d.OrderSagaStateCorrelationId);
            detail.HasKey(d => d.Id);
            detail.Property(d => d.UnitPrice).HasPrecision(18, 2);
            detail.Property(d => d.Total).HasPrecision(18, 2);
        });
        // Owned (not a plain HasOne/WithOne) so EF auto-includes it on every OrderSagaState
        // load, same as OrderDetails above — MassTransit's EntityFrameworkRepository reloads
        // the saga with no explicit .Include chain, and a regular navigation would come back
        // null on every event after the first in-memory OrderCreated assignment.
        builder.OwnsOne(s => s.OrderNotification, notification =>
        {
            notification.ToTable("SagaOrderNotifications");
            notification.WithOwner().HasForeignKey(n => n.OrderSagaStateCorrelationId);
            notification.HasKey(n => n.Id);
        });
    }
}
