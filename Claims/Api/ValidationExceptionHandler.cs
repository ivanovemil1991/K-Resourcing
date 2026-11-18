using Claims.Application.Validation;
using Microsoft.AspNetCore.Diagnostics;

namespace Claims.Api;

/// <summary>Turns a <see cref="ValidationException"/> into an HTTP 400 problem response.</summary>
public sealed class ValidationExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ValidationExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not ValidationException validationException)
        {
            return false;
        }

        logger.LogWarning("Request validation rejected with {ErrorCount} errors", validationException.Errors.Count);
        var errorsByField = validationException.Errors
            .GroupBy(error => error.Field)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray());

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new HttpValidationProblemDetails(errorsByField)
            {
                Status = StatusCodes.Status400BadRequest
            }
        });
    }
}
