using Claims.Application.Abstractions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Claims.Application.Covers;
using Claims.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Claims.Tests.Unit;

public class HostingTests
{
    [Fact]
    public void Production_fails_to_start_without_database_connections()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Database:AuditSqlServer"] = "",
                    ["Database:ClaimsMongoDb"] = "",
                    ["Authentication:Authority"] = "https://login.microsoftonline.com/test/v2.0",
                    ["Authentication:Audience"] = "test-api",
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32])
                }));
        });

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task Production_exposes_liveness_and_reports_unavailable_databases()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Database:UseLocalContainers"] = "false",
                    ["Database:MigrateAuditOnStart"] = "false",
                    ["Database:AuditSqlServer"] = "Server=127.0.0.1,1;Database=Audit;User Id=test;Password=test;Connect Timeout=1",
                    ["Database:ClaimsMongoDb"] = "mongodb://127.0.0.1:1",
                    ["Database:MongoDatabaseName"] = "Claims",
                    ["Authentication:Authority"] = "https://login.microsoftonline.com/test/v2.0",
                    ["Authentication:Audience"] = "test-api",
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32])
                }));
        });
        using var client = factory.CreateClient();
        var token = TestContext.Current.CancellationToken;

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live", token)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger", token)).StatusCode);
    }

    [Fact]
    public async Task Unexpected_error_returns_problem_details_with_a_trace_id()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Database:AuditSqlServer"] = "Server=127.0.0.1,1;Database=Audit;User Id=test;Password=test",
                    ["Database:ClaimsMongoDb"] = "mongodb://127.0.0.1:1",
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Authentication:Enabled"] = "false"
                }));
            builder.ConfigureTestServices(services => services.AddScoped<IQueryHandler<GetCoverByIdQuery, Cover?>, FailingGetCoverById>());
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/Covers/{Guid.NewGuid()}");
        request.Headers.Add("traceparent", "00-12345678901234567890123456789012-1234567890123456-01");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("12345678901234567890123456789012", problem.GetProperty("traceId").GetString());
        Assert.DoesNotContain("private data", problem.ToString());

        using var invalid = new HttpRequestMessage(HttpMethod.Post, "/Covers/compute?startDate=invalid");
        invalid.Headers.Add("traceparent", "00-12345678901234567890123456789012-1234567890123456-01");
        var invalidResponse = await client.SendAsync(invalid, TestContext.Current.CancellationToken);
        var invalidProblem = await invalidResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal("12345678901234567890123456789012", invalidProblem.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task Paging_is_bounded_and_callers_are_rate_limited()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Database:AuditSqlServer"] = "Server=127.0.0.1,1;Database=Audit;User Id=test;Password=test",
                    ["Database:ClaimsMongoDb"] = "mongodb://127.0.0.1:1",
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Authentication:Enabled"] = "false",
                    ["RateLimiting:PermitLimit"] = "3",
                    ["RateLimiting:WindowSeconds"] = "60"
                }));
        });
        using var client = factory.CreateClient();
        var token = TestContext.Current.CancellationToken;

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/Claims?take=101", token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/Covers?skip=-1", token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/Claims?take=0", token)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/Claims?take=0", token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live", token)).StatusCode);
    }

    [Theory]
    [InlineData("8443", HttpStatusCode.TemporaryRedirect)]
    [InlineData(null, HttpStatusCode.OK)]
    public async Task Http_is_redirected_to_https_only_when_an_https_port_is_configured(string? httpsPort, HttpStatusCode expected)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Database:AuditSqlServer"] = "Server=127.0.0.1,1;Database=Audit;User Id=test;Password=test",
                    ["Database:ClaimsMongoDb"] = "mongodb://127.0.0.1:1",
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Authentication:Enabled"] = "false",
                    ["HTTPS_PORT"] = httpsPort
                }));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
        if (httpsPort is not null)
            Assert.Equal($"https://localhost:{httpsPort}/health/live", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task A_malformed_id_in_the_route_is_not_found()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Database:AuditSqlServer"] = "Server=127.0.0.1,1;Database=Audit;User Id=test;Password=test",
                    ["Database:ClaimsMongoDb"] = "mongodb://127.0.0.1:1",
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Authentication:Enabled"] = "false"
                }));
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/Claims/not-a-guid", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class FailingGetCoverById : IQueryHandler<GetCoverByIdQuery, Cover?>
    {
        public Task<Cover?> HandleAsync(GetCoverByIdQuery query, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("private data");
    }
}
