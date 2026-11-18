using Claims.Application.Abstractions;
using Claims.Domain;

namespace Claims.Application.Covers;

/// <summary>Command: deletes the cover with the given id.</summary>
public sealed record DeleteCoverCommand(Guid Id);

/// <summary>Deletes a cover. Returns <c>true</c> if the cover existed.</summary>
public sealed class DeleteCoverCommandHandler(
    IRepository<Cover> covers,
    IAuditer auditer,
    ILogger<DeleteCoverCommandHandler> logger) : ICommandHandler<DeleteCoverCommand, bool>
{
    /// <inheritdoc />
    public async Task<bool> HandleAsync(DeleteCoverCommand command, CancellationToken cancellationToken)
    {
        auditer.AuditCover(command.Id, AuditAction.Deleted);
        var deleted = await covers.DeleteAsync(command.Id, cancellationToken);
        if (deleted)
            logger.LogInformation("Cover deleted {CoverId}", command.Id);

        else
            logger.LogInformation("Cover not found for deletion");
        return deleted;
    }
}
