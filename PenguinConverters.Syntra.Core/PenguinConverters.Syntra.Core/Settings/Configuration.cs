namespace PenguinConverters.Syntra.Core.Settings;

/// <summary>
/// Root configuration class for a Syntra synchronization job.
/// </summary>
public class Configuration
{
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
}
