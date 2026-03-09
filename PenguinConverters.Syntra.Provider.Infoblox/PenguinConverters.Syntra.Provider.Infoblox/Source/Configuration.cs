using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Provider.Infoblox.Source;

/// <summary>
/// Configuration settings for the Infoblox source provider.
/// </summary>
public class Configuration
{
    /// <summary>
    /// Gets or sets the Infoblox Grid Master hostname.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// Gets or sets the WAPI version (e.g., "v2.12").
    /// </summary>
    public string? ApiVersion { get; set; }

    /// <summary>
    /// Gets or sets the username credential.
    /// Uses <see cref="ProtectedString"/> for optional Keyra encryption support.
    /// </summary>
    public ProtectedString? Username { get; set; }

    /// <summary>
    /// Gets or sets the password credential.
    /// Uses <see cref="ProtectedString"/> for optional Keyra encryption support.
    /// </summary>
    public ProtectedString? Password { get; set; }
}
