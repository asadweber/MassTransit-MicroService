using Contracts;
using Contracts.Saga;
using Db.Repository;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddMassTransit(x =>
{
    x.AddAllConsumers(); // full topology metadata (saga excluded from endpoints)

    x.AddSagaStateMachine<OrderStateMachine, OrderSagaState, OrderSagaDefinition>() // re-register: this service owns the saga queue
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
