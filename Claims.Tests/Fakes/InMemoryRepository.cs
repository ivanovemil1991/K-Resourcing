using Claims.Application.Abstractions;
using Claims.Domain;

namespace Claims.Tests.Fakes;

/// <summary>A dictionary-backed repository for tests.</summary>
public sealed class InMemoryRepository<TEntity> : IRepository<TEntity> where TEntity : class, IEntity
{
    private readonly Dictionary<string, TEntity> _entities;

    public InMemoryRepository(params TEntity[] seed) =>
        _entities = seed.ToDictionary(entity => entity.Id);

    public IReadOnlyCollection<TEntity> Stored => _entities.Values;

    public Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TEntity>>(_entities.Values.ToList());

    public Task<TEntity?> GetByIdAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(_entities.GetValueOrDefault(id));

    public Task AddAsync(TEntity entity, CancellationToken cancellationToken)
    {
        _entities.Add(entity.Id, entity);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(_entities.Remove(id));
}
