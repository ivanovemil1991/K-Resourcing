using Claims.Domain;

namespace Claims.Application.Claims;

/// <summary>Use cases for claims: create, read and delete.</summary>
public interface IClaimService
{
    /// <summary>Returns all claims.</summary>
    Task<IReadOnlyList<Claim>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Returns the claim with the given id, or <c>null</c> if it does not exist.</summary>
    Task<Claim?> GetByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>Validates the request against its cover and stores a new claim.</summary>
    /// <exception cref="FluentValidation.ValidationException">The request breaks a business rule.</exception>
    Task<Claim> CreateAsync(CreateClaimRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes a claim.</summary>
    /// <returns><c>true</c> if the claim existed and was deleted.</returns>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);
}
