using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Provider.Exchange.Source;

/// <summary>
/// Configuration settings for the Exchange Online source provider.
/// Defines Microsoft Graph API connection parameters for Exchange mailbox data.
/// </summary>
public class Configuration
{
    /// <summary>
    /// Gets or sets the Azure AD tenant identifier.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the application (client) identifier.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the client secret credential.
    /// Uses <see cref="ProtectedString"/> for optional Keyra encryption support.
    /// </summary>
    public ProtectedString? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the Graph API endpoint path for Exchange data
    /// (e.g., "/users/{id}/mailFolders", "/users/{id}/messages").
    /// </summary>
    public string? EndPoint { get; set; }
}
