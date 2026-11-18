namespace Claims.Domain;

/// <summary>An insurance policy for a vessel over a period of time.</summary>
public class Cover : IEntity
{
    /// <inheritdoc />
    public required Guid Id { get; init; }

    /// <summary>First day of the insurance period.</summary>
    public required DateTime StartDate { get; init; }

    /// <summary>End of the insurance period. The insured period is at most one year.</summary>
    public required DateTime EndDate { get; init; }

    /// <summary>The kind of vessel that is insured. It determines the day rate.</summary>
    public required CoverType Type { get; init; }

    /// <summary>The total premium for the whole insurance period. Calculated by the server, never set by the client.</summary>
    public required decimal Premium { get; init; }
}

/// <summary>The kind of vessel insured by a <see cref="Cover"/>.</summary>
public enum CoverType
{
    Yacht = 0,
    PassengerShip = 1,
    ContainerShip = 2,
    BulkCarrier = 3,
    Tanker = 4
}
