using Application.Messaging.Events;
using Domain;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace NotificationService;

[ExcludeFromConfigureEndpoints]
public class PaciSenderConsumer(
    ILogger<PaciSenderConsumer> logger,
    IUnitOfWork uow) : IConsumer<OrderConfirmed>
{
    public async Task Consume(ConsumeContext<OrderConfirmed> context)
    {
        var message = context.Message;

        // If email notification is not required,
        // do nothing.
        if (!message.Order.OrderNotification?.NotifyToPaci ?? false)
        {
            logger.LogInformation(
                "Order {OrderId} [{CorrelationId}]: PACI notification is disabled",
                message.Order.Id,
                message.CorrelationId);

            return;
        }

        logger.LogInformation(
            "Order {OrderId} [{CorrelationId}]: Sending PACI notification",
            message.Order.Id,
            message.CorrelationId);

        // --------------------------------------------------
        // Your PACI logic
        // --------------------------------------------------

        var notification = (await uow.OrderNotifications.FindAsync(n => n.OrderId == message.Order.Id))
            .FirstOrDefault();

        if (notification is not null && notification.NotifyToPaci)
        {
            // TODO: send PACI notification for real — stubbed as sent for now.
            notification.PaciSendStatus = true;
            await uow.OrderNotifications.Update(notification);
            await uow.SaveChangesAsync();

            message.Order.OrderNotification.PaciSendStatus = true;

        }

        // If processing succeeds, publish completion.
        await context.Publish(new OrderConfirmedCompleted
        {
            CorrelationId = message.CorrelationId,
            Order = message.Order,
            Process = OrderConfirmationProcess.Paci
        });

        logger.LogInformation(
            "Order {OrderId} [{CorrelationId}]: PACI completed",
            message.Order.Id,
            message.CorrelationId);
    }
}
