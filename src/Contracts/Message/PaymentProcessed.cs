namespace Contracts.Message;

public record PaymentProcessed(
    Guid CorrelationId,
    int OrderId,
    bool IsSuccess
);
