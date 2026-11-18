using Claims.Domain;

namespace Claims.Application.Claims;

/// <summary>The data a client sends to file a new claim.</summary>
/// <param name="CoverId">Id of an existing cover to file the claim against.</param>
/// <param name="Created">Date of the claim. Must be within the period of the cover.</param>
/// <param name="Name">Short, human-readable name of the claim.</param>
/// <param name="Type">What caused the damage.</param>
/// <param name="DamageCost">The claimed cost of the damage. Cannot exceed 100,000.</param>
public sealed record CreateClaimCommand(
    string CoverId,
    DateTime Created,
    string Name,
    ClaimType Type,
    decimal DamageCost);
