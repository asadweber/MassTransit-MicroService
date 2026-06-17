using Domain.Entities;

namespace Domain.Repositories;

public interface IProductRepository : IGenericRepository<Product>
{
    Task<bool> HasSufficientStockAsync(int productId, int qty);
}
