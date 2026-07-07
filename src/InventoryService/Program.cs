using Application;              // AddApplication DI extension
using Infrastructure;           // AddInfrastructure DI extension
using Infrastructure.Persistence; // AppDbContext (EF Core, used by outbox)
using InventoryService;         // InventoryConsumer
using MassTransit;              // bus, outbox, retry, RabbitMQ transport


// Worker host — no HTTP surface, just the bus.
var builder = Host.CreateApplicationBuilder(args);

// Registers DbContext + repositories (needed by the EF outbox below).
builder.Services.AddInfrastructure(builder.Configuration);
// Registers application-layer services (IOrderService, AutoMapper, etc.).
builder.Services.AddApplication();


builder.Services.AddMassTransit(x =>
{
    // Exposes bus/consumer metadata so WebApp's dashboard can show it.
    x.AddBusMetadataExplorer();

    // This service owns InventoryConsumer (registered in every service via
    // AddAllConsumers, but only this one wires it to a real queue below).
    x.AddConsumer<InventoryConsumer>();

    // Transactional outbox: publishes only take effect once the enclosing
    // DB transaction commits, keeping DB writes and message sends atomic.
    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UseSqlServer();                       // outbox table lives in SQL Server
        o.QueryDelay = TimeSpan.FromSeconds(1); // how often the outbox is polled for pending messages

        o.UseBusOutbox(b =>
        {
            b.MessageDeliveryLimit = 100;                      // max messages delivered per outbox pass
            b.MessageDeliveryTimeout = TimeSpan.FromSeconds(10); // per-delivery timeout
        });
    });

    x.UsingRabbitMq((ctx, cfg) =>
    {
        var rmq = builder.Configuration.GetSection("RabbitMQ");
        cfg.Host(rmq["Host"], rmq["VirtualHost"], h =>
        {
            h.Username(rmq["Username"]!);
            h.Password(rmq["Password"]!);
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
            e.PrefetchCount = 64;           // messages fetched per consumer before ack
            e.ConcurrentMessageLimit = 32;  // max messages processed in parallel

            // Fast retries for transient failures (5 attempts, 1s-1m exponential backoff).
            e.UseMessageRetry(r =>
            {
                r.Exponential(
                    retryLimit: 5,
                    minInterval: TimeSpan.FromSeconds(1),
                    maxInterval: TimeSpan.FromMinutes(1),
                    intervalDelta: TimeSpan.FromSeconds(5));
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

            // EF Core outbox — atomic with the DB transaction.
            e.UseEntityFrameworkOutbox<AppDbContext>(ctx);

            // Consumer — always configured last, innermost in the pipeline.
            e.ConfigureConsumer<InventoryConsumer>(ctx);
        });

        // Registers endpoints for all other consumers/saga too (they're excluded
        // via ExcludeFromConfigureEndpoints in AddAllConsumers) so the dashboard
        // still sees the full message topology across services.
        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
