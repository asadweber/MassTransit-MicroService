namespace Application.Dtos;

[Serializable]
public class ProductDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public long Stock { get; set; }
}
