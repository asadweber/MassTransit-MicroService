using MassTransit;

namespace PaymentService;

public class PaymentConsumerDefinition : ConsumerDefinition<PaymentConsumer>
{
    public PaymentConsumerDefinition()
    {
        EndpointName = "order-payment"; // ← clean name
    }
}
