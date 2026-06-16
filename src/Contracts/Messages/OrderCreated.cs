using Contracts.Dto;

namespace Contracts.Messages;

public class OrderCreated
{
    public OrderDto Order { get; set; } = new();
}
