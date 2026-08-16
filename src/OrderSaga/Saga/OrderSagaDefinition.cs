using Application.Messaging.Events;
using Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
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

            // Broker-level buffer: how many unacked messages RabbitMQ will push at once.
            // Kept at 2x ConcurrentMessageLimit, same ratio as the other 3 services.
            endpointConfigurator.PrefetchCount = 32;

            // In-process concurrency: how many messages MassTransit processes simultaneously.
            // Lower than the other 3 services (64) — this endpoint repeatedly mutates shared
            // per-order saga state (SQL Server optimistic-concurrency writes), so fewer concurrent
            // touches means less contention to retry through under heavy load.
            endpointConfigurator.ConcurrentMessageLimit = 16;

            
            if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbitMqEndpointConfigurator)
            {
                rabbitMqEndpointConfigurator.Durable = true;
                rabbitMqEndpointConfigurator.AutoDelete = false;
            }
            // Inner policy — runs first: exponential retry for real faults.
            endpointConfigurator.UseMessageRetry(r =>
            {
                r.Exponential(
                    retryLimit: 5,
                    minInterval: TimeSpan.FromSeconds(1),
                    maxInterval: TimeSpan.FromMinutes(1),
                    intervalDelta: TimeSpan.FromSeconds(5));
            });

            // Outer policy — runs before the one above: EF Core optimistic-concurrency
            // conflicts (two messages racing to update the same saga row) are expected
            // under load and resolve almost instantly, so intercept and retry fast here
            // instead of falling into the slower exponential policy.
            endpointConfigurator.UseMessageRetry(r =>
            {
                r.Handle<DbUpdateConcurrencyException>();
                r.Interval(10, TimeSpan.FromMilliseconds(100));
            });

            // Trips after sustained failure (15% of a rolling 1-min window, min 10 attempts
            // evaluated) so a struggling downstream dependency doesn't get hammered further —
            // rejects fast instead of queuing more retries. Half-open probe after 5 min.
            endpointConfigurator.UseCircuitBreaker(cb =>
            {
                cb.TrackingPeriod = TimeSpan.FromMinutes(1);
                cb.TripThreshold = 15;
                cb.ActiveThreshold = 10;
                cb.ResetInterval = TimeSpan.FromMinutes(5);
            });

            endpointConfigurator.UseDelayedRedelivery(r =>
            {
                r.Intervals(
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(10),
                    TimeSpan.FromMinutes(30),
                    TimeSpan.FromHours(1),
                    TimeSpan.FromHours(6),
                    TimeSpan.FromHours(12),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3),
                    TimeSpan.FromDays(7));
            });

            // Ensures messages for the same saga (CorrelationId) are processed in order,
            // even though ConcurrentMessageLimit allows multiple sagas in parallel.
            sagaConfigurator.UsePartitioner(endpointConfigurator.ConcurrentMessageLimit ?? 16, x => x.Saga.CorrelationId);
            
            // Same partitioner for all Order saga messages
            var partitioner =
                endpointConfigurator.CreatePartitioner(16);
            
            sagaConfigurator.Message<OrderCreated>(x =>
            {
                x.UsePartitioner(
                    partitioner,
                    context => context.Message.Order.Id);
            });

            sagaConfigurator.Message<InventoryChecked>(x =>
            {
                x.UsePartitioner(
                    partitioner,
                    context => context.Message.CorrelationId);
            });

            sagaConfigurator.Message<PaymentProcessed>(x =>
            {
                x.UsePartitioner(
                    partitioner,
                    context => context.Message.CorrelationId);
            });
        }
    }
}
