namespace Claims.Domain;

/// <summary>An entity with a string identifier.</summary>
public interface IEntity
{
    /// <summary>Unique identifier of the entity.</summary>
    string Id { get; }
}
