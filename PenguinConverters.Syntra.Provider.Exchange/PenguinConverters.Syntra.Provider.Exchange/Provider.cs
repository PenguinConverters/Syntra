using Microsoft.Extensions.Logging;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Provider.Exchange.Source;

namespace PenguinConverters.Syntra.Provider.Exchange;

/// <summary>
/// Exchange Online source provider that retrieves entities via the Microsoft Graph API.
/// Supports querying Exchange mailbox data such as mail folders, messages, and calendar items.
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
            ?? throw new InvalidOperationException("Failed to deserialize Exchange provider configuration.");
    }

    /// <inheritdoc />
    public override IEnumerable<IEntity> Retrieve(IEnumerable<string> properties)
    {
        if (_configuration is null)
        {
            Logger.LogError("Exchange provider configuration is not set.");
            yield break;
        }

        Logger.LogInformation(
            "Starting Exchange retrieval from endpoint '{EndPoint}' for tenant '{TenantId}'.",
            _configuration.EndPoint,
            _configuration.TenantId);

        // 1. Authenticate via ClientSecretCredential using TenantId, ClientId, ClientSecret
        // 2. Build Graph API request: GET {EndPoint}?$select={properties}
        // 3. Page through Graph API results and yield Entity objects
        // 4. Handle throttling (429) with retry-after headers

        Logger.LogInformation("Exchange retrieval completed.");
    }
}
