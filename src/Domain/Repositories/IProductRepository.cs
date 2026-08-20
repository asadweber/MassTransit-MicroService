using Domain.Entities;

namespace Domain.Repositories;

public interface IProductRepository : IGenericRepository<Product>
{
    Task<bool> HasSufficientStockAsync(long productId, long qty);

    Task<bool> ReduceStockQtyAsync(long productId, long qty);

}
