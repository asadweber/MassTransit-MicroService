namespace OrderSaga.Saga;

public class SagaOrderDetail
{
    public int Id { get; set; }
    public Guid OrderSagaStateCorrelationId { get; set; }
    public int ProductId { get; set; }
    public int OrderQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}
