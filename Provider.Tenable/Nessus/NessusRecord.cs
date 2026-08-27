namespace PenguinConverters.Syntra.Provider.Tenable.Nessus;

/// <summary>
/// A single observation parsed out of a Nessus plugin output, carrying the asset it was made
/// against. The parsers derive from this to add what their plugin reports.
/// </summary>
public class NessusRecord : INessusRecord
{
    #region Constants

    /// <summary>
    /// Separator joining the parts of <see cref="GetKey"/>.
    /// </summary>
    public const char KeySeparator = '|';

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the name of the plugin that produced the observation.
    /// </summary>
    public string? PluginName { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the plugin that produced the observation.
    /// </summary>
    public long? Plugin { get; set; }

    /// <inheritdoc />
    public string? IPAddress { get; set; }

    /// <inheritdoc />
    public string? DNSName { get; set; }

    /// <inheritdoc />
    public string? ShortName { get; set; }

    /// <inheritdoc />
    public string? Protocol { get; set; }

    /// <inheritdoc />
    public int? Port { get; set; }

    /// <inheritdoc />
    public string? Message { get; set; }

    /// <inheritdoc />
    public DateOnly? FirstDiscovered { get; set; }

    /// <inheritdoc />
    public DateOnly? LastObserved { get; set; }

    /// <inheritdoc />
    public DateOnly? LastModified => FirstDiscovered > LastObserved ? FirstDiscovered : LastObserved;

    #endregion

    #region Methods

    /// <inheritdoc />
    /// <remarks>
    /// The parts are joined by a separator rather than concatenated, so that an address ending in
    /// a digit and a port cannot run together into the same key as a different pair.
    /// </remarks>
    public string GetKey()
    {
        return string.Join(
            KeySeparator,
            IPAddress ?? string.Empty,
            DNSName ?? string.Empty,
            Protocol ?? string.Empty,
            Port?.ToString() ?? string.Empty);
    }

    #endregion
}
