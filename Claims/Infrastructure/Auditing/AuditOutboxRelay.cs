using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Claims.Infrastructure.Auditing;

/// <summary>
/// Background service that relays audit events from the MongoDB outbox to SQL Server, at least once.
/// </summary>
/// <remarks>
/// Each pass reads a batch of events in id order, writes each one to SQL and then deletes it from the outbox.
/// A failed event stays in the outbox and is retried on the next scan; the receipt table makes retries and
/// concurrent relays idempotent. See <c>docs/FLOWS.md</c> for the full flow.
/// </remarks>
public sealed class AuditOutboxRelay(
    MongoClient mongoClient,
    IOptions<DatabaseOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<AuditOutboxRelay> logger) : BackgroundService
{
    private const int BatchSize = 50;

    private TimeSpan PollInterval => TimeSpan.FromSeconds(options.Value.AuditPollSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var outbox = mongoClient.GetDatabase(options.Value.MongoDatabaseName).GetCollection<OutboxEntry>("auditOutbox");

        // Everything up to the cursor was already attempted in this scan. Failed events are skipped
        // (and retried in the next scan) so that one bad event cannot block the ones behind it.
        string? cursor = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = await ReadBatchAsync(outbox, cursor, stoppingToken);
                if (batch.Count == 0)
                {
                    cursor = null;
                    await Task.Delay(PollInterval, stoppingToken);
                    continue;
                }

                cursor = batch[^1].Id;
                await RelayBatchAsync(outbox, batch, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError("Failed to read the audit outbox; error {ErrorType}; retrying", exception.GetType().Name);
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private static Task<List<OutboxEntry>> ReadBatchAsync(
        IMongoCollection<OutboxEntry> outbox, string? afterId, CancellationToken cancellationToken)
    {
        var filter = afterId is null
            ? Builders<OutboxEntry>.Filter.Empty
            : Builders<OutboxEntry>.Filter.Gt(entry => entry.Id, afterId);
        return outbox.Find(filter).SortBy(entry => entry.Id).Limit(BatchSize).ToListAsync(cancellationToken);
    }

    /// <summary>Relays every event of the batch and logs one summary line per outcome.</summary>
    private async Task RelayBatchAsync(
        IMongoCollection<OutboxEntry> outbox, IReadOnlyList<OutboxEntry> batch, CancellationToken cancellationToken)
    {
        var delivered = 0;
        var duplicates = 0;
        var failures = 0;
        string? firstFailedId = null;
        string? firstErrorType = null;

        foreach (var entry in batch)
        {
            try
            {
                if (await RelayEntryAsync(outbox, entry, cancellationToken))
                    delivered++;
                else
                    duplicates++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures++;
                firstFailedId ??= LoggableId(entry.Id);
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

    /// <summary>Writes one event to SQL, then removes it from the outbox. Returns <c>false</c> for a duplicate.</summary>
    private async Task<bool> RelayEntryAsync(
        IMongoCollection<OutboxEntry> outbox, OutboxEntry entry, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var auditContext = scope.ServiceProvider.GetRequiredService<AuditContext>();

        var inserted = await SaveToSqlAsync(auditContext, entry, cancellationToken);
        await outbox.DeleteOneAsync(item => item.Id == entry.Id, cancellationToken);

        logger.LogDebug(inserted ? "Audit event delivered {AuditId}" : "Duplicate audit event skipped {AuditId}",
            LoggableId(entry.Id));
        return inserted;
    }

    /// <summary>
    /// Saves the audit record and its receipt in one SQL transaction. The receipt's primary key is the outbox
    /// event id, so an event that was already saved is detected and reported as <c>false</c>.
    /// </summary>
    public static async Task<bool> SaveToSqlAsync(AuditContext auditContext, OutboxEntry entry, CancellationToken cancellationToken)
    {
        auditContext.AuditReceipts.Add(new AuditReceipt { Id = entry.Id });
        auditContext.Add(ToAuditRecord(entry));

        try
        {
            await auditContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A unique-key violation is only a duplicate if the receipt really exists.
            var receiptExists = await auditContext.AuditReceipts.AsNoTracking()
                .AnyAsync(receipt => receipt.Id == entry.Id, cancellationToken);
            if (!receiptExists)
                throw;
            return false;
        }
    }

    /// <summary>Maps an outbox event to the SQL audit record for its entity type.</summary>
    /// <exception cref="InvalidOperationException">The event names an unknown entity.</exception>
    public static object ToAuditRecord(OutboxEntry entry) => entry.Entity switch
    {
        "Claim" => new ClaimAudit
        {
            ClaimId = entry.EntityId, Created = entry.OccurredAtUtc, HttpRequestType = entry.HttpRequestType
        },
        "Cover" => new CoverAudit
        {
            CoverId = entry.EntityId, Created = entry.OccurredAtUtc, HttpRequestType = entry.HttpRequestType
        },
        _ => throw new InvalidOperationException($"Unknown audit entity: {entry.Entity}")
    };

    /// <summary>Ids are only logged when they are GUIDs, so that malformed input never reaches the logs.</summary>
    private static string LoggableId(string id) => Guid.TryParse(id, out var guid) ? guid.ToString() : "invalid";
}
