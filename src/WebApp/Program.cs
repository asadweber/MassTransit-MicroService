using Application;
using Application.Messaging;
using Infrastructure;
using Infrastructure.Persistence;
using MassTransit;
using Swashbuckle.AspNetCore.Filters;
using WebApp.Services;
using WebApp.Swagger;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

builder.Services.AddMassTransit(x =>
{
    // WebApp Program.cs — publish only
    x.AddAllConsumers();

    x.AddMongoDbOutbox(o =>
    {
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
