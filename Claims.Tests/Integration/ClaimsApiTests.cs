using Claims.Tests.Fakes;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Claims.Application.Claims;
using Claims.Application.Covers;
using Claims.Domain;
using Claims.Infrastructure.Auditing;
using Claims.Infrastructure.Persistence;
using Claims.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace Claims.Tests.Integration;

/// <summary>HTTP tests sharing MongoDB and SQL Server containers; Docker is required.</summary>
public class ClaimsApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly DateTime Today = DateTime.UtcNow.Date;
    private static readonly DateTime Tomorrow = Today.AddDays(1);

    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_Claims_returns_a_json_list()
    {
        var response = await _client.GetAsync("/Claims", Token);

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<List<Claim>>(Json, Token));
    }

    [Fact]
    public async Task Readiness_succeeds_when_both_databases_are_available()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health/ready", Token)).StatusCode);
    }

    [Fact]
    public async Task A_claim_can_be_created_read_and_deleted()
    {
        var cover = await SeedStartedCoverAsync();
        var request = new CreateClaimCommand(cover.Id, Today, "Grounded in port", ClaimType.Grounding, 25_000);

        var createResponse = await _client.PostAsJsonAsync("/Claims", request, Json, Token);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<Claim>(Json, Token))!;

        var fetched = await _client.GetFromJsonAsync<Claim>(createResponse.Headers.Location, Json, Token);
        Assert.Equivalent(created, fetched);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/Claims/{created.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/Claims/{created.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/Claims/{created.Id}", Token)).StatusCode);
    }

    [Fact]
    public async Task Claim_names_are_encrypted_in_the_database_and_ids_are_native_uuids()
    {
        var cover = await SeedStartedCoverAsync();
        var request = new CreateClaimCommand(cover.Id, Today, "Jane Doe", ClaimType.Fire, 100);
        var response = await _client.PostAsJsonAsync("/Claims", request, Json, Token);
        var created = (await response.Content.ReadFromJsonAsync<Claim>(Json, Token))!;

        var databaseName = factory.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.MongoDatabaseName;
        var stored = await factory.Services.GetRequiredService<MongoClient>().GetDatabase(databaseName)
            .GetCollection<BsonDocument>("claims").Find(new BsonDocument("_id", new BsonBinaryData(created.Id, GuidRepresentation.Standard))).SingleAsync(Token);

        Assert.Equal("Jane Doe", created.Name);
        Assert.StartsWith("v1:", stored["name"].AsString);
        Assert.DoesNotContain("Jane", stored["name"].AsString);
        Assert.Equal(BsonType.Binary, stored["_id"].BsonType);
        Assert.Equal(BsonBinarySubType.UuidStandard, stored["_id"].AsBsonBinaryData.SubType);
    }

    [Fact]
    public async Task A_cover_with_claims_cannot_be_deleted_until_they_are_gone()
    {
        var cover = await SeedStartedCoverAsync();
        var claimResponse = await _client.PostAsJsonAsync("/Claims",
            new CreateClaimCommand(cover.Id, Today, "Jane Doe", ClaimType.Fire, 100), Json, Token);
        var claim = (await claimResponse.Content.ReadFromJsonAsync<Claim>(Json, Token))!;

        Assert.Equal(HttpStatusCode.Conflict, (await _client.DeleteAsync($"/Covers/{cover.Id}", Token)).StatusCode);

        await _client.DeleteAsync($"/Claims/{claim.Id}", Token);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/Covers/{cover.Id}", Token)).StatusCode);
    }

    [Fact]
    public async Task An_invalid_claim_returns_400_with_every_broken_rule()
    {
        var cover = await SeedStartedCoverAsync();
        var request = new CreateClaimCommand(cover.Id, Today.AddYears(2), "Too late and too expensive", ClaimType.Fire, 100_001);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/Claims")
        {
            Content = JsonContent.Create(request, options: Json)
        };
        httpRequest.Headers.Add("traceparent", "00-12345678901234567890123456789012-1234567890123456-01");
        var response = await _client.SendAsync(httpRequest, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json, Token);
        Assert.Equivalent(new[] { "DamageCost", "Created" }, problem!.Errors.Keys);
        Assert.Equal("12345678901234567890123456789012",
            ((JsonElement)problem.Extensions["traceId"]!).GetString());
    }

    [Fact]
    public async Task Creating_a_cover_is_audited_in_the_background()
    {
        var cover = await CreateCoverAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var auditContext = scope.ServiceProvider.GetRequiredService<AuditContext>();

        // Audit entries are saved after the request returns, so wait for them for a short time.
        var coverId = cover.Id.ToString();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await auditContext.CoverAudits.AnyAsync(audit => audit.CoverId == coverId, Token))
        {
            Assert.True(DateTime.UtcNow < deadline, "The cover audit entry was not saved within 10 seconds.");
            await Task.Delay(100, Token);
        }
    }

    [Fact]
    public async Task Concurrent_relay_attempts_create_only_one_audit_row()
    {
        var id = Guid.NewGuid(); var idText = id.ToString();
        var entry = new OutboxEntry
        {
            Id = id, Entity = "Claim", EntityId = id,
            HttpRequestType = "POST", OccurredAtUtc = DateTime.UtcNow
        };

        async Task<bool> Relay()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await AuditOutboxRelay.SaveToSqlAsync(scope.ServiceProvider.GetRequiredService<AuditContext>(), entry, Token);
        }

        var outcomes = await Task.WhenAll(Relay(), Relay());
        Assert.Single(outcomes, inserted => inserted);
        Assert.Single(outcomes, inserted => !inserted);
        await using var verification = factory.Services.CreateAsyncScope();
        var audit = verification.ServiceProvider.GetRequiredService<AuditContext>();
        Assert.Equal(1, await audit.ClaimAudits.CountAsync(row => row.ClaimId == idText, Token));
        Assert.Equal(1, await audit.AuditReceipts.CountAsync(row => row.Id == idText, Token));
    }

    [Fact]
    public async Task Failed_cover_insert_does_not_leave_an_outbox_event()
    {
        var id = Guid.NewGuid();
        var coverId = Guid.NewGuid(); var idText = id.ToString();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
            db.Add(new Cover { Id = coverId, StartDate = Tomorrow, EndDate = Tomorrow.AddDays(1), Type = CoverType.Yacht, Premium = 1m });
            await db.SaveChangesAsync(Token);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
            db.Add(new Cover { Id = coverId, StartDate = Tomorrow, EndDate = Tomorrow.AddDays(1), Type = CoverType.Yacht, Premium = 1m });
            db.Add(new OutboxEntry
            {
                Id = id, Entity = "Cover", EntityId = coverId,
                HttpRequestType = "POST", OccurredAtUtc = DateTime.UtcNow
            });
            await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync(Token));
        }

        await using var verification = factory.Services.CreateAsyncScope();
        var claims = verification.ServiceProvider.GetRequiredService<ClaimsDbContext>();
        Assert.False(await claims.Set<OutboxEntry>().AnyAsync(entry => entry.Id == id, Token));
        var audit = verification.ServiceProvider.GetRequiredService<AuditContext>();
        Assert.False(await audit.AuditReceipts.AnyAsync(receipt => receipt.Id == idText, Token));
    }

    [Fact]
    public async Task Relay_failure_logs_the_event_and_keeps_it_for_retry()
    {
        var client = factory.Services.GetRequiredService<MongoClient>();
        var databaseName = "AuditRetry" + Guid.NewGuid().ToString("N");
        var collection = client.GetDatabase(databaseName).GetCollection<OutboxEntry>("auditOutbox");
        var id = Guid.NewGuid();
        await collection.InsertOneAsync(new OutboxEntry
        {
            Id = id, Entity = "Claim", EntityId = TestData.Id(1),
            HttpRequestType = "POST", OccurredAtUtc = DateTime.UtcNow
        }, cancellationToken: Token);

        await using var services = new ServiceCollection()
            .AddDbContext<AuditContext>(options => options.UseSqlServer(
                "Server=127.0.0.1,1;Database=Audit;User Id=test;Password=test-only-password;Connect Timeout=1"))
            .BuildServiceProvider();
        var logger = new ErrorLogger<AuditOutboxRelay>();
        var worker = new AuditOutboxRelay(client, Options.Create(new DatabaseOptions
        {
            MongoDatabaseName = databaseName, AuditPollSeconds = 1
        }), services.GetRequiredService<IServiceScopeFactory>(), logger);

        try
        {
            await worker.StartAsync(Token);
            var message = await logger.Error.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.Contains(id.ToString(), message);
            Assert.DoesNotContain("test-only-password", message);
            Assert.Equal(1, await collection.CountDocumentsAsync(item => item.Id == id, cancellationToken: Token));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            await client.DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }

    private sealed class ErrorLogger<T> : ILogger<T>
    {
        public TaskCompletionSource<string> Error { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Error;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
                Error.TrySetResult(formatter(state, exception));
        }
    }

    // The API refuses covers that start in the past, but claims can only be filed on covers that already started.
    private async Task<Cover> SeedStartedCoverAsync()
    {
        var cover = new Cover { Id = Guid.NewGuid(), StartDate = Today.AddDays(-10), EndDate = Today.AddDays(80), Type = CoverType.PassengerShip, Premium = 1m };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
        db.Add(cover);
        await db.SaveChangesAsync(Token);
        return cover;
    }

    private async Task<Cover> CreateCoverAsync()
    {
        var request = new CreateCoverCommand(Tomorrow, Tomorrow.AddDays(90), CoverType.PassengerShip);
        var response = await _client.PostAsJsonAsync("/Covers", request, Json, Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Cover>(Json, Token))!;
    }
}
