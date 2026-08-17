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

    }
}
