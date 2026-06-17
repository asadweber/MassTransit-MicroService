using Application.Messaging.Consumers;
using MassTransit;

namespace Application.Messaging.ConsumerDefinitions;

public class InventoryConsumerDefinition : ConsumerDefinition<InventoryConsumer>
{
    public InventoryConsumerDefinition()
    {
        EndpointName = "order-inventory"; // ← clean name
    }
}
