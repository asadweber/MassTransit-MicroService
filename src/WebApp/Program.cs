using Application;
using Application.Dtos;
using Infrastructure;
using MassTransit;
using MongoDB.Driver;
using Swashbuckle.AspNetCore.Filters;
using WebApp.Services;
using WebApp.Swagger;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();


// ── MongoDB Settings ──────────────────────────────────────────────────────
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection("MongoDb"));

var mongoSettings = builder.Configuration
    .GetSection("MongoDb")
    .Get<MongoDbSettings>()!;


// ── MassTransit(Publish - Only + MongoDB Outbox) ───────────────────────────
builder.Services.AddMassTransit(x =>
{
    x.AddBusMetadataExplorer();

    // MongoDB Transactional Outbox
    // IPublishEndpoint.Publish() → writes to MongoDB → OutboxDelivery forwards to RabbitMQ
    x.AddMongoDbOutbox(o =>
    {
        o.Connection = mongoSettings.ConnectionString;
        o.DatabaseName = mongoSettings.DatabaseName;
        o.QueryDelay = TimeSpan.FromSeconds(1); // ✅ add this

        // ✅ For publish-only: disable inbox cleanup (no consumers)
        o.DisableInboxCleanupService();

        o.UseBusOutbox(b =>
        {
            b.MessageDeliveryLimit = 100;
            b.MessageDeliveryTimeout = TimeSpan.FromSeconds(10);
        });
    });

    // RabbitMQ Transport
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

        // ✅ Required even for publish-only — activates outbox delivery pipeline
        cfg.ConfigureEndpoints(ctx);
    });
});


builder.Services.AddMassTransitDashboard(options =>
{
    options.Metrics.Enabled = true;
    options.Flow.Enabled = true;
});

builder.Services.AddHostedService<OrderSimulatorService>();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.ExampleFilters());
builder.Services.AddSwaggerExamplesFromAssemblyOf<OrderDtoExample>();





var app = builder.Build();

// ── Ensure MongoDB Outbox collections exist on startup ────────────────────
using (var scope = app.Services.CreateScope())
{
    var mongoClient = new MongoClient(mongoSettings.ConnectionString);
    var database = mongoClient.GetDatabase(mongoSettings.DatabaseName);

    var existingCollections = await database.ListCollectionNames().ToListAsync();

    if (!existingCollections.Contains("OutboxMessage"))
        await database.CreateCollectionAsync("OutboxMessage");

    if (!existingCollections.Contains("OutboxState"))
        await database.CreateCollectionAsync("OutboxState");

    if (!existingCollections.Contains("InboxState"))
        await database.CreateCollectionAsync("InboxState");
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

//masstransit
app.UseMassTransitDashboard();

app.Run();
