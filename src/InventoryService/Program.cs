using Contracts;
using Contracts.Consumers;
using MassTransit;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMassTransit(x =>
{
    x.AddAllConsumers();             // full topology metadata (all consumers excluded from endpoints)
    x.AddConsumer<InventoryConsumer>(); // re-register: this service owns this queue

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

        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
