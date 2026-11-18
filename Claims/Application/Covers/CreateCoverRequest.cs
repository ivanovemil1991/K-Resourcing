using Claims.Domain;

namespace Claims.Application.Covers;

/// <summary>The data a client sends to create a cover or to get a premium quote.</summary>
/// <param name="StartDate">First day of the insurance period. Cannot be in the past.</param>
/// <param name="EndDate">End of the insurance period. At most one year after <paramref name="StartDate"/>.</param>
/// <param name="Type">The kind of vessel to insure.</param>
public sealed record CreateCoverRequest(DateTime StartDate, DateTime EndDate, CoverType Type);
