namespace PenguinConverters.Syntra.Core.Entities;

/// <summary>
/// Default implementation of <see cref="IEntity"/> backed by a <see cref="Dictionary{TKey,TValue}"/>.
/// </summary>
public class Entity : IEntity
{
    private readonly Dictionary<string, object?> _properties = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="Entity"/> class.
    /// </summary>
    public Entity()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Entity"/> class with the specified identifier.
    /// </summary>
    /// <param name="identifier">The unique identifier for this entity.</param>
    public Entity(string? identifier)
    {
        Identifier = identifier;
    }

    /// <inheritdoc />
    public string? Identifier { get; }

    /// <inheritdoc />
    public EntityState State { get; set; } = EntityState.Unclassified;

    /// <inheritdoc />
    public IDictionary<string, object?> Properties => _properties;

    /// <inheritdoc />
    public object? this[string propertyName]
    {
        get => _properties.TryGetValue(propertyName, out object? value) ? value : null;
        set => _properties[propertyName] = value;
    }

    /// <summary>
    /// Returns a string representation of this entity.
    /// </summary>
    public override string ToString() => $"{Identifier} [{State}]";
}
