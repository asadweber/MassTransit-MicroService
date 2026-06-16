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
        this IBusRegistrationConfigurator x)
    {
        x.AddBusMetadataExplorer();

        // ExcludeFromConfigureEndpoints = topology metadata only, no queues created.
        // Each real service re-registers its own type WITHOUT this flag so its
        // queue is created when ConfigureEndpoints is called.
        x.AddConsumer<InventoryConsumer, InventoryConsumerDefinition>().ExcludeFromConfigureEndpoints();
        x.AddConsumer<PaymentConsumer, PaymentConsumerDefinition>().ExcludeFromConfigureEndpoints();
        x.AddConsumer<NotificationConsumer, NotificationConsumerDefinition>().ExcludeFromConfigureEndpoints();
        x.AddSagaStateMachine<OrderStateMachine, OrderSagaState, OrderSagaDefinition>().ExcludeFromConfigureEndpoints();
        return x;
    }
}
