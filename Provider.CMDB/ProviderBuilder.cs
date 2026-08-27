namespace PenguinConverters.Syntra.Provider.CMDB;

/// <summary>
/// Builds a CMDB <see cref="Provider"/>.
/// </summary>
/// <remarks>
/// The credentials, the transport and the middleware pipeline are assembled by
/// <see cref="RESTful.ProviderBuilder"/> from what <see cref="Source.Configuration"/> declares -
/// including the JWT session, which the base builder establishes and releases. Naming the
/// provider type is all this builder adds.
/// </remarks>
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
