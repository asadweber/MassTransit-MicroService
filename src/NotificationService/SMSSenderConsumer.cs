using Application.Messaging.Events;
using Domain;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace NotificationService;

[ExcludeFromConfigureEndpoints]
public class SMSSenderConsumer(
    ILogger<SMSSenderConsumer> logger,
    IUnitOfWork uow) : IConsumer<OrderConfirmed>
{
    public async Task Consume(ConsumeContext<OrderConfirmed> context)
    {
        var msg = context.Message;
        using var _ = Serilog.Context.LogContext.PushProperty("CorrelationId", msg.CorrelationId);
        using var __ = Serilog.Context.LogContext.PushProperty("OrderId", msg.Order.Id);

        var notification = (await uow.OrderNotifications.FindAsync(n => n.OrderId == msg.Order.Id))
            .FirstOrDefault();

        if (notification is null || !notification.NotifyToSMS)
            return;

        // TODO: send SMS for real — stubbed as sent for now.
        notification.SMSResult = "Sent";
        await uow.OrderNotifications.Update(notification);
        await uow.SaveChangesAsync();

        logger.LogInformation("SMS notification sent.");
    }
}
