using Claims.Application.Abstractions;
using Claims.Domain;
using Claims.Infrastructure.Auditing;
using Claims.Infrastructure.Persistence;
using Claims.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Claims.Infrastructure;

/// <summary>Registers the infrastructure layer: databases, repositories and background auditing.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Adds the database contexts, <see cref="IRepository{TEntity}"/> and <see cref="IAuditer"/>.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="redisConnection">Redis connection string for the shared cover cache; when empty an in-process cache is used.</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string? redisConnection)
    {
        services.AddSingleton<IFieldEncryptor>(provider =>
            provider.GetRequiredService<IOptions<EncryptionOptions>>().Value.Key is { Length: > 0 } key
                ? new AesGcmFieldEncryptor(Convert.FromBase64String(key))
                : AesGcmFieldEncryptor.CreateEphemeral());
        services.AddDbContext<ClaimsDbContext>((provider, options) =>
            options.UseMongoDB(provider.GetRequiredService<MongoClient>(),
                provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.MongoDatabaseName));
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<IClaimReferences, ClaimReferences>();
        AddCoverCache(services, redisConnection);
        services.AddScoped<IRepository<Cover>>(provider => new CachedCoverRepository(
            new EfRepository<Cover>(provider.GetRequiredService<ClaimsDbContext>()),
            provider.GetRequiredService<IDistributedCache>(),
            provider.GetRequiredService<ILogger<CachedCoverRepository>>()));

        services.AddDbContext<AuditContext>((provider, options) =>
            options.UseSqlServer(provider.GetRequiredService<DatabaseConnections>().AuditSqlServer));
        services.AddScoped<IAuditer, OutboxAuditer>();
        services.AddHostedService<AuditOutboxRelay>();
        services.AddSingleton(provider =>
        {
            MongoGuidConfiguration.Apply();
            var settings = MongoClientSettings.FromConnectionString(provider.GetRequiredService<DatabaseConnections>().ClaimsMongoDb);
            settings.WriteConcern = WriteConcern.WMajority;
            return new MongoClient(settings);
        });

        return services;
    }

    private static void AddCoverCache(IServiceCollection services, string? redisConnection)
    {
        if (string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddDistributedMemoryCache();
            return;
        }

        // A slow or unavailable Redis must not stop the API or make every request wait: the cache is optional.
        var redis = ConfigurationOptions.Parse(redisConnection);
        redis.AbortOnConnectFail = false;
        redis.AsyncTimeout = 500;
        services.AddStackExchangeRedisCache(options =>
        {
            options.ConfigurationOptions = redis;
            options.InstanceName = "claims:";
        });
    }

    /// <summary>Applies pending EF Core migrations to the audit database.</summary>
    public static async Task MigrateAuditDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var auditContext = scope.ServiceProvider.GetRequiredService<AuditContext>();
        await auditContext.Database.MigrateAsync();
    }
}
