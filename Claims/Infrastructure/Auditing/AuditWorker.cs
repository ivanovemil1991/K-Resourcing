using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Claims.Infrastructure.Auditing;

/// <summary>Relays durable MongoDB audit events to SQL Server.</summary>
public sealed class AuditWorker(
    MongoClient mongoClient,
    IOptions<DatabaseOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<AuditWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var collection = mongoClient.GetDatabase(options.Value.MongoDatabaseName)
            .GetCollection<OutboxEntry>("auditOutbox");
        string? cursor = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var filter = cursor is null
                    ? Builders<OutboxEntry>.Filter.Empty
                    : Builders<OutboxEntry>.Filter.Gt(entry => entry.Id, cursor);
                var entries = await collection.Find(filter).SortBy(entry => entry.Id)
                    .Limit(50).ToListAsync(stoppingToken);
                if (entries.Count == 0)
                {
                    cursor = null;
                    await Task.Delay(TimeSpan.FromSeconds(options.Value.AuditPollSeconds), stoppingToken);
                    continue;
                }

                var delivered = 0;
                var duplicates = 0;
                var failures = 0;
                string? firstFailedId = null;
                string? firstErrorType = null;
                foreach (var entry in entries)
                {
                    // Move past failed entries so they cannot starve the rest of the outbox.
                    cursor = entry.Id;
                    try
                    {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        var audit = scope.ServiceProvider.GetRequiredService<AuditContext>();
                        var inserted = await SaveAsync(audit, entry, stoppingToken);
                        await collection.DeleteOneAsync(item => item.Id == entry.Id, stoppingToken);
                        if (inserted)
                        {
                            delivered++;
                            logger.LogDebug("Audit event delivered {AuditId}", Guid.TryParse(entry.Id, out var savedId) ? savedId.ToString() : "invalid");
                        }
                        else
                        {
                            duplicates++;
                            logger.LogDebug("Duplicate audit event skipped {AuditId}", Guid.TryParse(entry.Id, out var duplicateId) ? duplicateId.ToString() : "invalid");
                        }
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        failures++;
                        firstFailedId ??= Guid.TryParse(entry.Id, out var auditId) ? auditId.ToString() : "invalid";
                        firstErrorType ??= exception.GetType().Name;
                    }
                }
                if (delivered > 0 || duplicates > 0)
                    logger.LogInformation("Audit relay delivered {DeliveredCount} events and skipped {DuplicateCount} duplicates",
                        delivered, duplicates);
                if (failures > 0)
                    logger.LogError("Audit relay failed for {FailureCount} events; first {AuditId}, error {ErrorType}; retrying",
                        failures, firstFailedId, firstErrorType);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError("Failed to read the audit outbox; error {ErrorType}; retrying", exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(options.Value.AuditPollSeconds), stoppingToken);
            }
        }
    }

    public static async Task<bool> SaveAsync(AuditContext audit, OutboxEntry entry, CancellationToken cancellationToken)
    {
        audit.AuditReceipts.Add(new AuditReceipt { Id = entry.Id });
        audit.Add(entry.Entity switch
        {
            "Claim" => (object)new ClaimAudit
            {
                ClaimId = entry.EntityId, Created = entry.OccurredAtUtc, HttpRequestType = entry.HttpRequestType
            },
            "Cover" => new CoverAudit
            {
                CoverId = entry.EntityId, Created = entry.OccurredAtUtc, HttpRequestType = entry.HttpRequestType
            },
            _ => throw new InvalidOperationException($"Unknown audit entity: {entry.Entity}")
        });

        try
        {
            await audit.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            if (!await audit.AuditReceipts.AsNoTracking().AnyAsync(receipt => receipt.Id == entry.Id, cancellationToken))
                throw;
            return false;
        }
    }
}
