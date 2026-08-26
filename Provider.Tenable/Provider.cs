using PenguinConverters.Syntra.Core.Entities;

namespace PenguinConverters.Syntra.Provider.Tenable;

/// <summary>
/// Tenable source provider stub. Not yet implemented.
/// </summary>
public class Provider : Core.Source.Provider
{
    #region Methods

    /// <inheritdoc />
    public override IAsyncEnumerable<IEntity> RetrieveAsync(IEnumerable<string> properties, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Tenable provider is not yet implemented.");
    }

    #endregion
}
