using Application;
using Application.Messaging.Command;
using Application.Messaging.Events;
using Hangfire;
using Hangfire.Redis.StackExchange;
using Infrastructure;
using Infrastructure.Persistence;
using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PaymentService;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
    optional: false, reloadOnChange: true);

// Serilog config lives entirely in appsettings.json ("Serilog" section).
builder.Services.AddSerilog(cfg => cfg.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

var rmqOptions = builder.Configuration.GetSection("RabbitMQ").Get<RabbitMqOptions>()!;

// Hangfire backs MassTransit's delayed-message scheduler (redelivery) via Redis
// storage instead of the RabbitMQ delayed-exchange plugin — avoids depending
// on rabbitmq_delayed_message_exchange being installed.
var redisOptions = builder.Configuration.GetSection("Redis").Get<RedisOptions>()!;
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseRedisStorage(redisOptions.ConnectionString));

var hangfireOptions = builder.Configuration.GetSection("Hangfire").Get<HangfireOptions>() ?? new HangfireOptions();
builder.Services.AddHangfireServer(opts =>
{
    opts.WorkerCount = hangfireOptions.WorkerCount;
    opts.Queues = hangfireOptions.Queues;
    opts.ServerTimeout = hangfireOptions.ServerTimeout;
    opts.ServerCheckInterval = hangfireOptions.ServerCheckInterval;
});

builder.Services.AddMassTransit(x =>
{
    // EF Core Outbox — writes OutboxMessage row in same DbContext/transaction as
    // any publish from here, and UseEntityFrameworkOutbox below on the receive
    // endpoint adds inbox-based idempotent consumption for PaymentConsumer.
    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UseSqlServer();
        o.QueryMessageLimit = rmqOptions.QueryMessageLimit;
        o.QueryDelay = TimeSpan.FromSeconds(rmqOptions.QueryDelaySeconds);
        o.UseBusOutbox(bo =>
        {
            bo.MessageDeliveryLimit = rmqOptions.MessageDeliveryLimit;
            bo.MessageDeliveryTimeout = TimeSpan.FromSeconds(rmqOptions.MessageDeliveryTimeoutSeconds);
        });
    });

    x.AddConsumer<PaymentConsumer>();

    // Registers IMessageScheduler in the container + the Hangfire consumers
    // that turn schedule/unschedule commands into Hangfire jobs.
    x.AddPublishMessageScheduler();
    x.AddHangfireConsumers();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        var rmq = ctx.GetRequiredService<RabbitMqOptions>();
        cfg.Host(rmq.Host, rmq.VirtualHost, h =>
        {
            h.Username(rmq.Username);
            h.Password(rmq.Password);

            // A dead/starved connection otherwise blocks Publish/Send indefinitely
            // (no default timeout) — a Hangfire-scheduled job's worker thread stays
            // stuck "Processing" forever instead of throwing and letting Hangfire's
            // AutomaticRetry recover it. Heartbeat detects the dead connection and
            // tears it down so pending operations fail fast instead of hanging.
            h.Heartbeat(TimeSpan.FromSeconds(10));
            h.RequestedConnectionTimeout(TimeSpan.FromSeconds(15));
        });

        cfg.UseNewtonsoftJsonSerializer();
        cfg.UseNewtonsoftJsonDeserializer();

        // Required for UseDelayedRedelivery below — routes scheduled messages
        // through the registered Hangfire (Redis-backed) scheduler instead of
        // the RabbitMQ delayed-exchange plugin.
        cfg.UsePublishMessageScheduler();

        // Manual endpoint — Payment Service owns this queue
        cfg.ReceiveEndpoint("payment-queue", e =>
        {
            e.Durable = true;
            e.AutoDelete = false;
            e.PrefetchCount = rmq.PrefetchCount;
            e.ConcurrentMessageLimit = rmq.ConcurrentMessageLimit;

            // Per-instance throughput cap (rmq.RateLimit msgs/sec on this endpoint).
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

            // Innermost of the two: scheduled redelivery of consumer failures. Placed
            // before (inside) the circuit breaker so breaker stats only see messages
            // that exhausted redelivery, not routine scheduled-retry churn — otherwise
            // a stretch of transient failures trips the breaker via retry noise instead
            // of genuine sustained failure.
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
            // Keeps messages for the same order (CorrelationId) processed in order,
            // even though ConcurrentMessageLimit allows 16 messages in parallel.
            var partitioner = e.CreatePartitioner(e.ConcurrentMessageLimit!.Value);
            e.UsePartitioner<ProcessPayment>(partitioner, m => m.Message.CorrelationId);

            // Inbox — dedupes redelivered messages via InboxState, and defers
            // outgoing publishes from the consumer until its DbContext commits.
            e.UseEntityFrameworkOutbox<AppDbContext>(ctx, o =>
            {
                o.MessageDeliveryLimit = rmqOptions.MessageDeliveryLimit;
                o.MessageDeliveryTimeout = TimeSpan.FromSeconds(rmqOptions.MessageDeliveryTimeoutSeconds);
            });

            // ✅ Consumer — always last
            e.ConfigureConsumer<PaymentConsumer>(ctx);
        });

        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
