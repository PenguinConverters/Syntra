using PenguinConverters.Syntra.Core.Entities;

namespace PenguinConverters.Syntra.Core.Source;

/// <summary>
/// Provides entity-level synchronization support on the source side.
/// Implement this interface on a provider to receive per-entity update callbacks.
/// </summary>
public interface ISynchronizable
{
    /// <summary>
    /// Asynchronously updates or processes a single entity during synchronization.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the update.</param>
    /// <returns>A task that completes when the entity has been processed.</returns>
    ValueTask UpdateEntityAsync(IEntity entity, CancellationToken cancellationToken = default);
}
