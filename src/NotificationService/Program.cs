using Application;
using Application.Messaging;
using Infrastructure;
using MassTransit;
using MongoDB.Driver;
using NotificationService;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

var mongoSection = builder.Configuration.GetSection("MongoDb");

builder.Services.AddSingleton<IMongoClient>(_ =>
    new MongoClient(mongoSection["ConnectionString"]));


builder.Services.AddMassTransit(x =>
{
    x.AddBusMetadataExplorer();
    x.AddConsumer<NotificationConsumer, NotificationConsumerDefinition>();

    
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
        cfg.ReceiveEndpoint("notification-queue", e =>
        {
            e.Durable = true;
            e.AutoDelete = false;
            e.PrefetchCount = 16;
            e.ConcurrentMessageLimit = 8;

            // ✅ Retry — outermost, wraps everything
            e.UseMessageRetry(r =>
                r.Intervals(
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(15),
                    TimeSpan.FromSeconds(30)
                ));

            // ✅ Consumer — always last
            e.ConfigureConsumer<NotificationConsumer>(ctx);
        });

        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
