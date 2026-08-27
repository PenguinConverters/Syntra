namespace PenguinConverters.Syntra.Provider.Tenable.Nessus;

/// <summary>
/// An X.509 certificate a host presented, as read out of the PEM a plugin printed.
/// </summary>
public class Certificate : NessusRecord
{
    #region Properties

    /// <summary>
    /// Gets or sets the full distinguished name of the subject.
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// Gets or sets the full distinguished name of the issuer.
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// Gets or sets the common name of the subject.
    /// </summary>
    public string? SubjectFriendlyName { get; set; }

    /// <summary>
    /// Gets or sets the common name of the issuer.
    /// </summary>
    public string? IssuerFriendlyName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the certificate issued itself.
    /// </summary>
    public bool SelfSigned { get; set; }

    /// <summary>
    /// Gets or sets the first day the certificate is valid.
    /// </summary>
    public string? ValidFrom { get; set; }

    /// <summary>
    /// Gets or sets the last day the certificate is valid.
    /// </summary>
    public string? ValidUntil { get; set; }

    /// <summary>
    /// Gets or sets the span of the validity period in whole days, which is what a policy on
    /// maximum certificate lifetime is expressed against.
    /// </summary>
    public int ValidityDays { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the validity period has passed.
    /// </summary>
    public bool Expired { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the private key was presented alongside the
    /// certificate. Always <c>false</c> for one read out of a scan, which sees only the public
    /// half.
    /// </summary>
    public bool HasPrivateKey { get; set; }

    /// <summary>
    /// Gets or sets the algorithm the certificate was signed with.
    /// </summary>
    public string? SignatureAlgorithm { get; set; }

    /// <summary>
    /// Gets or sets the algorithm of the public key the certificate carries.
    /// </summary>
    public string? PublicKeyAlgorithm { get; set; }

    /// <summary>
    /// Gets or sets the SHA-256 digest of the encoded certificate.
    /// </summary>
    public string? ThumbprintSHA256 { get; set; }

    /// <summary>
    /// Gets or sets the SHA-1 digest of the encoded certificate, which is what a scanner, a
    /// browser and a certificate store have historically shown and is kept for matching against
    /// those. It is not relied on to distinguish two certificates.
    /// </summary>
    public string? ThumbprintSHA1 { get; set; }

    /// <summary>
    /// Gets or sets the size of the public key in bits, or <c>null</c> when the key is of an
    /// algorithm whose size this connector does not read.
    /// </summary>
    public int? KeySize { get; set; }

    /// <summary>
    /// Gets or sets the extensions the certificate marks critical.
    /// </summary>
    public List<string>? CriticalExtensions { get; set; }

    /// <summary>
    /// Gets or sets the subject alternative names the certificate covers.
    /// </summary>
    public string? SubjectAlternativeNames { get; set; }

    /// <summary>
    /// Gets or sets the key usages the certificate permits.
    /// </summary>
    public string? KeyUsage { get; set; }

    /// <summary>
    /// Gets or sets the extended key usages the certificate permits.
    /// </summary>
    public string? ExtendedKeyUsage { get; set; }

    /// <summary>
    /// Gets or sets the PEM the certificate was read from.
    /// </summary>
    public string? PEM { get; set; }

    #endregion
}
