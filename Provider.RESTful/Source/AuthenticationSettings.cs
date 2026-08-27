using PenguinConverters.Keyra.Settings;

namespace PenguinConverters.Syntra.Provider.RESTful.Source;

/// <summary>
/// Credentials and the protocol they are presented with.
/// </summary>
/// <remarks>
/// Every credential is a Keyra <see cref="Secret"/>, so a configuration file may carry it either
/// as plaintext or as ciphertext the pipeline's decryptor discloses. Which of the properties are
/// consulted depends on <see cref="Mode"/>.
/// </remarks>
public class AuthenticationSettings
{
    #region Constants

    /// <summary>
    /// Header a credential is attached to when none is configured.
    /// </summary>
    public const string DefaultHeaderName = "Authorization";

    /// <summary>
    /// Scheme a token is presented under when none is configured.
    /// </summary>
    public const string DefaultScheme = "Bearer";

    /// <summary>
    /// Format a credential value is composed with when none is configured, which presents it
    /// unchanged.
    /// </summary>
    public const string DefaultValueFormat = "{0}";

    /// <summary>
    /// Lifetime in seconds a token is cached for when the token response states none.
    /// </summary>
    public const int DefaultTokenLifetimeSeconds = 3000;

    /// <summary>
    /// Seconds a cached token is retired ahead of its stated expiry, so that a token is never
    /// presented to the API in the moment it lapses.
    /// </summary>
    public const int DefaultTokenExpiryMarginSeconds = 60;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets how requests are authenticated. Defaults to <see cref="AuthenticationMode.None"/>.
    /// </summary>
    public AuthenticationMode Mode { get; set; } = AuthenticationMode.None;

    /// <summary>
    /// Gets or sets the username for <see cref="AuthenticationMode.Basic"/> and
    /// <see cref="AuthenticationMode.Session"/>.
    /// </summary>
    public Secret? Username { get; set; }

    /// <summary>
    /// Gets or sets the password for <see cref="AuthenticationMode.Basic"/> and
    /// <see cref="AuthenticationMode.Session"/>.
    /// </summary>
    public Secret? Password { get; set; }

    /// <summary>
    /// Gets or sets the client identifier for <see cref="AuthenticationMode.ClientCredentials"/>.
    /// </summary>
    public Secret? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the client secret for <see cref="AuthenticationMode.ClientCredentials"/>.
    /// </summary>
    public Secret? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the fixed credential for <see cref="AuthenticationMode.ApiKey"/> and
    /// <see cref="AuthenticationMode.Token"/>. It is substituted for <c>{0}</c> of
    /// <see cref="ValueFormat"/>.
    /// </summary>
    public Secret? Key { get; set; }

    /// <summary>
    /// Gets or sets the second half of a composite fixed credential, substituted for <c>{1}</c>
    /// of <see cref="ValueFormat"/>. An API that identifies a caller by an access key and a
    /// secret key in one header needs it.
    /// </summary>
    public Secret? SecondaryKey { get; set; }

    /// <summary>
    /// Gets or sets the header the credential is attached to.
    /// Defaults to <see cref="DefaultHeaderName"/>.
    /// </summary>
    public string HeaderName { get; set; } = DefaultHeaderName;

    /// <summary>
    /// Gets or sets the query parameter the credential is attached to instead of a header.
    /// Consulted for <see cref="AuthenticationMode.ApiKey"/> only; when it is set,
    /// <see cref="HeaderName"/> is not used.
    /// </summary>
    public string? ParameterName { get; set; }

    /// <summary>
    /// Gets or sets the scheme a token is presented under, which prefixes the header value.
    /// Defaults to <see cref="DefaultScheme"/>; an API that names its own scheme, such as
    /// <c>AR-JWT</c>, sets it here. An empty value presents the token with no prefix.
    /// </summary>
    public string Scheme { get; set; } = DefaultScheme;

    /// <summary>
    /// Gets or sets the composite format the credential value is built with, where <c>{0}</c> is
    /// <see cref="Key"/> and <c>{1}</c> is <see cref="SecondaryKey"/>. Defaults to
    /// <see cref="DefaultValueFormat"/>. Consulted for <see cref="AuthenticationMode.ApiKey"/>.
    /// </summary>
    public string ValueFormat { get; set; } = DefaultValueFormat;

    /// <summary>
    /// Gets or sets the endpoint a token is requested from, absolute or relative to the base URL.
    /// Required by <see cref="AuthenticationMode.ClientCredentials"/> and
    /// <see cref="AuthenticationMode.Session"/>.
    /// </summary>
    public string? TokenEndPoint { get; set; }

    /// <summary>
    /// Gets or sets the endpoint a session is released at when the provider is disposed,
    /// absolute or relative to the base URL. Leaving it unset abandons the session instead,
    /// which the API expires on its own schedule.
    /// </summary>
    public string? LogoutEndPoint { get; set; }

    /// <summary>
    /// Gets or sets how the credentials of the token request are encoded.
    /// Defaults to <see cref="TokenRequestFormat.Form"/>.
    /// </summary>
    public TokenRequestFormat RequestFormat { get; set; } = TokenRequestFormat.Form;

    /// <summary>
    /// Gets or sets the scope requested by a client credentials grant.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets the path to the token within the token response, such as
    /// <c>access_token</c>. Leaving it unset treats the whole response body as the token, which
    /// is how a login endpoint that answers with a bare string behaves.
    /// </summary>
    public string? TokenPath { get; set; }

    /// <summary>
    /// Gets or sets the path to the lifetime in seconds within the token response, such as
    /// <c>expires_in</c>. When it is absent, <see cref="TokenLifetimeSeconds"/> applies.
    /// </summary>
    public string? ExpiresInPath { get; set; }

    /// <summary>
    /// Gets or sets the lifetime in seconds a token is cached for when the response states none.
    /// Defaults to <see cref="DefaultTokenLifetimeSeconds"/>.
    /// </summary>
    public int TokenLifetimeSeconds { get; set; } = DefaultTokenLifetimeSeconds;

    /// <summary>
    /// Gets or sets the field name the username is sent under in a login request.
    /// Defaults to <c>username</c>.
    /// </summary>
    public string UsernameField { get; set; } = "username";

    /// <summary>
    /// Gets or sets the field name the password is sent under in a login request.
    /// Defaults to <c>password</c>.
    /// </summary>
    public string PasswordField { get; set; } = "password";

    /// <summary>
    /// Gets or sets further fields sent with the token or login request, such as an audience or
    /// a grant type an API spells differently.
    /// </summary>
    public SortedList<string, string>? RequestFields { get; set; }

    #endregion
}
