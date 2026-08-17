using Application.Messaging.Events;
using Domain;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace NotificationService;

[ExcludeFromConfigureEndpoints]
public class EmailSenderConsumer(
    ILogger<EmailSenderConsumer> logger,
    IUnitOfWork uow) : IConsumer<OrderConfirmed>
{
    public async Task Consume(ConsumeContext<OrderConfirmed> context)
    {
        var message = context.Message;

        // If email notification is not required,
        // do nothing.
        if (!message.Order.OrderNotification?.NotifyToEmail ?? false)
        {
            logger.LogInformation(
                "Order {OrderId} [{CorrelationId}]: Email notification is disabled",
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

        if (notification is not null && notification.NotifyToEmail)
        {
            // TODO: send email for real — stubbed as sent for now.
            notification.EmailSendStatus = true;
            await uow.OrderNotifications.Update(notification);
            await uow.SaveChangesAsync();

            
        }


        if (message.Order.OrderNotification is not null)
            message.Order.OrderNotification.EmailSendStatus = true;


        await Task.Delay(1000); // Simulate email sending delay

        // If processing succeeds, publish completion.
        await context.Publish(new OrderConfirmedCompleted
        {
            CorrelationId = message.CorrelationId,
            Order = message.Order,
            Process = OrderConfirmationProcess.Email
        });

        logger.LogInformation(
            "Order {OrderId} [{CorrelationId}]: Email completed",
            message.Order.Id,
            message.CorrelationId);
    }
}
