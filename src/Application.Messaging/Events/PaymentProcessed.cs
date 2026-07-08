using Application.Dtos;

namespace Application.Messaging.Events;

public class PaymentProcessed
{
    public Guid CorrelationId { get; set; }
    public OrderDto Order { get; set; }
    public bool IsSuccess { get; set; }
}
