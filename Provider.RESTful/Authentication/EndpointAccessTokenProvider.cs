using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions.Authentication;
using PenguinConverters.Keyra.Settings;
using PenguinConverters.Syntra.Provider.RESTful.Source;

namespace PenguinConverters.Syntra.Provider.RESTful.Authentication;

/// <summary>
/// Negotiates a token with a token or login endpoint and caches it until it expires.
/// </summary>
/// <remarks>
/// An OAuth 2.0 client credentials grant and a session login differ only in the fields they post
/// and in whether the session is released afterwards, so both are served here. The token is
/// requested once and renewed on demand: concurrent requests that arrive while a renewal is in
/// flight wait for it rather than each opening a session of their own, which matters against an
/// API that caps the sessions a caller may hold.
/// </remarks>
public sealed class EndpointAccessTokenProvider : IAccessTokenProvider, IDisposable
{
    #region Fields

    private readonly HttpClient _httpClient;
    private readonly AuthenticationSettings _settings;
    private readonly string? _baseUrl;
    private readonly DiscloseSecret _disclose;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;
    private bool _disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="EndpointAccessTokenProvider"/> class.
    /// </summary>
    /// <param name="httpClient">
    /// The client the token endpoint is called with. It must not itself be authenticated by this
    /// provider, which would recurse. It is owned by this instance and disposed with it.
    /// </param>
    /// <param name="settings">The credentials and the endpoints they are exchanged at.</param>
    /// <param name="baseUrl">The service root a relative token endpoint is resolved against.</param>
    /// <param name="disclose">The delegate that discloses a configured secret.</param>
    /// <param name="logger">The logger to use for diagnostic output.</param>
    /// <param name="allowedHosts">
    /// The hosts the token may be presented to. An empty set allows any host.
    /// </param>
    public EndpointAccessTokenProvider(
        HttpClient httpClient,
        AuthenticationSettings settings,
        string? baseUrl,
        DiscloseSecret disclose,
        ILogger logger,
        IEnumerable<string>? allowedHosts = null)
    {
        _httpClient = httpClient;
        _settings = settings;
        _baseUrl = baseUrl;
        _disclose = disclose;
        _logger = logger;

        AllowedHostsValidator = new AllowedHostsValidator(allowedHosts ?? []);
    }

    #endregion

    #region Properties

    /// <inheritdoc />
    public AllowedHostsValidator AllowedHostsValidator { get; }

    #endregion

    #region Methods

    /// <inheritdoc />
    public async Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        if (IsValid())
        {
            return _token!;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Another request may have renewed the token while this one waited for the gate.
            if (IsValid())
            {
                return _token!;
            }

            await RequestTokenAsync(cancellationToken).ConfigureAwait(false);

            return _token ?? string.Empty;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Determines whether the cached token is still usable.
    /// </summary>
    /// <returns><c>true</c> when a token is cached and has not lapsed; otherwise, <c>false</c>.</returns>
    private bool IsValid()
    {
        return !string.IsNullOrEmpty(_token) && DateTimeOffset.UtcNow < _expiresAt;
    }

    /// <summary>
    /// Requests a token and caches it.
    /// </summary>
    /// <param name="cancellationToken">A token to signal cancellation of the request.</param>
    /// <returns>A task that completes when the token has been cached.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the endpoint is not configured, the credentials cannot be disclosed, or the
    /// response carries no token.
    /// </exception>
    private async Task RequestTokenAsync(CancellationToken cancellationToken)
    {
        string endPoint = Resolve(_settings.TokenEndPoint)
            ?? throw new InvalidOperationException(
                "A negotiated credential requires Authentication.TokenEndPoint to be configured.");

        _logger.LogTrace("Requesting a token from {TokenEndPoint}.", endPoint);

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endPoint);

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = CreateContent();

        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"The token endpoint answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        (string? token, int? lifetime) = ReadToken(body);

        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException(
                "The token endpoint answered successfully but carried no token. Check "
                + "Authentication.TokenPath against the response shape.");
        }

        _token = token;

