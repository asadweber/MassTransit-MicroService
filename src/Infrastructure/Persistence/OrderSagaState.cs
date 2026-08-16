using Domain.Entities;
using MassTransit;

namespace Infrastructure.Persistence;

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

    public SagaOrderNotification? OrderNotification { get; set; }


    public DateTime? FirstUnavailableAt { get; set; }
    public DateTime? NextInventoryRetryAt { get; set; }

    public int InventoryRetryCount { get; set; } = 0;

    public Guid? InventoryRetryTokenId { get; set; }




    // Notification configuration
    public bool NotifyToEmail { get; set; }
    public bool NotifyToSMS { get; set; }
    public bool NotifyToPaci { get; set; }

    // Completion tracking
    public bool EmailCompleted { get; set; }
    public bool SmsCompleted { get; set; }
    public bool PaciCompleted { get; set; }
    public bool NotificationCompleted { get; set; }


}
