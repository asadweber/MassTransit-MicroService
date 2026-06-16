using Contracts.Message;
using MassTransit;

namespace InventoryService;

public class InventoryConsumer(ILogger<InventoryConsumer> logger) : IConsumer<CheckInventory>
{
    public async Task Consume(ConsumeContext<CheckInventory> context)
    {
        var msg = context.Message;
        logger.LogInformation("Checking inventory for Order {OrderId}, Products: {Products}",
            msg.OrderId, string.Join(',', msg.ProductIds));

        // TODO: real inventory check logic
        var isAvailable = true;

        await context.Publish(new InventoryChecked(
            msg.CorrelationId,
            msg.OrderId,
            isAvailable));
    }
}
