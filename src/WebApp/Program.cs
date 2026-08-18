using Application;
using Infrastructure;
using Infrastructure.Persistence;
using MassTransit;
using Serilog;
using Swashbuckle.AspNetCore.Filters;
using WebApp.Services;
using WebApp.Swagger;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
    optional: false, reloadOnChange: true);


// Serilog config lives entirely in appsettings.json ("Serilog" section).
builder.Services.AddSerilog(cfg => cfg.ReadFrom.Configuration(builder.Configuration));

// Add services to the container.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

var rmqOptions = builder.Configuration.GetSection("RabbitMQ").Get<RabbitMqOptions>()!;

// ── MassTransit(Publish - Only + EF Core Outbox) ────────────────────────────
builder.Services.AddMassTransit(x =>
{
    x.AddBusMetadataExplorer();

    // EF Core Outbox — writes OutboxMessage row in same DbContext/transaction as Order insert
    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UseSqlServer();        
    });

    // RabbitMQ Transport
    x.UsingRabbitMq((ctx, cfg) =>
    {
        var rmq = ctx.GetRequiredService<RabbitMqOptions>();

        cfg.Host(rmq.Host, rmq.VirtualHost, h =>
        {
            h.Username(rmq.Username);
            h.Password(rmq.Password);
        });

        cfg.UseNewtonsoftJsonSerializer();
        cfg.UseNewtonsoftJsonDeserializer();

        // ✅ Required even for publish-only — activates outbox delivery pipeline
        cfg.ConfigureEndpoints(ctx);
    });
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

app.Run();
