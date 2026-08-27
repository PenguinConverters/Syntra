namespace PenguinConverters.Syntra.Provider.Tenable.Nessus;

/// <summary>
/// The network asset metadata every record parsed out of a Nessus plugin output carries.
/// </summary>
public interface INessusRecord
{
    #region Properties

    /// <summary>
    /// Gets or sets the IP address of the scanned host.
    /// </summary>
    string? IPAddress { get; set; }

    /// <summary>
    /// Gets or sets the DNS name of the scanned host.
    /// </summary>
    string? DNSName { get; set; }

    /// <summary>
    /// Gets or sets the leading label of <see cref="DNSName"/>.
    /// </summary>
    string? ShortName { get; set; }

    /// <summary>
    /// Gets or sets the network protocol the service was reached over.
    /// </summary>
    string? Protocol { get; set; }

    /// <summary>
    /// Gets or sets the port the service was reached on.
    /// </summary>
    int? Port { get; set; }

    /// <summary>
    /// Gets or sets a note about the record, carrying the reason when a plugin output could not
    /// be parsed.
    /// </summary>
    string? Message { get; set; }

    /// <summary>
    /// Gets or sets the date the scanner first discovered the asset.
    /// </summary>
    DateOnly? FirstDiscovered { get; set; }

    /// <summary>
    /// Gets or sets the date the scanner last observed the asset.
    /// </summary>
    DateOnly? LastObserved { get; set; }

    /// <summary>
    /// Gets the later of <see cref="FirstDiscovered"/> and <see cref="LastObserved"/>.
    /// </summary>
    DateOnly? LastModified { get; }

    #endregion

    #region Methods

    /// <summary>
    /// Returns the key identifying the service this record was observed on.
    /// </summary>
    /// <returns>The key.</returns>
    string GetKey();

    #endregion
}
