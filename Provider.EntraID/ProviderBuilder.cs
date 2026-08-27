using System.Net;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Kiota.Authentication.Azure;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using PenguinConverters.Keyra;
using PenguinConverters.Syntra.Core.Extensions;
using PenguinConverters.Syntra.Core.Source;
using PenguinConverters.Syntra.Provider.EntraID.Source;

namespace PenguinConverters.Syntra.Provider.EntraID;

/// <summary>
/// Builds an Entra ID <see cref="IProvider"/> instance with a Microsoft Graph client,
/// credentials, and delta token state.
/// </summary>
public class ProviderBuilder : IProviderBuilder
{
    #region Constants

    /// <summary>
    /// The scope requested for the Graph service root, which grants the application permissions
    /// consented to the service principal.
    /// </summary>
    public const string DefaultScopeSuffix = "/.default";

    /// <summary>
    /// Upper bound the Kiota retry handler places on the number of retries.
    /// </summary>
    private const int MaximumRetryCount = 10;

    /// <summary>
    /// Upper bound in seconds the Kiota retry handler places on the retry delay.
    /// </summary>
    private const int MaximumRetryDelaySeconds = 180;

    #endregion

    #region Fields

    private readonly Provider _provider = new();
    private Func<byte[], Type, object>? _deserializer;
    private Decryptor? _decryptor;
    private ILogger? _logger;
    private byte[]? _configuration;
    private byte[]? _metadata;

    #endregion

    #region Methods

    /// <inheritdoc />
    public void AddConfiguration(byte[] configuration)
    {
        _configuration = configuration;
    }

    /// <inheritdoc />
    public void AddMetadata(byte[]? metadata)
    {
        _metadata = metadata;
    }

    /// <inheritdoc />
    public void AddDeserializer(Func<byte[], Type, object> deserializer)
    {
        _deserializer = deserializer;
    }

    /// <inheritdoc />
    public void AddLogger(ILogger logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void AddDecryptor(Decryptor decryptor)
    {
        _decryptor = decryptor;
    }

    /// <inheritdoc />
    public IProvider Build()
    {
        if (_deserializer is not null)
            _provider.SetDeserializer(_deserializer);

        if (_decryptor is not null)
            _provider.SetDecryptor(_decryptor);

        if (_logger is not null)
            _provider.SetLogger(_logger);

        if (_configuration is not null)
        {
            _provider.SetConfiguration(_configuration);
            _provider.DeserializeAndApplyConfiguration();
        }

        if (_metadata is not null)
            _provider.SetMetadata(_metadata);

        _provider.InitializeState();

        BuildGraphClient();

        return _provider;
    }

    /// <summary>
    /// Builds the Graph client from the provider configuration and credentials.
    /// </summary>
    /// <remarks>
    /// The pipeline is Kiota's: <c>KiotaClientFactory</c> chains a retry handler, a redirect
    /// handler, a parameter-name decoding handler and a user agent handler around the transport,
    /// and inserts an authorization handler in front of them for the supplied authentication
    /// provider. Throttling and transport failures are therefore handled by the pipeline rather
    /// than by retry loops in the provider, and the <c>Retry-After</c> header Graph sends with an
    /// HTTP 429 is honoured in place of a fixed delay.
    /// </remarks>
    private void BuildGraphClient()
    {
        Configuration? configuration = _provider.Configuration;

        if (configuration is null)
        {
            return;
        }

        // Not UriKind.Absolute on its own: on a Unix host a leading slash makes "/v1.0" an
        // absolute file URL, which would pass the check and then yield an empty host to the
        // allowed-hosts validator and a "file://" scope to the credential. Requiring a web scheme
        // fails the misconfiguration here, with the reason, rather than at the first request.
        if (!UrlResolver.IsAbsolute(configuration.BaseUrl, out Uri? baseUrl))
        {
            _logger?.LogError(
                "'{BaseUrl}' is not an absolute http or https URL, so no Graph service root can "
                + "be resolved.",
                configuration.BaseUrl);
            return;
        }

        TokenCredential? credential = CreateCredential(configuration);

        if (credential is null)
        {
            return;
        }

        _logger?.LogTrace(
            "Building Graph client for {BaseUrl} with {MaxRetry} retr(ies) at {Delay}s.",
            baseUrl, configuration.ReadRetryMaxCount, configuration.ReadRetryDelaySeconds);

        // 429, 503 and 504 are the responses worth repeating: Graph answers a throttled request
        // with 429 and a Retry-After the handler waits out, and 503/504 are transient. Anything
        // else is a decision the service has made, and repeating it only delays the failure.
        RetryHandlerOption retry = new RetryHandlerOption
        {
            MaxRetry = Math.Clamp(configuration.ReadRetryMaxCount, 0, MaximumRetryCount),
            Delay = Math.Clamp(configuration.ReadRetryDelaySeconds, 0, MaximumRetryDelaySeconds),
            ShouldRetry = (delay, attempt, response) =>
                response.StatusCode is HttpStatusCode.TooManyRequests
                                    or HttpStatusCode.ServiceUnavailable
                                    or HttpStatusCode.GatewayTimeout
        };

        // Parameter names are percent-encoded when the request URL is composed, so the leading
        // '$' of an OData system query option arrives here as '%24'. This handler restores it.
        ParametersNameDecodingOption decoding = new ParametersNameDecodingOption();

        RedirectHandlerOption redirect = new RedirectHandlerOption();

        AzureIdentityAuthenticationProvider authentication = new AzureIdentityAuthenticationProvider(
            credential,
            [baseUrl.Host],
            null,
            false,
            $"{baseUrl.GetLeftPart(UriPartial.Authority)}{DefaultScopeSuffix}");

        HttpClient httpClient = KiotaClientFactory.Create(authentication, [retry, redirect, decoding]);

        _provider.GraphClient = new GraphClient(httpClient, _logger ?? NullLogger.Instance);

        _logger?.LogTrace("Graph client created.");
    }

    /// <summary>
    /// Builds the credential the Graph client authenticates with.
    /// </summary>
    /// <remarks>
    /// A configured client identifier and secret authenticate as that service principal. Without
    /// them the ambient identity of the host is used, which is the managed identity when running
    /// in Azure; a client identifier on its own selects a user-assigned managed identity.
    /// </remarks>
    /// <param name="configuration">The provider configuration.</param>
    /// <returns>The credential, or <c>null</c> when the configuration cannot produce one.</returns>
    private TokenCredential? CreateCredential(Configuration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.TenantId))
        {
            _logger?.LogError("A Graph connection requires TenantId to be configured.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(configuration.ClientId) || configuration.ClientSecret is null)
        {
            _logger?.LogTrace(
                "No application secret is configured; authenticating with the ambient identity of the host.");

            return new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                TenantId = configuration.TenantId,
                ManagedIdentityClientId = configuration.ClientId
            });
        }

        if (!_provider.TryDiscloseSecret(configuration.ClientSecret, out char[] clientSecret))
        {
            _logger?.LogError("The configured Graph client secret could not be disclosed.");
            return null;
        }

        try
        {
            // ClientSecretCredential takes the secret as a string, which cannot be erased. The
            // disclosed buffer is cleared regardless, so the plaintext is not left in two places.
            return new ClientSecretCredential(
                configuration.TenantId, configuration.ClientId, new string(clientSecret));
        }
        finally
        {
            Array.Clear(clientSecret);
        }
    }

    #endregion
}
