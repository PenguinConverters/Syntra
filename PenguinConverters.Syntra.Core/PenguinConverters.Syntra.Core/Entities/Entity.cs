using PenguinConverters.Syntra.Core.Types;

namespace PenguinConverters.Syntra.Core.Entities;

/// <summary>
/// Default implementation of <see cref="IEntity"/> backed by a <see cref="QuickDictionary"/>,
/// which is sized for the handful of properties a typical entity carries.
/// </summary>
public class Entity : IEntity
{
    #region Fields

    private readonly QuickDictionary _properties = new QuickDictionary(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Constructors

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

    #endregion

    #region Properties

    /// <inheritdoc />
    public string? Identifier { get; }

    /// <inheritdoc />
    public EntityState State { get; set; } = EntityState.Unclassified;

    /// <inheritdoc />
    public IDictionary<string, object?> Properties => _properties;

    #endregion

    #region Indexers

    /// <inheritdoc />
    public object? this[string propertyName]
    {
        get => _properties.TryGetValue(propertyName, out object? value) ? value : null;
        set => _properties[propertyName] = value;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Returns a string representation of this entity.
    /// </summary>
    public override string ToString() => $"{Identifier} [{State}]";

    #endregion
}
