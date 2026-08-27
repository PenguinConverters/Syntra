namespace PenguinConverters.Syntra.Provider.Tenable.Nessus;

/// <summary>
/// An SSH algorithm a host reported as supported, in one of the negotiation categories.
/// </summary>
public class SecureShellAlgorithm : NessusRecord
{
    #region Properties

    /// <summary>
    /// Gets or sets the negotiation category as the plugin named it, such as
    /// <c>encryption_algorithms_client_to_server</c>.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the category the type falls under, such as <c>Encryption</c>, <c>MAC</c>,
    /// <c>KEX</c>, <c>Host Key</c> or <c>Compression</c>.
    /// </summary>
    public string? TypeGroup { get; set; }

    /// <summary>
    /// Gets or sets the algorithm name as the plugin reported it.
    /// </summary>
    public string Algorithm { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the algorithm name with its vendor suffix and encrypt-then-MAC marker
    /// removed, so that the same primitive compares equal however it was advertised.
    /// </summary>
    public string AlgorithmClean { get; set; } = string.Empty;

    #endregion
}
