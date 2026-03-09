using PenguinConverters.Syntra.Core.Entities;

namespace PenguinConverters.Syntra.Core.Target;

/// <summary>
/// Provides entity-level synchronization support on the consumer side.
/// Implement this interface on a consumer to receive per-entity update callbacks.
/// </summary>
public interface ISynchronizable
{
    /// <summary>
    /// Updates or processes a single entity during synchronization on the target side.
    /// </summary>
    /// <param name="entity">The entity to synchronize to the target.</param>
    void UpdateEntity(IEntity entity);
}
