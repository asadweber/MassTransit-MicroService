using MassTransit;

namespace OrderSaga.Saga;

public class OrderSagaState : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }

    public string CurrentState { get; set; } = string.Empty;

    public int Version { get; set; }

    public int OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Pending";

    public List<SagaOrderDetail> OrderDetails { get; set; } = [];

    public DateTime? FirstUnavailableAt { get; set; }
    public DateTime? NextInventoryRetryAt { get; set; }

    public int InventoryRetryCount { get; set; } = 0;

    public Guid? InventoryRetryTokenId { get; set; }
}
