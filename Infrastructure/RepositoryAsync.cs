using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using simpleShare_backend.Common;
using simpleShare_backend.Data;
using simpleShare_backend.Models;
using simpleShare_backend.Models.Base;

namespace simpleShare_backend.Infrastructure;

public class RepositoryAsync(ApplicationDbContext dbContext) : IRepositoryAsync
{

    public async Task<T> GetByIdAsync<T, TId>(
            TId id, ISpecification<T>? spec = null, CancellationToken ct = default
    ) where T : BaseModel<TId>
    {
        IQueryable<T> query;
        if (spec is not null)
        {
            query = SpecificationEvaluator.Default.GetQuery(
                query: dbContext.Set<T>(),
                specification: spec
            );
        }
        else
        {
            query = dbContext.Set<T>();
        }

        T? entity = await query.FirstOrDefaultAsync(e => e.Id!.Equals(id)) ??
            throw new Exception("Entity not found!");

        return entity;
    }

    public async Task<PageResponse<T>> GetAllAsync<T, TId>(
        int skip,
        int take,
        ISpecification<T>? spec = null,
        CancellationToken ct = default
    ) where T : BaseModel<TId>
    {
        IQueryable<T> query;
        if (spec is not null)
        {
            query = SpecificationEvaluator.Default.GetQuery(
                query: dbContext.Set<T>(),
                specification: spec
            );
        }
        else
        {
            query = dbContext.Set<T>();
        }

        List<T> result;
        int totalCount;

        try
        {
            totalCount = await query.CountAsync(ct);

            if (take == -1)
                result = await query.ToListAsync(ct);

            else
            {
                result = await query
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync(ct);
            }

            return new PageResponse<T>(result, totalCount, skip, take);

        }
        catch (Exception e)
        {
            totalCount = 0;
            throw new Exception("Error fetching entities: " + e.Message);
        }
    }

    public async Task<T> CreateAsync<T, TId>(T entity) where T : BaseModel<TId>
    {
        _ = await dbContext.Set<T>().AddAsync(entity);
        return entity;
    }

    public async Task<T> UpdateAsync<T, TId>(T entity) where T : BaseModel<TId>
    {

        if (dbContext.Entry(entity).State == EntityState.Unchanged)
            throw new Exception("Nothing to save!");

        T dbEntity = await dbContext.Set<T>().FindAsync(entity.Id)
            ?? throw new Exception("Entity not found!");

        dbContext.Entry(dbEntity).CurrentValues.SetValues(entity);
        return entity;
    }

    public async Task<T> DeleteAsync<T, TId>(TId entityId) where T : BaseModel<TId>, IDeletable
    {
        T dbEntity = await dbContext.Set<T>().FindAsync(entityId)
            ?? throw new Exception("Entity not found!");

        if (dbEntity.DeletedAt != null)
            throw new Exception("Entity already deleted!");

        dbEntity.DeletedAt = DateTime.UtcNow;
        return dbEntity;
    }

    public async Task<int> SaveChangesAsync()
    {
        return await dbContext.SaveChangesAsync();
    }

}
