using Application.Dtos;
using Application.Mappings;
using Application.Messaging.Events;
using Application.Services;
using AutoMapper;
using Domain;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using MassTransit;
using Moq;

namespace MicroService.Tests.Services;

public class OrderServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IOrderRepository> _orderRepo = new();
    private readonly Mock<IPublishEndpoint> _bus = new();
    private readonly IMapper _mapper;
    private readonly OrderService _sut;

    public OrderServiceTests()
    {
        _uow.Setup(u => u.Orders).Returns(_orderRepo.Object);

        var config = new MapperConfiguration(cfg => cfg.AddProfile<MapperProfile>(), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        _mapper = config.CreateMapper();

        _sut = new OrderService(_uow.Object, _bus.Object, _mapper);
    }

    [Fact]
    public async Task CreateAsync_HappyPath_PersistsOrderAndPublishesEvent()
    {
        var request = new OrderDto
        {
            CustomerName = "Alice",
            OrderDetails =
            [
                new OrderDetailDto { ProductId = 1, OrderQty = 2, UnitPrice = 10m }
            ]
        };

        Order? captured = null;
        _orderRepo.Setup(r => r.AddAsync(It.IsAny<Order>()))
            .Callback<Order>(o => captured = o)
            .Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        var result = await _sut.CreateAsync(request);

        result.TotalAmount.Should().Be(20m);
        captured.Should().NotBeNull();
        captured!.TotalAmount.Should().Be(20m);
        _uow.Verify(u => u.BeginTransactionAsync(), Times.Once);
        _uow.Verify(u => u.CommitAsync(), Times.Once);
        _bus.Verify(b => b.Publish(It.IsAny<OrderCreated>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_EmptyOrderDetails_TotalAmountIsZero()
    {
        var request = new OrderDto { CustomerName = "Bob", OrderDetails = [] };
        _uow.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        var result = await _sut.CreateAsync(request);

        result.TotalAmount.Should().Be(0m);
    }

    [Fact]
    public async Task GetByIdAsync_OrderNotFound_ReturnsNull()
    {
        _orderRepo.Setup(r => r.GetByIdWithDetailsAsync(It.IsAny<long>()))
            .ReturnsAsync((Order?)null);

        var result = await _sut.GetByIdAsync(99);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_MismatchedId_ReturnsFalseWithoutTouchingRepository()
    {
        var request = new OrderDto { Id = 1 };

        var result = await _sut.UpdateAsync(2, request);

        result.Should().BeFalse();
        _orderRepo.Verify(r => r.GetByIdWithDetailsAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_OrderNotFound_ReturnsFalse()
    {
        _orderRepo.Setup(r => r.GetByIdWithDetailsAsync(5)).ReturnsAsync((Order?)null);
        var request = new OrderDto { Id = 5 };

        var result = await _sut.UpdateAsync(5, request);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_OrderExists_RemovesAndSaves()
    {
        var order = new Order { Id = 7 };
        _orderRepo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(order);
        _uow.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        var result = await _sut.DeleteAsync(7);

        result.Should().BeTrue();
        _orderRepo.Verify(r => r.Remove(order), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_OrderNotFound_ReturnsFalse()
    {
        _orderRepo.Setup(r => r.GetByIdAsync(It.IsAny<long>())).ReturnsAsync((Order?)null);

        var result = await _sut.DeleteAsync(123);

        result.Should().BeFalse();
        _orderRepo.Verify(r => r.Remove(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_RepositoryThrows_PropagatesException()
    {
        var request = new OrderDto { CustomerName = "Carol", OrderDetails = [] };
        _orderRepo.Setup(r => r.AddAsync(It.IsAny<Order>()))
            .ThrowsAsync(new InvalidOperationException("db failure"));

        var act = () => _sut.CreateAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("db failure");
        _uow.Verify(u => u.CommitAsync(), Times.Never);
    }
}
