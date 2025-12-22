using Ardalis.Specification;
using simpleShare_backend.Common;
using simpleShare_backend.Models;
using simpleShare_backend.Models.Base;

namespace simpleShare_backend.Infrastructure;

public interface IRepositoryAsync
{
    Task<PageResponse<T>> GetAllAsync<T, TId>(
        int skip,
        int take,
        ISpecification<T>? spec = null, CancellationToken ct = default
    ) where T : BaseModel<TId>;

    Task<T> GetByIdAsync<T, TId>(
        TId id, ISpecification<T>? spec = null,
        CancellationToken ct = default
    ) where T : BaseModel<TId>;

    Task<T> CreateAsync<T, TId>(T entity)
    where T : BaseModel<TId>;

    Task<T> UpdateAsync<T, TId>(T entity)
    where T : BaseModel<TId>;

    Task<T> DeleteAsync<T, TId>(TId entityId)
    where T : BaseModel<TId>, IDeletable;

    Task<int> SaveChangesAsync();
}
