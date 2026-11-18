using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Claims.Api;

/// <summary>Settings for the per-instance request rate limit (section <c>RateLimiting</c>).</summary>
public sealed class RateLimitingOptions
{
    /// <summary>Name of the policy that controllers opt in to with <c>[EnableRateLimiting]</c>.</summary>
    public const string PolicyName = "api";

    /// <summary>Requests one caller may make per window.</summary>
    [Range(1, int.MaxValue)]
    public int PermitLimit { get; init; } = 30;

    /// <summary>Length of the window in seconds.</summary>
    [Range(1, 3600)]
    public int WindowSeconds { get; init; } = 1;
}

/// <summary>Registers the request rate limiter.</summary>
public static class RateLimitingExtensions
{
    /// <summary>
    /// Adds the <see cref="RateLimitingOptions.PolicyName"/> policy: each caller (the Entra object id or
    /// subject, otherwise the IP address) gets <see cref="RateLimitingOptions.PermitLimit"/> requests per
    /// window, and further requests get 429. Only controllers marked with the policy are limited.
    /// The limit applies per instance; use API Management for a global limit.
    /// </summary>
    public static IServiceCollection AddRequestRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>()
            .BindConfiguration("RateLimiting")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitingOptions>>((limiter, configured) =>
            {
                var settings = configured.Value;
                limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                limiter.AddPolicy(RateLimitingOptions.PolicyName, context =>
                {
                    var caller = context.User.FindFirst("oid")?.Value
                        ?? context.User.FindFirst("sub")?.Value
                        ?? context.Connection.RemoteIpAddress?.ToString()
                        ?? "unknown";
                    return RateLimitPartition.GetFixedWindowLimiter(caller, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.PermitLimit,
                        Window = TimeSpan.FromSeconds(settings.WindowSeconds)
                    });
                });
            });
        return services;
    }
}
