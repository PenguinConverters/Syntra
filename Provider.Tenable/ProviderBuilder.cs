namespace PenguinConverters.Syntra.Provider.Tenable;

/// <summary>
/// Builds a Tenable <see cref="Provider"/>.
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
