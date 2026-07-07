using Application.Interfaces;
using Application.Messaging.Command;
using Application.Messaging.Events;
using Domain.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace PaymentService;

[ExcludeFromConfigureEndpoints]
public class PaymentConsumer(ILogger<PaymentConsumer> logger, IOrderService orderService, IProductService productService) : IConsumer<ProcessPayment>
{
    public async Task Consume(ConsumeContext<ProcessPayment> context)
    {
        var msg = context.Message;
        logger.LogInformation("Processing payment for Order {OrderId}, Amount: {Amount}",
            msg.OrderId, msg.Amount);

        // TODO: real payment processing logic
        var isSuccess = true;

        var order = await orderService.GetByIdAsync(msg.OrderId);

        //foreach (var item in order.OrderDetails)
        //{
        //   await productService.ReduceStockQtyAsync(item.ProductId, item.OrderQty);
        //}

        await context.Publish(new PaymentProcessed
        {
            CorrelationId = msg.CorrelationId,
            OrderId = msg.OrderId,
            IsSuccess = isSuccess
        });
    }
}
