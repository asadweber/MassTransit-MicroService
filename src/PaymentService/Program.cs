using Application;
using Application.Messaging.Command;
using Application.Messaging.Events;
using Infrastructure;
using Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using PaymentService;
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
    x.AddConsumer<PaymentConsumer>();


    x.UsingRabbitMq((ctx, cfg) =>
    {
        var rmq = builder.Configuration.GetSection("RabbitMQ").Get<RabbitMqOptions>()!;
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

        // Manual endpoint — Payment Service owns this queue
        cfg.ReceiveEndpoint("payment-queue", e =>
        {
            e.Durable = true;
            e.AutoDelete = false;
            e.PrefetchCount = rmq.PrefetchCount;
            e.ConcurrentMessageLimit = rmq.ConcurrentMessageLimit;

            // Caps consumer throughput at 400 messages/sec for this endpoint.
            e.UseRateLimit(400, TimeSpan.FromSeconds(1));

            // Outer policy — added first, so it wraps everything below: exponential
            // retry for real faults that survive the inner concurrency-specific retry.
            e.UseMessageRetry(r =>
            {
                r.Exponential(
                    retryLimit: 5,
                    minInterval: TimeSpan.FromSeconds(1),
                    maxInterval: TimeSpan.FromMinutes(1),
                    intervalDelta: TimeSpan.FromSeconds(5));
            });

            // Inner policy — added second, so it runs closer to the consumer (before the
            // outer exponential policy sees the exception): EF Core optimistic-concurrency
            // conflicts are expected under load and resolve almost instantly, so intercept
            // and retry fast here instead of falling into the slower exponential policy.
            e.UseMessageRetry(r =>
            {
                r.Handle<DbUpdateConcurrencyException>();
                r.Interval(10, TimeSpan.FromMilliseconds(100));
            });

            // Trips after sustained failure (15% of a rolling 1-min window, min 10 attempts
            // evaluated) so a struggling downstream dependency doesn't get hammered further —
            // rejects fast instead of queuing more retries. Half-open probe after 5 min.
            e.UseCircuitBreaker(cb =>
            {
                cb.TrackingPeriod = TimeSpan.FromMinutes(1);
                cb.TripThreshold = 15;
                cb.ActiveThreshold = 10;
                cb.ResetInterval = TimeSpan.FromMinutes(5);
            });

            e.UseDelayedRedelivery(r =>
            {
                r.Intervals(
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(10),
                    TimeSpan.FromMinutes(30),
                    TimeSpan.FromHours(1),
                    TimeSpan.FromHours(6),
                    TimeSpan.FromHours(12),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3),
                    TimeSpan.FromDays(7));
            });
            // Keeps messages for the same order (CorrelationId) processed in order,
            // even though ConcurrentMessageLimit allows 16 messages in parallel.
            var partitioner = e.CreatePartitioner(e.ConcurrentMessageLimit!.Value);
            e.UsePartitioner<ProcessPayment>(partitioner, m => m.Message.CorrelationId);


            // ✅ Consumer — always last
            e.ConfigureConsumer<PaymentConsumer>(ctx);
        });

        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
