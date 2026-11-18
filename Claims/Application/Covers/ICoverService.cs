using Claims.Domain;

namespace Claims.Application.Covers;

/// <summary>Use cases for covers: quote, create, read and delete.</summary>
public interface ICoverService
{
    /// <summary>Returns all covers.</summary>
    Task<IReadOnlyList<Cover>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Returns the cover with the given id, or <c>null</c> if it does not exist.</summary>
    Task<Cover?> GetByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>Validates the request and returns the premium, without storing anything.</summary>
    /// <exception cref="FluentValidation.ValidationException">The request breaks a business rule.</exception>
    decimal ComputePremium(CreateCoverRequest request);

    /// <summary>Validates the request, calculates the premium and stores a new cover.</summary>
    /// <exception cref="FluentValidation.ValidationException">The request breaks a business rule.</exception>
    Task<Cover> CreateAsync(CreateCoverRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes a cover.</summary>
    /// <returns><c>true</c> if the cover existed and was deleted.</returns>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);
}
