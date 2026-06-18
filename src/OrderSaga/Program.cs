using Application;
using Infrastructure;
using Infrastructure.Persistence;
using MassTransit;
using MongoDB.Driver;
using OrderSaga.Saga;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

var mongoSection = builder.Configuration.GetSection("MongoDb");

builder.Services.AddMassTransit(x =>
{
    x.AddBusMetadataExplorer();

    x.AddSagaStateMachine<OrderStateMachine, OrderSagaState, OrderSagaDefinition>()
        .MongoDbRepository(r =>
        {
            // Use the same connection string — MassTransit will resolve
            // the shared IMongoClient internally via ClientFactory below
            r.Connection = mongoSection["ConnectionString"];
            r.DatabaseName = mongoSection["DatabaseName"];
            r.CollectionName = mongoSection["SagaCollection"];
        });

    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UseSqlServer();
        o.QueryDelay = TimeSpan.FromSeconds(1);

        o.UseBusOutbox(b =>
        {
            b.MessageDeliveryLimit = 100;
            b.MessageDeliveryTimeout = TimeSpan.FromSeconds(10);
        });
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