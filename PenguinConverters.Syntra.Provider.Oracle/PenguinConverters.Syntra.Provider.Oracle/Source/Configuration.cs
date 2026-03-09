using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Provider.Oracle.Source;

/// <summary>
/// Configuration settings for the Oracle source provider.
/// </summary>
public class Configuration
{
    /// <summary>
    /// Gets or sets the Oracle database connection string.
    /// Uses <see cref="ProtectedString"/> for optional Keyra encryption support.
    /// </summary>
    public ProtectedString? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the name of the source table or view to query.
    /// </summary>
    public string? TableName { get; set; }

    /// <summary>
    /// Gets or sets the SQL query to execute against the Oracle database.
    /// </summary>
    public string? Query { get; set; }
}
