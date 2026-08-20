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
    public async Task GetAllAsync_NoProducts_ReturnsEmptyList()
    {
        _productRepo.Setup(r => r.GetAllAsync()).ReturnsAsync((IReadOnlyList<Product>)[]);

        var result = await _sut.GetAllAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_RepositoryThrows_PropagatesException()
    {
        _productRepo.Setup(r => r.GetAllAsync()).ThrowsAsync(new InvalidOperationException("db failure"));

        var act = () => _sut.GetAllAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("db failure");
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
