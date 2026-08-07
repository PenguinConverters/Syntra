using PenguinConverters.Syntra.Core.Entities;

namespace PenguinConverters.Syntra.Provider.Oracle;

/// <summary>
/// Oracle source provider stub. Not yet implemented.
/// </summary>
public class Provider : Core.Source.Provider
{
    /// <inheritdoc />
    public override IAsyncEnumerable<IEntity> RetrieveAsync(IEnumerable<string> properties, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Oracle provider is not yet implemented.");
    }
}
