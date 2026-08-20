using System.Linq.Expressions;
using Application.Dtos;

namespace Application.Interfaces;

public interface IGenericService<TDto> where TDto : class
{
    Task<List<TDto>> GetAllAsync();

    Task<PagedResult<TDto>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Expression<Func<TDto, bool>>? filter = null,
        string? orderBy = null,
        bool descending = false);

    Task<TDto?> GetByIdAsync(long id);

    Task<TDto> CreateAsync(TDto request);

    Task<bool> UpdateAsync(long id, TDto request);

    Task<bool> DeleteAsync(long id);
}
