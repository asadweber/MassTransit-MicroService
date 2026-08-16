using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;

namespace Infrastructure.Repositories;

public class OrderNotificationRepository(AppDbContext context)
    : GenericRepository<OrderNotification>(context), IOrderNotificationRepository
{
}
