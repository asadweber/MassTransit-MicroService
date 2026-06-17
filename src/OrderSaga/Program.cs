
using Application;
using Infrastructure;
using MassTransit;
using MongoDB.Driver;
using OrderSaga.Saga;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

var mongoSection = builder.Configuration.GetSection("MongoDb");

builder.Services.AddSingleton<IMongoClient>(_ =>
    new MongoClient(mongoSection["ConnectionString"]));


builder.Services.AddMassTransit(x =>
{
    x.AddSagaStateMachine<OrderStateMachine, OrderSagaState, OrderSagaDefinition>()
        .MongoDbRepository(
         r =>
        {
            r.Connection = mongoSection["ConnectionString"];
            r.DatabaseName = mongoSection["DatabaseName"];
            r.CollectionName = mongoSection["SagaCollection"];
        });

    x.AddMongoDbOutbox(o =>
    {
        o.ClientFactory(provider =>
            provider.GetRequiredService<IMongoClient>());

        o.DatabaseFactory(provider =>
            provider.GetRequiredService<IMongoClient>()
                .GetDatabase(mongoSection["DatabaseName"]));

        o.QueryDelay = TimeSpan.FromSeconds(1);

        o.UseBusOutbox();
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

var host = builder.Build();
using (var scope = host.Services.CreateScope())
{
    var mongoClient =
        scope.ServiceProvider.GetRequiredService<IMongoClient>();

    var database = mongoClient.GetDatabase(
        builder.Configuration["MongoDb:DatabaseName"]);

    var collectionName =
        builder.Configuration["MongoDb:SagaCollection"];

    var collections =
        await database.ListCollectionNames().ToListAsync();

    if (!collections.Contains(collectionName))
    {
        await database.CreateCollectionAsync(collectionName);
    }
}

host.Run();
