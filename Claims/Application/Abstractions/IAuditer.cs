namespace Claims.Application.Abstractions;

/// <summary>Stages audit entries for the next repository write.</summary>
/// <remarks>The entry and the entity change must be saved together.</remarks>
public interface IAuditer
{
    /// <summary>Records that a claim was created or deleted.</summary>
    void AuditClaim(Guid claimId, AuditAction action);

    /// <summary>Records that a cover was created or deleted.</summary>
    void AuditCover(Guid coverId, AuditAction action);
}

/// <summary>The kind of change that is audited.</summary>
public enum AuditAction
{
    Created,
    Deleted
}
