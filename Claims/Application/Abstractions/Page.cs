namespace Claims.Application.Abstractions;

/// <summary>Which slice of a list to return, so that a request never loads the whole collection.</summary>
/// <param name="Skip">How many items to skip.</param>
/// <param name="Take">How many items to return.</param>
public readonly record struct Page(int Skip, int Take)
{
    /// <summary>Page size used when the caller does not ask for one.</summary>
    public const int DefaultTake = 50;

    /// <summary>The largest page size a caller can request.</summary>
    public const int MaxTake = 100;
}
