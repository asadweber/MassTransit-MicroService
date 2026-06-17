
using Application.Dtos;

namespace Contracts.Messages.Events;

public class OrderCreated
{
    public OrderDto Order { get; set; } = new();
}
