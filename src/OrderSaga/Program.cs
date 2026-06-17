
using Application;
using Application.Messaging;
using Infrastructure;
using MassTransit;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

builder.Services.AddMassTransit(x =>
{
    // OrderService Program.cs — owns the saga
    x.AddAllConsumers(
    configureSagaRepository: r =>
    {
        r.Connection = builder.Configuration["MongoDb:ConnectionString"];
        r.DatabaseName = "OrderSagaDb";
        r.CollectionName = "OrderSagas";
    });

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


        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
