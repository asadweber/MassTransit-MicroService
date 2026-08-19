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
    // EF Core Outbox — writes OutboxMessage row in same DbContext/transaction as
    // OrderService.CreateAsync's SaveChanges, so Publish only actually reaches
    // RabbitMQ once that transaction commits (no dual-write between DB + bus).
    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UseSqlServer();
        o.QueryMessageLimit= rmqOptions.QueryMessageLimit;
        o.QueryDelay = TimeSpan.FromSeconds(rmqOptions.QueryDelaySeconds);
        o.QueryTimeout = TimeSpan.FromSeconds(rmqOptions.QueryTimeoutSeconds);


        o.IsolationLevel = System.Data.IsolationLevel.ReadCommitted;
        o.UseBusOutbox(bo =>
        {
            bo.MessageDeliveryLimit = rmqOptions.MessageDeliveryLimit;
            bo.MessageDeliveryTimeout = TimeSpan.FromSeconds(rmqOptions.MessageDeliveryTimeoutSeconds);
        });
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
