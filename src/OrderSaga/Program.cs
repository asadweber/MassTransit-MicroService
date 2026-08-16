using Application;
using Infrastructure;
using Infrastructure.Persistence;
using MassTransit;
using OrderSaga.Saga;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
    optional: false, reloadOnChange: true);


// Serilog config lives entirely in appsettings.json ("Serilog" section).
builder.Services.AddSerilog(cfg => cfg.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

builder.Services.AddMassTransit(x =>
{
    x.AddBusMetadataExplorer();

    x.AddSagaStateMachine<OrderStateMachine, OrderSagaState, OrderSagaDefinition>()
        .EntityFrameworkRepository(r =>
        {
            r.ExistingDbContext<AppDbContext>();
            r.ConcurrencyMode = ConcurrencyMode.Optimistic;
        });


    x.UsingRabbitMq((ctx, cfg) =>
    {
        var rmq = ctx.GetRequiredService<RabbitMqOptions>();

        cfg.Host(rmq.Host, rmq.VirtualHost, h =>
        {
            h.Username(rmq.Username);
            h.Password(rmq.Password);
        });

        cfg.UseNewtonsoftJsonSerializer();
        cfg.UseNewtonsoftJsonDeserializer();

        // Required for UseDelayedRedelivery below — schedules redelivery via the
        // RabbitMQ delayed-exchange plugin (rabbitmq_delayed_message_exchange).
        cfg.UseDelayedMessageScheduler();

        // Caps saga consumer throughput at 100 messages/sec across its auto-generated endpoint.
        cfg.UseRateLimit(400, TimeSpan.FromSeconds(1));

        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();

//Ensure Serilog TTL index exists (or recreate if retention period changed)
SerilogRetentionSetup.EnsureSerilogTtlIndex(builder.Configuration, retentionDays: 1);

host.Run();