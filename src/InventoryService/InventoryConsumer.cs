using Application.Interfaces;
using Application.Messaging.Command;
using Application.Messaging.Events;
using Application.Services;
using Domain.Repositories;
using Infrastructure.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace InventoryService;

[ExcludeFromConfigureEndpoints]
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
            if (item.ProductId == 1)
            {
                logger.LogWarning("Simulated failure for Order {OrderId}, redelivery count {RedeliveryCount}", msg.OrderId, context.GetRedeliveryCount());
                throw new HttpRequestException("Inventory service unavailable");
            }

            var hasSufficientStock = await productService.HasSufficientStockAsync(item.ProductId, item.OrderQty);
            if (!hasSufficientStock)
            {
                order.Status = "Stock Not Available";
                await orderService.UpdateAsync(msg.OrderId, order);
                isAvailable = false;
                break;
            }
        }
        if (isAvailable)
        {
            order.Status = "Stock Available";
            await orderService.UpdateAsync(msg.OrderId, order);
        }

        await context.Publish(new InventoryChecked
        {
            CorrelationId = msg.CorrelationId,
            OrderId = msg.OrderId,
            IsAvailable = isAvailable
        });
    }
}
