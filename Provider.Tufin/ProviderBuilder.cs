namespace PenguinConverters.Syntra.Provider.Tufin;

/// <summary>
/// Builds a Tufin <see cref="Provider"/>.
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
