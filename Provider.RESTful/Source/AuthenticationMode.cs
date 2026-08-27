namespace PenguinConverters.Syntra.Provider.RESTful.Source;

/// <summary>
/// Selects how requests are authenticated.
/// </summary>
/// <remarks>
/// A mode that does not fit an API is not a dead end: <see cref="ProviderBuilder"/> exposes both
/// an override and a factory delegate that return an arbitrary Kiota authentication provider,
/// so a connector can authenticate any way it needs to without reimplementing the request loop.
/// </remarks>
public enum AuthenticationMode
{
    /// <summary>
    /// The API is anonymous and no credential is attached.
    /// </summary>
    None = 0,

    /// <summary>
    /// HTTP Basic authentication built from the configured username and password.
    /// </summary>
    Basic = 1,

    /// <summary>
    /// A fixed key attached to every request, as a header or a query parameter.
    /// </summary>
    ApiKey = 2,

    /// <summary>
    /// A fixed token attached to every request under a configured scheme, <c>Bearer</c> by default.
    /// </summary>
    Token = 3,

    /// <summary>
    /// An OAuth 2.0 client credentials grant exchanged for a token at the configured token
    /// endpoint and cached until it expires.
    /// </summary>
    ClientCredentials = 4,

    /// <summary>
    /// A session established by posting the configured credentials to a login endpoint. The
    /// token it answers with is cached, and the logout endpoint is called when the provider is
    /// disposed.
    /// </summary>
    Session = 5
}
