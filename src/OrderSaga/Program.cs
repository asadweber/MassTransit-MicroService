using Application;
using Infrastructure;
using Infrastructure.Persistence;
using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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

var rmqOptions = builder.Configuration.GetSection("RabbitMQ").Get<RabbitMqOptions>()!;

builder.Services.AddMassTransit(x =>
{
    // EF Core Outbox — writes OutboxMessage row in same DbContext/transaction as any publish from here
    //x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    //{
    //    o.UseSqlServer();
    //    o.DisableInboxCleanupService();
    //    o.QueryMessageLimit = rmqOptions.QueryMessageLimit;
    //    o.QueryDelay = TimeSpan.FromSeconds(rmqOptions.QueryDelaySeconds);

    //    o.UseBusOutbox(bo =>
    //    {
    //        bo.MessageDeliveryLimit = rmqOptions.MessageDeliveryLimit;
    //        bo.MessageDeliveryTimeout = TimeSpan.FromSeconds(rmqOptions.MessageDeliveryTimeoutSeconds);
    //    });

    //});

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
        cfg.UseRateLimit(rmq.RateLimit, TimeSpan.FromSeconds(1));

        // Notification fan-out (Email/SMS/Paci/Notification) publishes up to 4
        // OrderConfirmedCompleted events for the same saga near-simultaneously.
        // With ConcurrencyMode.Optimistic, concurrent saga updates race on
        // RowVersion — retry so a losing update reloads and reapplies instead
        // of faulting and silently dropping the fan-out flag / Finalize call.
        cfg.UseMessageRetry(retry =>
        {
            // SQL Server deadlock victim
            retry.Handle<SqlException>(ex => ex.Number == 1205);

            // SQL command timeout
            retry.Handle<SqlException>(ex => ex.Number == -2);

            // Lock request timeout
            retry.Handle<SqlException>(ex => ex.Number == 1222);

            // Could not acquire required database resources
            retry.Handle<SqlException>(ex => ex.Number == 1204);

            // EF optimistic concurrency conflict
            retry.Handle<DbUpdateConcurrencyException>();

            // Some EF operations wrap SqlException inside DbUpdateException
            retry.Handle<DbUpdateException>(ex =>
                ex.InnerException is SqlException sql &&
                sql.Number is 1204 or 1205 or 1222 or -2);

            retry.Exponential(
                        retryLimit: 5,
                        minInterval: TimeSpan.FromMilliseconds(200),
                        maxInterval: TimeSpan.FromSeconds(5),
                        intervalDelta: TimeSpan.FromMilliseconds(200));
        });

        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();

//Ensure Serilog TTL index exists (or recreate if retention period changed)
SerilogRetentionSetup.EnsureSerilogTtlIndex(builder.Configuration, retentionDays: 1);

host.Run();