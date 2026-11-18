using Claims.Application.Abstractions;
using Claims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Claims.Infrastructure.Persistence;

/// <summary>Looks up claims and covers directly in MongoDB.</summary>
public sealed class ClaimReferences(ClaimsDbContext dbContext) : IClaimReferences
{
    /// <inheritdoc />
    public Task<bool> AnyForCoverAsync(Guid coverId, CancellationToken cancellationToken) =>
        dbContext.Set<Claim>().AnyAsync(claim => claim.CoverId == coverId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> CoverExistsAsync(Guid coverId, CancellationToken cancellationToken) =>
        dbContext.Set<Cover>().AnyAsync(cover => cover.Id == coverId, cancellationToken);
}