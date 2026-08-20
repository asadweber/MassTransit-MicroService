using Application.Dtos;

namespace Application.Interfaces;

public interface IGenericService<TDto> where TDto : class
{
    Task<List<TDto>> GetAllAsync();

    Task<PagedResult<TDto>> GetPagedAsync(int pageNumber, int pageSize);

    Task<TDto?> GetByIdAsync(long id);

    Task<TDto> CreateAsync(TDto request);

    Task<bool> UpdateAsync(long id, TDto request);

    Task<bool> DeleteAsync(long id);
}
