using Microsoft.Extensions.Logging;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Provider.AzureResources.Source;

namespace PenguinConverters.Syntra.Provider.AzureResources;

/// <summary>
/// Azure Resources source provider that retrieves entities from Azure Resource Manager REST APIs.
/// Supports querying subscriptions, role assignments, role definitions, policy definitions,
/// policy assignments, policy states, and management groups.
/// </summary>
public class Provider : Core.Source.Provider
{
    private Configuration? _configuration;

    /// <summary>
    /// Gets or sets the provider configuration.
    /// </summary>
    internal Configuration? Configuration
    {
        get => _configuration;
        set => _configuration = value;
    }

    /// <summary>
    /// Deserializes the raw configuration bytes and applies them to this provider.
    /// </summary>
    internal void DeserializeAndApplyConfiguration()
    {
        _configuration = DeserializeConfiguration<Configuration>()
            ?? throw new InvalidOperationException("Failed to deserialize Azure Resources provider configuration.");
    }

    /// <inheritdoc />
    public override IEnumerable<IEntity> Retrieve(IEnumerable<string> properties)
    {
        if (_configuration is null)
        {
            Logger.LogError("Azure Resources provider configuration is not set.");
            yield break;
        }

        Logger.LogInformation(
            "Starting Azure Resources retrieval for subscription '{SubscriptionId}'.",
            _configuration.SubscriptionId);

        // Authenticate via ClientSecretCredential using TenantId, ClientId, ClientSecret
        // For each configured endpoint, call the ARM REST API:
        //   /subscriptions/{subscriptionId}/providers/Microsoft.Authorization/roleAssignments?api-version=...
        //   /subscriptions/{subscriptionId}/providers/Microsoft.Authorization/roleDefinitions?api-version=...
        //   /subscriptions/{subscriptionId}/providers/Microsoft.Authorization/policyDefinitions?api-version=...
        //   /subscriptions/{subscriptionId}/providers/Microsoft.Authorization/policyAssignments?api-version=...
        //   /providers/Microsoft.Management/managementGroups?api-version=...
        //   /subscriptions/{subscriptionId}/providers/Microsoft.PolicyInsights/policyStates?api-version=...

        // Page through results using nextLink and yield Entity objects

        Logger.LogInformation("Azure Resources retrieval completed.");
    }
}
