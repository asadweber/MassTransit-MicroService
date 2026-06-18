using Application;
using Application.Messaging;
using Infrastructure;
using Infrastructure.Persistence;
using InventoryService;
using MassTransit;


var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();


builder.Services.AddMassTransit(x =>
{
    x.AddBusMetadataExplorer();

    x.AddConsumer<InventoryConsumer, InventoryConsumerDefinition>();

    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UseSqlServer();
        o.QueryDelay = TimeSpan.FromSeconds(1);

        o.UseBusOutbox(b =>
        {
            b.MessageDeliveryLimit = 100;
            b.MessageDeliveryTimeout = TimeSpan.FromSeconds(10);
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

        cfg.UseNewtonsoftJsonSerializer();
        cfg.UseNewtonsoftJsonDeserializer();

        // ✅ Manual endpoint — Inventory Service owns this queue
        cfg.ReceiveEndpoint("inventory-queue", e =>
        {
            e.Durable = true;
            e.AutoDelete = false;
            e.PrefetchCount = 64;
            e.ConcurrentMessageLimit = 32;

            // ✅ Retry — Wait time increases exponentially.
            // Fast retries for temporary failures
            e.UseMessageRetry(r =>
            {
                r.Exponential(
                    retryLimit: 5,
                    minInterval: TimeSpan.FromSeconds(1),
                    maxInterval: TimeSpan.FromMinutes(1),
                    intervalDelta: TimeSpan.FromSeconds(5));
            });

            // Long-term retries
            e.UseDelayedRedelivery(r =>
            {
                r.Intervals(
                    TimeSpan.FromHours(1),
                    TimeSpan.FromHours(6),
                    TimeSpan.FromHours(12),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3),
                    TimeSpan.FromDays(7));
            });

            // ✅ EF Core outbox — atomic with the DB transaction
            e.UseEntityFrameworkOutbox<AppDbContext>(ctx);

            // ✅ Consumer — always last
            e.ConfigureConsumer<InventoryConsumer>(ctx);
        });


        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
