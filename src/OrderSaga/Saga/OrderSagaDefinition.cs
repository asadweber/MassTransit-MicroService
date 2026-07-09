using Infrastructure.Persistence;
using MassTransit;
using System;
using System.Collections.Generic;
using System.Text;

namespace OrderSaga.Saga
{
    public class OrderSagaDefinition : SagaDefinition<OrderSagaState>
    {
        protected override void ConfigureSaga(
            IReceiveEndpointConfigurator endpointConfigurator,
            ISagaConfigurator<OrderSagaState> sagaConfigurator,
            IRegistrationContext context)
        {

            // Broker-level buffer: how many unacked messages RabbitMQ will push at once
            endpointConfigurator.PrefetchCount = 32;

            // In-process concurrency: how many messages MassTransit processes simultaneously
            endpointConfigurator.ConcurrentMessageLimit = 8;

            
            if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbitMqEndpointConfigurator)
            {
                rabbitMqEndpointConfigurator.Durable = true;
                rabbitMqEndpointConfigurator.AutoDelete = false;
            }
            endpointConfigurator.UseMessageRetry(r =>
            {
                r.Exponential(
                    retryLimit: 5,
                    minInterval: TimeSpan.FromSeconds(1),
                    maxInterval: TimeSpan.FromMinutes(1),
                    intervalDelta: TimeSpan.FromSeconds(5));
            });

            // Ensures messages for the same saga (CorrelationId) are processed in order,
            // even though ConcurrentMessageLimit allows multiple sagas in parallel.
            sagaConfigurator.UsePartitioner(endpointConfigurator.ConcurrentMessageLimit ?? 8, x => x.Saga.CorrelationId);
        }
    }
}
