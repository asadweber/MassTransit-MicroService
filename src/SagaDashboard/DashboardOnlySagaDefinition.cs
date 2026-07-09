using MassTransit;
using OrderSaga.Saga;

namespace SagaDashboard;

// Registered only in SagaDashboard, purely so the dashboard can display saga
// flow across services. ExcludeFromConfigureEndpoints stops ConfigureEndpoints
// from binding a second consumer to OrderSaga's real durable queue here, which
// would otherwise split/steal OrderSagaState messages from the actual OrderSaga
// host and break its retry scheduling.
[ExcludeFromConfigureEndpoints]
public class DashboardOnlySagaDefinition : SagaDefinition<OrderSagaState>
{
    protected override void ConfigureSaga(
        IReceiveEndpointConfigurator endpointConfigurator,
        ISagaConfigurator<OrderSagaState> sagaConfigurator,
        IRegistrationContext context)
    {
    }
}
