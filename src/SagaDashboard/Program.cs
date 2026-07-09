using Application;
using Infrastructure;
using InventoryService;
using MassTransit;
using MongoDB.Driver;
using NotificationService;
using OrderSaga.Saga;
using PaymentService;
using SagaDashboard;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog config lives entirely in appsettings.json ("Serilog" section).
//builder.Services.AddSerilog(cfg => cfg.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

var mongoSection = builder.Configuration.GetSection("MongoDb").Get<MongoDbSettings>();

// Direct Mongo access for the saga list page (read-only, separate from MassTransit's own repository).
builder.Services.AddSingleton(sp =>
{
    var client = new MongoClient(mongoSection!.ConnectionString);
    var db = client.GetDatabase(mongoSection.DatabaseName);
    return db.GetCollection<OrderSagaState>(mongoSection.SagaCollection);
});

// ── MassTransit — dashboard visibility only ─────────────────────────────────
// Registers every consumer + the saga so the MassTransit dashboard shows the
// full flow across all services, but never binds a ReceiveEndpoint of its own,
// so this project never actually consumes/duplicates message handling.
builder.Services.AddMassTransit(x =>
{
    x.AddBusMetadataExplorer();

    x.AddConsumer<InventoryConsumer>();
    x.AddConsumer<PaymentConsumer>();
    x.AddConsumer<NotificationConsumer>();

    x.AddSagaStateMachine<OrderStateMachine, OrderSagaState, DashboardOnlySagaDefinition>()
        .MongoDbRepository(r =>
        {
            r.Connection = mongoSection!.ConnectionString;
            r.DatabaseName = mongoSection.DatabaseName;
            r.CollectionName = mongoSection.SagaCollection;
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
