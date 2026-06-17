using Contracts.Consumers;
using Contracts.Saga;
using Domain.Entities;
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
    Action<IEntityFrameworkSagaRepositoryConfigurator>? configureSagaRepository = null,
    Type? ownerConsumerType = null) // ← which consumer this service owns
    {
        x.AddBusMetadataExplorer();

        AddConsumerWithOwnership<InventoryConsumer, InventoryConsumerDefinition>(x, ownerConsumerType);
        AddConsumerWithOwnership<PaymentConsumer, PaymentConsumerDefinition>(x, ownerConsumerType);
        AddConsumerWithOwnership<NotificationConsumer, NotificationConsumerDefinition>(x, ownerConsumerType);

        var sagaRegistration = x.AddSagaStateMachine<OrderStateMachine, OrderSagaState, OrderSagaDefinition>();

        if (configureSagaRepository is not null)
            sagaRegistration.EntityFrameworkRepository(configureSagaRepository);
        else
            sagaRegistration.ExcludeFromConfigureEndpoints();

        return x;
    }

    private static void AddConsumerWithOwnership<TConsumer, TDefinition>(
        IBusRegistrationConfigurator x,
        Type? ownerConsumerType)
        where TConsumer : class, IConsumer
        where TDefinition : class, IConsumerDefinition<TConsumer>
    {
        var registration = x.AddConsumer<TConsumer, TDefinition>();

        if (ownerConsumerType == typeof(TConsumer))
            return; // ← this service owns it, don't exclude — ConfigureEndpoints will create queue

        registration.ExcludeFromConfigureEndpoints();
    }
}
