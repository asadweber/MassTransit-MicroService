using System.Linq.Expressions;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class GenericRepository<T>(AppDbContext context) : IGenericRepository<T> where T : class
{
    protected readonly AppDbContext Context = context;
    protected readonly DbSet<T> Set = context.Set<T>();

    public async Task<T?> GetByIdAsync(long id)
    {
        return await Set.FindAsync(id);
    }

    public async Task<IReadOnlyList<T>> GetAllAsync()
    {
        return await Set.ToListAsync();
    }

    public async Task<(IReadOnlyList<T> Items, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Expression<Func<T, bool>>? filter = null,
        string? orderBy = null,
        bool descending = false)
    {
        IQueryable<T> query = Set;

        if (filter is not null)
            query = query.Where(filter);

        var totalCount = await query.CountAsync();

        if (!string.IsNullOrWhiteSpace(orderBy))
            query = ApplyOrderBy(query, orderBy, descending);

        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    private static IQueryable<T> ApplyOrderBy(IQueryable<T> query, string propertyName, bool descending)
    {
        var property = typeof(T).GetProperty(propertyName,
            System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            ?? throw new ArgumentException($"Property '{propertyName}' does not exist on type '{typeof(T).Name}'.", nameof(propertyName));

        var parameter = Expression.Parameter(typeof(T), "x");
        var propertyAccess = Expression.Property(parameter, property);
        var lambda = Expression.Lambda(propertyAccess, parameter);

        var methodName = descending ? "OrderByDescending" : "OrderBy";
        var method = typeof(Queryable).GetMethods()
            .First(m => m.Name == methodName && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(T), property.PropertyType);

        return (IQueryable<T>)method.Invoke(null, [query, lambda])!;
    }

    public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate)
    {
        return await Set.Where(predicate).ToListAsync();
    }

    public async Task AddAsync(T entity)
    {
        await Set.AddAsync(entity);
    }

    public async Task Update(T entity)
    {
        Set.Update(entity);
    }

    public void Remove(T entity)
    {
        Set.Remove(entity);
    }
}
