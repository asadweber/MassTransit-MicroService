using Db.Repository;
using MassTransit;

namespace Contracts.Consumers;

public class NotificationConsumerDefinition : ConsumerDefinition<NotificationConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<NotificationConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r =>
            r.Incremental(5, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)));
        // ✅ Transactional Outbox (SQL Server via EF Core)
        endpointConfigurator.UseEntityFrameworkOutbox<AppDbContext>(context);
    }
}
