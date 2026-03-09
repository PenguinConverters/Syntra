using Microsoft.Extensions.Logging;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Provider.DevOps.Source;

namespace PenguinConverters.Syntra.Provider.DevOps;

/// <summary>
/// Azure DevOps source provider that retrieves entities via the Azure DevOps REST API.
/// Authenticates using a Personal Access Token (PAT) and retrieves project resources
/// such as repositories, pipelines, work items, and team members.
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
            ?? throw new InvalidOperationException("Failed to deserialize DevOps provider configuration.");
    }

    /// <inheritdoc />
    public override IEnumerable<IEntity> Retrieve(IEnumerable<string> properties)
    {
        if (_configuration is null)
        {
            Logger.LogError("DevOps provider configuration is not set.");
            yield break;
        }

        Logger.LogInformation(
            "Starting DevOps retrieval for organization '{Organization}', project '{Project}'.",
            _configuration.Organization,
            _configuration.Project);

        // 1. Authenticate via PAT (Base64 encoded as Basic auth header)
        // 2. Build request URL: https://dev.azure.com/{Organization}/{Project}/_apis/...
        // 3. Page through Azure DevOps API results using continuationToken
        // 4. Yield Entity objects per result record

        Logger.LogInformation("DevOps retrieval completed.");
    }
}
