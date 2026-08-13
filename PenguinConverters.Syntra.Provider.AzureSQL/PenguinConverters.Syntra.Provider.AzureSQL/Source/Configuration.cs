using PenguinConverters.Syntra.Core.Settings;

namespace PenguinConverters.Syntra.Provider.AzureSQL.Source;

/// <summary>
/// Configuration settings for the Azure SQL source provider.
/// Defines SQL connection parameters and offset-based delta synchronization settings.
/// </summary>
public class Configuration
{
    #region Properties

    /// <summary>
    /// Gets or sets the SQL Server connection string.
    /// Uses <see cref="ProtectedString"/> for optional Keyra encryption support.
    /// </summary>
    public ProtectedString? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the name of the source table or view to query.
    /// </summary>
    public string? TableName { get; set; }

    /// <summary>
    /// Gets or sets the column name used for offset-based delta synchronization.
    /// Typically a datetime column such as <c>ModifiedDate</c> or <c>LastUpdated</c>.
    /// </summary>
    public string? OffsetColumn { get; set; }

    /// <summary>
    /// Gets or sets an optional WHERE clause appended to the base query.
    /// Applied during both full and delta synchronization.
    /// </summary>
    public string? WhereClause { get; set; }

    #endregion
}
