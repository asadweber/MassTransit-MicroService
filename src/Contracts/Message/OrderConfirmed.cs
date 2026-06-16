namespace Contracts.Message;

public record OrderConfirmed(
    Guid CorrelationId,
    int OrderId,
    string CustomerName,
    decimal TotalAmount
);
