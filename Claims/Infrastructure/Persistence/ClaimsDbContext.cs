using Claims.Domain;
using Claims.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;
using MongoDB.EntityFrameworkCore.Extensions;

namespace Claims.Infrastructure.Persistence;

/// <summary>Maps claims and covers to MongoDB.</summary>
/// <remarks>Mappings preserve existing document names without adding MongoDB dependencies to the domain.</remarks>
public sealed class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OutboxEntry>(entry =>
        {
            entry.ToCollection("auditOutbox");
            entry.Property(e => e.Id).HasElementName("_id");
        });

        modelBuilder.Entity<Claim>(claim =>
        {
            claim.ToCollection("claims");
            claim.Property(c => c.CoverId).HasElementName("coverId");
            claim.Property(c => c.Created).HasElementName("created");
            claim.Property(c => c.Name).HasElementName("name");
            claim.Property(c => c.Type).HasElementName("claimType");
            claim.Property(c => c.DamageCost).HasElementName("damageCost");
        });

        modelBuilder.Entity<Cover>(cover =>
        {
            cover.ToCollection("covers");
            cover.Property(c => c.StartDate).HasElementName("startDate");
            cover.Property(c => c.EndDate).HasElementName("endDate");
            // The original schema stored the cover type as "claimType". The name is kept for compatibility.
            cover.Property(c => c.Type).HasElementName("claimType");
            cover.Property(c => c.Premium).HasElementName("premium");
        });
    }
}
