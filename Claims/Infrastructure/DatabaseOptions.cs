using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System.Text;

namespace Claims.Infrastructure;

/// <summary>Database and audit settings for the application.</summary>
public sealed class DatabaseOptions
{
    public bool UseLocalContainers { get; set; }
    public bool MigrateAuditOnStart { get; set; }
    public string AuditSqlServer { get; set; } = "";
    public string ClaimsMongoDb { get; set; } = "";
    public string MongoDatabaseName { get; set; } = "";
    public int AuditPollSeconds { get; set; } = 5;
}

/// <summary>Validates database settings before the application starts.</summary>
public sealed class DatabaseOptionsValidator(IHostEnvironment environment) : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        var errors = new List<string>();
        if (options.UseLocalContainers && !environment.IsDevelopment())
            errors.Add("Database:UseLocalContainers is allowed only in Development.");
        if (string.IsNullOrWhiteSpace(options.MongoDatabaseName))
            errors.Add("Database:MongoDatabaseName is required.");
        else if (Encoding.UTF8.GetByteCount(options.MongoDatabaseName) > 63 ||
                 options.MongoDatabaseName.Any(character => char.IsWhiteSpace(character) ||
                     character == '\0' || "/\\.$\"*<>:|?".Contains(character)))
            errors.Add("Database:MongoDatabaseName is invalid.");
        if (options.AuditPollSeconds <= 0)
            errors.Add("Database:AuditPollSeconds must be positive.");

        if (!options.UseLocalContainers)
        {
            if (string.IsNullOrWhiteSpace(options.AuditSqlServer))
                errors.Add("Database:AuditSqlServer is required.");
            else
            {
                try
                {
                    var sql = new SqlConnectionStringBuilder(options.AuditSqlServer);
                    if (string.IsNullOrWhiteSpace(sql.DataSource) || string.IsNullOrWhiteSpace(sql.InitialCatalog))
                        errors.Add("Database:AuditSqlServer must specify a server and database.");
                }
                catch (ArgumentException) { errors.Add("Database:AuditSqlServer is invalid."); }
            }

            if (string.IsNullOrWhiteSpace(options.ClaimsMongoDb))
                errors.Add("Database:ClaimsMongoDb is required.");
            else
            {
                try { _ = new MongoUrl(options.ClaimsMongoDb); }
                catch (MongoConfigurationException) { errors.Add("Database:ClaimsMongoDb is invalid."); }
                catch (FormatException) { errors.Add("Database:ClaimsMongoDb is invalid."); }
                catch (ArgumentException) { errors.Add("Database:ClaimsMongoDb is invalid."); }
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
