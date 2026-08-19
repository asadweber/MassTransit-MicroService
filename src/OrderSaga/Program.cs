using Application;
using Hangfire;
using Hangfire.Redis.StackExchange;
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

// Hangfire backs MassTransit's delayed-message scheduler (saga Schedule()/
// Unschedule() for InventoryRetry) via Redis storage instead of the RabbitMQ
// delayed-exchange plugin — avoids depending on
// rabbitmq_delayed_message_exchange being installed.
var redisOptions = builder.Configuration.GetSection("Redis").Get<RedisOptions>()!;
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseRedisStorage(redisOptions.ConnectionString));

var hangfireOptions = builder.Configuration.GetSection("Hangfire").Get<HangfireOptions>() ?? new HangfireOptions();
builder.Services.AddHangfireServer(opts =>
{
    opts.WorkerCount = hangfireOptions.WorkerCount;
    opts.Queues = hangfireOptions.Queues;
});

builder.Services.AddMassTransit(x =>
{
    // EF Core Outbox — writes OutboxMessage row in same DbContext/transaction as
    // the saga's EntityFrameworkRepository SaveChanges, so state-transition
    // publishes (CheckInventory/ProcessPayment/OrderConfirmed) only reach
    // RabbitMQ once the saga's DB update actually commits.
    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UseSqlServer();
        o.QueryMessageLimit = rmqOptions.QueryMessageLimit;
        o.QueryDelay = TimeSpan.FromSeconds(rmqOptions.QueryDelaySeconds);
        o.UseBusOutbox(bo =>
        {
            bo.MessageDeliveryLimit = rmqOptions.MessageDeliveryLimit;
            bo.MessageDeliveryTimeout = TimeSpan.FromSeconds(rmqOptions.MessageDeliveryTimeoutSeconds);
        });
    });

    // Registers IMessageScheduler in the container + the Hangfire consumers
    // that turn schedule/unschedule commands into Hangfire jobs.
    x.AddPublishMessageScheduler();
    x.AddHangfireConsumers();

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

        // Required for saga Schedule()/Unschedule() (InventoryRetry) — routes
        // scheduled messages through the registered Hangfire (Redis-backed)
        // scheduler instead of the RabbitMQ delayed-exchange plugin.
        cfg.UsePublishMessageScheduler();

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