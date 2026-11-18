using Claims.Application.Abstractions;
using Claims.Domain;
using FluentValidation;

namespace Claims.Application.Covers;

/// <summary>Query: one page of covers.</summary>
public sealed record GetCoversQuery(Page Page);

/// <summary>Returns one page of covers.</summary>
public sealed class GetCoversQueryHandler(IRepository<Cover> covers) : IQueryHandler<GetCoversQuery, IReadOnlyList<Cover>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Cover>> HandleAsync(GetCoversQuery query, CancellationToken cancellationToken) =>
        covers.GetPageAsync(query.Page, cancellationToken);
}

/// <summary>Query: the cover with the given id.</summary>
public sealed record GetCoverByIdQuery(string Id);

/// <summary>Returns the cover, or <c>null</c> if it does not exist.</summary>
public sealed class GetCoverByIdQueryHandler(
    IRepository<Cover> covers,
    ILogger<GetCoverByIdQueryHandler> logger) : IQueryHandler<GetCoverByIdQuery, Cover?>
{
    /// <inheritdoc />
    public async Task<Cover?> HandleAsync(GetCoverByIdQuery query, CancellationToken cancellationToken)
    {
        var cover = await covers.GetByIdAsync(query.Id, cancellationToken);
        if (cover is null)
            logger.LogInformation("Cover not found");
        return cover;
    }
}

/// <summary>Query: the premium for a period and vessel type, without creating a cover.</summary>
public sealed record ComputePremiumQuery(DateTime StartDate, DateTime EndDate, CoverType Type);

/// <summary>Quotes a premium. Applies the same validation as <see cref="CreateCoverCommand"/>.</summary>
/// <exception cref="ValidationException">The period breaks a business rule.</exception>
public sealed class ComputePremiumQueryHandler(IValidator<CreateCoverCommand> validator)
    : IQueryHandler<ComputePremiumQuery, decimal>
{
    /// <inheritdoc />
    public async Task<decimal> HandleAsync(ComputePremiumQuery query, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(new CreateCoverCommand(query.StartDate, query.EndDate, query.Type), cancellationToken);
        return PremiumCalculator.Calculate(query.StartDate, query.EndDate, query.Type);
    }
}
