using PenguinConverters.Syntra.Core.Entities;

namespace PenguinConverters.Syntra.Provider.Ciphersuite;

/// <summary>
/// Ciphersuite source provider stub. Not yet implemented.
/// </summary>
public class Provider : Core.Source.Provider
{
    /// <inheritdoc />
    public override IEnumerable<IEntity> Retrieve(IEnumerable<string> properties)
    {
        throw new NotImplementedException("Ciphersuite provider is not yet implemented.");
    }
}
