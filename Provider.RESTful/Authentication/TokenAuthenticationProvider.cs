using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace PenguinConverters.Syntra.Provider.RESTful.Authentication;

/// <summary>
/// Presents a token under a configurable scheme and header.
/// </summary>
/// <remarks>
/// Kiota's own bearer provider hard-codes the <c>Bearer</c> scheme and the <c>Authorization</c>
/// header. Enough APIs name their own scheme - <c>AR-JWT</c>, <c>SSWS</c>, <c>Token</c> - or
/// expect the token under a header of their own that the scheme is worth making configuration.
/// An empty scheme presents the token unprefixed.
/// </remarks>
public sealed class TokenAuthenticationProvider : IAuthenticationProvider, IDisposable
{
    #region Fields

    private readonly IAccessTokenProvider _accessTokenProvider;
    private readonly string _headerName;
    private readonly string _scheme;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenAuthenticationProvider"/> class.
    /// </summary>
    /// <param name="accessTokenProvider">The provider issuing the token.</param>
    /// <param name="headerName">The header the token is attached to.</param>
    /// <param name="scheme">The scheme prefixing the token, or an empty value for none.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="accessTokenProvider"/> is <c>null</c>.
    /// </exception>
    public TokenAuthenticationProvider(
        IAccessTokenProvider accessTokenProvider,
        string? headerName = null,
        string? scheme = null)
    {
        _accessTokenProvider = accessTokenProvider
            ?? throw new ArgumentNullException(nameof(accessTokenProvider));

        _headerName = string.IsNullOrWhiteSpace(headerName)
            ? AuthenticationSettingsDefaults.HeaderName
            : headerName;

        _scheme = scheme ?? AuthenticationSettingsDefaults.Scheme;
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public async Task AuthenticateRequestAsync(
        RequestInformation request,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.URI is null)
        {
            return;
        }

        string token = await _accessTokenProvider
            .GetAuthorizationTokenAsync(request.URI, additionalAuthenticationContext, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        request.Headers.Remove(_headerName);
        request.Headers.Add(
            _headerName,
            string.IsNullOrEmpty(_scheme) ? token : $"{_scheme} {token}");
    }

    /// <summary>
    /// Releases the token provider, which is how a negotiated session is handed back rather than
    /// left to expire on the API's own schedule.
    /// </summary>
    public void Dispose()
    {
        (_accessTokenProvider as IDisposable)?.Dispose();
    }

    #endregion
}

/// <summary>
/// Defaults shared by the authentication providers, kept apart from
/// <see cref="Source.AuthenticationSettings"/> so that the authentication layer does not depend
/// on the configuration layer.
/// </summary>
internal static class AuthenticationSettingsDefaults
{
    #region Constants

    /// <summary>
    /// Header a credential is attached to when none is named.
    /// </summary>
    public const string HeaderName = "Authorization";

    /// <summary>
    /// Scheme a token is presented under when none is named.
    /// </summary>
    public const string Scheme = "Bearer";

    #endregion
}
