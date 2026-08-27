using PenguinConverters.Syntra.Core.Security;

namespace PenguinConverters.Syntra.Core.Tests.Security;

[TestFixture]
public class PublisherVerifierTests
{
    #region Methods

    [Test]
    public void Verify_WithAMicrosoftSignedBinary_EstablishesThePublisher()
    {
        //Arrange
        // A Windows system binary is Authenticode-signed and chains to a root the machine trusts,
        // which makes it the one file guaranteed to be present and verifiable.
        string path = SignedSystemBinary();

        //Act
        PublisherVerification verification = PublisherVerifier.Verify(path, expectedSubject: null);

        //Assert
        if (!OperatingSystem.IsWindows())
        {
            Assert.That(verification.Trust, Is.EqualTo(PublisherTrust.NotVerifiable));
            return;
        }

        Assert.That(verification.Trust, Is.EqualTo(PublisherTrust.Trusted));
        Assert.That(verification.Subject, Is.Not.Null.And.Not.Empty);
        Assert.That(verification.Thumbprint, Has.Length.EqualTo(40));
    }

    [Test]
    public void Verify_WithADifferentPublisherExpected_ReportsItAsUntrusted()
    {
        //Arrange
        string path = SignedSystemBinary();

        //Act
        PublisherVerification verification = PublisherVerifier.Verify(path, "Penguin Converters AG");

        //Assert
        if (!OperatingSystem.IsWindows())
        {
            Assert.That(verification.Trust, Is.EqualTo(PublisherTrust.NotVerifiable));
            return;
        }

        // The signature is valid; it is simply not ours. That distinction is the point of the
        // check - an invalid signature and an unexpected publisher are different problems.
        Assert.That(verification.Trust, Is.EqualTo(PublisherTrust.Untrusted));
        Assert.That(verification.Detail, Does.Contain("Penguin Converters AG"));
    }

    [Test]
    public void Verify_WithAnUnsignedFile_SaysSoRatherThanFailing()
    {
        //Arrange
        // The build output is not Authenticode-signed: signing happens on-premises at release.
        string path = typeof(PublisherVerifier).Assembly.Location;

        //Act
        PublisherVerification verification = PublisherVerifier.Verify(path, expectedSubject: null);

        //Assert
        Assert.That(
            verification.Trust,
            Is.EqualTo(OperatingSystem.IsWindows() ? PublisherTrust.Unsigned : PublisherTrust.NotVerifiable));

        Assert.That(verification.Detail, Is.Not.Null);
    }

    [Test]
    public void Verify_WithAnAlteredBinary_DoesNotReportItAsTrusted()
    {
        //Arrange
        // Tampering is what a strong name cannot detect and Authenticode can: the bytes no longer
        // match what was signed.
        string original = SignedSystemBinary();
        string altered = Path.Combine(Path.GetTempPath(), $"altered-{Guid.NewGuid():N}.exe");

        File.Copy(original, altered, overwrite: true);

        try
        {
            byte[] bytes = File.ReadAllBytes(altered);

            // Midway through the file, which is code. The trailing bytes of a signed PE are the
            // certificate table itself, and Authenticode excludes that region from its hash - so
            // altering the end proves nothing and still verifies.
            bytes[bytes.Length / 2] ^= 0xFF;

            File.WriteAllBytes(altered, bytes);

            //Act
            PublisherVerification verification = PublisherVerifier.Verify(altered, expectedSubject: null);

            //Assert
            Assert.That(verification.Trust, Is.Not.EqualTo(PublisherTrust.Trusted));
        }
        finally
        {
            File.Delete(altered);
        }
    }

    [Test]
    public void GetPublisher_WithAnUnsignedFile_ReturnsNothing()
    {
        //Arrange
        //Act
        string? publisher = PublisherVerifier.GetPublisher(typeof(PublisherVerifier).Assembly.Location);

        //Assert
        Assert.That(publisher, Is.Null);
    }

    /// <summary>
    /// Returns a file that is Authenticode-signed and trusted on this machine.
    /// </summary>
    /// <returns>The path.</returns>
    private static string SignedSystemBinary()
    {
        if (!OperatingSystem.IsWindows())
        {
            return typeof(PublisherVerifier).Assembly.Location;
        }

        string path = Path.Combine(Environment.SystemDirectory, "kernel32.dll");

        Assert.That(File.Exists(path), Is.True, "this test needs a signed system binary to verify against");

        return path;
    }

    #endregion
}
