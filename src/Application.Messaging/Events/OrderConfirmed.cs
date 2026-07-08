namespace Application.Messaging.Events;

public class OrderConfirmed
{
    public Guid CorrelationId { get; set; }
    public int OrderId { get; set; }
}
