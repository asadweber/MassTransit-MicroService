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

        // Required for UseDelayedRedelivery below — schedules redelivery via the
        // RabbitMQ delayed-exchange plugin (rabbitmq_delayed_message_exchange).
        cfg.UseDelayedMessageScheduler();


        // ✅ Manual endpoint — Inventory Service owns this queue
        cfg.ReceiveEndpoint("notification-queue", e =>
        {
            e.Durable = true;
            e.AutoDelete = false;
            e.PrefetchCount = 16;
            e.ConcurrentMessageLimit = 8;

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
