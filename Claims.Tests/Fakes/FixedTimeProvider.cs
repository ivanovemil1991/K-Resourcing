namespace Claims.Tests.Fakes;

/// <summary>A fixed clock for testing date rules.</summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
