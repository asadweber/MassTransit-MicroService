using Application;
using Infrastructure;
using Infrastructure.Persistence;
using MassTransit;
using MongoDB.Driver;
using OrderSaga.Saga;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Serilog config lives entirely in appsettings.json ("Serilog" section).
builder.Services.AddSerilog(cfg => cfg.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

var mongoSection = builder.Configuration.GetSection("MongoDb").Get<MongoDbSettings>();

builder.Services.AddMassTransit(x =>
{
    x.AddBusMetadataExplorer();

    x.AddSagaStateMachine<OrderStateMachine, OrderSagaState, OrderSagaDefinition>()
        .MongoDbRepository(r =>
        {
            // Use the same connection string — MassTransit will resolve
            // the shared IMongoClient internally via ClientFactory below
            r.Connection = mongoSection.ConnectionString;
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

        // Required for UseDelayedRedelivery below — schedules redelivery via the
        // RabbitMQ delayed-exchange plugin (rabbitmq_delayed_message_exchange).
        cfg.UseDelayedMessageScheduler();


        cfg.ConfigureEndpoints(ctx);
    });
});

// ── Ensure saga collection exists ─────────────────────────────────────────
var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var mongoClient = scope.ServiceProvider.GetRequiredService<IMongoClient>();

    var database = mongoClient.GetDatabase(
        builder.Configuration["MongoDb:DatabaseName"]);

    var collectionName = builder.Configuration["MongoDb:SagaCollection"];

    var collections = await database.ListCollectionNames().ToListAsync();

    if (!collections.Contains(collectionName))
    {
        await database.CreateCollectionAsync(collectionName);
    }
}

host.Run();