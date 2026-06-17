using Application.Interfaces;
using Application.Messaging.Command;
using Application.Messaging.Events;
using Application.Services;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace InventoryService;

public class InventoryConsumer(ILogger<InventoryConsumer> logger, IOrderService orderService ) : IConsumer<CheckInventory>
{
    public async Task Consume(ConsumeContext<CheckInventory> context)
    {
        var msg = context.Message;
        logger.LogInformation("Checking inventory for Order {OrderId}",msg.OrderId);

        var order = await orderService.GetByIdAsync(msg.OrderId);


        //if (msg.OrderId == 15)
        //{
        //    throw new InvalidOperationException("Inventory check failed for order 15");
        //}
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
