namespace Domain.Entities;

public class OrderNotification
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public bool NotifyToEmail { get; set; }
    public bool NotifyToSMS { get; set; }
    public bool NotifyToPaci { get; set; }

    public string? EmailResult { get; set; }
    public string? SMSResult { get; set; }
    public string? PaciResult { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
