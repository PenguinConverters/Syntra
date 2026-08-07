using PenguinConverters.Syntra.Core.Entities;

namespace PenguinConverters.Syntra.Core.Target;

/// <summary>
/// Provides entity-level synchronization support on the consumer side.
/// Implement this interface on a consumer to receive per-entity update callbacks.
/// </summary>
public interface ISynchronizable
{
    /// <summary>
    /// Asynchronously updates or processes a single entity during synchronization on the target side.
    /// </summary>
    /// <param name="entity">The entity to synchronize to the target.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the update.</param>
    /// <returns>A task that completes when the entity has been written to the target.</returns>
    ValueTask UpdateEntityAsync(IEntity entity, CancellationToken cancellationToken = default);
}
