using Application.Messaging.ConsumerDefinitions;
using Application.Messaging.Consumers;
using Application.Messaging.Saga;
using Domain.Entities;
using MassTransit;
using MassTransit.MongoDbIntegration;

namespace Application.Messaging;

public static class BusTopologyExtensions
{
    public static IBusRegistrationConfigurator AddAllConsumers(
        this IBusRegistrationConfigurator x,
        Action<IMongoDbSagaRepositoryConfigurator>? configureSagaRepository = null,
        Type? ownerConsumerType = null)
    {
        x.AddBusMetadataExplorer();

        AddConsumerWithOwnership<InventoryConsumer, InventoryConsumerDefinition>(x, ownerConsumerType);
        AddConsumerWithOwnership<PaymentConsumer, PaymentConsumerDefinition>(x, ownerConsumerType);
        AddConsumerWithOwnership<NotificationConsumer, NotificationConsumerDefinition>(x, ownerConsumerType);

        var sagaRegistration =
            x.AddSagaStateMachine<OrderStateMachine, OrderSagaState, OrderSagaDefinition>();

        if (configureSagaRepository is not null)
            sagaRegistration.MongoDbRepository(configureSagaRepository);
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
            return;

        registration.ExcludeFromConfigureEndpoints();
    }
}