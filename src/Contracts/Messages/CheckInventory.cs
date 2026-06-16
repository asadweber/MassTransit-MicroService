namespace Contracts.Messages;

public class CheckInventory
{
    public Guid CorrelationId { get; set; }
    public int OrderId { get; set; }
    public List<int> ProductIds { get; set; } = [];
}
