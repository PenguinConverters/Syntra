using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PenguinConverters.Syntra.Provider.Tenable.Nessus;

namespace PenguinConverters.Syntra.Provider.Tenable.Tests.Nessus;

[TestFixture]
public class CertificateParserTests
{
    #region Methods

    [Test]
    public void ParseAll_ReadsTheFieldsACertificatePolicyIsWrittenAgainst()
    {
        //Arrange
        string pem = CreateCertificatePem("CN=web01.example.com", days: 90, keySize: 2048);

        //Act
        Certificate certificate = (Certificate)CertificateParser.ParseAll(Wrap(pem), Plugin()).Single();

        //Assert
        Assert.That(certificate.SubjectFriendlyName, Is.EqualTo("web01.example.com"));
        Assert.That(certificate.IssuerFriendlyName, Is.EqualTo("web01.example.com"));
        Assert.That(certificate.SelfSigned, Is.True);
        Assert.That(certificate.Expired, Is.False);
        Assert.That(certificate.HasPrivateKey, Is.False, "a scan sees only the public half");
        Assert.That(certificate.KeySize, Is.EqualTo(2048));
        Assert.That(certificate.PublicKeyAlgorithm, Is.EqualTo("RSA"));
        Assert.That(certificate.ThumbprintSHA256, Has.Length.EqualTo(64));
        Assert.That(certificate.ThumbprintSHA1, Has.Length.EqualTo(40));
        Assert.That(certificate.ThumbprintSHA256, Is.EqualTo(certificate.ThumbprintSHA256!.ToLowerInvariant()));
        Assert.That(certificate.PEM, Is.Not.Null);
    }

    [Test]
    public void ParseAll_MeasuresValidityInDaysRatherThanCalendarYears()
    {
        //Arrange
        // A lifetime policy is stated in days: the legacy connector subtracted calendar years,
        // which called a certificate spanning a new year "1 year" however short it was.
        string pem = CreateCertificatePem("CN=short.example.com", days: 30, keySize: 2048);

        //Act
        Certificate certificate = (Certificate)CertificateParser.ParseAll(Wrap(pem), Plugin()).Single();

        //Assert
        Assert.That(certificate.ValidityDays, Is.EqualTo(30));
    }

    [Test]
    public void ParseAll_ReadsTheUsagesAndAlternativeNames()
    {
        //Arrange
        string pem = CreateCertificatePem("CN=web01.example.com", days: 90, keySize: 2048);

        //Act
        Certificate certificate = (Certificate)CertificateParser.ParseAll(Wrap(pem), Plugin()).Single();

        //Assert
        Assert.That(certificate.KeyUsage, Does.Contain("DigitalSignature"));
        Assert.That(certificate.KeyUsage, Does.Contain("KeyEncipherment"));
        Assert.That(certificate.SubjectAlternativeNames, Does.Contain("web01.example.com"));
        Assert.That(certificate.CriticalExtensions, Is.Not.Empty);
    }

    [Test]
    public void ParseAll_WithSeveralCertificates_ReadsEachOne()
    {
        //Arrange
        // A plugin prints the whole chain it was presented.
        string first = CreateCertificatePem("CN=leaf.example.com", days: 90, keySize: 2048);
        string second = CreateCertificatePem("CN=issuing-ca.example.com", days: 365, keySize: 2048);

        //Act
        List<NessusRecord> certificates = CertificateParser.ParseAll(
            $"Subject Name:\n\n{first}\n\nIssuer Name:\n\n{second}\n", Plugin());

        //Assert
        Assert.That(certificates, Has.Count.EqualTo(2));
        Assert.That(
            certificates.Cast<Certificate>().Select(certificate => certificate.SubjectFriendlyName),
            Is.EqualTo(new[] { "leaf.example.com", "issuing-ca.example.com" }));
    }

    [Test]
    public void ParseAll_WithAnExpiredCertificate_MarksItExpired()
    {
        //Arrange
        string pem = CreateCertificatePem(
            "CN=old.example.com", days: 30, keySize: 2048, notBefore: DateTimeOffset.UtcNow.AddDays(-400));

        //Act
        Certificate certificate = (Certificate)CertificateParser.ParseAll(Wrap(pem), Plugin()).Single();

        //Assert
        Assert.That(certificate.Expired, Is.True);
    }

