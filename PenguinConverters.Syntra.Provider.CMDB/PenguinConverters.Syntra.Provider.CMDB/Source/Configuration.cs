using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Provider.CMDB.Source;

/// <summary>
/// Configuration settings for the CMDB source provider.
/// Defines HTTP REST client connection parameters for CMDB systems.
/// </summary>
public class Configuration
{
    #region Properties

    /// <summary>
    /// Gets or sets the CMDB system hostname.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// Gets or sets the REST API endpoint path.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets additional query parameters appended to the API request.
    /// </summary>
    public string? Parameters { get; set; }

    /// <summary>
    /// Gets or sets the username credential for CMDB authentication.
    /// Uses <see cref="ProtectedString"/> for optional Keyra encryption support.
    /// </summary>
    public ProtectedString? Username { get; set; }

    /// <summary>
    /// Gets or sets the password credential for CMDB authentication.
    /// Uses <see cref="ProtectedString"/> for optional Keyra encryption support.
    /// </summary>
    public ProtectedString? Password { get; set; }

    #endregion
}
