using Application;
using Application.Messaging;
using Infrastructure;
using InventoryService;
using MassTransit;
using MongoDB.Driver;


var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

var mongoSection = builder.Configuration.GetSection("MongoDb");

builder.Services.AddSingleton<IMongoClient>(_ =>
    new MongoClient(mongoSection["ConnectionString"]));


builder.Services.AddMassTransit(x =>
{
    x.AddBusMetadataExplorer();
    
    x.AddConsumer<InventoryConsumer, InventoryConsumerDefinition>();

    x.AddMongoDbOutbox(o =>
    {
        o.ClientFactory(provider =>
            provider.GetRequiredService<IMongoClient>());

        o.DatabaseFactory(provider =>
            provider.GetRequiredService<IMongoClient>()
                .GetDatabase(mongoSection["DatabaseName"]));

        o.QueryDelay = TimeSpan.FromSeconds(1);

        o.UseBusOutbox();
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

            // ✅ Consumer — always last
            e.ConfigureConsumer<InventoryConsumer>(ctx);
        });


        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
