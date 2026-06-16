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
        endpointConfigurator.UseMessageRetry(r => r.Immediate(5));
        endpointConfigurator.UseEntityFrameworkOutbox<AppDbContext>(context);
    }
}
