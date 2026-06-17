using Application.Messaging.Consumers;
using MassTransit;

namespace Application.Messaging.ConsumerDefinitions;

public class PaymentConsumerDefinition : ConsumerDefinition<PaymentConsumer>
{
    public PaymentConsumerDefinition()
    {
        EndpointName = "order-payment"; // ← clean name
    }
}
