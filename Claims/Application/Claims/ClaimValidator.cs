using Claims.Application.Validation;
using Claims.Domain;

namespace Claims.Application.Claims;

/// <summary>Validates new claims against their covers without database access.</summary>
public static class ClaimValidator
{
    /// <summary>The highest damage cost a single claim may have.</summary>
    public const decimal MaxDamageCost = 100_000m;

    /// <summary>Returns all validation errors, or an empty list if the claim is valid.</summary>
    /// <param name="request">The claim to validate.</param>
    /// <param name="cover">The cover the claim refers to, or <c>null</c> if no cover with that id exists.</param>
    public static IReadOnlyList<ValidationError> Validate(CreateClaimRequest request, Cover? cover)
    {
        var errors = new List<ValidationError>();

        if (request.DamageCost < 0)
        {
            errors.Add(new(nameof(request.DamageCost), "DamageCost cannot be negative."));
        }
        else if (request.DamageCost > MaxDamageCost)
        {
            errors.Add(new(nameof(request.DamageCost), $"DamageCost cannot exceed {MaxDamageCost:0}."));
        }

        if (cover is null)
        {
            errors.Add(new(nameof(request.CoverId), $"Cover '{request.CoverId}' does not exist."));
        }
        else if (!IsWithinCoverPeriod(request.Created, cover))
        {
            errors.Add(new(nameof(request.Created), "Created date must be within the period of the related cover."));
        }

        return errors;
    }

    /// <summary>Checks whether the date falls within the cover, including both endpoints.</summary>
    private static bool IsWithinCoverPeriod(DateTime created, Cover cover) =>
        created.Date >= cover.StartDate.Date && created.Date <= cover.EndDate.Date;
}
