using Application.Dtos;

namespace Application.Interfaces;

public interface IProductService
{
    Task<bool> HasSufficientStockAsync(long productId, long qty);

    Task<bool> ReduceStockQtyAsync(long productId, long qty);

    Task<List<ProductDto>> GetAllAsync();

    Task<ProductDto?> GetByIdAsync(long id);

    Task<ProductDto> CreateAsync(ProductDto request);

    Task<bool> UpdateAsync(long id, ProductDto request);

    Task<bool> DeleteAsync(long id);
}
