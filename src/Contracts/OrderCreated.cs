namespace Contracts;

public record OrderCreated(
    int OrderId,
    string CustomerName,
    DateTime OrderDate,
    decimal TotalAmount,
    string Status
);
