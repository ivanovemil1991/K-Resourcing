using Claims.Application.Abstractions;
using Claims.Infrastructure.Auditing;
using Claims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Claims.Infrastructure;

/// <summary>Registers the infrastructure layer: databases, repositories and background auditing.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Adds the database contexts, <see cref="IRepository{TEntity}"/> and <see cref="IAuditer"/>.</summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<ClaimsDbContext>((provider, options) =>
            options.UseMongoDB(provider.GetRequiredService<MongoClient>(),
                provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.MongoDatabaseName));
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

        services.AddDbContext<AuditContext>((provider, options) =>
            options.UseSqlServer(provider.GetRequiredService<DatabaseConnections>().AuditSqlServer));
        services.AddScoped<IAuditer, OutboxAuditer>();
        services.AddHostedService<AuditWorker>();
        services.AddSingleton(provider =>
        {
            var settings = MongoClientSettings.FromConnectionString(provider.GetRequiredService<DatabaseConnections>().ClaimsMongoDb);
            settings.WriteConcern = WriteConcern.WMajority;
            return new MongoClient(settings);
        });

        return services;
    }

    /// <summary>Applies pending EF Core migrations to the audit database.</summary>
    public static async Task MigrateAuditDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var auditContext = scope.ServiceProvider.GetRequiredService<AuditContext>();
        await auditContext.Database.MigrateAsync();
    }
}
