using Claims.Application.Abstractions;
using Claims.Domain;

namespace Claims.Application.Claims;

/// <summary>Command: deletes the claim with the given id.</summary>
public sealed record DeleteClaimCommand(Guid Id);

/// <summary>Deletes a claim. Returns <c>true</c> if the claim existed.</summary>
public sealed class DeleteClaimCommandHandler(
    IRepository<Claim> claims,
    IAuditer auditer,
    ILogger<DeleteClaimCommandHandler> logger) : ICommandHandler<DeleteClaimCommand, bool>
{
    /// <inheritdoc />
    public async Task<bool> HandleAsync(DeleteClaimCommand command, CancellationToken cancellationToken)
    {
        auditer.AuditClaim(command.Id, AuditAction.Deleted);
        var deleted = await claims.DeleteAsync(command.Id, cancellationToken);
        if (deleted)
            logger.LogInformation("Claim deleted {ClaimId}", command.Id);

        else
            logger.LogInformation("Claim not found for deletion");
        return deleted;
    }
}
