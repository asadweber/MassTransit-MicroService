using Application.Interfaces;
using Application.Messaging.Events;
using Domain;
using Domain.Entities;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace NotificationService;

[ExcludeFromConfigureEndpoints]
public class NotificationConsumer(
    ILogger<NotificationConsumer> logger,
    IOrderService orderService,
    IUnitOfWork uow) : IConsumer<OrderConfirmed>
{
    public async Task Consume(ConsumeContext<OrderConfirmed> context)
    {
        var msg = context.Message;
        using var _ = Serilog.Context.LogContext.PushProperty("CorrelationId", msg.CorrelationId);
        using var __ = Serilog.Context.LogContext.PushProperty("OrderId", msg.Order.Id);

        var order = await orderService.GetByIdAsync(msg.Order.Id);
        order.Status = "Complete";
        await orderService.UpdateAsync(msg.Order.Id, order);

        logger.LogInformation("Confirmed. Notification sent.");

        // TODO: send email / SMS / PACI notification for real — stubbed as sent for now.
        await uow.OrderNotifications.AddAsync(new OrderNotification
        {
            OrderId = msg.Order.Id,
            NotifyToEmail = true,
            NotifyToSMS = true,
            NotifyToPaci = true,
            Result = "Sent"
        });
        await uow.SaveChangesAsync();
    }
}
