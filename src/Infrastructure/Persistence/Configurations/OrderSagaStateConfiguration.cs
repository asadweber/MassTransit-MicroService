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
        // One-to-one relationship
        builder.HasOne(x => x.OrderNotification)
             .WithOne(x => x.OrderSagaState)
             .HasForeignKey<SagaOrderNotification>(
                 x => x.OrderSagaStateCorrelationId)
             .HasPrincipalKey<OrderSagaState>(
                 x => x.CorrelationId)
             .OnDelete(DeleteBehavior.Cascade);


    }
}
