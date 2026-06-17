namespace Application.Messaging.Events;

public class InventoryChecked
{
    public Guid CorrelationId { get; set; }
    public int OrderId { get; set; }
    public bool IsAvailable { get; set; }
}
