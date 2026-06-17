using Application.Dtos;

namespace Application.Messaging.Events;

public class OrderCreated
{
    public OrderDto Order { get; set; } = new();
}
