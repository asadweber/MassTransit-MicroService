using Db.Repository;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderSaga;
using OrderSaga.Consumers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddMassTransit(x =>
{
    x.AddSagaStateMachine<OrderStateMachine, OrderSagaState>()
     .EntityFrameworkRepository(r =>
     {
         r.ConcurrencyMode = ConcurrencyMode.Optimistic;
         r.ExistingDbContext<AppDbContext>();
         r.UseSqlServer();
     });

    x.AddConsumer<InventoryConsumer>();
    x.AddConsumer<PaymentConsumer>();
    x.AddConsumer<NotificationConsumer>();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        var rmq = builder.Configuration.GetSection("RabbitMQ");
        cfg.Host(rmq["Host"], rmq["VirtualHost"], h =>
        {
            h.Username(rmq["Username"]!);
            h.Password(rmq["Password"]!);
        });

        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
