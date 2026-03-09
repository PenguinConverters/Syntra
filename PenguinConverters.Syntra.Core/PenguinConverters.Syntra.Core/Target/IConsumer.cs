using PenguinConverters.Syntra.Core.Source;

namespace PenguinConverters.Syntra.Core.Target;

/// <summary>
/// Defines a destination consumer that writes entities to an external system.
/// </summary>
public interface IConsumer
{
    /// <summary>
    /// Gets a value indicating whether errors occurred during synchronization.
    /// </summary>
    bool HadErrors { get; }

    /// <summary>
    /// Synchronizes entities from the given provider to the target system.
    /// </summary>
    /// <param name="provider">The source provider to read entities from.</param>
    void Synchronize(IProvider provider);

    /// <summary>
    /// Finalizes the synchronization, performing any cleanup or post-processing.
    /// </summary>
    /// <param name="provider">The source provider used during synchronization.</param>
    void Finalize(IProvider provider);
}
