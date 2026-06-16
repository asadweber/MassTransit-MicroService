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
        // Immediate retries handle optimistic concurrency conflicts from SQL Server.
        // InMemoryOutbox holds published messages until saga state is committed,
        // preventing duplicate events when a retry succeeds after a failed save.
        endpointConfigurator.UseMessageRetry(r => r.Immediate(5));
        endpointConfigurator.UseInMemoryOutbox(context);
    }
}
