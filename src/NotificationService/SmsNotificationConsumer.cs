using Application.Interfaces;
using Application.Messaging.Events;
using Domain;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace NotificationService;

[ExcludeFromConfigureEndpoints]
public class SmsNotificationConsumer(
    ILogger<SmsNotificationConsumer> logger,
    IUnitOfWork uow) : IConsumer<OrderConfirmed>
{
    public async Task Consume(ConsumeContext<OrderConfirmed> context)
    {
        var message = context.Message;
        using var _ = Serilog.Context.LogContext.PushProperty("CorrelationId", message.CorrelationId);
        using var __ = Serilog.Context.LogContext.PushProperty("OrderId", message.Order.Id);

        var notification = (await uow.OrderNotifications.FindAsync(n => n.OrderId == message.Order.Id))
            .FirstOrDefault();

        var sent = false;
        if (notification is not null && notification.NotifyToSMS)
        {
            notification.SMSSendStatus = true;
            sent = true;

            await uow.OrderNotifications.Update(notification);
            await uow.SaveChangesAsync();
        }

        await context.Publish(new SmsNotificationSent
        {
            CorrelationId = message.CorrelationId,
            Order = message.Order,
        });

        logger.LogInformation(
            "Order {OrderId} [{CorrelationId}]: SMS notification completed, Sent={Sent}, Reason={Reason}",
            message.Order.Id,
            message.CorrelationId,
            sent,
            notification is null ? "NoNotificationRecord" : sent ? "OK" : "NotOptedIn");
    }
}
