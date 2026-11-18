using Claims.Application.Abstractions;
using Claims.Domain;

namespace Claims.Tests.Fakes;

/// <summary>Answers claim lookups from an in-memory claim repository.</summary>
public sealed class FakeClaimReferences(InMemoryRepository<Claim> claims, InMemoryRepository<Cover>? covers = null) : IClaimReferences
{
    public Task<bool> AnyForCoverAsync(Guid coverId, CancellationToken cancellationToken) =>
        Task.FromResult(claims.Stored.Any(claim => claim.CoverId == coverId));

    public Task<bool> CoverExistsAsync(Guid coverId, CancellationToken cancellationToken) =>
        Task.FromResult(covers is null || covers.Stored.Any(cover => cover.Id == coverId));
}