using Contracts.Dto;

namespace Contracts.Message;

public record OrderCreated(
    int OrderId,
    string CustomerName,
    DateTime OrderDate,
    decimal TotalAmount,
    string Status,
    IReadOnlyList<OrderDetailDto> OrderDetails
);
