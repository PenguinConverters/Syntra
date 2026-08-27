using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using PenguinConverters.Keyra.Settings;
using PenguinConverters.Syntra.Provider.RESTful.Authentication;
using PenguinConverters.Syntra.Provider.RESTful.Source;

namespace PenguinConverters.Syntra.Provider.RESTful.Tests.Authentication;

[TestFixture]
public class AuthenticationTests
{
    #region Methods

    [Test]
    public async Task AuthenticationHandler_WithABasicProvider_AttachesTheHeader()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""[]""");

        using HttpClient client = Chain(transport, new BasicAuthenticationProvider("bob", "s3cret"));

        //Act
        await client.GetAsync("https://host/api/records");

        //Assert
        string expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("bob:s3cret"));

        Assert.That(
            transport.Requests[0].Headers.Authorization?.ToString(),
            Is.EqualTo($"Basic {expected}"));
    }

    [Test]
    public async Task AuthenticationHandler_WithAnApiKeyInAQueryParameter_RewritesTheUrl()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""[]""");

        ApiKeyAuthenticationProvider authentication = new ApiKeyAuthenticationProvider(
            "abc123", "apikey", ApiKeyAuthenticationProvider.KeyLocation.QueryParameter);

        using HttpClient client = Chain(transport, authentication);

        //Act
        await client.GetAsync("https://host/api/records");

        //Assert
        Assert.That(transport.RequestUris[0], Does.Contain("apikey=abc123"));
    }

    [Test]
    public async Task AuthenticationHandler_OnEveryRequest_ReauthenticatesRatherThanReusing()
    {
        //Arrange
        // A token that lapses mid-retrieval has to be renewed on the next request, so the
        // handler asks the provider each time instead of stamping the client once.
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""[]""", """[]""");

        CountingAuthenticationProvider authentication = new CountingAuthenticationProvider();

        using HttpClient client = Chain(transport, authentication);

        //Act
        await client.GetAsync("https://host/api/records");
        await client.GetAsync("https://host/api/records");

        //Assert
        Assert.That(authentication.Count, Is.EqualTo(2));
        Assert.That(transport.Requests[1].Headers.Authorization?.Parameter, Is.EqualTo("2"));
    }

    [Test]
    public async Task TokenAuthenticationProvider_WithACustomScheme_PresentsTheTokenUnderIt()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""[]""");

        TokenAuthenticationProvider authentication = new TokenAuthenticationProvider(
            new StaticAccessTokenProvider("jwt-value"), "Authorization", "AR-JWT");

        using HttpClient client = Chain(transport, authentication);

        //Act
        await client.GetAsync("https://host/api/records");

        //Assert
        Assert.That(
            transport.Requests[0].Headers.Authorization?.ToString(),
            Is.EqualTo("AR-JWT jwt-value"));
    }

    [Test]
    public async Task TokenAuthenticationProvider_WithAnEmptyScheme_PresentsTheTokenBare()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""[]""");

        TokenAuthenticationProvider authentication = new TokenAuthenticationProvider(
            new StaticAccessTokenProvider("bare-token"), "X-Auth-Token", string.Empty);

        using HttpClient client = Chain(transport, authentication);

        //Act
        await client.GetAsync("https://host/api/records");

        //Assert
        Assert.That(
            transport.Requests[0].Headers.GetValues("X-Auth-Token").Single(),
            Is.EqualTo("bare-token"));
    }

    [Test]
    public async Task EndpointAccessTokenProvider_WithASessionLogin_PostsTheCredentialsAndCachesTheToken()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            (_, _) => StubHttpMessageHandler.Text("jwt-from-login"));

        AuthenticationSettings settings = new AuthenticationSettings
        {
            Mode = AuthenticationMode.Session,
            TokenEndPoint = "/api/jwt/login",
            Username = Secret.FromPlaintext("bob"),
            Password = Secret.FromPlaintext("s3cret"),
            RequestFormat = TokenRequestFormat.Form
        };

        using EndpointAccessTokenProvider provider = new EndpointAccessTokenProvider(
            new HttpClient(transport), settings, "https://host", Disclose, NullLogger.Instance);

        //Act
        string first = await provider.GetAuthorizationTokenAsync(new Uri("https://host/api"));
        string second = await provider.GetAuthorizationTokenAsync(new Uri("https://host/api"));

        //Assert
        Assert.That(first, Is.EqualTo("jwt-from-login"));
        Assert.That(second, Is.EqualTo("jwt-from-login"));
        Assert.That(transport.Requests, Has.Count.EqualTo(1), "the token is negotiated once");
        Assert.That(transport.RequestUris[0], Is.EqualTo("https://host/api/jwt/login"));
        Assert.That(transport.RequestBodies[0], Is.EqualTo("password=s3cret&username=bob"));
    }

    [Test]
    public async Task EndpointAccessTokenProvider_WithAClientCredentialsGrant_ReadsTheTokenFromItsPath()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            """{"access_token":"oauth-token","expires_in":3600}""");

        AuthenticationSettings settings = new AuthenticationSettings
        {
            Mode = AuthenticationMode.ClientCredentials,
            TokenEndPoint = "https://host/api/oauth/token",
            ClientId = Secret.FromPlaintext("client"),
            ClientSecret = Secret.FromPlaintext("secret"),
            Scope = "read",
            TokenPath = "access_token",
            ExpiresInPath = "expires_in"
        };

        using EndpointAccessTokenProvider provider = new EndpointAccessTokenProvider(
            new HttpClient(transport), settings, null, Disclose, NullLogger.Instance);

        //Act
        string token = await provider.GetAuthorizationTokenAsync(new Uri("https://host/api"));

        //Assert
        Assert.That(token, Is.EqualTo("oauth-token"));
        Assert.That(transport.RequestBodies[0], Does.Contain("grant_type=client_credentials"));
        Assert.That(transport.RequestBodies[0], Does.Contain("scope=read"));
    }

    [Test]
    public void EndpointAccessTokenProvider_WithAFailingTokenEndpoint_ThrowsRatherThanReturningEmpty()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            (_, _) => StubHttpMessageHandler.Text("denied", HttpStatusCode.Unauthorized));

        AuthenticationSettings settings = new AuthenticationSettings
        {
            Mode = AuthenticationMode.Session,
            TokenEndPoint = "https://host/api/jwt/login",
            Username = Secret.FromPlaintext("bob"),
            Password = Secret.FromPlaintext("wrong")
        };

        using EndpointAccessTokenProvider provider = new EndpointAccessTokenProvider(
            new HttpClient(transport), settings, null, Disclose, NullLogger.Instance);

        //Act
        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.GetAuthorizationTokenAsync(new Uri("https://host/api")));

        //Assert
        Assert.That(exception!.Message, Does.Contain("401"));
    }

    [Test]
    public void EndpointAccessTokenProvider_OnDispose_ReleasesTheSession()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler(
            (_, _) => StubHttpMessageHandler.Text("jwt-from-login"));

        AuthenticationSettings settings = new AuthenticationSettings
        {
            Mode = AuthenticationMode.Session,
            TokenEndPoint = "https://host/api/jwt/login",
            LogoutEndPoint = "https://host/api/jwt/logout",
            Scheme = "AR-JWT",
            Username = Secret.FromPlaintext("bob"),
            Password = Secret.FromPlaintext("s3cret")
        };

        EndpointAccessTokenProvider provider = new EndpointAccessTokenProvider(
            new HttpClient(transport), settings, null, Disclose, NullLogger.Instance);

        //Act
        provider.GetAuthorizationTokenAsync(new Uri("https://host/api")).GetAwaiter().GetResult();
        provider.Dispose();

        //Assert
        Assert.That(transport.RequestUris, Has.Count.EqualTo(2));
        Assert.That(transport.RequestUris[1], Is.EqualTo("https://host/api/jwt/logout"));
        Assert.That(
            transport.Requests[1].Headers.Authorization?.ToString(),
            Is.EqualTo("AR-JWT jwt-from-login"));
    }

    [Test]
    public void AuthenticationFactory_WithNoAuthentication_ReturnsNull()
    {
        //Arrange
        Configuration configuration = new Configuration { Host = "host" };

        //Act
        IAuthenticationProvider? authentication = AuthenticationFactory.Create(
            configuration, Disclose, () => new HttpClient(), NullLogger.Instance);

        //Assert
        Assert.That(authentication, Is.Null);
    }

    [Test]
    public async Task AuthenticationFactory_WithACompositeApiKey_FormatsBothHalvesIntoOneHeader()
    {
        //Arrange
        StubHttpMessageHandler transport = new StubHttpMessageHandler("""[]""");

        Configuration configuration = new Configuration
        {
            Host = "host",
            Authentication = new AuthenticationSettings
            {
                Mode = AuthenticationMode.ApiKey,
                HeaderName = "x-apikey",
                Key = Secret.FromPlaintext("AAA"),
                SecondaryKey = Secret.FromPlaintext("BBB"),
                ValueFormat = "accesskey={0};secretkey={1};"
            }
        };

        IAuthenticationProvider? authentication = AuthenticationFactory.Create(
            configuration, Disclose, () => new HttpClient(), NullLogger.Instance);

        using HttpClient client = Chain(transport, authentication!);

        //Act
        await client.GetAsync("https://host/api/records");

        //Assert
        Assert.That(
            transport.Requests[0].Headers.GetValues("x-apikey").Single(),
            Is.EqualTo("accesskey=AAA;secretkey=BBB;"));
    }

    [Test]
    public void AuthenticationFactory_WithMissingCredentials_ReportsRatherThanThrows()
    {
        //Arrange
        Configuration configuration = new Configuration
        {
            Host = "host",
            Authentication = new AuthenticationSettings { Mode = AuthenticationMode.Basic }
        };

        //Act
        IAuthenticationProvider? authentication = AuthenticationFactory.Create(
            configuration, Disclose, () => new HttpClient(), NullLogger.Instance);

        //Assert
        Assert.That(authentication, Is.Null);
    }

    /// <summary>
    /// Discloses a plaintext secret, standing in for the pipeline's Keyra disclosure.
    /// </summary>
    /// <param name="secret">The secret to disclose.</param>
    /// <param name="plaintext">When this method returns <c>true</c>, the disclosed characters.</param>
    /// <returns><c>true</c> when the value was disclosed; otherwise, <c>false</c>.</returns>
    private static bool Disclose(Secret? secret, out char[] plaintext)
    {
        plaintext = secret?.Value?.ToCharArray() ?? [];

        return secret?.Value is not null;
    }

    /// <summary>
    /// Builds a client that authenticates through a provider before reaching a stub transport.
    /// </summary>
    /// <param name="transport">The transport answering the requests.</param>
    /// <param name="authentication">The authentication provider.</param>
    /// <returns>The client.</returns>
    private static HttpClient Chain(
        StubHttpMessageHandler transport,
        IAuthenticationProvider authentication)
    {
        AuthenticationHandler handler = new AuthenticationHandler(authentication)
        {
            InnerHandler = transport
        };

        return new HttpClient(handler);
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// An authentication provider issuing a different credential each time it is asked, so that a
    /// handler reusing a stale one is visible.
    /// </summary>
    private sealed class CountingAuthenticationProvider : IAuthenticationProvider
    {
        #region Properties

        /// <summary>
        /// Gets how many times a request has been authenticated.
        /// </summary>
        public int Count { get; private set; }

        #endregion

        #region Methods

        /// <inheritdoc />
        public Task AuthenticateRequestAsync(
            RequestInformation request,
            Dictionary<string, object>? additionalAuthenticationContext = null,
            CancellationToken cancellationToken = default)
        {
            Count++;

            request.Headers.Remove("Authorization");
            request.Headers.Add("Authorization", $"Bearer {Count}");

            return Task.CompletedTask;
        }

        #endregion
    }

    #endregion
}
