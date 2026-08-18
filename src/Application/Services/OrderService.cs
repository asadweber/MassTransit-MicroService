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
        await uow.SaveChangesAsync();                                    // 1) flush Order row, Id assigned
        OrderCreated message = new() { Order = mapper.Map<OrderDto>(order) };
        await bus.Publish(message);                                      // 2) writes OutboxMessage row via same DbContext
        await uow.SaveChangesAsync();                                    // 1) flush Order row, Id assigned
        await uow.CommitAsync();                                         // both rows commit atomically
        return mapper.Map<OrderDto>(order);
    }


    public async Task<bool> UpdateAsync(long id, OrderDto request)
    {
        if (id != request.Id) return false;

        var existing = await uow.Orders.GetByIdWithDetailsAsync(id);
        if (existing is null) return false;

        // mapper.Map(OrderDto, Order) has no OrderNotification config, so a null
        // request.OrderNotification would overwrite existing.OrderNotification and
        // cascade-delete it on save. Preserve it around the map.
        var notification = existing.OrderNotification;
        mapper.Map(request, existing);
        existing.OrderNotification = notification;

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
