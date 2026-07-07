using Application.Interfaces;
using Application.Messaging.Command;
using Application.Messaging.Events;
using MassTransit;

namespace InventoryService;

[ExcludeFromConfigureEndpoints]
public class InventoryConsumer(ILogger<InventoryConsumer> logger, IOrderService orderService, IProductService productService ) : IConsumer<CheckInventory>
{
    public async Task Consume(ConsumeContext<CheckInventory> context)
    {
        var msg = context.Message;
        using var _ = Serilog.Context.LogContext.PushProperty("CorrelationId", msg.CorrelationId);
        using var __ = Serilog.Context.LogContext.PushProperty("OrderId", msg.OrderId);

        logger.LogInformation("Checking inventory");

        var order = await orderService.GetByIdAsync(msg.OrderId);
        var isAvailable = true;
        foreach (var item in order.OrderDetails)
        {
            if (item.ProductId == 1)
            {
                logger.LogWarning("Simulated failure, redelivery count {RedeliveryCount}", context.GetRedeliveryCount());
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

        logger.LogInformation("Inventory check result -> IsAvailable={IsAvailable}", isAvailable);

        await context.Publish(new InventoryChecked
        {
            CorrelationId = msg.CorrelationId,
            OrderId = msg.OrderId,
            IsAvailable = isAvailable
        });
    }
}
