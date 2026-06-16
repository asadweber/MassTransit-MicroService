using Contracts.Dto;

namespace Contracts.Message;

public class OrderCreated
{
    public OrderDto Order { get; set; } = new();
}
