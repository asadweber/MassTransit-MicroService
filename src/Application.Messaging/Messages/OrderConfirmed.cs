namespace Application.Messaging.Messages;

public class OrderConfirmed
{
    public Guid CorrelationId { get; set; }
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
}
