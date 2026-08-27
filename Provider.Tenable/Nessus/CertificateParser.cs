using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PenguinConverters.Syntra.Provider.Tenable.Nessus;

/// <summary>
/// Reads the certificate plugin output: every PEM the plugin printed, decoded into the fields a
/// certificate policy is written against.
/// </summary>
public static partial class CertificateParser
{
    #region Constants

    /// <summary>
    /// Header a PEM body is wrapped in for decoding.
    /// </summary>
    public const string PemHeader = "-----BEGIN CERTIFICATE-----";

    /// <summary>
    /// Footer a PEM body is wrapped in for decoding.
    /// </summary>
    public const string PemFooter = "-----END CERTIFICATE-----";

    /// <summary>
    /// Protocol the certificate plugin reports against.
    /// </summary>
    public const string Protocol = "TCP";

    /// <summary>
    /// Object identifier of the subject alternative name extension.
    /// </summary>
    private const string SubjectAlternativeNameOid = "2.5.29.17";

    /// <summary>
    /// Object identifier of the RSA public key algorithm.
    /// </summary>
    private const string RsaOid = "1.2.840.113549.1.1.1";

    /// <summary>
    /// Object identifier of the DSA public key algorithm.
    /// </summary>
    private const string DsaOid = "1.2.840.10040.4.1";

    /// <summary>
    /// Object identifier of the elliptic curve public key algorithm.
    /// </summary>
    private const string EllipticCurveOid = "1.2.840.10045.2.1";

    #endregion

    #region Methods

    /// <summary>
    /// Reads every certificate a plugin output carries.
    /// </summary>
    /// <remarks>
    /// A plugin output that carries no PEM yields nothing rather than failing. A scan reaches
    /// hosts that answer without a certificate, and one such host must not take the retrieval
    /// down with it.
    /// </remarks>
    /// <param name="output">The plugin output.</param>
    /// <param name="plugin">The row the output came from.</param>
    /// <param name="logger">The logger to report an unreadable certificate to.</param>
    /// <returns>One record per certificate.</returns>
    public static List<NessusRecord> ParseAll(
        string? output,
        NessusPlugin? plugin = null,
        ILogger? logger = null)
    {
        List<NessusRecord> certificates = [];

        if (string.IsNullOrEmpty(output))
        {
            return certificates;
        }

        foreach (Match match in PemExpression().Matches(output))
        {
            certificates.Add(Parse(Wrap(match.Groups["body"].Value.Trim()), plugin, logger));
        }

        return certificates;
    }

