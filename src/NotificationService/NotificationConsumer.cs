using Application.Interfaces;
using Application.Messaging.Events;
using Domain;
using MassTransit;
using Microsoft.Extensions.Logging;
using static MassTransit.Monitoring.Performance.BuiltInCounters;

namespace NotificationService;

[ExcludeFromConfigureEndpoints]
public class NotificationConsumer(
    ILogger<NotificationConsumer> logger,
    IOrderService orderService, IUnitOfWork uow) : IConsumer<OrderConfirmed>
{
    public async Task Consume(ConsumeContext<OrderConfirmed> context)
    {
        var message = context.Message;
        using var _ = Serilog.Context.LogContext.PushProperty("CorrelationId", message.CorrelationId);
        using var __ = Serilog.Context.LogContext.PushProperty("OrderId", message.Order.Id);

        var order = await orderService.GetByIdAsync(message.Order.Id);
        if (order is null)
        {
            logger.LogWarning(
                "Order {OrderId} [{CorrelationId}]: not found, skipping notification completion",
                message.Order.Id, message.CorrelationId);
            return;
        }

        order.Status = "Complete";
        await orderService.UpdateAsync(message.Order.Id, order);


        var notification = (await uow.OrderNotifications.FindAsync(n => n.OrderId == message.Order.Id))
            .FirstOrDefault();

        if (notification is not null)
        {
            // TODO: send email for real — stubbed as sent for now.
            notification.NotificationSendStatus = true;
            await uow.OrderNotifications.Update(notification);
            await uow.SaveChangesAsync();
        }


        if (message.Order.OrderNotification is not null)
            message.Order.OrderNotification.NotificationSendStatus = true;
        
        await Task.Delay(1000); // Simulate email sending delay

        await context.Publish(new OrderConfirmedCompleted
        {
            CorrelationId = message.CorrelationId,
            Order = message.Order,
            Process = OrderConfirmationProcess.Notification
        });

        logger.LogInformation(
            "Order {OrderId} [{CorrelationId}]: Notification completed",
            message.Order.Id,
            message.CorrelationId);
    }
}
