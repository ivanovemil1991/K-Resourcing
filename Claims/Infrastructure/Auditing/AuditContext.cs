using Microsoft.EntityFrameworkCore;

namespace Claims.Infrastructure.Auditing;

/// <summary>Stores claim and cover audit records in SQL Server using EF Core migrations.</summary>
public class AuditContext(DbContextOptions<AuditContext> options) : DbContext(options)
{
    /// <summary>Audit records for claims.</summary>
    public DbSet<ClaimAudit> ClaimAudits { get; set; } = null!;

    /// <summary>Audit records for covers.</summary>
    public DbSet<CoverAudit> CoverAudits { get; set; } = null!;

    public DbSet<AuditReceipt> AuditReceipts { get; set; } = null!;
}

/// <summary>Records an outbox event already saved to SQL.</summary>
public sealed class AuditReceipt
{
    public required string Id { get; set; }
}
