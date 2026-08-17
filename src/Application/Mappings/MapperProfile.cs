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

        CreateMap<OrderNotification, OrderNotificationDto>().ReverseMap()
            .ForMember(x => x.Id, opt => opt.Ignore())
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
            .ForMember(d => d.InventoryRetryTokenId, o => o.Ignore());

        CreateMap<OrderDetailDto, SagaOrderDetail>()
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.OrderSagaStateCorrelationId, o => o.Ignore());

        CreateMap<OrderNotificationDto, SagaOrderNotification>()
            .ForMember(d => d.OrderSagaStateCorrelationId, o => o.Ignore());

    }
}
