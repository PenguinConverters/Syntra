using PenguinConverters.Keyra.Settings;
using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Provider.Tufin.Source;

/// <summary>
/// Configuration settings for the Tufin source provider.
/// </summary>
public class Configuration
{
    #region Properties

    /// <summary>
    /// Gets or sets the Tufin server hostname.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// Gets or sets the REST API endpoint path.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the username credential.
    /// Uses <see cref="Secret"/> for optional Keyra encryption support.
    /// </summary>
    public Secret? Username { get; set; }

    /// <summary>
    /// Gets or sets the password credential.
    /// Uses <see cref="Secret"/> for optional Keyra encryption support.
    /// </summary>
    public Secret? Password { get; set; }

    #endregion
}
