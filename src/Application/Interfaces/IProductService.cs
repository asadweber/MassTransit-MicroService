using Application.Dtos;

namespace Application.Interfaces;

public interface IProductService : IGenericService<ProductDto>
{
    Task<bool> HasSufficientStockAsync(long productId, long qty);

    Task<bool> ReduceStockQtyAsync(long productId, long qty);
}
