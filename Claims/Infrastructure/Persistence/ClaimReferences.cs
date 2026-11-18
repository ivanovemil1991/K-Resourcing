using Claims.Application.Abstractions;
using Claims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Claims.Infrastructure.Persistence;

/// <summary>Looks up claims of a cover in MongoDB.</summary>
public sealed class ClaimReferences(ClaimsDbContext dbContext) : IClaimReferences
{
    /// <inheritdoc />
    public Task<bool> AnyForCoverAsync(Guid coverId, CancellationToken cancellationToken) =>
        dbContext.Set<Claim>().AnyAsync(claim => claim.CoverId == coverId, cancellationToken);
}