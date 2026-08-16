using Application.Dtos;
using AutoMapper;
using Domain.Entities;

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
    }
}