    /// <summary>
    /// Reads one certificate from its PEM.
    /// </summary>
    /// <param name="pem">The PEM.</param>
    /// <param name="plugin">The row the PEM came from.</param>
    /// <param name="logger">The logger to report an unreadable certificate to.</param>
    /// <returns>
    /// The certificate, or a bare record carrying the reason it could not be read. A scan sees
    /// whatever a host presents, including a certificate no parser accepts, and recording that it
    /// was seen is worth more than dropping the observation.
    /// </returns>
    public static NessusRecord Parse(string pem, NessusPlugin? plugin = null, ILogger? logger = null)
    {
        ILogger log = logger ?? NullLogger.Instance;

        try
        {
            // X509Certificate2's byte[] constructor is obsolete because it guessed at the content
            // type; the loader states outright that this is a single DER certificate.
            using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(Decode(pem));

            string? subject = certificate.GetNameInfo(X509NameType.SimpleName, false);
            string? issuer = certificate.GetNameInfo(X509NameType.SimpleName, true);

            Certificate parsed = new Certificate
            {
                Subject = certificate.SubjectName.Name,
                Issuer = certificate.IssuerName.Name,
                SubjectFriendlyName = subject?.ToLowerInvariant(),
                IssuerFriendlyName = issuer,
                SelfSigned = subject is not null
                    && string.Equals(subject, issuer, StringComparison.OrdinalIgnoreCase),
                ValidFrom = certificate.NotBefore.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ValidUntil = certificate.NotAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                // Days rather than a subtraction of calendar years: a certificate issued on the
                // last day of a year and expiring on the first of the next is not a year long,
                // and the lifetime limits a policy states are counted in days.
                ValidityDays = (int)Math.Round((certificate.NotAfter - certificate.NotBefore).TotalDays),
                Expired = DateTime.UtcNow > certificate.NotAfter.ToUniversalTime(),
                HasPrivateKey = certificate.HasPrivateKey,
                SignatureAlgorithm = certificate.SignatureAlgorithm.FriendlyName,
                PublicKeyAlgorithm = certificate.PublicKey.Oid.FriendlyName,
                ThumbprintSHA256 = ToHex(SHA256.HashData(certificate.RawData)),
                ThumbprintSHA1 = ToHex(SHA1.HashData(certificate.RawData)),
                KeySize = GetKeySize(certificate, log),
                CriticalExtensions = GetCriticalExtensions(certificate),
                SubjectAlternativeNames = GetSubjectAlternativeNames(certificate),
                KeyUsage = GetKeyUsage(certificate),
                ExtendedKeyUsage = GetExtendedKeyUsage(certificate),
                PEM = pem
            };

            Stamp(parsed, plugin);

            return parsed;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            log.LogWarning(
                ex,
                "A certificate presented by {Host} on port {Port} could not be read.",
                plugin?.DNSName ?? plugin?.IPAddress,
                plugin?.Port);

            NessusRecord unreadable = new NessusRecord
            {
                Message = $"{ex.Message}: {pem}"
            };

            Stamp(unreadable, plugin);

            return unreadable;
        }
    }

    /// <summary>
    /// Wraps a base-64 body in the PEM delimiters.
    /// </summary>
    /// <param name="body">The base-64 body.</param>
    /// <returns>The PEM.</returns>
    private static string Wrap(string body)
    {
        return $"{PemHeader}\n{body}\n{PemFooter}";
    }

    /// <summary>
    /// Decodes the base-64 body of a PEM.
    /// </summary>
    /// <param name="pem">The PEM.</param>
    /// <returns>The encoded certificate.</returns>
    /// <exception cref="ArgumentException">Thrown when the text is not a PEM.</exception>
    private static byte[] Decode(string pem)
    {
        int start = pem.IndexOf(PemHeader, StringComparison.Ordinal);
        int end = pem.IndexOf(PemFooter, StringComparison.Ordinal);

        if (start < 0 || end <= start)
        {
            throw new ArgumentException("The text carries no PEM certificate.", nameof(pem));
        }

        string body = pem[(start + PemHeader.Length)..end];

        return Convert.FromBase64String(WhitespaceExpression().Replace(body, string.Empty));
    }

    /// <summary>
    /// Returns the size of a certificate's public key in bits.
    /// </summary>
    /// <remarks>
    /// An algorithm this does not recognise yields <c>null</c> rather than failing the
    /// certificate: an Ed25519 key has no size worth reporting, and refusing the whole
    /// certificate over one field would discard everything else read from it.
    /// </remarks>
    /// <param name="certificate">The certificate.</param>
    /// <param name="logger">The logger to report an unrecognised algorithm to.</param>
    /// <returns>The size in bits, or <c>null</c>.</returns>
    private static int? GetKeySize(X509Certificate2 certificate, ILogger logger)
    {
        switch (certificate.PublicKey.Oid.Value)
        {
            case RsaOid:
                using (RSA? rsa = certificate.GetRSAPublicKey())
                {
                    return rsa?.KeySize;
                }

            case DsaOid:
                using (DSA? dsa = certificate.GetDSAPublicKey())
                {
                    return dsa?.KeySize;
                }

            case EllipticCurveOid:
                using (ECDsa? ecdsa = certificate.GetECDsaPublicKey())
                {
                    return ecdsa?.KeySize;
                }

            default:
                logger.LogDebug(
                    "No key size is reported for public key algorithm {Algorithm}.",
                    certificate.PublicKey.Oid.Value);
                return null;
        }
    }

