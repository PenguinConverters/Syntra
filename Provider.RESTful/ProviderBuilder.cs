using System.Net;
using System.Net.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using PenguinConverters.Keyra;
using PenguinConverters.Syntra.Core.Source;
using PenguinConverters.Syntra.Provider.RESTful.Authentication;
using PenguinConverters.Syntra.Provider.RESTful.Source;

namespace PenguinConverters.Syntra.Provider.RESTful;

/// <summary>
/// Builds a <see cref="Provider"/> with its credentials, its transport and the Kiota middleware
/// pipeline around them.
/// </summary>
/// <remarks>
/// A connector for a specific API derives from this builder and overrides
/// <see cref="CreateProvider"/> to return its own provider type. Everything else has a default
/// that works, and each step is a <c>protected virtual</c> method:
/// <list type="bullet">
///   <item><description>
///     <see cref="CreateAuthenticationProvider"/> - authenticate in a way the configured modes do
///     not cover. Assigning <see cref="AuthenticationFactory"/> does the same without subclassing.
///   </description></item>
///   <item><description>
///     <see cref="CreateTransport"/> - change how connections are made, for client certificates
///     or a pinned certificate check.
///   </description></item>
///   <item><description>
///     <see cref="CreateRequestOptions"/> - change what the middleware pipeline does, such as
///     which status codes are worth retrying.
///   </description></item>
///   <item><description>
///     <see cref="ConfigureProvider"/> - register value handlers and the other retrieval hooks.
///   </description></item>
/// </list>
/// </remarks>
public class ProviderBuilder : IProviderBuilder
{
    #region Constants

    /// <summary>
    /// Upper bound the retry handler places on the number of retries.
    /// </summary>
    private const int MaximumRetryCount = 10;

    /// <summary>
    /// Upper bound in seconds the retry handler places on the retry delay.
    /// </summary>
    private const int MaximumRetryDelaySeconds = 180;

    #endregion

    #region Fields

    private Func<byte[], Type, object>? _deserializer;
    private Decryptor? _decryptor;
    private ILogger? _logger;
    private byte[]? _configuration;
    private byte[]? _metadata;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets a factory that builds the authentication provider, for a host wiring up a
    /// bespoke credential without deriving a builder. It takes precedence over the configured
    /// authentication mode.
    /// </summary>
    public Func<Configuration, DiscloseSecret, IAuthenticationProvider?>? AuthenticationFactory { get; set; }

    /// <summary>
    /// Gets the logger diagnostics are written to.
    /// </summary>
    protected ILogger Logger => _logger ?? NullLogger.Instance;

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
        Provider provider = CreateProvider();

        if (_deserializer is not null)
        {
            provider.SetDeserializer(_deserializer);
        }

        if (_decryptor is not null)
        {
            provider.SetDecryptor(_decryptor);
        }

        if (_logger is not null)
        {
            provider.SetLogger(_logger);
        }

        if (_configuration is not null)
        {
            provider.SetConfiguration(_configuration);
            provider.ApplyConfiguration();
        }

        if (_metadata is not null)
        {
            provider.SetMetadata(_metadata);
        }

        provider.InitializeState();

        ConfigureProvider(provider);

        BuildRestClient(provider);

