using Claims.Application.Abstractions;

namespace Claims.Tests.Fakes;

/// <summary>Records audit calls for tests.</summary>
public sealed class RecordingAuditer : IAuditer
{
    public List<(string Entity, string Id, AuditAction Action)> Entries { get; } = [];

    public void AuditClaim(string claimId, AuditAction action) => Entries.Add(("Claim", claimId, action));

    public void AuditCover(string coverId, AuditAction action) => Entries.Add(("Cover", coverId, action));
}
