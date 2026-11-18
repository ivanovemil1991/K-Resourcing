using Claims.Application.Abstractions;
using Claims.Application.Claims;
using Claims.Application.Covers;
using Claims.Domain;
using FluentValidation;

namespace Claims.Application;

/// <summary>Registers the application layer: command handlers, query handlers and validators.</summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Adds all command handlers, query handlers and FluentValidation validators.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssemblyContaining<CreateClaimCommandValidator>();

        services.AddScoped<ICommandHandler<CreateClaimCommand, Claim>, CreateClaimCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteClaimCommand, bool>, DeleteClaimCommandHandler>();
        services.AddScoped<IQueryHandler<GetClaimsQuery, IReadOnlyList<Claim>>, GetClaimsQueryHandler>();
        services.AddScoped<IQueryHandler<GetClaimByIdQuery, Claim?>, GetClaimByIdQueryHandler>();

        services.AddScoped<ICommandHandler<CreateCoverCommand, Cover>, CreateCoverCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteCoverCommand, bool>, DeleteCoverCommandHandler>();
        services.AddScoped<IQueryHandler<GetCoversQuery, IReadOnlyList<Cover>>, GetCoversQueryHandler>();
        services.AddScoped<IQueryHandler<GetCoverByIdQuery, Cover?>, GetCoverByIdQueryHandler>();
        services.AddScoped<IQueryHandler<ComputePremiumQuery, decimal>, ComputePremiumQueryHandler>();
        return services;
    }
}