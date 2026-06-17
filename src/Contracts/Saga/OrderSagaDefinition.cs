using Db.Repository;
using MassTransit;

namespace Contracts.Saga;

public class OrderSagaDefinition : SagaDefinition<OrderSagaState>
{
    protected override void ConfigureSaga(
        IReceiveEndpointConfigurator endpointConfigurator,
        ISagaConfigurator<OrderSagaState> sagaConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
        // ✅ Transactional Outbox (SQL Server via EF Core)
        endpointConfigurator.UseEntityFrameworkOutbox<AppDbContext>(context);
    }
}
