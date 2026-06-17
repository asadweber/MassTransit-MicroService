using Db.Repository;
using MassTransit;

namespace Contracts.Consumers;

public class InventoryConsumerDefinition : ConsumerDefinition<InventoryConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<InventoryConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r =>
            r.Intervals(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(30)
            ));

        // ✅ Transactional Outbox (SQL Server via EF Core)
        endpointConfigurator.UseEntityFrameworkOutbox<AppDbContext>(context);
    }
}
