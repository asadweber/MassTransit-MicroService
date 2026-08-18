using Application.Dtos;

namespace Application.Messaging.Events;

public record SmsNotificationSent
{
    public Guid CorrelationId { get; init; }

    public OrderDto Order { get; set; } = new();
}
