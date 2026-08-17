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
        var message = context.Message;

        // If SMS notification is not required,
        // do nothing.
        if (!message.Order.OrderNotification?.NotifyToSMS ?? false)
        {
            logger.LogInformation(
                "Order {OrderId} [{CorrelationId}]: SMS notification is disabled",
                message.Order.Id,
                message.CorrelationId);

            return;
        }

        logger.LogInformation(
            "Order {OrderId} [{CorrelationId}]: Sending email",
            message.Order.Id,
            message.CorrelationId);

        // --------------------------------------------------
        // Your email logic
        // --------------------------------------------------

        var notification = (await uow.OrderNotifications.FindAsync(n => n.OrderId == message.Order.Id))
            .FirstOrDefault();

        if (notification is not null && notification.NotifyToSMS)
        {
            // TODO: send SMS for real — stubbed as sent for now.
            notification.SMSSendStatus = true;
            await uow.OrderNotifications.Update(notification);
            await uow.SaveChangesAsync();

            message.Order.OrderNotification.SMSSendStatus = true;

        }

        // If processing succeeds, publish completion.
        await context.Publish(new OrderConfirmedCompleted
        {
            CorrelationId = message.CorrelationId,
            Order = message.Order,
            Process = OrderConfirmationProcess.SMS
        });

        logger.LogInformation(
            "Order {OrderId} [{CorrelationId}]: SMS completed",
            message.Order.Id,
            message.CorrelationId);
    }
}
