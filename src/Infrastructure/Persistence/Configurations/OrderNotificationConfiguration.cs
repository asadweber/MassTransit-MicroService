using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {


        builder.Property(n => n.NotifyToEmail).HasDefaultValue(false);
        builder.Property(n => n.NotifyToSMS).HasDefaultValue(false);
        builder.Property(n => n.NotifyToPaci).HasDefaultValue(false);


        builder.Property(n => n.EmailSendStatus).HasDefaultValue(false);
        builder.Property(n => n.SMSSendStatus).HasDefaultValue(false);
        builder.Property(n => n.PaciSendStatus).HasDefaultValue(false);

        builder.HasIndex(n => n.OrderId).IsUnique();

        builder.HasOne(n => n.Order)
            .WithOne(o => o.OrderNotification)
            .HasForeignKey<OrderNotification>(n => n.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
