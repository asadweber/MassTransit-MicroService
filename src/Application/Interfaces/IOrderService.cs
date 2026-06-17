using Contracts.Dto;

namespace Application.Interfaces;

public interface IOrderService
{
    Task<List<OrderDto>> GetAllAsync();
    Task<OrderDto?> GetByIdAsync(int id);
    Task<OrderDto> CreateAsync(OrderDto request);
    Task<bool> UpdateAsync(int id, OrderDto request);
    Task<bool> DeleteAsync(int id);
}
