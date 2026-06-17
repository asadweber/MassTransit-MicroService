using Contracts;
using Contracts.Consumers;
using MassTransit;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMassTransit(x =>
{
    x.AddAllConsumers();                  // full topology metadata (all consumers excluded from endpoints)
    //x.AddConsumer<NotificationConsumer>(); // re-register: this service owns this queue

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

        cfg.ReceiveEndpoint("notification-queue", e =>
        {
            //1️ Queue/exchange properties
            e.Durable = true;
            e.AutoDelete = false;
            e.PrefetchCount = 16;
            e.ConcurrentMessageLimit = 8;

            //e.UseInMemoryOutbox(ctx);

            //2.Consumer last
            e.ConfigureConsumer<NotificationConsumer>(ctx);
        });

        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
