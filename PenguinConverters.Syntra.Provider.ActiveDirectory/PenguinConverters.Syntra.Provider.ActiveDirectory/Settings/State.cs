namespace PenguinConverters.Syntra.Provider.ActiveDirectory.Settings;

/// <summary>
/// Tracks the synchronization state for the Active Directory provider.
/// Stores the USN watermark and domain controller identity to ensure
/// delta sync consistency across runs.
/// </summary>
public class State
{
    /// <summary>
    /// Gets or sets the highest committed USN from the domain controller
    /// at the time of the last successful synchronization.
    /// </summary>
    public long HighestCommittedUSN { get; set; }

    /// <summary>
    /// Gets or sets the DNS hostname of the domain controller used in the last sync.
    /// </summary>
    public string? Server { get; set; }

    /// <summary>
    /// Gets or sets the objectGUID of the domain controller server object.
    /// Used to verify DC affinity between sync runs.
    /// </summary>
    public string? ServerObjectGuid { get; set; }

    /// <summary>
    /// Gets the UTC timestamp when this state was recorded.
    /// </summary>
    public DateTime DateTime => DateTime.UtcNow;
}
