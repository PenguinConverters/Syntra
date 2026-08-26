namespace PenguinConverters.Syntra.Core.Entities;

/// <summary>
/// Represents a single data entity flowing through the synchronization pipeline.
/// </summary>
public interface IEntity
{
    #region Properties

    /// <summary>
    /// Gets the unique identifier for this entity, typically a composite key value.
    /// </summary>
    string? Identifier { get; }

    /// <summary>
    /// Gets or sets the synchronization state of this entity.
    /// </summary>
    EntityState State { get; set; }

    /// <summary>
    /// Gets the property bag containing all entity attributes.
    /// </summary>
    IDictionary<string, object?> Properties { get; }

    #endregion

    #region Indexers

    /// <summary>
    /// Gets or sets a property value by name.
    /// </summary>
    /// <param name="propertyName">The name of the property.</param>
    /// <returns>The property value, or <c>null</c> if not set.</returns>
    object? this[string propertyName] { get; set; }

    #endregion
}
