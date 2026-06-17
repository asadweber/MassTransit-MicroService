using Contracts;
using Contracts.Consumers;
using Db.Repository;
using MassTransit;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMassTransit(x =>
{
    x.AddAllConsumers();            // full topology metadata (all consumers excluded from endpoints)
    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UseSqlServer();
        o.UseBusOutbox();
        o.QueryDelay = TimeSpan.FromSeconds(1);
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

            // ✅ Outbox — inner, atomic with DB transaction
            e.UseEntityFrameworkOutbox<AppDbContext>(ctx);

            // ✅ Consumer — always last
            e.ConfigureConsumer<PaymentConsumer>(ctx);
        });

        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