        // The token is retired ahead of its stated expiry so that one is never presented in the
        // moment it lapses, which would surface as an authentication failure mid-retrieval.
        int seconds = Math.Max(
            1,
            (lifetime ?? _settings.TokenLifetimeSeconds)
            - AuthenticationSettings.DefaultTokenExpiryMarginSeconds);

        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds);

        _logger.LogTrace("Token acquired; it is renewed in {Seconds}s.", seconds);
    }

    /// <summary>
    /// Builds the body of the token request from the configured credentials.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the credentials the mode needs cannot be disclosed.
    /// </exception>
    private HttpContent CreateContent()
    {
        SortedList<string, string> fields = new SortedList<string, string>(StringComparer.Ordinal);

        if (_settings.Mode == AuthenticationMode.ClientCredentials)
        {
            Add(fields, "grant_type", "client_credentials");
            Add(fields, "client_id", Disclose(_settings.ClientId, nameof(AuthenticationSettings.ClientId)));
            Add(fields, "client_secret", Disclose(_settings.ClientSecret, nameof(AuthenticationSettings.ClientSecret)));

            if (!string.IsNullOrWhiteSpace(_settings.Scope))
            {
                Add(fields, "scope", _settings.Scope);
            }
        }
        else
        {
            Add(fields, _settings.UsernameField, Disclose(_settings.Username, nameof(AuthenticationSettings.Username)));
            Add(fields, _settings.PasswordField, Disclose(_settings.Password, nameof(AuthenticationSettings.Password)));
        }

        if (_settings.RequestFields is not null)
        {
            foreach (KeyValuePair<string, string> field in _settings.RequestFields)
            {
                fields[field.Key] = field.Value;
            }
        }

        if (_settings.RequestFormat == TokenRequestFormat.Json)
        {
            return new StringContent(JsonSerializer.Serialize(fields), Encoding.UTF8, "application/json");
        }

        return new FormUrlEncodedContent(fields);
    }

    /// <summary>
    /// Reads the token and its lifetime out of a token response.
    /// </summary>
    /// <param name="body">The response body.</param>
    /// <returns>The token and the lifetime in seconds the response stated, if any.</returns>
    private (string? Token, int? Lifetime) ReadToken(string body)
    {
        // A login endpoint that answers with the bare token has no path to address, so the body
        // is the token - stripped of the quotes when it is a JSON string rather than plain text.
        if (string.IsNullOrWhiteSpace(_settings.TokenPath))
        {
            string token = body.Trim();

            if (token.Length > 1 && token[0] == '"' && token[^1] == '"')
            {
                token = token[1..^1];
            }

            return (token, null);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);

            string? token = JsonPath.ResolveString(document.RootElement, _settings.TokenPath);
            string? lifetime = JsonPath.ResolveString(document.RootElement, _settings.ExpiresInPath);

            return (token, int.TryParse(lifetime, out int seconds) ? seconds : null);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "The token response is not the JSON document a token path can be read from.");
            return (null, null);
        }
    }

    /// <summary>
    /// Discloses a configured credential.
    /// </summary>
    /// <param name="secret">The secret to disclose.</param>
    /// <param name="name">The setting name, for the failure message.</param>
    /// <returns>The disclosed value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the value cannot be disclosed.</exception>
    private string Disclose(Secret? secret, string name)
    {
        if (!_disclose(secret, out char[] plaintext))
        {
            throw new InvalidOperationException(
                $"Authentication.{name} is required by this authentication mode and could not be disclosed.");
        }

        try
        {
            return new string(plaintext);
        }
        finally
        {
            Array.Clear(plaintext);
        }
    }

    /// <summary>
    /// Adds a field to the request body when it carries a value.
    /// </summary>
    /// <param name="fields">The fields under construction.</param>
    /// <param name="name">The field name.</param>
    /// <param name="value">The field value.</param>
    private static void Add(SortedList<string, string> fields, string name, string? value)
    {
        if (!string.IsNullOrEmpty(name) && value is not null)
        {
            fields[name] = value;
        }
    }

    /// <summary>
    /// Resolves an endpoint that may be stated relative to the service root.
    /// </summary>
    /// <param name="endPoint">The endpoint, absolute or relative.</param>
    /// <returns>The absolute URL, or <c>null</c> when none can be composed.</returns>
    private string? Resolve(string? endPoint)
    {
        if (string.IsNullOrWhiteSpace(endPoint))
        {
            return null;
        }

        if (UrlResolver.IsAbsolute(endPoint, out Uri? absolute))
        {
            return absolute!.ToString();
        }

        return string.IsNullOrWhiteSpace(_baseUrl)
            ? null
            : $"{_baseUrl.TrimEnd('/')}/{endPoint.TrimStart('/')}";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        ReleaseSession();

        _gate.Dispose();
        _httpClient.Dispose();
    }

    /// <summary>
    /// Releases the session the token represents, so that it does not occupy one of the sessions
    /// the API caps a caller at until it expires on its own.
    /// </summary>
    /// <remarks>
    /// This runs from <see cref="Dispose"/>, which is synchronous, so the request is waited on
    /// under a short timeout. A host that runs without a synchronization context - console,
    /// service, function - is what this connector runs in, so the wait cannot deadlock there.
    /// Failing to log out is not worth propagating: the retrieval has already finished, and the
    /// session expires on the API's own schedule.
    /// </remarks>
    private void ReleaseSession()
    {
        string? endPoint = Resolve(_settings.LogoutEndPoint);

        if (endPoint is null || string.IsNullOrEmpty(_token))
        {
            return;
        }

        try
        {
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endPoint);

            request.Headers.TryAddWithoutValidation(
                _settings.HeaderName,
                string.IsNullOrEmpty(_settings.Scheme) ? _token : $"{_settings.Scheme} {_token}");

            using HttpResponseMessage response = _httpClient
                .SendAsync(request, timeout.Token)
                .GetAwaiter()
                .GetResult();

            _logger.LogTrace("Session released: {Status}.", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release the session at {LogoutEndPoint}.", endPoint);
        }
        finally
        {
            _token = null;
        }
    }

    #endregion
}
