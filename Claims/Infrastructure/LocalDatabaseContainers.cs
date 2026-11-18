using System.Runtime.InteropServices;
using Testcontainers.MongoDb;
using Testcontainers.MsSql;

namespace Claims.Infrastructure;

/// <summary>Connection strings for the two databases the API uses.</summary>
/// <param name="AuditSqlServer">SQL Server that holds the audit trail.</param>
/// <param name="ClaimsMongoDb">MongoDB that holds claims and covers.</param>
public sealed record DatabaseConnections(string AuditSqlServer, string ClaimsMongoDb);

/// <summary>Starts disposable SQL Server and MongoDB containers for local development and tests.</summary>
/// <remarks>Testcontainers removes the containers when the process exits.</remarks>
public static class LocalDatabaseContainers
{
    /// <summary>Starts both containers and returns their connection strings.</summary>
    public static async Task<DatabaseConnections> StartAsync()
    {
        // The 2019 image is used on Windows/macOS hosts because it starts faster there.
        var sqlServerImage = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? "mcr.microsoft.com/mssql/server:2022-latest"
            : "mcr.microsoft.com/mssql/server:2019-CU18-ubuntu-20.04";

        var sqlServer = new MsSqlBuilder(sqlServerImage).Build();
        var mongoDb = new MongoDbBuilder("mongo:8.0").WithReplicaSet().Build();

        await Task.WhenAll(sqlServer.StartAsync(), mongoDb.StartAsync());

        return new DatabaseConnections(sqlServer.GetConnectionString(), mongoDb.GetConnectionString());
    }
}
