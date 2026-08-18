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
        var interval = TimeSpan.FromSeconds(
            config.GetValue("OrderSimulator:IntervalSeconds", 3));
        var ordersPerTick = config.GetValue("OrderSimulator:OrdersPerTick", 1);

        logger.LogInformation(
            "Order simulator started — interval: {Interval}s, orders/tick: {OrdersPerTick}",
            interval.TotalSeconds, ordersPerTick);

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!config.GetValue("OrderSimulator:Enabled", true))
                continue;

            // Loaded once per tick instead of once per order — avoids re-querying
            // Products up to OrdersPerTick times per tick under higher simulator load.
            List<Product> products;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                products = await db.Products.AsNoTracking().ToListAsync(stoppingToken);
            }

            if (products.Count == 0)
            {
                logger.LogWarning("Simulator skipped tick — no products in database");
                continue;
            }

            var tasks = Enumerable.Range(0, ordersPerTick).Select(async _ =>
            {
                try
                {
                    await PlaceOrderAsync(products, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Simulator failed to place order");
                }
            });

            await Task.WhenAll(tasks);
        }
    }

    private async Task PlaceOrderAsync(List<Product> products, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bus    = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var mapper = scope.ServiceProvider.GetRequiredService<IMapper>();

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
            }).ToList(),
            OrderNotification = new OrderNotification
            {
                NotifyToEmail = Random.Shared.Next(2) == 1,
                NotifyToSMS   = Random.Shared.Next(2) == 1,
                NotifyToPaci  = Random.Shared.Next(2) == 1
            }
        };

        foreach (var d in order.OrderDetails)
            d.Total = d.OrderQty * d.UnitPrice;

        order.TotalAmount = order.OrderDetails.Sum(d => d.Total);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        OrderCreated message = new() { Order = mapper.Map<OrderDto>(order) };
        await bus.Publish(message, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation(
            "Simulated order #{Id} for {Customer} — {Items} item(s), ${Total:F2}",
            order.Id, order.CustomerName, order.OrderDetails.Count, order.TotalAmount);
    }
}
