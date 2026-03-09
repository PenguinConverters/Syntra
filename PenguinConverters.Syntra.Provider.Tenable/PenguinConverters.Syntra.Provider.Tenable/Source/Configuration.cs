using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Provider.Tenable.Source;

/// <summary>
/// Configuration settings for the Tenable source provider.
/// </summary>
public class Configuration
{
    /// <summary>
    /// Gets or sets the Tenable.io API hostname.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// Gets or sets the API access key.
    /// Uses <see cref="ProtectedString"/> for optional Keyra encryption support.
    /// </summary>
    public ProtectedString? AccessKey { get; set; }

    /// <summary>
    /// Gets or sets the API secret key.
    /// Uses <see cref="ProtectedString"/> for optional Keyra encryption support.
    /// </summary>
    public ProtectedString? SecretKey { get; set; }
}
