namespace Contracts.Message;

public record InventoryChecked(
    Guid CorrelationId,
    int OrderId,
    bool IsAvailable
);
