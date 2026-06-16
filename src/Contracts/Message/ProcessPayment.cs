namespace Contracts.Message;

public record ProcessPayment(
    Guid CorrelationId,
    int OrderId,
    decimal Amount
);
