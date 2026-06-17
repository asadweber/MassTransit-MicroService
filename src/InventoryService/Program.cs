using Contracts;
using Contracts.Consumers;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMassTransit(x =>
{
    x.AddAllConsumers();             // full topology metadata (all consumers excluded from endpoints)
    //x.AddConsumer<InventoryConsumer, InventoryConsumerDefinition>(); // re-register: this service owns this queue

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

        // ✅ Manually declare the endpoint so this service "owns" the queue
        cfg.ReceiveEndpoint("inventory-queue", e =>
        {
            //1️ Queue/exchange properties
            e.Durable = true;
            e.AutoDelete = false;
            e.PrefetchCount = 16;
            e.ConcurrentMessageLimit = 8;

            //e.UseInMemoryOutbox(ctx);

            // 3️⃣ Consumer last
            e.ConfigureConsumer<InventoryConsumer>(ctx);
        });


        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
