using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Provider.Ciphersuite.Source;

/// <summary>
/// Configuration settings for the Ciphersuite source provider.
/// </summary>
public class Configuration
{
    /// <summary>
    /// Gets or sets the Ciphersuite server hostname.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// Gets or sets the REST API endpoint path.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the API key credential.
    /// Uses <see cref="ProtectedString"/> for optional Keyra encryption support.
    /// </summary>
    public ProtectedString? ApiKey { get; set; }
}
