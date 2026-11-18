using Claims.Application.Abstractions;
using Claims.Application.Validation;
using Claims.Domain;

namespace Claims.Application.Claims;

/// <inheritdoc />
public sealed class ClaimService(
    IRepository<Claim> claims,
    IRepository<Cover> covers,
    IAuditer auditer,
    ILogger<ClaimService> logger) : IClaimService
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Claim>> GetAllAsync(CancellationToken cancellationToken) =>
        claims.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Claim?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        var claim = await claims.GetByIdAsync(id, cancellationToken);
        if (claim is null)
            logger.LogInformation("Claim not found");
        return claim;
    }

    /// <inheritdoc />
    public async Task<Claim> CreateAsync(CreateClaimRequest request, CancellationToken cancellationToken)
    {
        var cover = await covers.GetByIdAsync(request.CoverId, cancellationToken);
        ValidationException.ThrowIfAny(ClaimValidator.Validate(request, cover));

        var claim = new Claim
        {
            Id = Guid.NewGuid().ToString(),
            CoverId = request.CoverId,
            Created = request.Created,
            Name = request.Name,
            Type = request.Type,
            DamageCost = request.DamageCost
        };

        auditer.AuditClaim(claim.Id, AuditAction.Created);
        await claims.AddAsync(claim, cancellationToken);
        logger.LogInformation("Claim created {ClaimId}", claim.Id);
        return claim;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        auditer.AuditClaim(id, AuditAction.Deleted);
        var deleted = await claims.DeleteAsync(id, cancellationToken);
        if (deleted && Guid.TryParse(id, out var claimId))
            logger.LogInformation("Claim deleted {ClaimId}", claimId);
        else if (deleted)
            logger.LogInformation("Claim deleted");
        else
            logger.LogInformation("Claim not found for deletion");
        return deleted;
    }
}
