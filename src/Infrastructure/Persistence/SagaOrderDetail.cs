namespace Infrastructure.Persistence;

public class SagaOrderDetail
{
    public long Id { get; set; }
    public Guid OrderSagaStateCorrelationId { get; set; }
    public long ProductId { get; set; }
    public long OrderQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}
