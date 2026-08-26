using PenguinConverters.Keyra.Settings;
using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Provider.AzureResources.Source;

/// <summary>
/// Configuration settings for the Azure Resources source provider.
/// Defines Azure Resource Manager REST API connection parameters.
/// </summary>
public class Configuration
{
    #region Properties

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
    /// Uses <see cref="Secret"/> for optional Keyra encryption support.
    /// </summary>
    public Secret? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the Azure subscription identifier.
    /// </summary>
    public string? SubscriptionId { get; set; }

    /// <summary>
    /// Gets or sets the ARM API version (e.g., "2022-09-01").
    /// </summary>
    public string? ApiVersion { get; set; }

    /// <summary>
    /// Gets or sets the resource endpoints to query.
    /// Supported values include: subscriptions, roleAssignments, roleDefinitions,
    /// policyDefinitions, policyAssignments, policyStates, managementGroups.
    /// </summary>
    public List<string>? Endpoints { get; set; }

    #endregion
}
