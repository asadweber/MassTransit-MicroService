namespace Application.Messaging.Command;

public class PaymentProcessed
{
    public Guid CorrelationId { get; set; }
    public int OrderId { get; set; }
    public bool IsSuccess { get; set; }
}
