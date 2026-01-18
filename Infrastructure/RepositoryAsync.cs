using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using simpleShare_backend.Common;
using simpleShare_backend.Common.Exceptions;
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
        IQueryable<T> query = ApplyQuerySpecification<T, TId>(spec);
        // TODO implement query filters here with ignore query filters logic
        query = FilterSoftDeleted<T, TId>(query);

        T entity = await query.FirstOrDefaultAsync(e => e.Id!.Equals(id), cancellationToken: ct)
            ?? throw new NotFoundAppException("entity_not_found", "Entity not found!");

        return entity;
    }

    public async Task<T?> GetByIdDefaultAsync<T, TId>(
        TId id, ISpecification<T>? spec = null,
        bool ignoreDeleted = false,
        CancellationToken ct = default
    ) where T : BaseModel<TId>
    {
        IQueryable<T> query = ApplyQuerySpecification<T, TId>(spec);

        if (ignoreDeleted && typeof(IDeletable).IsAssignableFrom(typeof(T)))
            query = FilterSoftDeleted<T, TId>(query);


        T? entity = await query.FirstOrDefaultAsync(e => e.Id!.Equals(id), cancellationToken: ct);

        return entity;
    }

    public async Task<T?> GetSingleDefaultAsync<T, TId>(
        ISpecification<T> spec,
        bool ignoreDeleted = false,
        CancellationToken ct = default
    ) where T : BaseModel<TId>
    {
        IQueryable<T> query = SpecificationEvaluator.Default.GetQuery(
            query: dbContext.Set<T>(),
            specification: spec
        );

        if (ignoreDeleted && typeof(IDeletable).IsAssignableFrom(typeof(T)))
            query = query.Where(e => ((IDeletable)e).DeletedAt == null);

        T? entity = await query.FirstOrDefaultAsync(ct);
        return entity;
    }

    public async Task<T?> GetSingleDefaultAsync<T>(
        ISpecification<T> spec,
        bool ignoreDeleted = false,
        CancellationToken ct = default
    ) where T : class
    {
        IQueryable<T> query = SpecificationEvaluator.Default.GetQuery(
            query: dbContext.Set<T>(),
            specification: spec
        );

        if (ignoreDeleted && typeof(IDeletable).IsAssignableFrom(typeof(T)))
            query = query.Where(e => ((IDeletable)e).DeletedAt == null);

        T? entity = await query.FirstOrDefaultAsync(ct);
        return entity;
    }

    public async Task<PageResponse<T>> GetAllAsync<T, TId>(
        int skip,
        int take,
        ISpecification<T>? spec = null,
        CancellationToken ct = default
    ) where T : BaseModel<TId>
    {
        var query = this.ApplyQuerySpecification<T, TId>(spec);

        if (typeof(IDeletable).IsAssignableFrom(typeof(T)))
            query = query.Where(e => ((IDeletable)e).DeletedAt == null);

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
            throw new ConflictAppException("entity_fetch_error", "Error fetching entities: " + e.Message);
        }
    }

    public async Task<ICollection<T>?> GetAllDefaultAsync<T, TId>(
        ISpecification<T>? spec = null,
        bool ignoreDeleted = false,
        CancellationToken ct = default
    ) where T : BaseModel<TId>
    {
        var query = ApplyQuerySpecification<T, TId>(spec);

        if (ignoreDeleted && typeof(IDeletable).IsAssignableFrom(typeof(T)))
            query = query.Where(e => ((IDeletable)e).DeletedAt == null);

        return await query.ToListAsync(cancellationToken: ct);
    }

    public async Task<T> CreateAsync<T, TId>(T entity) where T : BaseModel<TId>
    {
        _ = await dbContext.Set<T>().AddAsync(entity);
        return entity;
    }

    public async Task<T> UpdateAsync<T, TId>(T entity) where T : BaseModel<TId>
    {

        if (dbContext.Entry(entity).State == EntityState.Unchanged)
            throw new ConflictAppException("not_data", "Nothing to save!");

        T dbEntity = await dbContext.Set<T>().FindAsync(entity.Id)
            ?? throw new NotFoundAppException("entity_not_found", "Entity not found!");

        dbContext.Entry(dbEntity).CurrentValues.SetValues(entity);
        return entity;
    }

    public async Task<T> DeleteAsync<T, TId>(TId entityId) where T : BaseModel<TId>, IDeletable
    {
        T dbEntity = await dbContext.Set<T>().FindAsync(entityId)
            ?? throw new NotFoundAppException("entity_not_found", "Entity not found!");

        if (dbEntity.DeletedAt != null)
            throw new ConflictAppException("entity_deleted", "Entity already deleted!");

        dbEntity.DeletedAt = DateTime.UtcNow;
        return dbEntity;
    }

    public async Task<int> SaveChangesAsync()
    {
        return await dbContext.SaveChangesAsync();
    }

    public async Task<T> RestoreAsync<T, TId>(TId entityId) where T : BaseModel<TId>, IDeletable
    {
        T dbEntity = await dbContext.Set<T>().FindAsync(entityId)
            ?? throw new NotFoundAppException("entity_not_found", "Entity not found!");

        if (dbEntity.DeletedAt == null)
            throw new ConflictAppException("entity_not_deleted", "Entity is not deleted!");

        dbEntity.DeletedAt = null;
        return dbEntity;
    }

    public async Task<T> CreateWithCompositeId<T>(T entity) where T : class
    {
        _ = await dbContext.Set<T>().AddAsync(entity);
        return entity;
    }

    public T UpdateWithCompositeId<T>(T entity) where T : class
    {
        _ = dbContext.Set<T>().Update(entity);
        return entity;
    }

    private IQueryable<T> ApplyQuerySpecification<T, TId>(ISpecification<T>? spec) where T : BaseModel<TId>
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
            query = dbContext.Set<T>();

        return query;
    }

    private IQueryable<T> FilterSoftDeleted<T, TId>(IQueryable<T> query) where T : BaseModel<TId>
    {
        if (typeof(IDeletable).IsAssignableFrom(typeof(T)))
            return query.Where(e => ((IDeletable)e).DeletedAt == null);

        return query;
    }

    public async Task<ICollection<T>> AddRangeAsync<T>(ICollection<T> items) where T : class
    {
        await dbContext.AddRangeAsync(items);
        return items;
    }
}
