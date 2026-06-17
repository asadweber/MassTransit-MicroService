using Application.Messaging.Messages;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Application.Messaging.Consumers;

public class NotificationConsumer(ILogger<NotificationConsumer> logger) : IConsumer<OrderConfirmed>
{
    public async Task Consume(ConsumeContext<OrderConfirmed> context)
    {
        var msg = context.Message;
        logger.LogInformation(
            "Order {OrderId} confirmed for {CustomerName}. Total: {TotalAmount}. Notification sent.",
            msg.OrderId, msg.CustomerName, msg.TotalAmount);

        // TODO: send email / push notification
        await Task.CompletedTask;
    }
}
