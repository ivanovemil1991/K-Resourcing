namespace Claims.Infrastructure.Auditing;

/// <summary>One row in the cover audit trail: a cover was created or deleted.</summary>
public class CoverAudit
{
    /// <summary>Database-generated key.</summary>
    public int Id { get; set; }

    /// <summary>Id of the cover that changed.</summary>
    public required string CoverId { get; set; }

    /// <summary>When the change happened (UTC).</summary>
    public DateTime Created { get; set; }

    /// <summary>The HTTP verb that caused the change: <c>POST</c> or <c>DELETE</c>.</summary>
    public required string HttpRequestType { get; set; }
}
