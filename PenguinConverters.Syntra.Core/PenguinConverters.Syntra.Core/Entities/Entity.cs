using PenguinConverters.Syntra.Core.Types;

namespace PenguinConverters.Syntra.Core.Entities;

/// <summary>
/// Default implementation of <see cref="IEntity"/> backed by a <see cref="QuickDictionary"/>,
/// which is sized for the handful of properties a typical entity carries.
/// </summary>
public class Entity : IEntity
{
    #region Fields

    private readonly QuickDictionary _properties;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="Entity"/> class.
    /// </summary>
    public Entity()
    {
        _properties = new QuickDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Entity"/> class with the specified identifier.
    /// </summary>
    /// <param name="identifier">The unique identifier for this entity.</param>
    public Entity(string? identifier) : this()
    {
        Identifier = identifier;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Entity"/> class from a property bag,
    /// as produced by a source query returning one dictionary per record.
    /// </summary>
    /// <param name="properties">The properties of this entity.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="properties"/> is <c>null</c>.
    /// </exception>
    public Entity(IDictionary<string, object?> properties) : this(null, properties)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Entity"/> class with the specified identifier
    /// and property bag.
    /// </summary>
    /// <remarks>
    /// A <see cref="QuickDictionary"/> that already carries the case-insensitive key semantics
    /// this type guarantees is taken over rather than copied, because a source query allocates one
    /// per record and copying every one of them would double the allocation for no gain. Any other
    /// dictionary is copied, so the entity is never left aliasing a collection with different key
    /// semantics.
    /// </remarks>
    /// <param name="identifier">The unique identifier for this entity.</param>
    /// <param name="properties">The properties of this entity.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="properties"/> is <c>null</c>.
    /// </exception>
    public Entity(string? identifier, IDictionary<string, object?> properties)
    {
        if (properties is null)
            throw new ArgumentNullException(nameof(properties));

        Identifier = identifier;

        _properties = properties is QuickDictionary quickDictionary
            && ReferenceEquals(quickDictionary.Comparer, StringComparer.OrdinalIgnoreCase)
            ? quickDictionary
            : new QuickDictionary(properties, StringComparer.OrdinalIgnoreCase);
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
