using PenguinConverters.Syntra.Core.Source;

namespace PenguinConverters.Syntra.Core.Target;

/// <summary>
/// Defines a destination consumer that writes entities to an external system.
/// </summary>
public interface IConsumer
{
    #region Properties

    /// <summary>
    /// Gets a value indicating whether errors occurred during synchronization.
    /// </summary>
    bool HadErrors { get; }

    #endregion

    #region Methods

    /// <summary>
    /// Asynchronously synchronizes entities from the given provider to the target system.
    /// </summary>
    /// <param name="provider">The source provider to read entities from.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the synchronization.</param>
    /// <returns>A task that completes when synchronization has finished.</returns>
    Task SynchronizeAsync(IProvider provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously finalizes the synchronization, performing any cleanup or post-processing.
    /// </summary>
    /// <param name="provider">The source provider used during synchronization.</param>
    /// <param name="cancellationToken">A token to signal cancellation of the finalization.</param>
    /// <returns>A task that completes when finalization has finished.</returns>
    Task FinalizeAsync(IProvider provider, CancellationToken cancellationToken = default);

    #endregion
}
