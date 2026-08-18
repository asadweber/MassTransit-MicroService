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
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseRedisStorage(redisOptions.ConnectionString));
builder.Services.AddHangfireServer();

// ── MassTransit — dashboard visibility only ─────────────────────────────────
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<InventoryConsumer>();
    x.AddConsumer<PaymentConsumer>();
    x.AddConsumer<EmailNotificationConsumer>();
    x.AddConsumer<SmsNotificationConsumer>();

    // Registers IMessageScheduler in the container + the Hangfire consumers
    // that turn schedule/unschedule commands into Hangfire jobs.
    x.AddPublishMessageScheduler();
    x.AddHangfireConsumers();

    x.AddSagaStateMachine<OrderStateMachine, OrderSagaState, DashboardOnlySagaDefinition>()
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
        cfg.UsePublishMessageScheduler();

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
