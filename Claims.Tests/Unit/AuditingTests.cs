using Claims.Application.Abstractions;
using Claims.Infrastructure.Auditing;
using Claims.Infrastructure.Persistence;
using Claims.Domain;
using Claims.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.EntityFrameworkCore.Extensions;
using Xunit;

namespace Claims.Tests.Unit;

public class AuditingTests
{
    [Fact]
    public async Task Auditer_stages_an_event_in_the_repository_context()
    {
        var options = new DbContextOptionsBuilder<ClaimsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new ClaimsDbContext(options);
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var auditer = new OutboxAuditer(context, new FixedTimeProvider(now), NullLogger<OutboxAuditer>.Instance);

        auditer.AuditClaim("claim-1", AuditAction.Created);

        var entry = Assert.Single(context.ChangeTracker.Entries<OutboxEntry>());
        Assert.Equal(EntityState.Added, entry.State);
        Assert.Equal(("Claim", "claim-1", "POST", now.UtcDateTime),
            (entry.Entity.Entity, entry.Entity.EntityId, entry.Entity.HttpRequestType, entry.Entity.OccurredAtUtc));
        Assert.False(await context.Set<OutboxEntry>().AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Repository_saves_the_staged_event_with_the_cover()
    {
        var options = new DbContextOptionsBuilder<ClaimsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new ClaimsDbContext(options);
        var auditer = new OutboxAuditer(context, TimeProvider.System, NullLogger<OutboxAuditer>.Instance);
        var repository = new EfRepository<Cover>(context);
        var cover = new Cover
        {
            Id = "cover-1", StartDate = DateTime.UtcNow.Date,
            EndDate = DateTime.UtcNow.Date.AddDays(1), Type = CoverType.Yacht, Premium = 1m
        };
        auditer.AuditCover(cover.Id, AuditAction.Created);

        await repository.AddAsync(cover, TestContext.Current.CancellationToken);

        Assert.NotNull(await context.Set<Cover>().FindAsync([cover.Id], TestContext.Current.CancellationToken));
        var entry = Assert.Single(await context.Set<OutboxEntry>().ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(("Cover", cover.Id, "POST"), (entry.Entity, entry.EntityId, entry.HttpRequestType));
    }

    [Fact]
    public void Mongo_outbox_mapping_matches_the_driver_and_uses_transactions()
    {
        var entry = new OutboxEntry
        {
            Id = "event-1", Entity = "Claim", EntityId = "claim-1",
            HttpRequestType = "POST", OccurredAtUtc = DateTime.UtcNow
        };
        var document = entry.ToBsonDocument();
        Assert.Equal("event-1", document["_id"].AsString);
        Assert.Equal("Claim", document["Entity"].AsString);
        var options = new DbContextOptionsBuilder<ClaimsDbContext>()
            .UseMongoDB(new MongoClient("mongodb://localhost:27017"), "Claims").Options;
        using var context = new ClaimsDbContext(options);
        var entity = context.Model.FindEntityType(typeof(OutboxEntry))!;

        Assert.Equal(AutoTransactionBehavior.WhenNeeded, context.Database.AutoTransactionBehavior);
        Assert.Equal("_id", entity.FindProperty(nameof(OutboxEntry.Id))!.GetElementName());
        Assert.Equal("Entity", entity.FindProperty(nameof(OutboxEntry.Entity))!.GetElementName());
    }
}
