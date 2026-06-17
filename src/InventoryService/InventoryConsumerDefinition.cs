using MassTransit;

namespace InventoryService;

public class InventoryConsumerDefinition : ConsumerDefinition<InventoryConsumer>
{
    public InventoryConsumerDefinition()
    {
        EndpointName = "order-inventory"; // ← clean name
    }
}
