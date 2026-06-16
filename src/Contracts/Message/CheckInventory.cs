namespace Contracts.Message;

public record CheckInventory(
    Guid CorrelationId,
    int OrderId,
    List<int> ProductIds
);
