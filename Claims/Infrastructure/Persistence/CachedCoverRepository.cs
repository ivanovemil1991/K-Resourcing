using System.Text.Json;
using Claims.Application.Abstractions;
using Claims.Domain;
using Microsoft.Extensions.Caching.Distributed;

namespace Claims.Infrastructure.Persistence;

/// <summary>Caches cover lookups, because every claim creation reads its cover and covers are never updated.</summary>
/// <remarks>
/// The cache is shared by all instances when Redis is configured, and deleting a cover evicts it for everyone.
/// The cache is only an optimisation: if it fails, the error is logged and the database is used.
/// </remarks>
public sealed class CachedCoverRepository(
    IRepository<Cover> inner,
    IDistributedCache cache,
    ILogger<CachedCoverRepository> logger) : IRepository<Cover>
{
    /// <summary>How long a cover is served from the cache, in case an eviction is missed.</summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private static readonly DistributedCacheEntryOptions Expiration = new() { AbsoluteExpirationRelativeToNow = CacheDuration };

    /// <inheritdoc />
    public Task<IReadOnlyList<Cover>> GetPageAsync(Page page, CancellationToken cancellationToken) =>
        inner.GetPageAsync(page, cancellationToken);

    /// <inheritdoc />
    public async Task<Cover?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var cached = await TryCacheAsync(async () => await cache.GetStringAsync(CacheKey(id), cancellationToken));
        if (cached is not null)
            return JsonSerializer.Deserialize<Cover>(cached);

        var cover = await inner.GetByIdAsync(id, cancellationToken);
        if (cover is not null)
            await StoreAsync(cover, cancellationToken);
        return cover;
    }

    /// <inheritdoc />
    public async Task AddAsync(Cover entity, CancellationToken cancellationToken)
    {
        await inner.AddAsync(entity, cancellationToken);
        await StoreAsync(entity, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await inner.DeleteAsync(id, cancellationToken);
        await TryCacheAsync(async () =>
        {
            await cache.RemoveAsync(CacheKey(id), cancellationToken);
            return true;
        });
        return deleted;
    }

    /// <summary>Writes the cover to the cache, replacing any previous entry, so the cache is rebuilt whenever a cover changes.</summary>
    private Task StoreAsync(Cover cover, CancellationToken cancellationToken) =>
        TryCacheAsync(async () =>
        {
            await cache.SetStringAsync(CacheKey(cover.Id), JsonSerializer.Serialize(cover), Expiration, cancellationToken);
            return true;
        });

    private static string CacheKey(Guid id) => id.ToString("N");

    private async Task<T?> TryCacheAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Cover cache unavailable ({ExceptionType}); using the database", exception.GetType().Name);
            return default;
        }
    }
}
