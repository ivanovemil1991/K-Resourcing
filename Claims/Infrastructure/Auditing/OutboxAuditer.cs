using Claims.Application.Abstractions;
using Claims.Infrastructure.Persistence;

namespace Claims.Infrastructure.Auditing;

/// <summary>Stages audit events in the same MongoDB transaction as entity changes.</summary>
public sealed class OutboxAuditer(
    ClaimsDbContext dbContext, TimeProvider timeProvider, ILogger<OutboxAuditer> logger) : IAuditer
{
    public void AuditClaim(string claimId, AuditAction action) => Stage("Claim", claimId, action);
    public void AuditCover(string coverId, AuditAction action) => Stage("Cover", coverId, action);

    private void Stage(string entity, string id, AuditAction action)
    {
        var entry = new OutboxEntry
        {
            Id = Guid.NewGuid().ToString(),
            Entity = entity,
            EntityId = id,
            HttpRequestType = action switch
            {
                AuditAction.Created => "POST",
                AuditAction.Deleted => "DELETE",
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            },
            OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };
        dbContext.Add(entry);
        logger.LogInformation("Audit event staged {AuditId}", entry.Id);
    }
}
