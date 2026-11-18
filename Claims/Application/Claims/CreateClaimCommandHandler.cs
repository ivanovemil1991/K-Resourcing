using Claims.Application.Abstractions;
using Claims.Domain;
using FluentValidation;

namespace Claims.Application.Claims;

/// <summary>Command: validates the claim against its cover and stores it.</summary>
/// <exception cref="ValidationException">The command breaks a business rule.</exception>
public sealed class CreateClaimCommandHandler(
    IRepository<Claim> claims,
    IValidator<CreateClaimCommand> validator,
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
        logger.LogInformation("Claim created {ClaimId}", claim.Id);
        return claim;
    }
}
