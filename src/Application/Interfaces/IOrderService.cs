using Application.Dtos;

namespace Application.Interfaces;

public interface IOrderService
{
    Task<List<OrderDto>> GetAllAsync();
    Task<OrderDto?> GetByIdAsync(long id);
    Task<OrderDto> CreateAsync(OrderDto request);
    Task<bool> UpdateAsync(long id, OrderDto request);
    Task<bool> DeleteAsync(long id);
}
