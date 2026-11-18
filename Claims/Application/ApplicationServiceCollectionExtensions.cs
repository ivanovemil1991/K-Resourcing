using Claims.Application.Claims;
using Claims.Application.Covers;

namespace Claims.Application;

/// <summary>Registers the application layer (the use-case services).</summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Adds <see cref="IClaimService"/> and <see cref="ICoverService"/>.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IClaimService, ClaimService>();
        services.AddScoped<ICoverService, CoverService>();
        return services;
    }
}
