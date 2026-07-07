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

// ── MassTransit(Publish - Only + EF Core Outbox) ────────────────────────────
builder.Services.AddMassTransit(x =>
{
    x.AddBusMetadataExplorer();

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
