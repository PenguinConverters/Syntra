namespace PenguinConverters.Syntra.Provider.Ciphersuite;

/// <summary>
/// Builds a cipher suite <see cref="Provider"/>.
/// </summary>
public class ProviderBuilder : RESTful.ProviderBuilder
{
    #region Methods

    /// <inheritdoc />
    protected override RESTful.Provider CreateProvider()
    {
        return new Provider();
    }

    #endregion
}
