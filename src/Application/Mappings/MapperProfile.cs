using Application.Dtos;
using AutoMapper;
using Domain.Entities;
using Infrastructure.Persistence;

namespace Application.Mappings;

public class MapperProfile : Profile
{
    public MapperProfile()
    {
        CreateMap<Product, ProductDto>().ReverseMap();

        // OrderId is the FK to Orders — never let an incoming DTO's OrderId overwrite
        // the tracked entity's value, or a stale/default OrderId on the request triggers
        // FK_OrderNotifications_Orders_OrderId when EF tries to persist the change.
        CreateMap<OrderNotification, OrderNotificationDto>().ReverseMap()
            .ForMember(x => x.Id, opt => opt.Ignore())
            .ForMember(x => x.OrderId, opt => opt.Ignore())
            .ForMember(x => x.Order, opt => opt.Ignore());

        CreateMap<OrderDetail, OrderDetailDto>().ReverseMap();

        CreateMap<Order, OrderDto>()
            .ReverseMap()
            .ForMember(d => d.OrderDate, o => o.Ignore())
            .ForMember(d => d.TotalAmount, o => o.Ignore());



        CreateMap<OrderDto, OrderSagaState>()
            .ForMember(d => d.OrderId, o => o.MapFrom(s => s.Id))
            .ForMember(d => d.CorrelationId, o => o.Ignore())
            .ForMember(d => d.CurrentState, o => o.Ignore())
            .ForMember(d => d.Version, o => o.Ignore())
            .ForMember(d => d.FirstUnavailableAt, o => o.Ignore())
            .ForMember(d => d.NextInventoryRetryAt, o => o.Ignore())
            .ForMember(d => d.InventoryRetryCount, o => o.Ignore())
            .ForMember(d => d.InventoryRetryTokenId, o => o.Ignore())
            .ReverseMap()
            .ForMember(s => s.Id, o => o.MapFrom(d => d.OrderId));

        // Id is an identity column on both target entities — never copy the DTO's
        // Id across, or EF Core issues an explicit-value INSERT and SQL Server
        // rejects it (IDENTITY_INSERT is OFF).
        CreateMap<OrderDetailDto, SagaOrderDetail>()
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.OrderSagaStateCorrelationId, o => o.Ignore())
            .ReverseMap();

        CreateMap<OrderNotificationDto, SagaOrderNotification>()
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.OrderSagaStateCorrelationId, o => o.Ignore())
            .ReverseMap();

    }
}
