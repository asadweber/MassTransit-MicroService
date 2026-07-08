using Application;
using Infrastructure;
using InventoryService;
using MassTransit;
using NotificationService;
using OrderSaga.Saga;
using PaymentService;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog config lives entirely in appsettings.json ("Serilog" section).
//builder.Services.AddSerilog(cfg => cfg.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

var mongoSection = builder.Configuration.GetSection("MongoDb").Get<MongoDbSettings>();

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

    x.AddSagaStateMachine<OrderStateMachine, OrderSagaState, OrderSagaDefinition>()
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

var app = builder.Build();

app.UseMassTransitDashboard();

app.Run();
