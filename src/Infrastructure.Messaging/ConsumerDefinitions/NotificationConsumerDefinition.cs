using Infrastructure.Messaging.Consumers;
using MassTransit;

namespace Infrastructure.Messaging.ConsumerDefinitions;

public class NotificationConsumerDefinition : ConsumerDefinition<NotificationConsumer>
{
    public NotificationConsumerDefinition()
    {
        EndpointName = "order-notification"; // ← clean name
    }
}
