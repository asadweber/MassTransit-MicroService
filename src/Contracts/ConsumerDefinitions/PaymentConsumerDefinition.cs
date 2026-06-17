using Infrastructure.Messaging.Consumers;
using MassTransit;

namespace Infrastructure.Messaging.ConsumerDefinitions;

public class PaymentConsumerDefinition : ConsumerDefinition<PaymentConsumer>
{
    public PaymentConsumerDefinition()
    {
        EndpointName = "order-payment"; // ← clean name
    }
}
