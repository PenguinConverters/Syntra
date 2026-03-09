using PenguinConverters.Syntra.Core.Entities;

namespace PenguinConverters.Syntra.Core.Source;

/// <summary>
/// Provides entity-level synchronization support on the source side.
/// Implement this interface on a provider to receive per-entity update callbacks.
/// </summary>
public interface ISynchronizable
{
    /// <summary>
    /// Updates or processes a single entity during synchronization.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    void UpdateEntity(IEntity entity);
}
