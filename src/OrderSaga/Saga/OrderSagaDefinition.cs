using Application.Messaging.Command;
using Application.Messaging.Events;
using Infrastructure;
using Infrastructure.Persistence;
using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace OrderSaga.Saga
{
    public class OrderSagaDefinition(RabbitMqOptions rabbitMqOptions) : SagaDefinition<OrderSagaState>
    {
        protected override void ConfigureSaga(
            IReceiveEndpointConfigurator endpointConfigurator,
            ISagaConfigurator<OrderSagaState> sagaConfigurator,
            IRegistrationContext context)
        {

            // Broker-level buffer: how many unacked messages RabbitMQ will push at once.
            // Kept at 2x ConcurrentMessageLimit, same ratio as the other 3 services.
            endpointConfigurator.PrefetchCount = rabbitMqOptions.PrefetchCount;

            // In-process concurrency: how many messages MassTransit processes simultaneously.
            // Lower than the other 3 services (64) — this endpoint repeatedly mutates shared
            // per-order saga state (SQL Server optimistic-concurrency writes), so fewer concurrent
            // touches means less contention to retry through under heavy load.
            endpointConfigurator.ConcurrentMessageLimit = rabbitMqOptions.ConcurrentMessageLimit;

            // Per-instance throughput cap (rabbitMqOptions.RateLimit msgs/sec on this endpoint).
            // Scales linearly with instance count: N instances = N * RateLimit cluster-wide.
            endpointConfigurator.UseRateLimit(rabbitMqOptions.RateLimit, TimeSpan.FromSeconds(1));

            if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbitMqEndpointConfigurator)
            {
                rabbitMqEndpointConfigurator.Durable = true;
                rabbitMqEndpointConfigurator.AutoDelete = false;
            }
            // Outer policy — added first, so it wraps everything below: exponential
            // retry for real faults that survive the inner concurrency-specific retry.
            endpointConfigurator.UseMessageRetry(r =>
            {
                r.Exponential(
                    retryLimit: 5,
                    minInterval: TimeSpan.FromSeconds(1),
                    maxInterval: TimeSpan.FromMinutes(1),
                    intervalDelta: TimeSpan.FromSeconds(5));
            });

            // Inner policy — added second, so it runs closer to the consumer (before the
            // outer exponential policy sees the exception): both EF Core optimistic-concurrency
            // conflicts and SQL Server deadlock victims (error 1205, wrapped as DbUpdateException
            // when EF's save fails) are transient — two messages racing to update the same saga
            // row — and resolve almost instantly, so intercept and retry fast here instead of
            // falling into the slower exponential policy.
            endpointConfigurator.UseMessageRetry(retry =>
            {
                // SQL Server deadlock victim
                retry.Handle<SqlException>(ex => ex.Number == 1205);

                // SQL command timeout
                retry.Handle<SqlException>(ex => ex.Number == -2);

                // Lock request timeout
                retry.Handle<SqlException>(ex => ex.Number == 1222);

                // Could not acquire required database resources
                retry.Handle<SqlException>(ex => ex.Number == 1204);

                // EF optimistic concurrency conflict
                retry.Handle<DbUpdateConcurrencyException>();

                // Some EF operations wrap SqlException inside DbUpdateException
                retry.Handle<DbUpdateException>(ex =>
                    ex.InnerException is SqlException sql &&
                    sql.Number is 1204 or 1205 or 1222 or -2);


                retry.Exponential(
                        retryLimit: 5,
                        minInterval: TimeSpan.FromMilliseconds(200),
                        maxInterval: TimeSpan.FromSeconds(5),
                        intervalDelta: TimeSpan.FromMilliseconds(200));
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
            // Applied per-message-type below (not a blanket sagaConfigurator.UsePartitioner)
            // so OrderCreated can key on Order.Id (no CorrelationId exists yet at that point)
            // while the later events key on the saga's own CorrelationId.
            var partitioner =
                endpointConfigurator.CreatePartitioner(endpointConfigurator.ConcurrentMessageLimit!.Value);
            
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

            sagaConfigurator.Message<EmailNotificationSent>(x =>
            {
                x.UsePartitioner(
                    partitioner,
                    context => context.Message.CorrelationId);
            });

            sagaConfigurator.Message<SmsNotificationSent>(x =>
            {
                x.UsePartitioner(
                    partitioner,
                    context => context.Message.CorrelationId);
            });

            // InventoryRetry's scheduled fire is consumed as CheckInventory (its Schedule
            // message type) — partition it too, or a retry firing can race an in-flight
            // InventoryChecked reply for the same saga and hit avoidable EF concurrency conflicts.
            sagaConfigurator.Message<CheckInventory>(x =>
            {
                x.UsePartitioner(
                    partitioner,
                    context => context.Message.CorrelationId);
            });
        }
    }
}
