using Db.Repository;
using MassTransit;

namespace Contracts.Consumers;

public class NotificationConsumerDefinition : ConsumerDefinition<NotificationConsumer>
{
    public NotificationConsumerDefinition()
    {
        EndpointName = "order-notification"; // ← clean name
    }
}
