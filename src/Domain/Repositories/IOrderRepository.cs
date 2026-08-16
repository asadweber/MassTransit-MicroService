using Domain.Entities;

namespace Domain.Repositories;

public interface IOrderRepository : IGenericRepository<Order>
{
    Task<Order?> GetByIdWithDetailsAsync(long id);
    Task<IReadOnlyList<Order>> GetAllWithDetailsAsync();
}
