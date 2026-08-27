namespace PenguinConverters.Syntra.Provider.Tenable.Nessus;

/// <summary>
/// A TLS cipher suite a host reported as supported, under the protocol version it was offered on.
/// </summary>
public class CipherSuite : NessusRecord
{
    #region Properties

    /// <summary>
    /// Gets or sets the protocol version the suite was offered on, such as <c>TLSv1.2</c>.
    /// </summary>
    public string TLSVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the suite as the plugin reported it.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the two-byte code identifying the suite on the wire.
    /// </summary>
    public List<string> Code { get; set; } = [];

    #endregion
}
