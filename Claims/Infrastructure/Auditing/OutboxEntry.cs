namespace Claims.Infrastructure.Auditing;

/// <summary>A durable audit event stored with an entity change.</summary>
public sealed class OutboxEntry
{
    public required string Id { get; set; }
    public required string Entity { get; set; }
    public required string EntityId { get; set; }
    public required string HttpRequestType { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
