using Domain.Repositories;

namespace Domain;

public interface IUnitOfWork : IAsyncDisposable
{
    IOrderRepository Orders { get; }
    IProductRepository Products { get; }
    IOrderNotificationRepository OrderNotifications { get; }

    Task<int> SaveChangesAsync();
    Task BeginTransactionAsync();
    Task CommitAsync();
    Task RollbackAsync();
}
