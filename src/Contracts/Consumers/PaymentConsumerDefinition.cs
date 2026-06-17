using Db.Repository;
using MassTransit;

namespace Contracts.Consumers;

public class PaymentConsumerDefinition : ConsumerDefinition<PaymentConsumer>
{
    public PaymentConsumerDefinition()
    {
        EndpointName = "order-payment"; // ← clean name
    }
}
