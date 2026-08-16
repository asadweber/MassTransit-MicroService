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
        var existing = (await uow.OrderNotifications.FindAsync(n => n.OrderId == msg.Order.Id))
            .FirstOrDefault();

        if (existing is not null)
        {
            existing.Result = "Sent";
            await uow.OrderNotifications.Update(existing);
        }


        await uow.SaveChangesAsync();
        await Task.CompletedTask;
    }
}
