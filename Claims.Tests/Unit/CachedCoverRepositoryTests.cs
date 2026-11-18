using Claims.Application.Abstractions;
using Claims.Domain;
using Claims.Infrastructure.Persistence;
using Claims.Tests.Fakes;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Claims.Tests.Unit;

public class CachedCoverRepositoryTests
{
    private static readonly Cover Cover = new()
    {
        Id = TestData.Id(1), StartDate = new DateTime(2030, 1, 1), EndDate = new DateTime(2030, 2, 1), Type = CoverType.Yacht, Premium = 1
    };

    private readonly CountingRepository _inner = new(new InMemoryRepository<Cover>(Cover));
    private readonly CachedCoverRepository _cached;

    public CachedCoverRepositoryTests() => _cached = NewCache(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

    private CachedCoverRepository NewCache(IDistributedCache cache) =>
        new(_inner, cache, NullLogger<CachedCoverRepository>.Instance);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_unreadable_cache_entry_is_replaced_from_the_database()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        await cache.SetStringAsync(Cover.Id.ToString("N"), "not json", Token);

        var cover = await NewCache(cache).GetByIdAsync(Cover.Id, Token);

        Assert.Equal(Cover.Id, cover!.Id);
        Assert.Equal(1, _inner.GetByIdCalls);
    }

    [Fact]
    public async Task Repeated_lookups_read_the_database_once()
    {
        await _cached.GetByIdAsync(Cover.Id, Token);
        await _cached.GetByIdAsync(Cover.Id, Token);

        Assert.Equal(1, _inner.GetByIdCalls);
    }

    [Fact]
    public async Task A_cover_served_from_the_cache_equals_the_stored_cover()
    {
        await _cached.GetByIdAsync(Cover.Id, Token);

        var fromCache = await _cached.GetByIdAsync(Cover.Id, Token);

        Assert.Equal(Cover.Id, fromCache!.Id);
        Assert.Equal((Cover.StartDate, Cover.EndDate, Cover.Type, Cover.Premium),
            (fromCache.StartDate, fromCache.EndDate, fromCache.Type, fromCache.Premium));
    }

    [Fact]
    public async Task A_created_cover_is_cached_immediately()
    {
        var created = new Cover { Id = TestData.Id(2), StartDate = Cover.StartDate, EndDate = Cover.EndDate, Type = CoverType.Tanker, Premium = 2 };

        await _cached.AddAsync(created, Token);
        await _cached.GetByIdAsync(created.Id, Token);

        Assert.Equal(0, _inner.GetByIdCalls);
    }

    [Fact]
    public async Task A_deleted_cover_is_no_longer_served_from_the_cache()
    {
        await _cached.GetByIdAsync(Cover.Id, Token);

        await _cached.DeleteAsync(Cover.Id, Token);

        Assert.Null(await _cached.GetByIdAsync(Cover.Id, Token));
    }

    [Fact]
    public async Task Missing_covers_are_not_cached()
    {
        await _cached.GetByIdAsync(TestData.Id(404), Token);
        await _cached.GetByIdAsync(TestData.Id(404), Token);

        Assert.Equal(2, _inner.GetByIdCalls);
    }

    [Fact]
    public async Task A_failing_cache_falls_back_to_the_database()
    {
        var cached = NewCache(new UnavailableCache());

        Assert.Same(Cover, await cached.GetByIdAsync(Cover.Id, Token));
        Assert.True(await cached.DeleteAsync(Cover.Id, Token));
    }

    private sealed class UnavailableCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new TimeoutException();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw new TimeoutException();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new TimeoutException();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw new TimeoutException();
        public void Refresh(string key) => throw new TimeoutException();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw new TimeoutException();
        public void Remove(string key) => throw new TimeoutException();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw new TimeoutException();
    }

    private sealed class CountingRepository(IRepository<Cover> inner) : IRepository<Cover>
    {
        public int GetByIdCalls { get; private set; }

        public Task<IReadOnlyList<Cover>> GetPageAsync(Page page, CancellationToken cancellationToken) =>
            inner.GetPageAsync(page, cancellationToken);

        public Task<Cover?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            GetByIdCalls++;
            return inner.GetByIdAsync(id, cancellationToken);
        }

        public Task AddAsync(Cover entity, CancellationToken cancellationToken) => inner.AddAsync(entity, cancellationToken);

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => inner.DeleteAsync(id, cancellationToken);
    }
}
