using MassTransit;

namespace Contracts.Consumers;

public class InventoryConsumerDefinition : ConsumerDefinition<InventoryConsumer>
{
    public InventoryConsumerDefinition()
    {
        EndpointName = "order-inventory"; // ← clean name
    }
}
