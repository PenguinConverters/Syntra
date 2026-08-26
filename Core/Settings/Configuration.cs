namespace PenguinConverters.Syntra.Core.Settings;

/// <summary>
/// Root configuration class for a Syntra synchronization job.
/// </summary>
public class Configuration
{
    #region Properties

    /// <summary>
    /// Gets or sets the name identifying this synchronization job to the host.
    /// </summary>
    /// <remarks>
    /// The host uses it to name the run so that two of the same job cannot overlap: it is the
    /// identity behind the lease the host takes for the duration of a run - a locked file on
    /// Windows and Linux, a leased blob in Azure.
    ///
    /// A host that loads its configuration from a file can fall back to the file name, but one
    /// handed the configuration as bytes has no such name and needs this value.
    /// </remarks>
    public string? ObjectNamespace { get; set; }

    /// <summary>
    /// Gets or sets the source connector configuration.
    /// </summary>
    public SourceConfiguration Source { get; set; } = new();

    /// <summary>
    /// Gets or sets the target connector configuration.
    /// </summary>
    public TargetConfiguration Target { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether delta synchronization is enabled.
    /// When <c>false</c>, a full synchronization is performed.
    /// </summary>
    public bool Delta { get; set; }

    /// <summary>
    /// Gets or sets the CRON schedule expression for recurring synchronization.
    /// </summary>
    public string? Schedule { get; set; }

    /// <summary>
    /// Gets or sets the maximum degree of parallelism for entity processing.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 1;

    /// <summary>
    /// Gets or sets the certificate settings for authentication.
    /// </summary>
    public CertificateSettings? Certificate { get; set; }

    /// <summary>
    /// Gets or sets the Keyra vault key location used to disclose protected values in this
    /// configuration. Required only when a configuration actually carries a protected value.
    /// </summary>
    public KeyraSettings? Keyra { get; set; }

    /// <summary>
    /// Gets or sets threshold values used for synchronization safety checks.
    /// Keys are threshold names (e.g., <c>MaxDeletes</c>, <c>MaxErrors</c>)
    /// and values are the numeric limits.
    /// </summary>
    public Dictionary<string, int> Thresholds { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether SchemaDesigner mode is active.
    /// In this mode, the consumer generates schema information instead of writing data.
    /// </summary>
    public bool SchemaDesigner { get; set; }

    #endregion
}
