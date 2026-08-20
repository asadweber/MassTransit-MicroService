using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using MicroService.Tests.Fakes;

namespace MicroService.Tests.Repositories;

public class OrderRepositoryTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly OrderRepository _sut;

    public OrderRepositoryTests()
    {
        _context = FakeDbContext.Create();
        _sut = new OrderRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetByIdWithDetailsAsync_OrderExists_IncludesDetailsAndNotification()
    {
        var order = new Order
        {
            CustomerName = "Alice",
            OrderDetails = [new OrderDetail { ProductId = 1, OrderQty = 1, UnitPrice = 5m, Total = 5m }],
            OrderNotification = new OrderNotification { NotifyToEmail = true }
        };
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _sut.GetByIdWithDetailsAsync(order.Id);

        result.Should().NotBeNull();
        result!.OrderDetails.Should().HaveCount(1);
        result.OrderNotification.Should().NotBeNull();
    }

    [Fact]
    public async Task GetByIdWithDetailsAsync_OrderMissing_ReturnsNull()
    {
        var result = await _sut.GetByIdWithDetailsAsync(999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllWithDetailsAsync_NoOrders_ReturnsEmptyList()
    {
        var result = await _sut.GetAllWithDetailsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task AddAsync_ThenSave_PersistsOrder()
    {
        var order = new Order { CustomerName = "Bob" };

        await _sut.AddAsync(order);
        await _context.SaveChangesAsync();

        (await _context.Orders.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Remove_ThenSave_DeletesOrder()
    {
        var order = new Order { CustomerName = "Carol" };
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        _sut.Remove(order);
        await _context.SaveChangesAsync();

        (await _context.Orders.CountAsync()).Should().Be(0);
    }
}
