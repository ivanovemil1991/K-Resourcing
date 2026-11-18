using Claims.Application.Abstractions;
using Claims.Domain;
using FluentValidation;

namespace Claims.Application.Claims;

/// <summary>Validates a <see cref="CreateClaimCommand"/>, including its relation to the cover. "Today" comes from the injected clock.</summary>
public sealed class CreateClaimCommandValidator : AbstractValidator<CreateClaimCommand>
{
    /// <summary>The highest damage cost a single claim may have.</summary>
    public const decimal MaxDamageCost = 100_000m;

    /// <summary>The longest claim name accepted, so that a request cannot store unbounded text.</summary>
    public const int MaxNameLength = 200;

    public CreateClaimCommandValidator(IRepository<Cover> covers, TimeProvider timeProvider)
    {
        RuleFor(claim => claim.Name).NotEmpty().MaximumLength(MaxNameLength);
        RuleFor(claim => claim.Type).IsInEnum();
        RuleFor(claim => claim.CoverId).NotEmpty();

        // A claim reports damage that already happened, so it cannot be dated in the future.
        RuleFor(claim => claim.Created)
            .Must(created => DateOnly.FromDateTime(created) <= DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
            .WithMessage("Created date cannot be in the future.");

        RuleFor(claim => claim.DamageCost)
            .GreaterThanOrEqualTo(0).WithMessage("DamageCost cannot be negative.")
            .LessThanOrEqualTo(MaxDamageCost).WithMessage($"DamageCost cannot exceed {MaxDamageCost:0}.");

        // Needs the cover from the database, so it is looked up once for both checks.
        RuleFor(claim => claim).CustomAsync(async (claim, context, cancellationToken) =>
        {
            if (claim.CoverId == Guid.Empty)
            {
                return;
            }

            var cover = await covers.GetByIdAsync(claim.CoverId, cancellationToken);
            if (cover is null)
            {
                context.AddFailure(nameof(claim.CoverId), $"Cover '{claim.CoverId}' does not exist.");
            }
            else if (!IsWithinCoverPeriod(claim.Created, cover))
            {
                context.AddFailure(nameof(claim.Created), "Created date must be within the period of the related cover.");
            }
        });
    }

    /// <summary>Checks whether the date falls within the cover, including both endpoints.</summary>
    private static bool IsWithinCoverPeriod(DateTime created, Cover cover) =>
        created.Date >= cover.StartDate.Date && created.Date <= cover.EndDate.Date;
}
