using PenguinConverters.Syntra.Core.Entities;

namespace PenguinConverters.Syntra.Core.Source;

/// <summary>
/// Defines a source data provider that retrieves entities from an external system.
/// </summary>
public interface IProvider
{
    /// <summary>
    /// Gets the serialized metadata used for delta synchronization (e.g., USN, delta token, timestamp).
    /// Returns <c>null</c> when no metadata is available (full sync).
    /// </summary>
    byte[]? Metadata { get; }

    /// <summary>
    /// Asynchronously streams entities from the source system, projecting only the specified properties.
    /// </summary>
    /// <param name="properties">The property names to retrieve for each entity.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the retrieval.</param>
    /// <returns>An asynchronous stream of entities from the source.</returns>
    IAsyncEnumerable<IEntity> RetrieveAsync(IEnumerable<string> properties, CancellationToken cancellationToken = default);
}
