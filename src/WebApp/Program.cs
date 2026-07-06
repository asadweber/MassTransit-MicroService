using Application;
using Application.Dtos;
using Infrastructure;
using Infrastructure.Persistence;
using MassTransit;
using MassTransit.MongoDbIntegration;
using MongoDB.Driver;
using Swashbuckle.AspNetCore.Filters;
using WebApp.Services;
using WebApp.Swagger;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

//// MongoDB
//var mongoSettings = builder.Configuration.GetSection("MongoDb").Get<MongoDbSettings>()!;
//builder.Services.AddSingleton(mongoSettings);
//builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoSettings.ConnectionString));
//builder.Services.AddSingleton<IMongoDatabase>(provider =>provider.GetRequiredService<IMongoClient>().GetDatabase(mongoSettings.DatabaseName));

//builder.Services.AddScoped<MongoDbContext>();


// ── MassTransit(Publish - Only + EF Core Outbox) ────────────────────────────
builder.Services.AddMassTransit(x =>
{
    x.AddBusMetadataExplorer();

    // EF Core Transactional Outbox
    // IPublishEndpoint.Publish() → writes to AppDbContext outbox tables → OutboxDelivery forwards to RabbitMQ
    //x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    //{
    //    o.UseSqlServer();
    //    o.QueryDelay = TimeSpan.FromSeconds(1);

    //    // ✅ For publish-only: disable inbox cleanup (no consumers)
    //    o.DisableInboxCleanupService();

    //    o.UseBusOutbox(b =>
    //    {
    //        b.MessageDeliveryLimit = 100;
    //        b.MessageDeliveryTimeout = TimeSpan.FromSeconds(10);
    //    });
    //});

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

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

//masstransit
app.UseMassTransitDashboard();

app.Run();
