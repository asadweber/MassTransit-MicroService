namespace Application.Dtos;

[Serializable]
public class OrderDetailDto
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int OrderQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}
