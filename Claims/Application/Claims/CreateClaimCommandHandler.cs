using Claims.Application.Abstractions;
using Claims.Domain;
using FluentValidation;
using FluentValidation.Results;

namespace Claims.Application.Claims;

/// <summary>Command: validates the claim against its cover and stores it.</summary>
/// <remarks>
/// A cover can be deleted between validation and the insert, and the two writes cannot share a transaction.
/// The cover delete refuses covers that have claims, so after storing the claim the cover is checked once more
/// against the database: if it is gone, the claim is withdrawn again instead of being left without a cover.
/// </remarks>
/// <exception cref="ValidationException">The command breaks a business rule.</exception>
public sealed class CreateClaimCommandHandler(
    IRepository<Claim> claims,
    IValidator<CreateClaimCommand> validator,
    IClaimReferences claimReferences,
    IAuditer auditer,
    ILogger<CreateClaimCommandHandler> logger) : ICommandHandler<CreateClaimCommand, Claim>
{
    /// <inheritdoc />
    public async Task<Claim> HandleAsync(CreateClaimCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var claim = new Claim
        {
            Id = Guid.NewGuid(),
            CoverId = command.CoverId,
            Created = command.Created,
            Name = command.Name,
            Type = command.Type,
            DamageCost = command.DamageCost
        };

        auditer.AuditClaim(claim.Id, AuditAction.Created);
        await claims.AddAsync(claim, cancellationToken);
        await WithdrawIfCoverWasDeletedAsync(claim, cancellationToken);
        logger.LogInformation("Claim created {ClaimId}", claim.Id);
        return claim;
    }

    private async Task WithdrawIfCoverWasDeletedAsync(Claim claim, CancellationToken cancellationToken)
    {
        if (await claimReferences.CoverExistsAsync(claim.CoverId, cancellationToken))
        {
            return;
        }

        auditer.AuditClaim(claim.Id, AuditAction.Deleted);
        await claims.DeleteAsync(claim.Id, cancellationToken);
        logger.LogWarning("Claim {ClaimId} withdrawn because cover {CoverId} was deleted meanwhile", claim.Id, claim.CoverId);
        throw new ValidationException([new ValidationFailure(nameof(claim.CoverId), $"Cover '{claim.CoverId}' does not exist.")]);
    }
}
