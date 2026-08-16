namespace Domain.Entities;

public class OrderNotification
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public bool NotifyToEmail { get; set; }
    public bool NotifyToSMS { get; set; }
    public bool NotifyToPaci { get; set; }

    public string Result { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
