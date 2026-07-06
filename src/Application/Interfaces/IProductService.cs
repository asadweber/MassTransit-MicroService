using Application.Dtos;

namespace Application.Interfaces;

public interface IProductService
{
    Task<bool> HasSufficientStockAsync(int productId, int qty);

    Task<bool> ReduceStockQtyAsync(int productId, int qty);
}
