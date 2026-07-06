using Application.Interfaces;
using Application.Messaging.Command;
using Application.Messaging.Events;
using Application.Services;
using Domain.Repositories;
using Infrastructure.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace InventoryService;

public class InventoryConsumer(ILogger<InventoryConsumer> logger, IOrderService orderService, IProductService productService ) : IConsumer<CheckInventory>
{
    public async Task Consume(ConsumeContext<CheckInventory> context)
    {
        var msg = context.Message;
        logger.LogInformation("Checking inventory for Order {OrderId}",msg.OrderId);

        var order = await orderService.GetByIdAsync(msg.OrderId);
        var isAvailable = true;
        foreach (var item in order.OrderDetails)
        {
            var hasSufficientStock = await productService.HasSufficientStockAsync(item.ProductId, item.OrderQty);
            if (!hasSufficientStock)
            {
                order.Status = "Stock Not Available";
                await orderService.UpdateAsync(msg.OrderId, order);
                isAvailable = false;
                break;
            }
        }
        

        await context.Publish(new InventoryChecked
        {
            CorrelationId = msg.CorrelationId,
            OrderId = msg.OrderId,
            IsAvailable = isAvailable
        });
    }
}
