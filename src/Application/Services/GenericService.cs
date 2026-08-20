using System.Linq.Expressions;
using Application.Dtos;
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

    public virtual async Task<PagedResult<TDto>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Expression<Func<TDto, bool>>? filter = null,
        string? orderBy = null,
        bool descending = false)
    {
        var entityFilter = filter is null ? null : RetargetParameter<TDto, TEntity>(filter);

        var (items, totalCount) = await Repository.GetPagedAsync(pageNumber, pageSize, entityFilter, orderBy, descending);

        return new PagedResult<TDto>
        {
            Items = mapper.Map<List<TDto>>(items),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    // Rebuilds a predicate written against TSource (the DTO) so it runs against TTarget (the entity).
    // Valid only when both types expose identically-named members for every property the predicate touches,
    // which holds here because MapperProfile maps Order/Product to their DTOs 1:1.
    private static Expression<Func<TTarget, bool>> RetargetParameter<TSource, TTarget>(Expression<Func<TSource, bool>> source)
    {
        var targetParameter = Expression.Parameter(typeof(TTarget), source.Parameters[0].Name);
        var body = new ParameterRetargetVisitor(source.Parameters[0], targetParameter).Visit(source.Body);
        return Expression.Lambda<Func<TTarget, bool>>(body!, targetParameter);
    }

    private sealed class ParameterRetargetVisitor(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == source ? target : base.VisitParameter(node);

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression == source)
            {
                var property = target.Type.GetProperty(node.Member.Name)
                    ?? throw new InvalidOperationException($"'{target.Type.Name}' has no member '{node.Member.Name}' matching '{source.Type.Name}'.");
                return Expression.Property(target, property);
            }

            return base.VisitMember(node);
        }
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
