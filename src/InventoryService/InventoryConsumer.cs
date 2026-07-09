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
        using var __ = Serilog.Context.LogContext.PushProperty("OrderId", msg.Order.Id);

        logger.LogInformation("Checking inventory");

        //var order = await orderService.GetByIdAsync(msg.Order.Id);
        var isAvailable = true;
        foreach (var item in msg.Order.OrderDetails)
        {
            //if (item.ProductId == 1)
            //{
            //    logger.LogWarning("Simulated failure, redelivery count {RedeliveryCount}", context.GetRedeliveryCount());
            //    throw new HttpRequestException("Inventory service unavailable");
            //}

            var hasSufficientStock = await productService.HasSufficientStockAsync(item.ProductId, item.OrderQty);
            if (!hasSufficientStock)
            {
                isAvailable = false;
                break;
            }
        }

        // Skip the write on repeat retries when status hasn't actually changed —
        // avoids a redundant UpdateAsync every backoff attempt over the 7-day window.
        var newStatus = isAvailable ? "Stock Available" : "Stock Not Available";
        if (msg.Order.Status != newStatus)
        {
            msg.Order.Status = newStatus;
            await orderService.UpdateAsync(msg.Order.Id, msg.Order);
        }

        logger.LogInformation("Inventory check result -> IsAvailable={IsAvailable}", isAvailable);

        await context.Publish(new InventoryChecked
        {
            CorrelationId = msg.CorrelationId,
            Order = msg.Order,
            IsAvailable = isAvailable
        });
    }
}
