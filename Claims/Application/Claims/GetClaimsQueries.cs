using Claims.Application.Abstractions;
using Claims.Domain;

namespace Claims.Application.Claims;

/// <summary>Query: one page of claims.</summary>
public sealed record GetClaimsQuery(Page Page);

/// <summary>Returns one page of claims.</summary>
public sealed class GetClaimsQueryHandler(IRepository<Claim> claims) : IQueryHandler<GetClaimsQuery, IReadOnlyList<Claim>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Claim>> HandleAsync(GetClaimsQuery query, CancellationToken cancellationToken) =>
        claims.GetPageAsync(query.Page, cancellationToken);
}

/// <summary>Query: the claim with the given id.</summary>
public sealed record GetClaimByIdQuery(Guid Id);

/// <summary>Returns the claim, or <c>null</c> if it does not exist.</summary>
public sealed class GetClaimByIdQueryHandler(
    IRepository<Claim> claims,
    ILogger<GetClaimByIdQueryHandler> logger) : IQueryHandler<GetClaimByIdQuery, Claim?>
{
    /// <inheritdoc />
    public async Task<Claim?> HandleAsync(GetClaimByIdQuery query, CancellationToken cancellationToken)
    {
        var claim = await claims.GetByIdAsync(query.Id, cancellationToken);
        if (claim is null)
            logger.LogInformation("Claim not found");
        return claim;
    }
}
