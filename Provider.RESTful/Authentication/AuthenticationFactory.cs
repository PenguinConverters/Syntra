using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions.Authentication;
using PenguinConverters.Keyra.Settings;
using PenguinConverters.Syntra.Provider.RESTful.Source;

namespace PenguinConverters.Syntra.Provider.RESTful.Authentication;

/// <summary>
/// Builds the Kiota authentication provider a configuration describes.
/// </summary>
/// <remarks>
/// The modes here cover what a REST API typically asks for. An API that asks for something else -
/// a signed request, a mutual TLS handshake, a bespoke challenge - is served by overriding
/// <c>ProviderBuilder.CreateAuthenticationProvider</c> or by assigning its authentication
/// factory, either of which returns an arbitrary <see cref="IAuthenticationProvider"/> that the
/// same request pipeline then applies.
/// </remarks>
public static class AuthenticationFactory
{
    #region Methods

    /// <summary>
    /// Builds the authentication provider for a configuration.
    /// </summary>
    /// <param name="configuration">The root configuration carrying the credentials.</param>
    /// <param name="disclose">The delegate that discloses a configured secret.</param>
    /// <param name="tokenClientFactory">
    /// Creates the client a negotiated credential is requested with. It must not be authenticated
    /// by the provider being built, which would recurse.
    /// </param>
    /// <param name="logger">The logger to use for diagnostic output.</param>
    /// <returns>
    /// The authentication provider, or <c>null</c> when the API is anonymous or the credentials
    /// the configured mode needs are missing.
    /// </returns>
    public static IAuthenticationProvider? Create(
        Configuration configuration,
        DiscloseSecret disclose,
        Func<HttpClient> tokenClientFactory,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        AuthenticationSettings? settings = configuration.Authentication;

        if (settings is null || settings.Mode == AuthenticationMode.None)
        {
            return null;
        }

        switch (settings.Mode)
        {
            case AuthenticationMode.Basic:
                return CreateBasic(settings, disclose, logger);

            case AuthenticationMode.ApiKey:
                return CreateApiKey(settings, disclose, logger);

            case AuthenticationMode.Token:
                return CreateToken(settings, disclose, logger);

            case AuthenticationMode.ClientCredentials:
            case AuthenticationMode.Session:
                return new TokenAuthenticationProvider(
                    new EndpointAccessTokenProvider(
                        tokenClientFactory(),
                        settings,
                        configuration.GetBaseUrl(),
                        disclose,
                        logger),
                    settings.HeaderName,
                    settings.Scheme);

            default:
                logger.LogError("Authentication mode '{Mode}' is not supported.", settings.Mode);
                return null;
        }
    }

    /// <summary>
    /// Builds an HTTP Basic authentication provider.
    /// </summary>
    /// <param name="settings">The credentials.</param>
    /// <param name="disclose">The delegate that discloses a configured secret.</param>
    /// <param name="logger">The logger to use for diagnostic output.</param>
    /// <returns>The provider, or <c>null</c> when the credentials cannot be disclosed.</returns>
    private static IAuthenticationProvider? CreateBasic(
        AuthenticationSettings settings,
        DiscloseSecret disclose,
        ILogger logger)
    {
        if (!TryDisclose(disclose, settings.Username, out string? username, logger, nameof(settings.Username))
            || !TryDisclose(disclose, settings.Password, out string? password, logger, nameof(settings.Password)))
        {
            return null;
        }

        return new BasicAuthenticationProvider(username!, password!);
    }

    /// <summary>
    /// Builds a provider that attaches a fixed key to every request.
    /// </summary>
    /// <param name="settings">The credentials.</param>
    /// <param name="disclose">The delegate that discloses a configured secret.</param>
    /// <param name="logger">The logger to use for diagnostic output.</param>
    /// <returns>The provider, or <c>null</c> when the key cannot be disclosed.</returns>
    private static IAuthenticationProvider? CreateApiKey(
        AuthenticationSettings settings,
        DiscloseSecret disclose,
        ILogger logger)
    {
        string? value = ComposeKey(settings, disclose, logger);

        if (value is null)
        {
            return null;
        }

        bool asParameter = !string.IsNullOrWhiteSpace(settings.ParameterName);

        return new ApiKeyAuthenticationProvider(
            value,
            asParameter ? settings.ParameterName! : settings.HeaderName,
            asParameter
                ? ApiKeyAuthenticationProvider.KeyLocation.QueryParameter
                : ApiKeyAuthenticationProvider.KeyLocation.Header);
    }

    /// <summary>
    /// Builds a provider that presents a configured token under a scheme.
    /// </summary>
    /// <param name="settings">The credentials.</param>
    /// <param name="disclose">The delegate that discloses a configured secret.</param>
    /// <param name="logger">The logger to use for diagnostic output.</param>
    /// <returns>The provider, or <c>null</c> when the token cannot be disclosed.</returns>
    private static IAuthenticationProvider? CreateToken(
        AuthenticationSettings settings,
        DiscloseSecret disclose,
        ILogger logger)
    {
        string? value = ComposeKey(settings, disclose, logger);

        return value is null
            ? null
            : new TokenAuthenticationProvider(
                new StaticAccessTokenProvider(value), settings.HeaderName, settings.Scheme);
    }

    /// <summary>
    /// Composes the credential value from the configured key, the optional second half of a
    /// composite key, and the value format.
    /// </summary>
    /// <param name="settings">The credentials.</param>
    /// <param name="disclose">The delegate that discloses a configured secret.</param>
    /// <param name="logger">The logger to use for diagnostic output.</param>
    /// <returns>The composed value, or <c>null</c> when the key cannot be disclosed.</returns>
    private static string? ComposeKey(
        AuthenticationSettings settings,
        DiscloseSecret disclose,
        ILogger logger)
    {
        if (!TryDisclose(disclose, settings.Key, out string? key, logger, nameof(settings.Key)))
        {
            return null;
        }

        // A second half is optional: an API that identifies a caller by one key leaves it unset,
        // and one that wants an access key beside a secret key sets both and formats them into a
        // single header value.
        string secondary = TryDisclose(disclose, settings.SecondaryKey, out string? value, logger, null)
            ? value!
            : string.Empty;

        string format = string.IsNullOrEmpty(settings.ValueFormat)
            ? AuthenticationSettings.DefaultValueFormat
            : settings.ValueFormat;

        return string.Format(format, key, secondary);
    }

    /// <summary>
    /// Discloses a configured credential as a string.
    /// </summary>
    /// <param name="disclose">The delegate that discloses a configured secret.</param>
    /// <param name="secret">The secret to disclose.</param>
    /// <param name="value">When this method returns <c>true</c>, the disclosed value.</param>
    /// <param name="logger">The logger to use for diagnostic output.</param>
    /// <param name="name">
    /// The setting name to report when disclosure fails, or <c>null</c> to fail silently because
    /// the setting is optional.
    /// </param>
    /// <returns><c>true</c> when the value was disclosed; otherwise, <c>false</c>.</returns>
    private static bool TryDisclose(
        DiscloseSecret disclose,
        Secret? secret,
        out string? value,
        ILogger logger,
        string? name)
    {
        value = null;

        if (!disclose(secret, out char[] plaintext))
        {
            if (name is not null)
            {
                logger.LogError(
                    "Authentication.{Setting} is required by the configured authentication mode "
                    + "and could not be disclosed.",
                    name);
            }

            return false;
        }

        try
        {
            value = new string(plaintext);
            return true;
        }
        finally
        {
            Array.Clear(plaintext);
        }
    }

    #endregion
}
