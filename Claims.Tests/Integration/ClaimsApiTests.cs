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

    private static readonly DateTime Tomorrow = DateTime.UtcNow.Date.AddDays(1);

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
        var cover = await CreateCoverAsync();
        var request = new CreateClaimCommand(cover.Id, Tomorrow.AddDays(5), "Grounded in port", ClaimType.Grounding, 25_000);

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
    public async Task An_invalid_claim_returns_400_with_every_broken_rule()
    {
        var cover = await CreateCoverAsync();
        var request = new CreateClaimCommand(cover.Id, Tomorrow.AddYears(2), "Too late and too expensive", ClaimType.Fire, 100_001);

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
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await auditContext.CoverAudits.AnyAsync(audit => audit.CoverId == cover.Id, Token))
        {
            Assert.True(DateTime.UtcNow < deadline, "The cover audit entry was not saved within 10 seconds.");
            await Task.Delay(100, Token);
        }
    }

    [Fact]
    public async Task Concurrent_relay_attempts_create_only_one_audit_row()
    {
        var id = Guid.NewGuid().ToString();
        var entry = new OutboxEntry
        {
            Id = id, Entity = "Claim", EntityId = id,
            HttpRequestType = "POST", OccurredAtUtc = DateTime.UtcNow
        };

        async Task<bool> Relay()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await AuditWorker.SaveAsync(scope.ServiceProvider.GetRequiredService<AuditContext>(), entry, Token);
        }

        var outcomes = await Task.WhenAll(Relay(), Relay());
        Assert.Single(outcomes, inserted => inserted);
        Assert.Single(outcomes, inserted => !inserted);
        await using var verification = factory.Services.CreateAsyncScope();
        var audit = verification.ServiceProvider.GetRequiredService<AuditContext>();
        Assert.Equal(1, await audit.ClaimAudits.CountAsync(row => row.ClaimId == id, Token));
        Assert.Equal(1, await audit.AuditReceipts.CountAsync(row => row.Id == id, Token));
    }

    [Fact]
    public async Task Failed_cover_insert_does_not_leave_an_outbox_event()
    {
        var id = Guid.NewGuid().ToString();
        var coverId = Guid.NewGuid().ToString();
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
        Assert.False(await audit.AuditReceipts.AnyAsync(receipt => receipt.Id == id, Token));
    }

    [Fact]
    public async Task Relay_failure_logs_the_event_and_keeps_it_for_retry()
    {
        var client = factory.Services.GetRequiredService<MongoClient>();
        var databaseName = "AuditRetry" + Guid.NewGuid().ToString("N");
        var collection = client.GetDatabase(databaseName).GetCollection<OutboxEntry>("auditOutbox");
        var id = Guid.NewGuid().ToString();
        await collection.InsertOneAsync(new OutboxEntry
        {
            Id = id, Entity = "Claim", EntityId = "claim-1",
            HttpRequestType = "POST", OccurredAtUtc = DateTime.UtcNow
        }, cancellationToken: Token);

        await using var services = new ServiceCollection()
            .AddDbContext<AuditContext>(options => options.UseSqlServer(
                "Server=127.0.0.1,1;Database=Audit;User Id=test;Password=test-only-password;Connect Timeout=1"))
            .BuildServiceProvider();
        var logger = new ErrorLogger<AuditWorker>();
        var worker = new AuditWorker(client, Options.Create(new DatabaseOptions
        {
            MongoDatabaseName = databaseName, AuditPollSeconds = 1
        }), services.GetRequiredService<IServiceScopeFactory>(), logger);

        try
        {
            await worker.StartAsync(Token);
            var message = await logger.Error.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
            Assert.Contains(id, message);
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

    private async Task<Cover> CreateCoverAsync()
    {
        var request = new CreateCoverCommand(Tomorrow, Tomorrow.AddDays(90), CoverType.PassengerShip);
        var response = await _client.PostAsJsonAsync("/Covers", request, Json, Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Cover>(Json, Token))!;
    }
}
