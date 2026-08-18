using Application.Dtos;

namespace Application.Messaging.Events;

public record EmailNotificationSent
{
    public Guid CorrelationId { get; init; }

    public OrderDto Order { get; set; } = new();
}
