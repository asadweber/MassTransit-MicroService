
using Application.Dtos;

namespace Infrastructure.Messaging.Messages.Events;

public class OrderCreated
{
    public OrderDto Order { get; set; } = new();
}
