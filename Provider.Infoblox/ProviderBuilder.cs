namespace PenguinConverters.Syntra.Provider.Infoblox;

/// <summary>
/// Builds an Infoblox <see cref="Provider"/>.
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
