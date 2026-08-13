using PenguinConverters.Syntra.Core.Entities;

namespace PenguinConverters.Syntra.Provider.Infoblox;

/// <summary>
/// Infoblox source provider stub. Not yet implemented.
/// </summary>
public class Provider : Core.Source.Provider
{
    #region Methods

    /// <inheritdoc />
    public override IAsyncEnumerable<IEntity> RetrieveAsync(IEnumerable<string> properties, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Infoblox provider is not yet implemented.");
    }

    #endregion
}
