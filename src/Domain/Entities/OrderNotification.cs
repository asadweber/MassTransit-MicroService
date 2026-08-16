namespace Domain.Entities;

public class OrderNotification
{
    public int Id { get; set; }

    public bool NotifyToEmail { get; set; }
    public bool NotifyToSMS { get; set; }
    public bool NotifyToPaci { get; set; }

    public bool EmailSendStatus { get; set; } = false;
    public bool SMSSendStatus { get; set; } = false;
    public bool PaciSendStatus { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;


    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
}
