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
    public async Task AddAsync_ThenSave_PersistsProduct()
    {
        var product = new Product { Name = "Gadget", Price = 9.99m, Stock = 3 };

        await _sut.AddAsync(product);
        await _context.SaveChangesAsync();

        (await _context.Products.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetByIdAsync_ProductExists_ReturnsProduct()
    {
        var product = new Product { Name = "Widget", Price = 5m, Stock = 1 };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        var result = await _sut.GetByIdAsync(product.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Widget");
    }

    [Fact]
    public async Task GetByIdAsync_ProductMissing_ReturnsNull()
    {
        var result = await _sut.GetByIdAsync(999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_NoProducts_ReturnsEmptyList()
    {
        var result = await _sut.GetAllAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_ProductsExist_ReturnsAll()
    {
        _context.Products.AddRange(
            new Product { Name = "Widget", Price = 1m, Stock = 1 },
            new Product { Name = "Gadget", Price = 2m, Stock = 2 });
        await _context.SaveChangesAsync();

        var result = await _sut.GetAllAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPagedAsync_ReturnsCorrectPageAndTotalCount()
    {
        for (var i = 1; i <= 5; i++)
            _context.Products.Add(new Product { Name = $"Product{i}", Price = i, Stock = i });
        await _context.SaveChangesAsync();

        var (items, totalCount) = await _sut.GetPagedAsync(pageNumber: 2, pageSize: 2);

        totalCount.Should().Be(5);
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPagedAsync_WithFilter_ReturnsOnlyMatchingItemsAndTotalCount()
    {
        _context.Products.AddRange(
            new Product { Name = "InStock1", Stock = 5 },
            new Product { Name = "InStock2", Stock = 3 },
            new Product { Name = "OutOfStock", Stock = 0 });
        await _context.SaveChangesAsync();

        var (items, totalCount) = await _sut.GetPagedAsync(1, 10, filter: p => p.Stock > 0);

        totalCount.Should().Be(2);
        items.Should().OnlyContain(p => p.Stock > 0);
    }

    [Fact]
    public async Task GetPagedAsync_WithOrderBy_ReturnsSortedItems()
    {
        _context.Products.AddRange(
            new Product { Name = "Charlie", Price = 3m },
            new Product { Name = "Alice", Price = 1m },
            new Product { Name = "Bob", Price = 2m });
        await _context.SaveChangesAsync();

        var (items, _) = await _sut.GetPagedAsync(1, 10, orderBy: nameof(Product.Name));

        items.Select(p => p.Name).Should().ContainInOrder("Alice", "Bob", "Charlie");
    }

    [Fact]
    public async Task GetPagedAsync_NoProducts_ReturnsEmptyWithZeroTotal()
    {
        var (items, totalCount) = await _sut.GetPagedAsync(pageNumber: 1, pageSize: 10);

        totalCount.Should().Be(0);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task FindAsync_MatchingPredicate_ReturnsFilteredProducts()
    {
        _context.Products.AddRange(
            new Product { Name = "InStock", Price = 1m, Stock = 5 },
            new Product { Name = "OutOfStock", Price = 1m, Stock = 0 });
        await _context.SaveChangesAsync();

        var result = await _sut.FindAsync(p => p.Stock > 0);

        result.Should().ContainSingle().Which.Name.Should().Be("InStock");
    }

    [Fact]
    public async Task Update_ThenSave_PersistsChanges()
    {
        var product = new Product { Name = "Widget", Price = 5m, Stock = 1 };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        product.Name = "Updated Widget";
        product.Price = 7.5m;
        await _sut.Update(product);
        await _context.SaveChangesAsync();

        var persisted = await _context.Products.FindAsync(product.Id);
        persisted!.Name.Should().Be("Updated Widget");
        persisted.Price.Should().Be(7.5m);
    }

    [Fact]
    public void Remove_ThenSave_DeletesProduct()
    {
        var product = new Product { Name = "Widget", Price = 5m, Stock = 1 };
        _context.Products.Add(product);
        _context.SaveChanges();

        _sut.Remove(product);
        _context.SaveChanges();

        _context.Products.Should().BeEmpty();
    }

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
}
