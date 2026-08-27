namespace PenguinConverters.Syntra.Provider.RESTful.Settings;

/// <summary>
/// The watermark a delta run resumes from, recorded alongside the endpoint that produced it.
/// </summary>
/// <remarks>
/// A watermark belongs to the endpoint it was read from, so one recorded against a different
/// endpoint is discarded rather than replayed: filtering a new endpoint by the previous one's
/// high-water mark would silently skip everything older than it.
/// </remarks>
public class State
{
    #region Properties

    /// <summary>
    /// Gets or sets the highest modification timestamp seen in the previous run.
    /// <c>null</c> when no delta run has completed yet, which produces a full pass.
    /// </summary>
    public DateTime? Offset { get; set; }

    /// <summary>
    /// Gets or sets the opaque continuation token the previous run ended on, for an API that
    /// issues one instead of a timestamp. Unused by the built-in retrieval, and carried here so
    /// that a derived connector has somewhere to record it.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Gets or sets the endpoint the watermark was read from.
    /// </summary>
    public string? EndPoint { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp the watermark was recorded at.
    /// </summary>
    public DateTime Recorded { get; set; } = DateTime.UtcNow;

    #endregion
}
