using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using MicroService.Tests.Fakes;

namespace MicroService.Tests.Repositories;

public class GenericRepositoryTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly GenericRepository<Product> _sut;

    public GenericRepositoryTests()
    {
        _context = FakeDbContext.Create();
        _sut = new GenericRepository<Product>(_context);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task AddAsync_ThenSave_PersistsEntity()
    {
        var product = new Product { Name = "Gadget", Price = 9.99m, Stock = 3 };

        await _sut.AddAsync(product);
        await _context.SaveChangesAsync();

        (await _context.Products.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetByIdAsync_EntityExists_ReturnsEntity()
    {
        var product = new Product { Name = "Widget", Price = 5m, Stock = 1 };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        var result = await _sut.GetByIdAsync(product.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Widget");
    }

    [Fact]
    public async Task GetByIdAsync_EntityMissing_ReturnsNull()
    {
        var result = await _sut.GetByIdAsync(999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_NoEntities_ReturnsEmptyList()
    {
        var result = await _sut.GetAllAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_EntitiesExist_ReturnsAll()
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
    public async Task GetPagedAsync_LastPagePartial_ReturnsRemainder()
    {
        for (var i = 1; i <= 5; i++)
            _context.Products.Add(new Product { Name = $"Product{i}", Price = i, Stock = i });
        await _context.SaveChangesAsync();

        var (items, totalCount) = await _sut.GetPagedAsync(pageNumber: 3, pageSize: 2);

        totalCount.Should().Be(5);
        items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetPagedAsync_NoEntities_ReturnsEmptyWithZeroTotal()
    {
        var (items, totalCount) = await _sut.GetPagedAsync(pageNumber: 1, pageSize: 10);

        totalCount.Should().Be(0);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task FindAsync_MatchingPredicate_ReturnsFilteredEntities()
    {
        _context.Products.AddRange(
            new Product { Name = "InStock", Price = 1m, Stock = 5 },
            new Product { Name = "OutOfStock", Price = 1m, Stock = 0 });
        await _context.SaveChangesAsync();

        var result = await _sut.FindAsync(p => p.Stock > 0);

        result.Should().ContainSingle().Which.Name.Should().Be("InStock");
    }

    [Fact]
    public async Task FindAsync_NoMatch_ReturnsEmptyList()
    {
        _context.Products.Add(new Product { Name = "OutOfStock", Price = 1m, Stock = 0 });
        await _context.SaveChangesAsync();

        var result = await _sut.FindAsync(p => p.Stock > 0);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_ThenSave_PersistsChanges()
    {
        var product = new Product { Name = "Widget", Price = 5m, Stock = 1 };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        product.Name = "Updated Widget";
        await _sut.Update(product);
        await _context.SaveChangesAsync();

        (await _context.Products.FindAsync(product.Id))!.Name.Should().Be("Updated Widget");
    }

    [Fact]
    public void Remove_ThenSave_DeletesEntity()
    {
        var product = new Product { Name = "Widget", Price = 5m, Stock = 1 };
        _context.Products.Add(product);
        _context.SaveChanges();

        _sut.Remove(product);
        _context.SaveChanges();

        _context.Products.Should().BeEmpty();
    }
}
