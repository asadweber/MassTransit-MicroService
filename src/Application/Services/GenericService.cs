using Application.Interfaces;
using AutoMapper;
using Domain;
using Domain.Repositories;

namespace Application.Services;

public abstract class GenericService<TEntity, TDto>(
    IGenericRepository<TEntity> repository,
    IUnitOfWork uow,
    IMapper mapper) : IGenericService<TDto>
    where TEntity : class
    where TDto : class
{
    protected readonly IGenericRepository<TEntity> Repository = repository;
    protected readonly IUnitOfWork Uow = uow;
    protected readonly IMapper Mapper = mapper;

    public virtual async Task<List<TDto>> GetAllAsync()
    {
        var entities = await Repository.GetAllAsync();
        return mapper.Map<List<TDto>>(entities);
    }

    public virtual async Task<TDto?> GetByIdAsync(long id)
    {
        var entity = await Repository.GetByIdAsync(id);
        return entity is null ? null : mapper.Map<TDto>(entity);
    }

    public virtual async Task<TDto> CreateAsync(TDto request)
    {
        var entity = mapper.Map<TEntity>(request);

        await Repository.AddAsync(entity);
        await uow.SaveChangesAsync();

        return mapper.Map<TDto>(entity);
    }

    public virtual async Task<bool> UpdateAsync(long id, TDto request)
    {
        var existing = await Repository.GetByIdAsync(id);
        if (existing is null) return false;

        mapper.Map(request, existing);

        await Repository.Update(existing);
        await uow.SaveChangesAsync();
        return true;
    }

    public virtual async Task<bool> DeleteAsync(long id)
    {
        var entity = await Repository.GetByIdAsync(id);
        if (entity is null) return false;

        Repository.Remove(entity);
        await uow.SaveChangesAsync();
        return true;
    }
}
