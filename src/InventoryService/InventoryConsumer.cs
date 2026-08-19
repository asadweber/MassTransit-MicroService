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

        logger.LogInformation(
            "Order {OrderId} [{CorrelationId}]: checking inventory for {ItemCount} line item(s)",
            msg.Order.Id, msg.CorrelationId, msg.Order.OrderDetails.Count);

        var isAvailable = true;
        long shortProductId = 0;
        foreach (var item in msg.Order.OrderDetails)
        {
            var hasSufficientStock = await productService.HasSufficientStockAsync(item.ProductId, item.OrderQty);
            if (!hasSufficientStock)
            {
                isAvailable = false;
                shortProductId = item.ProductId;
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

        logger.LogInformation(
            "Order {OrderId} [{CorrelationId}]: inventory check result -> IsAvailable={IsAvailable}, ShortProductId={ShortProductId}",
            msg.Order.Id, msg.CorrelationId, isAvailable, shortProductId);

        await context.Publish(new InventoryChecked
        {
            CorrelationId = msg.CorrelationId,
            Order = msg.Order,
            IsAvailable = isAvailable
        });
    }
}
