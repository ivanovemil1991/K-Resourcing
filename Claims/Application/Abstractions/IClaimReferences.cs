namespace Claims.Application.Abstractions;

/// <summary>Checks the link between claims and covers; MongoDB has no foreign keys to enforce it.</summary>
public interface IClaimReferences
{
    /// <summary>Returns <c>true</c> if at least one claim belongs to the cover.</summary>
    Task<bool> AnyForCoverAsync(Guid coverId, CancellationToken cancellationToken);

    /// <summary>Returns <c>true</c> if the cover exists in the database. Never answered from a cache.</summary>
    Task<bool> CoverExistsAsync(Guid coverId, CancellationToken cancellationToken);
}