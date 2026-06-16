using Contracts;
using Db.Repository;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderSaga;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddMassTransit(x =>
{
    x.AddAllConsumers(); // full topology metadata

    x.AddSagaStateMachine<OrderStateMachine, OrderSagaState>()
     .EntityFrameworkRepository(r =>
     {
         r.ConcurrencyMode = ConcurrencyMode.Optimistic;
         r.ExistingDbContext<AppDbContext>();
         r.UseSqlServer();
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
