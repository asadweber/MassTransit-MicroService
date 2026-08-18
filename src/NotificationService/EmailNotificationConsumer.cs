using Application.Interfaces;
using Application.Messaging.Events;
using Domain;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace NotificationService;

[ExcludeFromConfigureEndpoints]
public class EmailNotificationConsumer(
    ILogger<EmailNotificationConsumer> logger,
    IUnitOfWork uow) : IConsumer<OrderConfirmed>
{
    public async Task Consume(ConsumeContext<OrderConfirmed> context)
    {
        var message = context.Message;
        using var _ = Serilog.Context.LogContext.PushProperty("CorrelationId", message.CorrelationId);
        using var __ = Serilog.Context.LogContext.PushProperty("OrderId", message.Order.Id);

        var notification = (await uow.OrderNotifications.FindAsync(n => n.OrderId == message.Order.Id))
            .FirstOrDefault();

        if (notification is null || !notification.NotifyToEmail)
        {
            logger.LogInformation(
                "Order {OrderId} [{CorrelationId}]: Email not requested, skipping",
                message.Order.Id,
                message.CorrelationId);

            await context.Publish(new EmailNotificationSent
            {
                CorrelationId = message.CorrelationId,
                Order = message.Order,
            });
            return;
        }

        // TODO: send email for real — stubbed as sent for now.
        notification.EmailSendStatus = true;

        await uow.OrderNotifications.Update(notification);
        await uow.SaveChangesAsync();

        await context.Publish(new EmailNotificationSent
        {
            CorrelationId = message.CorrelationId,
            Order = message.Order,
        });

        logger.LogInformation(
            "Order {OrderId} [{CorrelationId}]: Email notification completed",
            message.Order.Id,
            message.CorrelationId);
    }
}
