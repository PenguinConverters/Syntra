using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace PenguinConverters.Syntra.Provider.RESTful.Authentication;

/// <summary>
/// Applies a Kiota authentication provider to every request leaving the pipeline.
/// </summary>
/// <remarks>
/// Kiota ships an authorization handler of its own, but it accepts only a
/// <see cref="BaseBearerTokenAuthenticationProvider"/>. A REST connector has to present Basic
/// credentials and API keys as readily as bearer tokens, so this handler works against the
/// <see cref="IAuthenticationProvider"/> interface instead and any provider will do - including
/// one a derived connector writes itself.
/// <para>
/// Authentication is applied per request rather than once per client, so a token that expires
/// mid-retrieval is renewed by its provider on the next request instead of failing the run, and
/// a request the pipeline retries carries a freshly issued credential.
/// </para>
/// </remarks>
public sealed class AuthenticationHandler : DelegatingHandler
{
    #region Fields

    private readonly IAuthenticationProvider _authenticationProvider;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthenticationHandler"/> class.
    /// </summary>
    /// <param name="authenticationProvider">The provider that authenticates each request.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="authenticationProvider"/> is <c>null</c>.
    /// </exception>
    public AuthenticationHandler(IAuthenticationProvider authenticationProvider)
    {
        _authenticationProvider = authenticationProvider
            ?? throw new ArgumentNullException(nameof(authenticationProvider));
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        RequestInformation information = new RequestInformation
        {
            HttpMethod = ToMethod(request.Method),
            URI = request.RequestUri
        };

        await _authenticationProvider
            .AuthenticateRequestAsync(information, null, cancellationToken)
            .ConfigureAwait(false);

        foreach (KeyValuePair<string, IEnumerable<string>> header in information.Headers)
        {
            // The provider owns the credential headers it sets, so an earlier attempt's value is
            // replaced rather than joined - appending a second Authorization header would send
            // both.
            request.Headers.Remove(header.Key);
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // A key carried as a query parameter reaches the request through the URI, not the headers.
        request.RequestUri = information.URI;

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Projects an HTTP method onto the Kiota method a request description carries.
    /// </summary>
    /// <param name="method">The method to project.</param>
    /// <returns>The Kiota method, falling back to <see cref="Method.GET"/>.</returns>
    private static Method ToMethod(HttpMethod method)
    {
        return Enum.TryParse(method.Method, true, out Method parsed) ? parsed : Method.GET;
    }

    #endregion
}
