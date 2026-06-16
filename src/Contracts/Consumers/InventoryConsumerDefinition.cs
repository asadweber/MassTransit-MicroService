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
            r.Interval(3, TimeSpan.FromSeconds(1)));
    }
}
