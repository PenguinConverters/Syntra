using System.Collections.Concurrent;
using PenguinConverters.Syntra.Core.Entities;

namespace PenguinConverters.Syntra.Consumer.SchemaDesigner.Entities;

/// <summary>
/// Builds a statistical profile of entity properties using thread-safe collections.
/// Accumulates observations from multiple entities to infer data types and sizes
/// for SQL schema generation.
/// </summary>
public class EntityBuilder
{
    private readonly ConcurrentDictionary<string, Property> _properties = new(StringComparer.OrdinalIgnoreCase);
    private int _entityCount;

    /// <summary>
    /// Gets the total number of entities processed by this builder.
    /// </summary>
    public int EntityCount => _entityCount;

    /// <summary>
    /// Adds an entity's properties to the statistical profile.
    /// Each property value is analyzed for type, length, and character encoding.
    /// </summary>
    /// <param name="entity">The entity to analyze.</param>
    public void AddEntity(IEntity entity)
    {
        Interlocked.Increment(ref _entityCount);

        foreach (KeyValuePair<string, object?> kvp in entity.Properties)
        {
            Property property = _properties.GetOrAdd(kvp.Key, _ => new Property(kvp.Key));
            property.Observe(kvp.Value);
        }
    }

    /// <summary>
    /// Builds <see cref="Column"/> definitions from the accumulated property observations.
    /// </summary>
    /// <returns>A list of <see cref="Column"/> instances representing the inferred SQL schema.</returns>
    public List<Column> BuildColumns()
    {
        List<Column> columns = new List<Column>();

        foreach (KeyValuePair<string, Property> kvp in _properties)
        {
            columns.Add(kvp.Value.ToColumn());
        }

        return columns;
    }
}
