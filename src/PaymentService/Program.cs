using Application;
using Application.Messaging;
using Application.Messaging.Consumers;
using Infrastructure;
using MassTransit;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

builder.Services.AddMassTransit(x =>
{
    // PaymentService Program.cs
    x.AddAllConsumers(ownerConsumerType: typeof(PaymentConsumer));

    x.AddMongoDbOutbox(o =>
    {
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
        cfg.ReceiveEndpoint("payment-queue", e =>
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
            e.ConfigureConsumer<PaymentConsumer>(ctx);
        });

        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
