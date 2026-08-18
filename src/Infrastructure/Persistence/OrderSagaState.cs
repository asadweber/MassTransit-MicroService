using Domain.Entities;
using MassTransit;

namespace Infrastructure.Persistence;

public class OrderSagaState : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }

    public string CurrentState { get; set; } = string.Empty;

    public int Version { get; set; }

    public long OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Pending";

    public List<SagaOrderDetail> OrderDetails { get; set; } = [];

    public DateTime? FirstUnavailableAt { get; set; }
    public DateTime? NextInventoryRetryAt { get; set; }

    public int InventoryRetryCount { get; set; } = 0;

    public Guid? InventoryRetryTokenId { get; set; }

    /// <summary>
    /// Bitmask tracking which notification channels have completed, managed by
    /// MassTransit's CompositeEvent (EmailNotificationSent / SmsNotificationSent).
    /// </summary>
    public int NotificationsCompleted { get; set; }
}
