using Claims.Application.Abstractions;
using Claims.Domain;

namespace Claims.Application.Covers;

/// <summary>Command: deletes the cover with the given id.</summary>
public sealed record DeleteCoverCommand(Guid Id);

/// <summary>What happened to a cover that was asked to be deleted.</summary>
public enum DeleteCoverResult
{
    Deleted,
    NotFound,

    /// <summary>Claims still belong to the cover, so deleting it would leave them without one.</summary>
    InUse
}

/// <summary>Deletes a cover, unless claims still belong to it.</summary>
public sealed class DeleteCoverCommandHandler(
    IRepository<Cover> covers,
    IClaimReferences claimReferences,
    IAuditer auditer,
    ILogger<DeleteCoverCommandHandler> logger) : ICommandHandler<DeleteCoverCommand, DeleteCoverResult>
{
    /// <inheritdoc />
    public async Task<DeleteCoverResult> HandleAsync(DeleteCoverCommand command, CancellationToken cancellationToken)
    {
        if (await claimReferences.AnyForCoverAsync(command.Id, cancellationToken))
        {
            logger.LogWarning("Cover {CoverId} not deleted because claims still use it", command.Id);
            return DeleteCoverResult.InUse;
        }

        auditer.AuditCover(command.Id, AuditAction.Deleted);
        if (!await covers.DeleteAsync(command.Id, cancellationToken))
        {
            logger.LogInformation("Cover not found for deletion");
            return DeleteCoverResult.NotFound;
        }

        logger.LogInformation("Cover deleted {CoverId}", command.Id);
        return DeleteCoverResult.Deleted;
    }
}