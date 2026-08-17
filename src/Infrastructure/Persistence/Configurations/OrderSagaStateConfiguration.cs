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
        // One-to-one relationship. CorrelationId is already the primary key (HasKey above),
        // so EF uses it as the principal key by default — no need for an explicit HasPrincipalKey.
        builder.HasOne(x => x.OrderNotification)
             .WithOne()
             .HasForeignKey<SagaOrderNotification>(
                 x => x.OrderSagaStateCorrelationId)
             .OnDelete(DeleteBehavior.Cascade);


    }
}
