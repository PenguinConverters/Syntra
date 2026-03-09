using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Provider.ServiceNow.Source;

/// <summary>
/// Configuration settings for the ServiceNow source provider.
/// Defines REST API connection parameters with JWT authentication.
/// </summary>
public class Configuration
{
    /// <summary>
    /// Gets or sets the ServiceNow instance hostname (e.g., "instance.service-now.com").
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// Gets or sets the REST API endpoint path (e.g., "/api/now/table/sys_user").
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets additional query parameters appended to the API request.
    /// </summary>
    public string? Parameters { get; set; }

    /// <summary>
    /// Gets or sets the property name that indicates a record has been deleted.
    /// Used during delta synchronization to identify soft-deleted records.
    /// </summary>
    public string? DeletedProperty { get; set; }

    /// <summary>
    /// Gets or sets the OAuth client identifier for JWT authentication.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the OAuth client secret for JWT authentication.
    /// Uses <see cref="ProtectedString"/> for optional Keyra encryption support.
    /// </summary>
    public ProtectedString? ClientSecret { get; set; }
}
