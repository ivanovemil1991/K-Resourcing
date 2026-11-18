using Claims.Application.Abstractions;
using Claims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Claims.Infrastructure.Persistence;

/// <summary>A MongoDB repository backed by <see cref="ClaimsDbContext"/>.</summary>
public sealed class EfRepository<TEntity>(ClaimsDbContext dbContext) : IRepository<TEntity>
    where TEntity : class, IEntity
{
    private DbSet<TEntity> Entities => dbContext.Set<TEntity>();

    /// <inheritdoc />
    public async Task<IReadOnlyList<TEntity>> GetPageAsync(Page page, CancellationToken cancellationToken) =>
        await Entities.AsNoTracking().OrderBy(entity => entity.Id).Skip(page.Skip).Take(page.Take).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<TEntity?> GetByIdAsync(string id, CancellationToken cancellationToken) =>
        Entities.FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken)
    {
        Entities.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        var entity = await GetByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            // The handler already staged an audit event; save it so the delete attempt is still audited.
            await dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }

        Entities.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
