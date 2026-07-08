using Application.Dtos;

namespace Application.Messaging.Command;

public class CheckInventory
{
    public Guid CorrelationId { get; set; }
    public OrderDto Order { get; set; }

    //public List<int> ProductIds { get; set; } = [];
}
