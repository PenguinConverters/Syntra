using PenguinConverters.Keyra.Settings;

namespace PenguinConverters.Syntra.Core.Settings;

/// <summary>
/// Configuration for a destination connector (consumer).
/// </summary>
public class TargetConfiguration
{
    #region Properties

    /// <summary>
    /// Gets or sets the assembly name of the consumer connector to load dynamically.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target table or object name.
    /// </summary>
    public string? TableName { get; set; }

    /// <summary>
    /// Gets or sets the connection string for the target system.
    /// May be a Keyra-protected value; disclose it with the pipeline's decryptor.
    /// </summary>
    public Secret? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the list of column/property names to write.
    /// </summary>
    public List<string> Columns { get; set; } = [];

    /// <summary>
    /// Gets or sets the primary key columns used for entity identification.
    /// </summary>
    public List<string> PrimaryKeys { get; set; } = [];

    #endregion
}
