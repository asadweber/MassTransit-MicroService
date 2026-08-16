using Application.Dtos;
using Application.Interfaces;
using Application.Messaging.Events;
using AutoMapper;
using Domain;
using Domain.Entities;
using MassTransit;
using MassTransit.MongoDbIntegration;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using static MassTransit.ValidationResultExtensions;

namespace Application.Services;

public class OrderService(IUnitOfWork uow, IPublishEndpoint bus, IMapper mapper) : IOrderService
{
    public async Task<List<OrderDto>> GetAllAsync()
    {
        var orders = await uow.Orders.GetAllWithDetailsAsync();
        return mapper.Map<List<OrderDto>>(orders);
    }

    public async Task<OrderDto?> GetByIdAsync(long id)
    {
        var order = await uow.Orders.GetByIdWithDetailsAsync(id);
        return order is null ? null : mapper.Map<OrderDto>(order);
    }


    public async Task<OrderDto> CreateAsync(OrderDto request)
    {
        var order = mapper.Map<Order>(request);
        order.OrderDate = DateTime.UtcNow;

        foreach (var detail in order.OrderDetails)
            detail.Total = detail.OrderQty * detail.UnitPrice;

        order.TotalAmount = order.OrderDetails.Sum(d => d.Total);

        await uow.BeginTransactionAsync();
        await uow.Orders.AddAsync(order);
        await uow.SaveChangesAsync();                                              // 2) flush OutboxMessage row
        await uow.CommitAsync();

        OrderCreated message = new() { Order = mapper.Map<OrderDto>(order) };// both rows commit atomically
        await bus.Publish(message); // Id is valid

        return mapper.Map<OrderDto>(order);
    }


    public async Task<bool> UpdateAsync(long id, OrderDto request)
    {
        if (id != request.Id) return false;

        var existing = await uow.Orders.GetByIdWithDetailsAsync(id);
        if (existing is null) return false;

        mapper.Map(request, existing);

        existing.OrderDetails.Clear();
        var newDetails = mapper.Map<List<OrderDetail>>(request.OrderDetails);
        foreach (var detail in newDetails)
        {
            detail.OrderId = id;
            detail.Total = detail.OrderQty * detail.UnitPrice;
            existing.OrderDetails.Add(detail);
        }

        existing.TotalAmount = existing.OrderDetails.Sum(d => d.Total);

       await uow.Orders.Update(existing);
        await uow.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var order = await uow.Orders.GetByIdAsync(id);
        if (order is null) return false;
        uow.Orders.Remove(order);
        await uow.SaveChangesAsync();
        return true;
    }
}
