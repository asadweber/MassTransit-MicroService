namespace Infrastructure.Messaging.Messages.Events;

public class PaymentProcessed
{
    public Guid CorrelationId { get; set; }
    public int OrderId { get; set; }
    public bool IsSuccess { get; set; }
}