    [Test]
    public void ParseAll_WithAnEllipticCurveKey_ReadsItsSize()
    {
        //Arrange
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        CertificateRequest request = new CertificateRequest(
            "CN=ec.example.com", key, HashAlgorithmName.SHA256);

        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(89));

        //Act
        Certificate parsed = (Certificate)CertificateParser
            .ParseAll(Wrap(certificate.ExportCertificatePem()), Plugin())
            .Single();

        //Assert
        Assert.That(parsed.KeySize, Is.EqualTo(256));
    }

    [Test]
    public void ParseAll_WithNoCertificate_ReturnsNothingRatherThanFailing()
    {
        //Arrange
        // A scan reaches hosts that answer without a certificate; one such host must not take the
        // whole retrieval down with it.
        //Act
        List<NessusRecord> certificates = CertificateParser.ParseAll(
            "The remote host is not an SSL service.", Plugin());

        //Assert
        Assert.That(certificates, Is.Empty);
    }

    [Test]
    public void ParseAll_WithAnUnreadableCertificate_RecordsThatItWasSeen()
    {
        //Arrange
        string pem = "-----BEGIN CERTIFICATE-----\nbm90IGEgY2VydGlmaWNhdGU=\n-----END CERTIFICATE-----";

        //Act
        NessusRecord record = CertificateParser.ParseAll(pem, Plugin()).Single();

        //Assert
        // The observation survives even though the certificate could not be decoded.
        Assert.That(record, Is.Not.InstanceOf<Certificate>());
        Assert.That(record.Message, Is.Not.Null);
        Assert.That(record.IPAddress, Is.EqualTo("10.0.0.9"));
        Assert.That(record.Port, Is.EqualTo(443));
    }

    [Test]
    public void ParseAll_StampsEveryCertificateWithTheAssetItWasPresentedBy()
    {
        //Arrange
        string pem = CreateCertificatePem("CN=web01.example.com", days: 90, keySize: 2048);

        //Act
        NessusRecord certificate = CertificateParser.ParseAll(Wrap(pem), Plugin()).Single();

        //Assert
        Assert.That(certificate.IPAddress, Is.EqualTo("10.0.0.9"));
        Assert.That(certificate.ShortName, Is.EqualTo("api01"));
        Assert.That(certificate.Protocol, Is.EqualTo("TCP"));
        Assert.That(certificate.Port, Is.EqualTo(443));
    }

    /// <summary>
    /// Builds a self-signed certificate and returns its PEM, so that the decoding path runs
    /// against a real certificate rather than a recorded fixture.
    /// </summary>
    /// <param name="subject">The subject distinguished name.</param>
    /// <param name="days">The length of the validity period in days.</param>
    /// <param name="keySize">The size of the key in bits.</param>
    /// <param name="notBefore">The first moment of validity. Defaults to yesterday.</param>
    /// <returns>The PEM.</returns>
    private static string CreateCertificatePem(
        string subject,
        int days,
        int keySize,
        DateTimeOffset? notBefore = null)
    {
        using RSA key = RSA.Create(keySize);

        CertificateRequest request = new CertificateRequest(
            subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));

        SubjectAlternativeNameBuilder names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(subject.Replace("CN=", string.Empty, StringComparison.Ordinal));
        request.CertificateExtensions.Add(names.Build());

        DateTimeOffset start = notBefore ?? DateTimeOffset.UtcNow.AddDays(-1);

        using X509Certificate2 certificate = request.CreateSelfSigned(start, start.AddDays(days));

        return certificate.ExportCertificatePem();
    }

    /// <summary>
    /// Surrounds a PEM with the prose a plugin prints around it.
    /// </summary>
    /// <param name="pem">The PEM.</param>
    /// <returns>The plugin output.</returns>
    private static string Wrap(string pem)
    {
        return $"Subject Name:\n\nCommon Name: web01.example.com\n\n{pem}\n";
    }

    /// <summary>
    /// Returns the export row the parser stamps its records from.
    /// </summary>
    /// <returns>The row.</returns>
    private static NessusPlugin Plugin()
    {
        return new NessusPlugin
        {
            IPAddress = "10.0.0.9",
            DNSName = "api01.example.com",
            PluginName = "SSL Certificate Information",
            Port = 443,
            Plugin = 10863
        };
    }

    #endregion
}
