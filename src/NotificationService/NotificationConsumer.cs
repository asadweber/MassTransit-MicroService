using Application.Interfaces;
using Application.Messaging.Events;
using MassTransit;
using Microsoft.Extensions.Logging;
using static MassTransit.Monitoring.Performance.BuiltInCounters;

namespace NotificationService;

[ExcludeFromConfigureEndpoints]
public class NotificationConsumer(
    ILogger<NotificationConsumer> logger,
    IOrderService orderService) : IConsumer<OrderConfirmed>
{
    public async Task Consume(ConsumeContext<OrderConfirmed> context)
    {
        var msg = context.Message;
        using var _ = Serilog.Context.LogContext.PushProperty("CorrelationId", msg.CorrelationId);
        using var __ = Serilog.Context.LogContext.PushProperty("OrderId", msg.Order.Id);

        var order = await orderService.GetByIdAsync(msg.Order.Id);
        if (order is null)
        {
            logger.LogWarning(
                "Order {OrderId} [{CorrelationId}]: not found, skipping notification completion",
                msg.Order.Id, msg.CorrelationId);
            return;
        }

        order.Status = "Complete";
        await orderService.UpdateAsync(msg.Order.Id, order);

        if (msg.Order.OrderNotification is not null)
            msg.Order.OrderNotification.NotificationSendStatus = true;
        
        System.Threading.Thread.Sleep(1000); // Simulate email sending delay

        await context.Publish(new OrderConfirmedCompleted
        {
            CorrelationId = msg.CorrelationId,
            Order = msg.Order,
            Process = OrderConfirmationProcess.Notification
        });

        logger.LogInformation(
            "Order {OrderId} [{CorrelationId}]: Notification completed",
            msg.Order.Id,
            msg.CorrelationId);
    }
}
