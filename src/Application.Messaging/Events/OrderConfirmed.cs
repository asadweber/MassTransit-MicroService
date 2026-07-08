using Application.Dtos;

namespace Application.Messaging.Events;

public class OrderConfirmed
{
    public Guid CorrelationId { get; set; }
    public OrderDto Order { get; set; }
}
