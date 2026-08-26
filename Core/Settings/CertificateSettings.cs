using System.Security.Cryptography.X509Certificates;
using PenguinConverters.Keyra.Settings;

namespace PenguinConverters.Syntra.Core.Settings;

/// <summary>
/// Configuration settings for X.509 certificate-based authentication.
/// </summary>
public class CertificateSettings
{
    #region Properties

    /// <summary>
    /// Gets or sets the certificate store name (e.g., <c>My</c>, <c>Root</c>).
    /// </summary>
    public StoreName StoreName { get; set; } = StoreName.My;

    /// <summary>
    /// Gets or sets the certificate store location (e.g., <c>CurrentUser</c>, <c>LocalMachine</c>).
    /// </summary>
    public StoreLocation StoreLocation { get; set; } = StoreLocation.CurrentUser;

    /// <summary>
    /// Gets or sets the certificate thumbprint for store-based lookup.
    /// </summary>
    public string? Thumbprint { get; set; }

    /// <summary>
    /// Gets or sets the file path to a certificate file (PFX/PEM).
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the password for the certificate file, if applicable.
    /// May be a Keyra-protected value; disclose it with the pipeline's decryptor.
    /// </summary>
    public Secret? Password { get; set; }

    #endregion
}
