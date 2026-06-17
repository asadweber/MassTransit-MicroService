using Application.Messaging.Command;
using Application.Messaging.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Application.Messaging.Consumers;

public class PaymentConsumer(ILogger<PaymentConsumer> logger) : IConsumer<ProcessPayment>
{
    public async Task Consume(ConsumeContext<ProcessPayment> context)
    {
        var msg = context.Message;
        logger.LogInformation("Processing payment for Order {OrderId}, Amount: {Amount}",
            msg.OrderId, msg.Amount);

        // TODO: real payment processing logic
        var isSuccess = true;

        await context.Publish(new PaymentProcessed
        {
            CorrelationId = msg.CorrelationId,
            OrderId = msg.OrderId,
            IsSuccess = isSuccess
        });
    }
}
