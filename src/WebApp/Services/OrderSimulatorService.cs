using AutoMapper;
using Application.Dtos;
using Domain.Entities;
using Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Application.Messaging.Events;

namespace WebApp.Services;

public class OrderSimulatorService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<OrderSimulatorService> logger) : BackgroundService
{
    private static readonly string[] Customers =
        ["Alice Johnson", "Bob Smith", "Carol White", "David Brown", "Eva Martinez",
         "Frank Lee", "Grace Kim", "Henry Patel", "Isla Brown", "Jack Wilson"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("OrderSimulator:Enabled", true))
            return;

        var interval = TimeSpan.FromSeconds(
            config.GetValue("OrderSimulator:IntervalSeconds", 3));

        logger.LogInformation("Order simulator started — interval: {Interval}s", interval.TotalSeconds);

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PlaceOrderAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Simulator failed to place order");
            }
        }
    }

    private async Task PlaceOrderAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bus    = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var mapper = scope.ServiceProvider.GetRequiredService<IMapper>();

        var products = await db.Products.ToListAsync(ct);
        if (products.Count == 0)
        {
            logger.LogWarning("Simulator skipped — no products in database");
            return;
        }

        var itemCount = Random.Shared.Next(1, Math.Min(4, products.Count + 1));
        var picked = products.OrderBy(_ => Random.Shared.Next()).Take(itemCount).ToList();

        var order = new Order
        {
            CustomerName = Customers[Random.Shared.Next(Customers.Length)],
            OrderDate    = DateTime.UtcNow,
            Status       = "Pending",
            OrderDetails = picked.Select(p => new OrderDetail
            {
                ProductId = p.Id,
                OrderQty  = Random.Shared.Next(1, 6),
                UnitPrice = p.Price
            }).ToList()
        };

        foreach (var d in order.OrderDetails)
            d.Total = d.OrderQty * d.UnitPrice;

        order.TotalAmount = order.OrderDetails.Sum(d => d.Total);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // Publish is written to the outbox table inside the same transaction.
        // The outbox delivery service will forward it to RabbitMQ, even after a restart.
        await bus.Publish(new OrderCreated { Order = mapper.Map<OrderDto>(order) }, ct);

        logger.LogInformation(
            "Simulated order #{Id} for {Customer} — {Items} item(s), ${Total:F2}",
            order.Id, order.CustomerName, order.OrderDetails.Count, order.TotalAmount);
    }
}
