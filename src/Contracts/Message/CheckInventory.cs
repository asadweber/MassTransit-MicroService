namespace Contracts.Message;

public class CheckInventory
{
    public Guid CorrelationId { get; set; }
    public int OrderId { get; set; }
    public List<int> ProductIds { get; set; } = [];
}
