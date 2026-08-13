using Infrastructure.Persistence;
using MassTransit;
using MassTransit.MongoDbIntegration.Saga;
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
            endpointConfigurator.PrefetchCount = 128;

            // In-process concurrency: how many messages MassTransit processes simultaneously
            endpointConfigurator.ConcurrentMessageLimit = 64;

            
            if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbitMqEndpointConfigurator)
            {
                rabbitMqEndpointConfigurator.Durable = true;
                rabbitMqEndpointConfigurator.AutoDelete = false;
            }
            // Mongo optimistic-concurrency conflicts (two messages racing to update the same
            // saga doc) are expected under load and resolve almost instantly — retry fast
            // instead of waiting on the slower fault-retry policy below.
            endpointConfigurator.UseMessageRetry(r =>
            {
                r.Handle<MongoDbConcurrencyException>();
                r.Interval(5, TimeSpan.FromMilliseconds(50));
            });

            endpointConfigurator.UseMessageRetry(r =>
            {
                r.Exponential(
                    retryLimit: 5,
                    minInterval: TimeSpan.FromSeconds(1),
                    maxInterval: TimeSpan.FromMinutes(1),
                    intervalDelta: TimeSpan.FromSeconds(5));
            });

            //endpointConfigurator.UseDelayedRedelivery(r =>
            //{
            //    r.Intervals(
            //        TimeSpan.FromMinutes(5),
            //        TimeSpan.FromMinutes(10),
            //        TimeSpan.FromMinutes(30),
            //        TimeSpan.FromHours(1),
            //        TimeSpan.FromHours(6),
            //        TimeSpan.FromHours(12),
            //        TimeSpan.FromDays(1),
            //        TimeSpan.FromDays(3),
            //        TimeSpan.FromDays(7));
            //});

            // Ensures messages for the same saga (CorrelationId) are processed in order,
            // even though ConcurrentMessageLimit allows multiple sagas in parallel.
            sagaConfigurator.UsePartitioner(endpointConfigurator.ConcurrentMessageLimit ?? 8, x => x.Saga.CorrelationId);
        }
    }
}
