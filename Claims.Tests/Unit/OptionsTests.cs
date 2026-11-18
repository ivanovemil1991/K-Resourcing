using Claims.Infrastructure;
using Claims.Api;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Claims.Tests.Unit;

public class OptionsTests
{
    private static DatabaseOptions ValidSettings() => new()
    {
        AuditSqlServer = "Server=localhost;Database=Audit;Integrated Security=true",
        ClaimsMongoDb = "mongodb://localhost:27017",
        MongoDatabaseName = "Claims",
        AuditPollSeconds = 5
    };

    [Fact]
    public void Production_requires_valid_database_settings()
    {
        var validator = new DatabaseOptionsValidator(new TestEnvironment("Production"));
        Assert.True(validator.Validate(null, ValidSettings()).Succeeded);

        var invalid = ValidSettings();
        invalid.AuditSqlServer = "";
        invalid.ClaimsMongoDb = "not-a-mongo-url";
        invalid.MongoDatabaseName = "";
        invalid.AuditPollSeconds = 0;
        var result = validator.Validate(null, invalid);

        Assert.True(result.Failed);
        Assert.Equal(4, result.Failures.Count());
    }

    [Fact]
    public void Containers_are_allowed_only_in_development()
    {
        var settings = new DatabaseOptions { UseLocalContainers = true, MongoDatabaseName = "Claims" };
        Assert.True(new DatabaseOptionsValidator(new TestEnvironment("Development")).Validate(null, settings).Succeeded);
        Assert.True(new DatabaseOptionsValidator(new TestEnvironment("Production")).Validate(null, settings).Failed);
    }

    [Fact]
    public void Sql_connection_requires_server_and_database()
    {
        var settings = ValidSettings();
        settings.AuditSqlServer = "Server=localhost";
        Assert.True(new DatabaseOptionsValidator(new TestEnvironment("Production")).Validate(null, settings).Failed);
    }

    [Fact]
    public void Invalid_mongo_database_name_is_rejected()
    {
        var settings = ValidSettings();
        settings.MongoDatabaseName = "invalid/name";
        Assert.True(new DatabaseOptionsValidator(new TestEnvironment("Production")).Validate(null, settings).Failed);
    }

    [Fact]
    public void Authentication_cannot_be_disabled_in_production()
    {
        var settings = new Claims.Api.AuthenticationOptions { Enabled = false };
        Assert.True(new AuthenticationOptionsValidator(new TestEnvironment("Production")).Validate(null, settings).Failed);
        Assert.True(new AuthenticationOptionsValidator(new TestEnvironment("Development")).Validate(null, settings).Succeeded);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Claims";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
