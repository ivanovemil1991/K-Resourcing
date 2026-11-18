using Claims.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Claims.Infrastructure;

/// <summary>Checks the databases needed to handle requests.</summary>
public sealed class DatabaseHealthCheck(AuditContext auditContext, MongoClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await auditContext.Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Unhealthy("Audit database is unavailable.");

            await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1), cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy($"Database check failed ({exception.GetType().Name}).");
        }
    }
}
