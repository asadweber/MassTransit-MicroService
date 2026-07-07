using Application.Messaging.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace NotificationService;

[ExcludeFromConfigureEndpoints]
public class NotificationConsumer(ILogger<NotificationConsumer> logger) : IConsumer<OrderConfirmed>
{
    public async Task Consume(ConsumeContext<OrderConfirmed> context)
    {
        var msg = context.Message;
        using var _ = Serilog.Context.LogContext.PushProperty("CorrelationId", msg.CorrelationId);
        using var __ = Serilog.Context.LogContext.PushProperty("OrderId", msg.OrderId);

        logger.LogInformation(
            "Confirmed for {CustomerName}, Total={TotalAmount}. Notification sent.",
            msg.CustomerName, msg.TotalAmount);

        // TODO: send email / push notification
        await Task.CompletedTask;
    }
}
