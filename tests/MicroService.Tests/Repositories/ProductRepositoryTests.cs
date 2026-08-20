using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using MicroService.Tests.Fakes;

namespace MicroService.Tests.Repositories;

public class ProductRepositoryTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly ProductRepository _sut;

    public ProductRepositoryTests()
    {
        _context = FakeDbContext.Create();
        _sut = new ProductRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task HasSufficientStockAsync_StockCoversQty_ReturnsTrue()
    {
        var product = new Product { Name = "Widget", Stock = 10 };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        var result = await _sut.HasSufficientStockAsync(product.Id, 5);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasSufficientStockAsync_StockBelowQty_ReturnsFalse()
    {
        var product = new Product { Name = "Widget", Stock = 2 };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        var result = await _sut.HasSufficientStockAsync(product.Id, 5);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasSufficientStockAsync_ProductMissing_ReturnsFalse()
    {
        var result = await _sut.HasSufficientStockAsync(999, 1);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ReduceStockQtyAsync_SufficientStock_PersistsDecrement()
    {
        var product = new Product { Name = "Widget", Stock = 10 };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        var result = await _sut.ReduceStockQtyAsync(product.Id, 4);

        result.Should().BeTrue();
        (await _context.Products.FindAsync(product.Id))!.Stock.Should().Be(6);
    }

    [Fact]
    public async Task ReduceStockQtyAsync_InsufficientStock_ReturnsFalseAndLeavesStockUnchanged()
    {
        var product = new Product { Name = "Widget", Stock = 2 };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        var result = await _sut.ReduceStockQtyAsync(product.Id, 5);

        result.Should().BeFalse();
        (await _context.Products.FindAsync(product.Id))!.Stock.Should().Be(2);
    }

    [Fact]
    public async Task ReduceStockQtyAsync_ProductMissing_ReturnsFalse()
    {
        var result = await _sut.ReduceStockQtyAsync(999, 1);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetAllAsync_NoProducts_ReturnsEmptyList()
    {
        var result = await _sut.GetAllAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task AddAsync_ThenSave_PersistsProduct()
    {
        var product = new Product { Name = "Gadget", Price = 9.99m, Stock = 3 };

        await _sut.AddAsync(product);
        await _context.SaveChangesAsync();

        (await _context.Products.CountAsync()).Should().Be(1);
    }
}
