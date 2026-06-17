using Infrastructure.Messaging.Consumers;
using MassTransit;

namespace Infrastructure.Messaging.ConsumerDefinitions;

public class InventoryConsumerDefinition : ConsumerDefinition<InventoryConsumer>
{
    public InventoryConsumerDefinition()
    {
        EndpointName = "order-inventory"; // ← clean name
    }
}
