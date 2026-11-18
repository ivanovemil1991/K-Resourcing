using Claims.Application.Claims;
using Claims.Application.Covers;
using FluentValidation;

namespace Claims.Application;

/// <summary>Registers the application layer (the use-case services and their validators).</summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Adds <see cref="IClaimService"/>, <see cref="ICoverService"/> and all FluentValidation validators.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssemblyContaining<CreateClaimRequestValidator>();
        services.AddScoped<IClaimService, ClaimService>();
        services.AddScoped<ICoverService, CoverService>();
        return services;
    }
}
