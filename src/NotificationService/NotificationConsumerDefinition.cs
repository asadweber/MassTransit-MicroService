using MassTransit;

namespace NotificationService;

public class NotificationConsumerDefinition : ConsumerDefinition<NotificationConsumer>
{
    public NotificationConsumerDefinition()
    {
        EndpointName = "order-notification"; // ← clean name
    }
}
