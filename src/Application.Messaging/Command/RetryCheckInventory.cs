namespace Application.Messaging.Command;

public class RetryCheckInventory
{
    public Guid CorrelationId { get; set; }
    public int OrderId { get; set; }
    //public List<int> ProductIds { get; set; } = [];
}
