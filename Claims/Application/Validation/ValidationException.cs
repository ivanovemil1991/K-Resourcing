namespace Claims.Application.Validation;

/// <summary>A single broken business rule.</summary>
/// <param name="Field">Name of the request field that is invalid.</param>
/// <param name="Message">Human-readable explanation of the problem.</param>
public sealed record ValidationError(string Field, string Message);

/// <summary>Signals one or more business-rule errors.</summary>
public sealed class ValidationException(IReadOnlyList<ValidationError> errors)
    : Exception("One or more validation errors occurred.")
{
    /// <summary>All validation errors for the request.</summary>
    public IReadOnlyList<ValidationError> Errors { get; } = errors;

    /// <summary>Throws a <see cref="ValidationException"/> if <paramref name="errors"/> is not empty.</summary>
    public static void ThrowIfAny(IReadOnlyList<ValidationError> errors)
    {
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }
}
