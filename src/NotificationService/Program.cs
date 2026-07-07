using Application;
using Application.Messaging;
using Infrastructure;
using Infrastructure.Persistence;
using MassTransit;
using NotificationService;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();


builder.Services.AddMassTransit(x =>
{
    x.AddBusMetadataExplorer();
    x.AddConsumer<NotificationConsumer>();

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

            // ✅ EF Core outbox — atomic with the DB transaction
            e.UseEntityFrameworkOutbox<AppDbContext>(ctx);

            // ✅ Consumer — always last
            e.ConfigureConsumer<NotificationConsumer>(ctx);
        });

        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
