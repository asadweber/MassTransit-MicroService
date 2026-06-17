using Domain.Entities;

namespace Domain.Repositories;

public interface IOrderRepository : IGenericRepository<Order>
{
    Task<Order?> GetByIdWithDetailsAsync(int id);
    Task<IReadOnlyList<Order>> GetAllWithDetailsAsync();
}
