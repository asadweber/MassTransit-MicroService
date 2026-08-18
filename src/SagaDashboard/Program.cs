using Application;
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

// ── MassTransit — dashboard visibility only ─────────────────────────────────
// Registers every consumer + the saga so the MassTransit dashboard shows the
// full flow across all services, but never binds a ReceiveEndpoint of its own,
// so this project never actually consumes/duplicates message handling.
builder.Services.AddMassTransit(x =>
{
    x.AddBusMetadataExplorer();

    x.AddConsumer<InventoryConsumer>();
    x.AddConsumer<PaymentConsumer>();
    x.AddConsumer<EmailNotificationConsumer>();
    x.AddConsumer<SmsNotificationConsumer>();

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
        cfg.UseDelayedMessageScheduler();

        cfg.ConfigureEndpoints(ctx);
    });
});

builder.Services.AddMassTransitDashboard(options =>
{
    options.Metrics.Enabled = true;
    options.Flow.Enabled = true;
});

builder.Services.AddControllersWithViews();

var app = builder.Build();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.UseMassTransitDashboard();

app.Run();
