using Claims.Domain;

namespace Claims.Application.Abstractions;

/// <summary>Stores entities independently of the database implementation.</summary>
/// <typeparam name="TEntity">The entity type that is stored.</typeparam>
public interface IRepository<TEntity> where TEntity : class, IEntity
{
    /// <summary>Returns one page of entities, ordered by id so that pages are stable.</summary>
    Task<IReadOnlyList<TEntity>> GetPageAsync(Page page, CancellationToken cancellationToken);

    /// <summary>Returns the entity with the given id, or <c>null</c> if it does not exist.</summary>
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Stores a new entity.</summary>
    Task AddAsync(TEntity entity, CancellationToken cancellationToken);

    /// <summary>Deletes the entity with the given id.</summary>
    /// <returns><c>true</c> if the entity existed and was deleted, otherwise <c>false</c>.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
