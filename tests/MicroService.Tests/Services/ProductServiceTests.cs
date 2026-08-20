using System.Linq.Expressions;
using Application.Dtos;
using Application.Mappings;
using Application.Services;
using AutoMapper;
using Domain;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using MassTransit;
using Moq;

namespace MicroService.Tests.Services;

public class ProductServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IProductRepository> _productRepo = new();
    private readonly Mock<IPublishEndpoint> _bus = new();
    private readonly IMapper _mapper;
    private readonly ProductService _sut;

    public ProductServiceTests()
    {
        _uow.Setup(u => u.Products).Returns(_productRepo.Object);

        var config = new MapperConfiguration(cfg => cfg.AddProfile<MapperProfile>(), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        _mapper = config.CreateMapper();

        _sut = new ProductService(_uow.Object, _bus.Object, _mapper);
    }

    [Fact]
    public async Task CreateAsync_HappyPath_PersistsProduct()
    {
        var request = new ProductDto { Name = "Widget", Price = 5m, Stock = 10 };
        _uow.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        var result = await _sut.CreateAsync(request);

        result.Name.Should().Be("Widget");
        _productRepo.Verify(r => r.AddAsync(It.Is<Product>(p => p.Name == "Widget" && p.Stock == 10)), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_RepositoryThrows_PropagatesException()
    {
        var request = new ProductDto { Name = "Widget" };
        _productRepo.Setup(r => r.AddAsync(It.IsAny<Product>()))
            .ThrowsAsync(new InvalidOperationException("db failure"));

        var act = () => _sut.CreateAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("db failure");
    }

    [Fact]
    public async Task GetByIdAsync_ProductExists_ReturnsDto()
    {
        _productRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new Product { Id = 1, Name = "Widget" });

        var result = await _sut.GetByIdAsync(1);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Widget");
    }

    [Fact]
    public async Task GetByIdAsync_ProductNotFound_ReturnsNull()
    {
        _productRepo.Setup(r => r.GetByIdAsync(It.IsAny<long>())).ReturnsAsync((Product?)null);

        var result = await _sut.GetByIdAsync(99);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_NoProducts_ReturnsEmptyList()
    {
        _productRepo.Setup(r => r.GetAllAsync()).ReturnsAsync((IReadOnlyList<Product>)[]);

        var result = await _sut.GetAllAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPagedAsync_ReturnsMappedItemsAndTotalCount()
    {
        var products = new List<Product>
        {
            new() { Id = 1, Name = "Widget" },
            new() { Id = 2, Name = "Gadget" }
        };
        _productRepo.Setup(r => r.GetPagedAsync(1, 2, null, null, false))
            .ReturnsAsync(((IReadOnlyList<Product>)products, 5));

        var result = await _sut.GetPagedAsync(1, 2);

        result.TotalCount.Should().Be(5);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task GetPagedAsync_WithDtoFilter_RetargetsPredicateToEntity()
    {
        Expression<Func<Product, bool>>? capturedFilter = null;
        _productRepo.Setup(r => r.GetPagedAsync(1, 10, It.IsAny<Expression<Func<Product, bool>>>(), null, false))
            .Callback<int, int, Expression<Func<Product, bool>>?, string?, bool>((_, _, f, _, _) => capturedFilter = f)
            .ReturnsAsync(((IReadOnlyList<Product>)[], 0));

        await _sut.GetPagedAsync(1, 10, filter: dto => dto.Stock > 0);

        capturedFilter.Should().NotBeNull();
        capturedFilter!.Compile()(new Product { Stock = 5 }).Should().BeTrue();
        capturedFilter.Compile()(new Product { Stock = 0 }).Should().BeFalse();
    }

    [Fact]
    public async Task GetPagedAsync_WithOrderBy_PassesThroughToRepository()
    {
        _productRepo.Setup(r => r.GetPagedAsync(1, 10, null, "Name", true))
            .ReturnsAsync(((IReadOnlyList<Product>)[], 0));

        var result = await _sut.GetPagedAsync(1, 10, orderBy: "Name", descending: true);

        result.TotalCount.Should().Be(0);
        _productRepo.Verify(r => r.GetPagedAsync(1, 10, null, "Name", true), Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_RepositoryThrows_PropagatesException()
    {
        _productRepo.Setup(r => r.GetAllAsync()).ThrowsAsync(new InvalidOperationException("db failure"));

        var act = () => _sut.GetAllAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("db failure");
    }

    [Fact]
    public async Task UpdateAsync_HappyPath_UpdatesAndReturnsTrue()
    {
        var existing = new Product { Id = 1, Name = "Old", Price = 1m, Stock = 1 };
        _productRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(existing);
        _uow.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);
        var request = new ProductDto { Id = 1, Name = "New", Price = 2m, Stock = 5 };

        var result = await _sut.UpdateAsync(1, request);

        result.Should().BeTrue();
        existing.Name.Should().Be("New");
        _productRepo.Verify(r => r.Update(existing), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_MismatchedId_ReturnsFalseWithoutTouchingRepository()
    {
        var request = new ProductDto { Id = 1 };

        var result = await _sut.UpdateAsync(2, request);

        result.Should().BeFalse();
        _productRepo.Verify(r => r.GetByIdAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ProductNotFound_ReturnsFalse()
    {
        _productRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync((Product?)null);
        var request = new ProductDto { Id = 5 };

        var result = await _sut.UpdateAsync(5, request);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ProductExists_RemovesAndSaves()
    {
        var product = new Product { Id = 7 };
        _productRepo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(product);
        _uow.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        var result = await _sut.DeleteAsync(7);

        result.Should().BeTrue();
        _productRepo.Verify(r => r.Remove(product), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ProductNotFound_ReturnsFalse()
    {
        _productRepo.Setup(r => r.GetByIdAsync(It.IsAny<long>())).ReturnsAsync((Product?)null);

        var result = await _sut.DeleteAsync(123);

        result.Should().BeFalse();
        _productRepo.Verify(r => r.Remove(It.IsAny<Product>()), Times.Never);
    }

    [Fact]
    public async Task HasSufficientStockAsync_StockCoversQty_ReturnsTrue()
    {
        _productRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new Product { Id = 1, Stock = 10 });

        var result = await _sut.HasSufficientStockAsync(1, 5);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasSufficientStockAsync_StockBelowQty_ReturnsFalse()
    {
        _productRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new Product { Id = 1, Stock = 2 });

        var result = await _sut.HasSufficientStockAsync(1, 5);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasSufficientStockAsync_ProductNotFound_ReturnsFalse()
    {
        _productRepo.Setup(r => r.GetByIdAsync(It.IsAny<long>())).ReturnsAsync((Product?)null);

        var result = await _sut.HasSufficientStockAsync(99, 1);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ReduceStockQtyAsync_SufficientStock_DecrementsAndCommits()
    {
        var product = new Product { Id = 1, Stock = 10 };
        _productRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(product);
        _uow.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        var result = await _sut.ReduceStockQtyAsync(1, 4);

        result.Should().BeTrue();
        product.Stock.Should().Be(6);
        _uow.Verify(u => u.BeginTransactionAsync(), Times.Once);
        _productRepo.Verify(r => r.Update(product), Times.Once);
        _uow.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task ReduceStockQtyAsync_InsufficientStock_ReturnsFalseWithoutCommitting()
    {
        var product = new Product { Id = 1, Stock = 2 };
        _productRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(product);

        var result = await _sut.ReduceStockQtyAsync(1, 5);

        result.Should().BeFalse();
        product.Stock.Should().Be(2);
        _uow.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task ReduceStockQtyAsync_ProductNotFound_ReturnsFalse()
    {
        _productRepo.Setup(r => r.GetByIdAsync(It.IsAny<long>())).ReturnsAsync((Product?)null);

        var result = await _sut.ReduceStockQtyAsync(123, 1);

        result.Should().BeFalse();
        _uow.Verify(u => u.BeginTransactionAsync(), Times.Never);
    }
}
