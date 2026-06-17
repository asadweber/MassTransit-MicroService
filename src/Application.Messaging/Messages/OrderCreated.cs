
using Application.Dtos;

namespace Application.Messaging.Messages;

public class OrderCreated
{
    public OrderDto Order { get; set; } = new();
}
