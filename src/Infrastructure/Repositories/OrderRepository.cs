using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class OrderRepository(AppDbContext context)
    : GenericRepository<Order>(context), IOrderRepository
{
    public async Task<Order?> GetByIdWithDetailsAsync(int id)
    {
        return await Set
            .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<IReadOnlyList<Order>> GetAllWithDetailsAsync()
    {
        return await Set
            .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
            .ToListAsync();
    }
}
