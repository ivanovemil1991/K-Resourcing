namespace Claims.Domain;

/// <summary>A damage claim against an existing <see cref="Cover"/>.</summary>
public class Claim : IEntity
{
    /// <inheritdoc />
    public required Guid Id { get; init; }

    /// <summary>Id of the <see cref="Cover"/> this claim is filed against.</summary>
    public required Guid CoverId { get; init; }

    /// <summary>The date the claim was created. Must fall within the period of the related cover.</summary>
    public required DateTime Created { get; init; }

    /// <summary>Short, human-readable name of the claim.</summary>
    public required string Name { get; init; }

    /// <summary>What caused the damage.</summary>
    public required ClaimType Type { get; init; }

    /// <summary>The claimed cost of the damage. Cannot exceed 100,000.</summary>
    public required decimal DamageCost { get; init; }
}

/// <summary>The cause of the damage described by a <see cref="Claim"/>.</summary>
public enum ClaimType
{
    Collision = 0,
    Grounding = 1,
    BadWeather = 2,
    Fire = 3
}
