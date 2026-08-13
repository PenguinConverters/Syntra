namespace PenguinConverters.Syntra.Core.Settings;

/// <summary>
/// Configuration for a source connector (provider).
/// </summary>
public class SourceConfiguration
{
    #region Properties

    /// <summary>
    /// Gets or sets the assembly name of the provider connector to load dynamically.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source table or object name.
    /// </summary>
    public string? TableName { get; set; }

    /// <summary>
    /// Gets or sets the connection string for the source system.
    /// </summary>
    public ProtectedString? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the list of column/property names to retrieve.
    /// </summary>
    public List<string> Columns { get; set; } = [];

    /// <summary>
    /// Gets or sets the primary key columns used for entity identification.
    /// </summary>
    public List<string> PrimaryKeys { get; set; } = [];

    #endregion
}
