using Claims.Application.Validation;

namespace Claims.Application.Covers;

/// <summary>Validates new covers against a supplied current date.</summary>
public static class CoverValidator
{
    /// <summary>Returns all validation errors, or an empty list if the cover is valid.</summary>
    /// <param name="request">The cover to validate.</param>
    /// <param name="today">The current date (UTC), used for the "not in the past" rule.</param>
    public static IReadOnlyList<ValidationError> Validate(CreateCoverRequest request, DateOnly today)
    {
        var errors = new List<ValidationError>();
        var startDate = DateOnly.FromDateTime(request.StartDate);
        var endDate = DateOnly.FromDateTime(request.EndDate);

        if (startDate < today)
        {
            errors.Add(new(nameof(request.StartDate), "StartDate cannot be in the past."));
        }

        if (endDate <= startDate)
        {
            errors.Add(new(nameof(request.EndDate), "EndDate must be after StartDate."));
        }
        else if (endDate > startDate.AddYears(1))
        {
            errors.Add(new(nameof(request.EndDate), "The insurance period cannot exceed 1 year."));
        }

        return errors;
    }
}
