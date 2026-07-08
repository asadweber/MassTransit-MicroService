using Application.Dtos;

namespace Application.Messaging.Events;

public class InventoryChecked
{
    public Guid CorrelationId { get; set; }
    public OrderDto Order { get; set; }
    public bool IsAvailable { get; set; }
}
