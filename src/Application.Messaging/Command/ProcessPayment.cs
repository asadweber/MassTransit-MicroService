using Application.Dtos;

namespace Application.Messaging.Command;

public class ProcessPayment
{
    public Guid CorrelationId { get; set; }
    public OrderDto Order { get; set; }
}
