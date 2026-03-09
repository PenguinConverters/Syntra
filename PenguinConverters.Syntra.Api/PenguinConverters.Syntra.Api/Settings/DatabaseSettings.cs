namespace PenguinConverters.Syntra.Api.Settings;

/// <summary>
/// Configuration settings for database connectivity and SQL impersonation.
/// </summary>
public sealed class DatabaseSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Database";

    /// <summary>
    /// Gets or sets the SQL Server connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Azure SQL resource identifier used when acquiring access tokens
    /// for the On-Behalf-Of (OBO) flow.
    /// </summary>
    /// <remarks>
    /// Typically set to <c>https://database.windows.net/.default</c> for Azure SQL Database.
    /// </remarks>
    public string SqlResource { get; set; } = "https://database.windows.net/.default";

    /// <summary>
    /// Gets or sets a value indicating whether to use Windows Integrated Authentication
    /// (Negotiate/Kerberos) for SQL Server connections.
    /// </summary>
    public bool UseIntegratedSecurity { get; set; }

    /// <summary>
    /// Gets or sets the certificate thumbprint used for client certificate authentication
    /// when impersonating SQL connections.
    /// </summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>
    /// Gets or sets the certificate store location for SQL impersonation certificates.
    /// </summary>
    public string CertificateStoreLocation { get; set; } = "CurrentUser";

    /// <summary>
    /// Gets or sets the default command timeout in seconds for SQL queries.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Gets or sets the maximum number of rows returned when no $top is specified.
    /// </summary>
    public int DefaultMaxRows { get; set; } = 10000;
}
