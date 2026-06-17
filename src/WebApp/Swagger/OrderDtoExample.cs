using Application.Dtos;
using Swashbuckle.AspNetCore.Filters;

namespace WebApp.Swagger;

public class OrderDtoExample : IExamplesProvider<OrderDto>
{
    public OrderDto GetExamples() => new()
    {
        CustomerName = "John Doe",
        Status = "Pending",
        OrderDetails =
        [
            new OrderDetailDto
            {
                ProductId = 1,
                OrderQty = 2,
                UnitPrice = 9.99m
            },
            new OrderDetailDto
            {
                ProductId = 3,
                OrderQty = 1,
                UnitPrice = 24.50m
            }
        ]
    };
}
