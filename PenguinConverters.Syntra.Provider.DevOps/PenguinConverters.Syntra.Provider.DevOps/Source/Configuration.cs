using PenguinConverters.Keyra.Settings;
using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Provider.DevOps.Source;

/// <summary>
/// Configuration settings for the Azure DevOps source provider.
/// Defines Azure DevOps REST API connection parameters.
/// </summary>
public class Configuration
{
    #region Properties

    /// <summary>
    /// Gets or sets the Azure DevOps organization name.
    /// </summary>
    public string? Organization { get; set; }

    /// <summary>
    /// Gets or sets the Azure DevOps project name.
    /// </summary>
    public string? Project { get; set; }

    /// <summary>
    /// Gets or sets the Personal Access Token (PAT) for authentication.
    /// Uses <see cref="Secret"/> for optional Keyra encryption support.
    /// </summary>
    public Secret? PAT { get; set; }

    #endregion
}
