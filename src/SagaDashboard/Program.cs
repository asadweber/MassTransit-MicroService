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
builder.Services.AddSerilog(cfg => cfg.ReadFrom.Configuration(builder.Configuration));

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

builder.Services.AddMassTransit(x =>
{
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

builder.Services.AddControllersWithViews();

var app = builder.Build();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
