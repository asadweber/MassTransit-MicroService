namespace Domain.Entities;

public class Order
{
    public long Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Pending";

    public ICollection<OrderDetail> OrderDetails { get; set; } = [];
    public OrderNotification? OrderNotification { get; set; }
}
