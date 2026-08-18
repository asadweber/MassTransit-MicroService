using Application;              // AddApplication DI extension
using Application.Messaging.Command;   // CheckInventory
using Infrastructure;           // AddInfrastructure DI extension
using Infrastructure.Persistence; // AppDbContext (needed by EF outbox)
using InventoryService;         // InventoryConsumer
using MassTransit;              // bus, outbox, retry, RabbitMQ transport
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Serilog;


// Worker host — no HTTP surface, just the bus.
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
    optional: false, reloadOnChange: true);

// Serilog config lives entirely in appsettings.json ("Serilog" section).
builder.Services.AddSerilog(cfg => cfg.ReadFrom.Configuration(builder.Configuration));

// Registers DbContext + repositories (needed by the EF outbox below).
builder.Services.AddInfrastructure(builder.Configuration);
// Registers application-layer services (IOrderService, AutoMapper, etc.).
builder.Services.AddApplication();

var rmqOptions = builder.Configuration.GetSection("RabbitMQ").Get<RabbitMqOptions>()!;

builder.Services.AddMassTransit(x =>
{
    // EF Core Outbox — writes OutboxMessage row in same DbContext/transaction as any publish from here
    //x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    //{
    //    o.UseSqlServer();
    //    o.QueryMessageLimit = rmqOptions.QueryMessageLimit;
    //    o.QueryDelay = TimeSpan.FromSeconds(rmqOptions.QueryDelaySeconds);

    //    o.UseBusOutbox(bo =>
    //    {
    //        bo.MessageDeliveryLimit = rmqOptions.MessageDeliveryLimit;
    //        bo.MessageDeliveryTimeout = TimeSpan.FromSeconds(rmqOptions.MessageDeliveryTimeoutSeconds);
    //    });
    //});

    // This service owns InventoryConsumer (registered in every service via
    x.AddConsumer<InventoryConsumer>();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        var rmq = ctx.GetRequiredService<RabbitMqOptions>();
        cfg.Host(rmq.Host, rmq.VirtualHost, h =>
        {
            h.Username(rmq.Username);
            h.Password(rmq.Password);
        });

        // Use Newtonsoft (not default System.Text.Json) for message (de)serialization.
        cfg.UseNewtonsoftJsonSerializer();
        cfg.UseNewtonsoftJsonDeserializer();

        // Required for UseDelayedRedelivery below — schedules redelivery via the
        // RabbitMQ delayed-exchange plugin (rabbitmq_delayed_message_exchange).
        cfg.UseDelayedMessageScheduler();

        // Manual endpoint — Inventory Service owns this queue.
        cfg.ReceiveEndpoint("inventory-queue", e =>
        {
            e.Durable = true;               // queue survives broker restart
            e.AutoDelete = false;           // keep queue when no consumers connected
            e.PrefetchCount = rmq.PrefetchCount;                 // messages fetched per consumer before ack
            e.ConcurrentMessageLimit = rmq.ConcurrentMessageLimit; // max messages processed in parallel

            // Caps consumer throughput at 400 messages/sec for this endpoint.
            e.UseRateLimit(rmq.RateLimit, TimeSpan.FromSeconds(1));

            // Outer policy — added first, so it wraps everything below: exponential
            // retry for real faults that survive the inner concurrency-specific retry.
            e.UseMessageRetry(r =>
            {
                r.Exponential(
                    retryLimit: 5,
                    minInterval: TimeSpan.FromSeconds(1),
                    maxInterval: TimeSpan.FromMinutes(1),
                    intervalDelta: TimeSpan.FromSeconds(5));
            });

            // Inner policy — added second, so it runs closer to the consumer (before the
            // outer exponential policy sees the exception): EF Core optimistic-concurrency
            // conflicts are expected under load and resolve almost instantly, so intercept
            // and retry fast here instead of falling into the slower exponential policy.
            e.UseMessageRetry(retry =>
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
            e.UseCircuitBreaker(cb =>
            {
                cb.TrackingPeriod = TimeSpan.FromMinutes(1);
                cb.TripThreshold = 15;
                cb.ActiveThreshold = 10;
                cb.ResetInterval = TimeSpan.FromMinutes(5);
            });

            e.UseDelayedRedelivery(r =>
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

            // Keeps messages for the same inventory correlation processed in order,
            // even though ConcurrentMessageLimit allows 16 messages in parallel.
            // NOTE: this only protects CheckInventory. If InventoryConsumer also
            // handles other message types on this endpoint (e.g. ReleaseInventory,
            // AdjustStock) that can mutate the same inventory row, add a partitioner
            // for each of those types too — otherwise those messages get zero
            // serialization protection against concurrent mutation of the same item.
            var partitioner = e.CreatePartitioner(e.ConcurrentMessageLimit!.Value);
            e.UsePartitioner<CheckInventory>(partitioner, m => m.Message.CorrelationId);

            // Consumer — always configured last, innermost in the pipeline.
            e.ConfigureConsumer<InventoryConsumer>(ctx);
        });

        // Registers endpoints for all other consumers/saga too (they're excluded
        // via ExcludeFromConfigureEndpoints in AddAllConsumers) so the dashboard
        // still sees the full message topology across services.
        //
        // IMPORTANT: before shipping, verify InventoryConsumer is actually excluded
        // here — e.g. add a startup assertion or integration test confirming only
        // ONE queue in RabbitMQ is bound to CheckInventory. If the exclusion is ever
        // missed, MassTransit will create a second auto-named queue bound to the
        // same message type, and RabbitMQ will deliver every CheckInventory message
        // to both queues — double-processing with none of the retry/redelivery/
        // partitioner protection configured above.
        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
