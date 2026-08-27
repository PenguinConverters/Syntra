using System.Diagnostics.CodeAnalysis;

namespace PenguinConverters.Syntra.Core.Extensions;

/// <summary>
/// Resolves the URLs a configuration and an API response state, which may be absolute or
/// relative.
/// </summary>
/// <remarks>
/// Whether a string is an absolute URL cannot be settled by <see cref="UriKind.Absolute"/> alone.
/// On a Unix host a leading slash makes <c>/api/jwt/login</c> an absolute <c>file</c> URL, so a
/// root-relative endpoint parses successfully and then addresses the local filesystem instead of
/// the API - on Linux only, which is where the service host runs. Only an <c>http</c> or
/// <c>https</c> URL is treated as absolute here; anything else is resolved against its base.
/// <para>
/// This lives in the core rather than in one connector because every connector that reads an
/// HTTP API has to settle the same question, and each one answering it separately is how the
/// mistake gets made again.
/// </para>
/// </remarks>
public static class UrlResolver
{
    #region Methods

    /// <summary>
    /// Determines whether a URL addresses an API on its own.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <param name="absolute">When this method returns <c>true</c>, the parsed URL.</param>
    /// <returns><c>true</c> when the URL is an absolute web URL; otherwise, <c>false</c>.</returns>
    public static bool IsAbsolute(string? url, [NotNullWhen(true)] out Uri? absolute)
    {
        absolute = null;

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        absolute = parsed;
        return true;
    }

    /// <summary>
    /// Resolves a URL against the request it was stated relative to.
    /// </summary>
    /// <param name="baseUrl">The absolute URL to resolve against.</param>
    /// <param name="url">The URL, absolute or relative.</param>
    /// <returns>The absolute URL.</returns>
    public static string Resolve(string baseUrl, string url)
    {
        return IsAbsolute(url, out Uri? absolute)
            ? absolute.ToString()
            : new Uri(new Uri(baseUrl), url).ToString();
    }

    #endregion
}
