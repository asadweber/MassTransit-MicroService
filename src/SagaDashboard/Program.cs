using Application;
using Hangfire;
using Hangfire.Redis.StackExchange;
using Infrastructure;
using Infrastructure.Persistence;
using InventoryService;
using MassTransit;
using NotificationService;
using OrderSaga.Saga;
using PaymentService;
using SagaDashboard;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
    optional: false, reloadOnChange: true);

// Serilog config lives entirely in appsettings.json ("Serilog" section).
//builder.Services.AddSerilog(cfg => cfg.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

// Hangfire backs MassTransit's delayed-message scheduler via Redis storage
// instead of the RabbitMQ delayed-exchange plugin — avoids depending on
// rabbitmq_delayed_message_exchange being installed.
var redisOptions = builder.Configuration.GetSection("Redis").Get<RedisOptions>()!;
var hangfireOptions = builder.Configuration.GetSection("Hangfire").Get<HangfireOptions>() ?? new HangfireOptions();

var redisConfig = StackExchange.Redis.ConfigurationOptions.Parse(redisOptions.ConnectionString);
redisConfig.ConnectTimeout = redisOptions.ConnectTimeoutMs;
redisConfig.SyncTimeout = redisOptions.SyncTimeoutMs;
redisConfig.AbortOnConnectFail = redisOptions.AbortOnConnectFail;

builder.Services.AddHangfire(cfg => cfg
    // Required: Hangfire.Redis.StackExchange does not implement
    // TransactionalAcknowledge. Without pinning this, jobs that transition
    // to FailedState throw NotSupportedException in RemoveFromQueue.
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseRedisStorage(redisConfig.ToString(), new RedisStorageOptions
    {
        InvisibilityTimeout = hangfireOptions.InvisibilityTimeout,
    }));


// ── MassTransit — dashboard visibility only ─────────────────────────────────
builder.Services.AddMassTransit(x =>
{
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

        cfg.ConfigureEndpoints(ctx);
    });
});


builder.Services.AddControllersWithViews();

var app = builder.Build();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

// Job dashboard for the Hangfire-backed MassTransit scheduler (Redis storage).
// Local-only by default; DashboardOptions.Authorization enforces that.
app.UseHangfireDashboard("/hangfire");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");


app.Run();
