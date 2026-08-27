namespace PenguinConverters.Syntra.Provider.Tenable.Nessus;

/// <summary>
/// An SSH protocol version a host reported as supported.
/// </summary>
public class SecureShellVersion : NessusRecord
{
    #region Properties

    /// <summary>
    /// Gets or sets the protocol version, such as <c>2.0</c> or <c>1.99</c>.
    /// </summary>
    public string? Version { get; set; }

    #endregion
}
