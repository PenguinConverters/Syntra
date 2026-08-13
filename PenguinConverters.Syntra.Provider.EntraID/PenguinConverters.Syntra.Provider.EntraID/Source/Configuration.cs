using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Provider.EntraID.Source;

/// <summary>
/// Configuration settings for the Entra ID (Azure AD) source provider.
/// Defines Microsoft Graph API connection parameters and relationship support.
/// </summary>
public class Configuration
{
    #region Properties

    /// <summary>
    /// Gets or sets the Azure AD tenant identifier.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the application (client) identifier for the Graph API service principal.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the client secret credential for the Graph API service principal.
    /// Uses <see cref="ProtectedString"/> for optional Keyra encryption support.
    /// </summary>
    public ProtectedString? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the Microsoft Graph API endpoint path (e.g., "/users", "/groups").
    /// </summary>
    public string? EndPoint { get; set; }

    /// <summary>
    /// Gets or sets the relationship definitions for resolving nested objects.
    /// Supports placeholder resolution using <c>&lt;%objectId%&gt;</c> syntax.
    /// </summary>
    public List<RelationshipDefinition>? Relationships { get; set; }

    #endregion
}

/// <summary>
/// Defines a relationship endpoint for resolving nested Graph API objects.
/// </summary>
public class RelationshipDefinition
{
    #region Properties

    /// <summary>
    /// Gets or sets the Graph API endpoint template for the relationship.
    /// May include placeholders like <c>&lt;%objectId%&gt;</c> that are resolved per entity.
    /// </summary>
    public string? EndPoint { get; set; }

    /// <summary>
    /// Gets or sets the property name to store the resolved relationship data.
    /// </summary>
    public string? Property { get; set; }

    #endregion
}
