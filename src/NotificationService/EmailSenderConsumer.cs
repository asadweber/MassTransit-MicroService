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
        var msg = context.Message;
        using var _ = Serilog.Context.LogContext.PushProperty("CorrelationId", msg.CorrelationId);
        using var __ = Serilog.Context.LogContext.PushProperty("OrderId", msg.Order.Id);

        var notification = (await uow.OrderNotifications.FindAsync(n => n.OrderId == msg.Order.Id))
            .FirstOrDefault();

        if (notification is null || !notification.NotifyToEmail)
            return;

        // TODO: send email for real — stubbed as sent for now.
        notification.EmailSendStatus = true;
        await uow.OrderNotifications.Update(notification);
        await uow.SaveChangesAsync();

        logger.LogInformation("Email notification sent.");
    }
}
