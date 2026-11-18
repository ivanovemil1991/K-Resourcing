namespace Claims.Application.Abstractions;

/// <summary>Answers whether claims still point at a cover; MongoDB has no foreign keys to enforce this.</summary>
public interface IClaimReferences
{
    /// <summary>Returns <c>true</c> if at least one claim belongs to the cover.</summary>
    Task<bool> AnyForCoverAsync(Guid coverId, CancellationToken cancellationToken);
}