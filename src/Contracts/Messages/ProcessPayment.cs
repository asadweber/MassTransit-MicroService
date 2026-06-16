namespace Contracts.Messages;

public class ProcessPayment
{
    public Guid CorrelationId { get; set; }
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
}
