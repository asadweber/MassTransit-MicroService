using Application.Messaging.Command;
using Application.Messaging.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Application.Messaging.Consumers;

public class InventoryConsumer(ILogger<InventoryConsumer> logger) : IConsumer<CheckInventory>
{
    public async Task Consume(ConsumeContext<CheckInventory> context)
    {
        var msg = context.Message;
        logger.LogInformation("Checking inventory for Order {OrderId}",msg.OrderId);


        // TODO: real inventory check logic
        var isAvailable = true;
        
        

        await context.Publish(new InventoryChecked
        {
            CorrelationId = msg.CorrelationId,
            OrderId = msg.OrderId,
            IsAvailable = isAvailable
        });
    }
}