        return provider;
    }

    /// <summary>
    /// Creates the provider instance this builder configures. A derived builder overrides this
    /// to return its own provider type.
    /// </summary>
    /// <returns>The provider.</returns>
    protected virtual Provider CreateProvider()
    {
        return new Provider();
    }

    /// <summary>
    /// Registers the retrieval hooks on a newly configured provider, before its HTTP client is
    /// built. A derived builder overrides this to attach value handlers, an entry transform or a
    /// content reader.
    /// </summary>
    /// <param name="provider">The provider being built.</param>
    protected virtual void ConfigureProvider(Provider provider)
    {
    }

    /// <summary>
    /// Builds the authentication provider requests are authenticated with.
    /// </summary>
    /// <param name="configuration">The root configuration carrying the credentials.</param>
    /// <param name="disclose">The delegate that discloses a configured secret.</param>
    /// <returns>
    /// The authentication provider, or <c>null</c> when the API is anonymous.
    /// </returns>
    protected virtual IAuthenticationProvider? CreateAuthenticationProvider(
        Configuration configuration,
        DiscloseSecret disclose)
    {
        if (AuthenticationFactory is not null)
        {
            return AuthenticationFactory(configuration, disclose);
        }

        return Authentication.AuthenticationFactory.Create(
            configuration,
            disclose,
            () => new HttpClient(CreateTransport(configuration, disclose))
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(1, configuration.ConnectTimeoutSeconds))
            },
            Logger);
    }

    /// <summary>
    /// Builds the transport connections are made over.
    /// </summary>
    /// <remarks>
    /// A pooled connection is retired on a lifetime rather than kept indefinitely, so that a
    /// changed DNS answer - a failed-over appliance, a rotated load balancer - is picked up
    /// without restarting the host.
    /// </remarks>
    /// <param name="configuration">The root configuration.</param>
    /// <param name="disclose">The delegate that discloses a configured secret.</param>
    /// <returns>The transport.</returns>
    protected virtual HttpMessageHandler CreateTransport(Configuration configuration, DiscloseSecret disclose)
    {
        SocketsHttpHandler transport = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromSeconds(
                Math.Max(1, configuration.PooledConnectionLifetimeSeconds)),
            MaxConnectionsPerServer = Math.Max(1, configuration.MaxConnectionsPerServer),
            ConnectTimeout = TimeSpan.FromSeconds(Math.Max(1, configuration.ConnectTimeoutSeconds)),
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false
        };

        if (!configuration.RemoteCertificateValidation)
        {
            Logger.LogWarning(
                "Server certificate validation is disabled for {BaseUrl}. Any certificate will be "
                + "accepted, which leaves the connection open to interception.",
                configuration.GetBaseUrl());

            transport.SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            };
        }

        if (configuration.Proxy is not null && configuration.Proxy.TryGetProxy(disclose, out IWebProxy? proxy))
        {
            transport.UseProxy = true;
            transport.Proxy = proxy;
        }
        else
        {
            transport.UseProxy = false;
        }

        return transport;
    }

    /// <summary>
    /// Builds the options the Kiota middleware pipeline is assembled from.
    /// </summary>
    /// <param name="configuration">The root configuration.</param>
    /// <returns>The options.</returns>
    protected virtual IRequestOption[] CreateRequestOptions(Configuration configuration)
    {
        // 408, 429, 503 and 504 are the responses worth repeating: a throttled request comes back
        // as 429 with a Retry-After the handler waits out, and the rest are transient. Anything
        // else is a decision the service has made, and repeating it only delays the failure.
        RetryHandlerOption retry = new RetryHandlerOption
        {
            MaxRetry = Math.Clamp(configuration.ReadRetryMaxCount, 0, MaximumRetryCount),
            Delay = Math.Clamp(configuration.ReadRetryDelaySeconds, 0, MaximumRetryDelaySeconds),
            ShouldRetry = (_, _, response) =>
                response.StatusCode is HttpStatusCode.RequestTimeout
                                    or HttpStatusCode.TooManyRequests
                                    or HttpStatusCode.ServiceUnavailable
                                    or HttpStatusCode.GatewayTimeout
        };

        // Parameter names are percent-encoded when the request URL is composed, so the leading
        // '$' of an OData system query option arrives as '%24'. This handler restores it.
        ParametersNameDecodingOption decoding = new ParametersNameDecodingOption();

        RedirectHandlerOption redirect = new RedirectHandlerOption();

        return [retry, redirect, decoding];
    }

    /// <summary>
    /// Builds the HTTP client the provider reads through and assigns it.
    /// </summary>
    /// <param name="provider">The provider being built.</param>
    private void BuildRestClient(Provider provider)
    {
        Configuration? configuration = provider.Configuration;

        if (configuration is null)
        {
            Logger.LogError("No configuration was supplied, so no HTTP client can be built.");
            return;
        }

        if (configuration.GetBaseUrl() is null)
        {
            Logger.LogError(
                "The configuration names neither BaseUrl nor Host, so no service root can be resolved.");
            return;
        }

        DiscloseSecret disclose = provider.TryDiscloseSecret;

        IAuthenticationProvider? authentication = CreateAuthenticationProvider(configuration, disclose);

        IList<DelegatingHandler> handlers = KiotaClientFactory.CreateDefaultHandlers(
            CreateRequestOptions(configuration));

        if (authentication is not null)
        {
            // Authentication goes in front of the pipeline so that a request the retry handler
            // repeats is authenticated again, which renews a token that lapsed between attempts.
            handlers.Insert(0, new AuthenticationHandler(authentication));
        }

        HttpClient httpClient = KiotaClientFactory.Create(
            handlers, CreateTransport(configuration, disclose));

        httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, configuration.RequestTimeoutSeconds));

        // The token provider holds the session open, so it is released when the client that uses
        // it is disposed rather than left to expire on the API's own schedule.
        provider.RestClient = new RestClient(httpClient, Logger, authentication as IDisposable);

        Logger.LogTrace(
            "HTTP client created for {BaseUrl} with {MaxRetry} retr(ies) at {Delay}s.",
            configuration.GetBaseUrl(),
            configuration.ReadRetryMaxCount,
            configuration.ReadRetryDelaySeconds);
    }

    #endregion
}