    /// <summary>
    /// Returns the subject alternative names a certificate covers.
    /// </summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>The names, or <c>null</c> when the certificate carries none.</returns>
    private static string? GetSubjectAlternativeNames(X509Certificate2 certificate)
    {
        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension.Oid?.Value != SubjectAlternativeNameOid)
            {
                continue;
            }

            AsnEncodedData data = new AsnEncodedData(extension.Oid, extension.RawData);

            return data.Format(true).Replace("\r", string.Empty).Replace("\n", ", ").Trim(',', ' ');
        }

        return null;
    }

    /// <summary>
    /// Returns the key usages a certificate permits.
    /// </summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>The usages, ordered so that two certificates compare equal on the same set.</returns>
    private static string? GetKeyUsage(X509Certificate2 certificate)
    {
        foreach (X509Extension extension in certificate.Extensions)
        {
            // Pattern matching rather than a cast: an extension carrying this identifier is
            // materialized as the typed class, but a malformed one need not be.
            if (extension is not X509KeyUsageExtension usage)
            {
                continue;
            }

            List<string> flags = [];

            foreach (X509KeyUsageFlags flag in Enum.GetValues<X509KeyUsageFlags>())
            {
                if (flag != X509KeyUsageFlags.None && usage.KeyUsages.HasFlag(flag))
                {
                    flags.Add(flag.ToString());
                }
            }

            return string.Join(", ", flags.OrderBy(flag => flag, StringComparer.Ordinal));
        }

        return null;
    }

    /// <summary>
    /// Returns the extended key usages a certificate permits.
    /// </summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>The usages, ordered so that two certificates compare equal on the same set.</returns>
    private static string? GetExtendedKeyUsage(X509Certificate2 certificate)
    {
        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension is not X509EnhancedKeyUsageExtension usage)
            {
                continue;
            }

            return string.Join(
                ", ",
                usage.EnhancedKeyUsages
                    .Cast<Oid>()
                    .Select(oid => oid.FriendlyName ?? oid.Value ?? string.Empty)
                    .OrderBy(name => name, StringComparer.Ordinal));
        }

        return null;
    }

    /// <summary>
    /// Returns the extensions a certificate marks critical.
    /// </summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>The extensions.</returns>
    private static List<string> GetCriticalExtensions(X509Certificate2 certificate)
    {
        List<string> critical = [];

        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension.Critical)
            {
                critical.Add($"{extension.Oid?.FriendlyName ?? extension.Oid?.Value} ({extension.Oid?.Value})");
            }
        }

        return critical;
    }

    /// <summary>
    /// Renders a digest as lower-case hexadecimal, which is how a thumbprint is quoted.
    /// </summary>
    /// <param name="digest">The digest.</param>
    /// <returns>The hexadecimal text.</returns>
    private static string ToHex(byte[] digest)
    {
        return Convert.ToHexStringLower(digest);
    }

    /// <summary>
    /// Stamps a record with the asset the plugin ran against.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <param name="plugin">The row the output came from.</param>
    private static void Stamp(NessusRecord record, NessusPlugin? plugin)
    {
        record.IPAddress = plugin?.IPAddress ?? string.Empty;
        record.DNSName = plugin?.DNSName ?? string.Empty;
        record.ShortName = plugin?.ShortName;
        record.Protocol = Protocol;
        record.Port = plugin?.Port ?? -1;
        record.FirstDiscovered = plugin?.FirstDiscovered;
        record.LastObserved = plugin?.LastObserved;
        record.PluginName = plugin?.PluginName;
        record.Plugin = plugin?.Plugin;
    }

    /// <summary>
    /// Matches a PEM certificate and captures its base-64 body.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"-+BEGIN CERTIFICATE-+\s*(?<body>.*?)\s*-+END CERTIFICATE-+", RegexOptions.Singleline)]
    private static partial Regex PemExpression();

    /// <summary>
    /// Matches the whitespace a PEM body is wrapped across lines with.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceExpression();

    #endregion
}
