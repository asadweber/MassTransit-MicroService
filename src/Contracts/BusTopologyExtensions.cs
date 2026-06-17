using Contracts.Consumers;
using Contracts.Saga;
using Db.Repository;
using MassTransit;

namespace Contracts;

public static class BusTopologyExtensions
{
    /// <summary>
    /// Registers all consumers so every service and the dashboard see the full message topology.
    /// Each service should still call cfg.ConfigureEndpoints(ctx) to create only its own queue.
    /// WebApp should NOT call ConfigureEndpoints — it publishes only, no queues needed.
    /// </summary>
    public static IBusRegistrationConfigurator AddAllConsumers(
        this IBusRegistrationConfigurator x,
        Action<IEntityFrameworkSagaRepositoryConfigurator>? configureSagaRepository = null)
    {
        x.AddBusMetadataExplorer();

        x.AddConsumer<InventoryConsumer>()
            .ExcludeFromConfigureEndpoints();

        x.AddConsumer<PaymentConsumer>()
            .ExcludeFromConfigureEndpoints();

        x.AddConsumer<NotificationConsumer>()
            .ExcludeFromConfigureEndpoints();

        // ✅ Only attach EF repository when this service owns the saga
        var sagaRegistration = x.AddSagaStateMachine<OrderStateMachine, OrderSagaState, OrderSagaDefinition>();

        if (configureSagaRepository is not null)
        {
            sagaRegistration.EntityFrameworkRepository(configureSagaRepository);
            // ✅ Don't exclude — this service owns the queue, ConfigureEndpoints will create it
        }
        else
        {
            // ✅ Other services — topology only, no queue, no repository
            sagaRegistration.ExcludeFromConfigureEndpoints();
        }

        return x;
    }
}
