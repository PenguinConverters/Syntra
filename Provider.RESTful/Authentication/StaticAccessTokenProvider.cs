using Microsoft.Kiota.Abstractions.Authentication;

namespace PenguinConverters.Syntra.Provider.RESTful.Authentication;

/// <summary>
/// Issues a token that was configured rather than negotiated, for an API whose credential is a
/// long-lived personal or service token.
/// </summary>
public sealed class StaticAccessTokenProvider : IAccessTokenProvider
{
    #region Fields

    private readonly string _token;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="StaticAccessTokenProvider"/> class.
    /// </summary>
    /// <param name="token">The token to present.</param>
    /// <param name="allowedHosts">
    /// The hosts the token may be presented to. An empty set allows any host, which is what a
    /// configuration naming a single service root amounts to.
    /// </param>
    public StaticAccessTokenProvider(string token, IEnumerable<string>? allowedHosts = null)
    {
        _token = token ?? string.Empty;
        AllowedHostsValidator = new AllowedHostsValidator(allowedHosts ?? []);
    }

    #endregion

    #region Properties

    /// <inheritdoc />
    public AllowedHostsValidator AllowedHostsValidator { get; }

    #endregion

    #region Methods

    /// <inheritdoc />
    public Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_token);
    }

    #endregion
}
