using Application.Messaging.Consumers;
using MassTransit;

namespace Application.Messaging.ConsumerDefinitions;

public class NotificationConsumerDefinition : ConsumerDefinition<NotificationConsumer>
{
    public NotificationConsumerDefinition()
    {
        EndpointName = "order-notification"; // ← clean name
    }
}
